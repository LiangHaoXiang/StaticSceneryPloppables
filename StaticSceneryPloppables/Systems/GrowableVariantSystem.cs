using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Colossal.Entities;
using Game;
using Game.Economy;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace StaticSceneryPloppables.Systems
{
    /// <summary>
    /// 全量图章工厂：为每个原版分区建筑生成两件套——
    /// ① 剥离版 BuildingPrefab（SSP_S_ 前缀，摘 SpawnableBuilding 等属性来源，
    ///    保留网格/地皮声明 ObjectSubAreas/随机装饰 SpawnableObject）
    /// ② AssetStampPrefab（SSP_ 前缀，子物体引用剥离版 + 整组复制地皮槽位）。
    ///
    /// 生成节奏：分帧预算（每 tick 处理 BatchSize 个），避免一次性 3619 组阻塞加载。
    /// 激活一律经 Tools.SceneryPlacementTool（Mode.Stamp）；绝不用 ActivatePrefabTool（Create 模式=崩溃根因）。
    /// 验证结论（2026-11-12 用户实测）：地皮跟随/地皮变体随机/装饰随机/存读档安全全部通过。
    /// </summary>
    public partial class GrowableVariantSystem : GameSystemBase
    {
        public const string Prefix = "SSP_";
        public const string StrippedPrefix = "SSP_S_";

        private const int BatchSize = 40;        // 每帧最多处理的建筑数
        private const double FrameBudgetMs = 3;  // 每帧在该系统上最多花的时间（ms）

        /// <summary>目录条目：UI 面板搜索/筛选/展示/选择所需信息。</summary>
        public class CatalogEntry
        {
            public PrefabBase Stamp;
            public BuildingPrefab Building; // 剥离建筑本体：直接放置用
            public string Name;      // 本地化显示名（Find It 同源）
            public string RawName;   // 内部 prefab 名（搜索兜底）
            public string IconUrl;   // 图标（AIL 替换后的 m_Icon 或缩略图 URL，Find It 同源）
            public string Zone;      // Residential/Commercial/Industrial/Office
            public string Density;   // Low/Medium/High/""（zone 名解析）
            public int Level;        // 1-5
            public int LotW;         // 面宽（格）
            public int LotD;         // 进深（格）

            // ---- 序列化预计算（构建期算一次，避免每次推送重复 Escape/Regex）----
            public bool NameHasInfo;     // 名字里已含 Lx/宽x深 → UI 不再显示小字
            public string NameJson;      // 已转义的 JSON 字符串内容
            public string RawNameJson;
            public string IconJson;
            public bool IsSignature;     // 标志性建筑（当前不纳入目录，保留字段以支持排序第 6 级）
            public bool IsMixed;         // 混合型住宅（住宅属性 + 可售卖/可制造）
            public string Style;         // 风格："EU" / "NA" / ""（按 prefab 名前缀）
        }

        // 供 UISystem 读取的进程级目录
        public static readonly List<CatalogEntry> Catalog = new List<CatalogEntry>();

        /// <summary>剥离版 prefab 名 → 原版 prefab 的 titleId（用于补 Assets.NAME 词条，切换语言时可重新解析）。</summary>
        public static readonly Dictionary<string, string> PrefabTitleIds = new Dictionary<string, string>();
        public static bool BuildComplete { get; private set; }
        public static int TotalCandidates { get; private set; }

        private EntityQuery m_PrefabQuery;
        private PrefabSystem m_PrefabSystem;
        private Game.UI.InGame.PrefabUISystem m_PrefabUISystem;
        private bool m_ScanDone;

        /// <summary>目录构建计时（定位载入耗时用）。</summary>
        private System.Diagnostics.Stopwatch m_BuildSw;
        private int m_NextProgressLog = 500;
        // 分段计时（#24）：只在 DiagnosticsEnabled 时输出，平时仅累加几次 double 运算
        private double m_CloneMs;
        private double m_AddMs;
        private double m_EntryMs;
        private readonly List<BuildingPrefab> m_Pending = new List<BuildingPrefab>();

        private bool m_Enabled = true;

        private static string UserDataDir =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                @"AppData\LocalLow\Colossal Order\Cities Skylines II");

        protected override void OnCreate()
        {
            base.OnCreate();
            LoadConfig();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PrefabUISystem = World.GetOrCreateSystemManaged<Game.UI.InGame.PrefabUISystem>();
            m_PrefabQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<PrefabData>(),
                    ComponentType.ReadOnly<BuildingData>()
                }
            });
            RequireForUpdate(m_PrefabQuery);
            Mod.log.Warn($"[SSP] stamp factory created. enableVariants={m_Enabled}");
        }

        protected override void OnUpdate()
        {
            if (!m_Enabled || BuildComplete)
            {
                return;
            }

            if (!m_ScanDone)
            {
                m_ScanDone = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                ScanSources();
                sw.Stop();
                TotalCandidates = m_Pending.Count;
                Mod.log.Warn($"[SSP] scan done: {m_Pending.Count} growable buildings queued in {sw.ElapsedMilliseconds}ms");
                m_BuildSw = System.Diagnostics.Stopwatch.StartNew();
                return;
            }

            // 分帧预算生成：按时间预算而不是固定条数，避免不同机器上出现长时间卡帧
            int processed = 0;
            var budgetSw = System.Diagnostics.Stopwatch.StartNew();
            while (m_Pending.Count > 0 && processed < BatchSize)
            {
                var source = m_Pending[m_Pending.Count - 1];
                m_Pending.RemoveAt(m_Pending.Count - 1);
                processed++;
                BuildFor(source);

                if (processed >= 4 && budgetSw.Elapsed.TotalMilliseconds >= FrameBudgetMs)
                {
                    break;
                }
            }

            if (m_Pending.Count == 0)
            {
                // 目录构建完成 → 按用户规则排序一次（UI 收到即最终顺序）
                Catalog.Sort(CatalogComparer.Instance);
                BuildComplete = true;
                m_BuildSw?.Stop();
                Mod.log.Warn($"[SSP] BUILD COMPLETE: {Catalog.Count} entries in catalog, build took {m_BuildSw?.ElapsedMilliseconds ?? 0}ms");
                LogIconSummary();
                // 给剥离版 prefab 补 Assets.NAME[...] 词条（否则信息面板/提示显示原始 key）
                Localization.PrefabNamePatcher.SetMapping(PrefabTitleIds);

                if (Mod.DiagnosticsEnabled)
                {
                    Mod.log.Warn($"[SSP] build breakdown: clone={m_CloneMs:F0}ms, addPrefab={m_AddMs:F0}ms, entry+name={m_EntryMs:F0}ms");
                    LogSortSample();
                }
            }
            else if (Catalog.Count >= m_NextProgressLog)
            {
                // 每 500 条一条进度日志（原来是每 40 条，载入期白白写 80+ 行盘）
                while (Catalog.Count >= m_NextProgressLog)
                {
                    m_NextProgressLog += 500;
                }

                Mod.log.Warn($"[SSP] progress: {Catalog.Count}/{TotalCandidates}");
            }
        }

        /// <summary>图标排查：统计空缩略图 URL 数量并抽样打印（UI 空白图标的判定依据）。</summary>
        private void LogIconSummary()
        {
            int empty = 0;
            int logged = 0;
            for (int i = 0; i < Catalog.Count; i++)
            {
                var e = Catalog[i];
                if (string.IsNullOrEmpty(e.IconUrl))
                {
                    empty++;
                    if (logged < 5)
                    {
                        Mod.log.Warn($"[SSP] empty icon url: {e.RawName} / name={e.Name}");
                        logged++;
                    }
                }
            }

            Mod.log.Warn($"[SSP] icon summary: {empty}/{Catalog.Count} entries have empty thumbnail url");
            for (int i = 0; i < Catalog.Count && i < 3; i++)
            {
                Mod.log.Warn($"[SSP] icon sample: {Catalog[i].RawName} -> {Catalog[i].IconUrl}");
            }
        }

        private void ScanSources()
        {
            var entities = m_PrefabQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    PrefabBase prefab = SafeGetPrefab(entity);
                    if (prefab is BuildingPrefab bp && bp.Has<SpawnableBuilding>() && !bp.name.StartsWith(Prefix))
                    {
                        // 等级去重：2 级外观同 1 级、4 级同 3 级，列表也不显示 → 连 prefab 都不需要建。
                        // （旧版为兼容开发期存档注册了全部等级；0.1.x 上线后不存在含 L2/L4 的玩家存档）
                        int level = LevelOf(bp);
                        if (level == 2 || level == 4)
                        {
                            continue;
                        }

                        m_Pending.Add(bp);
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        private void BuildFor(BuildingPrefab source)
        {
            // 兜底：2/4 级不建（ScanSources 已过滤，这里再挡一次，保证列表与 prefab 一致）
            int sourceLevel = LevelOf(source);
            if (sourceLevel == 2 || sourceLevel == 4)
            {
                return;
            }

            // ① 剥离版
            var phaseSw = System.Diagnostics.Stopwatch.StartNew();
            var stripped = CreateStrippedBuilding(source, StrippedPrefix + source.name);
            if (stripped != null)
            {
                m_CloneMs += phaseSw.Elapsed.TotalMilliseconds;
            }
            if (stripped == null)
            {
                return;
            }

            try
            {
                phaseSw.Restart();
                if (!m_PrefabSystem.AddPrefab(stripped, null, null, null))
                {
                    return;
                }
                m_PrefabSystem.UpdatePrefab(stripped); // 排队下帧 PrefabUpdate 初始化
                m_AddMs += phaseSw.Elapsed.TotalMilliseconds;
            }
            catch (System.Exception e)
            {
                Mod.log.Warn($"[SSP] stripped AddPrefab threw for {source.name}: {e.Message}");
                return;
            }

            // ② 目录条目（不再创建图章：放置已改为「直接放剥离建筑」，图章纯属载入开销）
            phaseSw.Restart();
            var entry = BuildEntry(source, null);
            entry.Building = stripped; // 直接放置用（与 Find It 同源：ActivatePrefabTool）
            m_EntryMs += phaseSw.Elapsed.TotalMilliseconds;

            // 信息面板/悬停提示按 "Assets.NAME[prefabName]" 查字典，先把我们的名字映射到原版名称
            if (!PrefabTitleIds.ContainsKey(stripped.name))
            {
                string titleId = TitleIdOf(source);
                if (!string.IsNullOrEmpty(titleId))
                {
                    PrefabTitleIds[stripped.name] = titleId;
                }
            }

            Catalog.Add(entry);
        }

        /// <summary>取建筑等级（SpawnableBuildingData.m_Level；取不到返回 0 表示不过滤）。</summary>
        private int LevelOf(BuildingPrefab source)
        {
            try
            {
                if (m_PrefabSystem.TryGetEntity(source, out var entity)
                    && EntityManager.HasComponent<SpawnableBuildingData>(entity))
                {
                    return EntityManager.GetComponentData<SpawnableBuildingData>(entity).m_Level;
                }
            }
            catch
            {
                // 取不到按 0 处理
            }

            return 0;
        }

        /// <summary>构建目录条目：本地化名（Find It 同源）、图标、zone/等级/密度/宽深。</summary>
        private CatalogEntry BuildEntry(BuildingPrefab source, PrefabBase stamp)
        {
            string zone = "";
            string density = "";
            int level = 0;
            string icon = SafeIcon(source); // 建筑自带 SVG 图标（若有）

            try
            {
                if (m_PrefabSystem.TryGetEntity(source, out var srcEntity)
                    && EntityManager.HasComponent<SpawnableBuildingData>(srcEntity))
                {
                    var spawnData = EntityManager.GetComponentData<SpawnableBuildingData>(srcEntity);
                    level = spawnData.m_Level;
                    if (m_PrefabSystem.TryGetPrefab<ZonePrefab>(spawnData.m_ZonePrefab, out var zonePrefab))
                    {
                        if (zonePrefab.m_Office)
                        {
                            zone = "Office";
                        }
                        else
                        {
                            var zoneEntity = m_PrefabSystem.GetEntity(zonePrefab);
                            if (EntityManager.HasComponent<ZoneData>(zoneEntity))
                            {
                                zone = EntityManager.GetComponentData<ZoneData>(zoneEntity).m_AreaType.ToString();
                            }
                        }

                        string zn = zonePrefab.name ?? "";
                        if (zn.Contains("Low")) density = "Low";
                        else if (zn.Contains("Medium")) density = "Medium";
                        else if (zn.Contains("High")) density = "High";

                        // 分区图标（纯 SVG，零渲染开销）；替代 thumbnail:// ——后者会为每条目触发一次
                        // 缩略图相机渲染，是面板卡顿与空白图标的根因。
                        if (icon.Length == 0)
                        {
                            icon = SafeIcon(zonePrefab);
                        }
                    }
                }
            }
            catch
            {
                // 元数据缺失不阻塞目录
            }

            var entry = new CatalogEntry
            {
                Stamp = stamp,
                Name = LocalizedName(source),
                RawName = source.name,
                IconUrl = icon, // 仅 SVG 图标；空则由 UI 按分区内置图标兜底
                Zone = zone,
                Density = density,
                Level = level,
                LotW = source.m_LotWidth,
                LotD = source.m_LotDepth
            };

            // 构建期一次算好：JSON 转义 + 「名字里是否已含等级/尺寸」
            entry.NameJson = JsonEscape(entry.Name);
            entry.RawNameJson = JsonEscape(entry.RawName);
            entry.IconJson = JsonEscape(entry.IconUrl);
            entry.NameHasInfo = System.Text.RegularExpressions.Regex.IsMatch(
                entry.Name ?? string.Empty, @"(L\d|\d+x\d)");
            entry.IsSignature = source.Has<SignatureBuilding>();

            // 风格：原版 prefab 名以 EU_ / NA_ 前缀区分欧美/北美，其余归入「全部」
            if (source.name.StartsWith("EU_"))
            {
                entry.Style = "EU";
            }
            else if (source.name.StartsWith("NA_"))
            {
                entry.Style = "NA";
            }
            else
            {
                entry.Style = "";
            }

            // 风格：优先用主题（ThemeObject.m_Theme），其次用 prefab 名 EU_/NA_ 前缀
            if (entry.Style.Length == 0)
            {
                try
                {
                    if (source.TryGet<ThemeObject>(out var themeObject) && themeObject.m_Theme != null)
                    {
                        string themeName = themeObject.m_Theme.name ?? string.Empty;
                        string themePrefix = themeObject.m_Theme.assetPrefix ?? string.Empty;
                        if (themePrefix.IndexOf("EU", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || themeName.IndexOf("Europe", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            entry.Style = "EU";
                        }
                        else if (themePrefix.IndexOf("NA", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || themeName.IndexOf("North", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            entry.Style = "NA";
                        }
                    }
                }
                catch
                {
                    // 主题缺失不影响目录
                }
            }

            // 混合型住宅：住宅属性 > 0 且可售卖或可制造（与游戏 PropertyUtils.IsMixedBuilding 同判据）
            if (zone == "Residential")
            {
                try
                {
                    if (m_PrefabSystem.TryGetEntity(source, out var propEntity)
                        && EntityManager.HasComponent<BuildingPropertyData>(propEntity))
                    {
                        var prop = EntityManager.GetComponentData<BuildingPropertyData>(propEntity);
                        entry.IsMixed = prop.m_ResidentialProperties > 0
                            && (prop.m_AllowedSold != Resource.NoResource
                                || prop.m_AllowedManufactured != Resource.NoResource);
                    }
                }
                catch
                {
                    // 元数据缺失按纯住宅处理
                }
            }

            return entry;
        }

        /// <summary>
        /// 目录排序（用户规则，2026-11-14 最终版，共 8 级）：
        /// ① 分区：住宅 → 混合居民区 → 商业 → 办公 → 工业 → 未知
        /// ② 密度：无密度 → 低 → 中 → 高
        /// ③ 占地总面积（面宽×进深）小的在前
        /// ④ 面宽小的在前
        /// ⑤ 进深小的在前
        /// ⑥ 无等级最前，然后等级小的在前
        /// ⑦ 标志性建筑排在常规建筑之后（当前目录不含标志性建筑，此级恒等）
        /// ⑧ 名称按字母/数字自然顺序
        /// 完全相等时按内部名兜底，保证顺序稳定。
        /// </summary>
        private sealed class CatalogComparer : IComparer<CatalogEntry>
        {
            public static readonly CatalogComparer Instance = new CatalogComparer();

            public int Compare(CatalogEntry a, CatalogEntry b)
            {
                if (ReferenceEquals(a, b))
                {
                    return 0;
                }

                if (a == null || b == null)
                {
                    return a == null ? 1 : -1;
                }

                int c = ZoneRank(a).CompareTo(ZoneRank(b));
                if (c != 0)
                {
                    return c;
                }

                c = DensityRank(a.Density).CompareTo(DensityRank(b.Density));
                if (c != 0)
                {
                    return c;
                }

                c = (a.LotW * a.LotD).CompareTo(b.LotW * b.LotD);
                if (c != 0)
                {
                    return c;
                }

                c = a.LotW.CompareTo(b.LotW);
                if (c != 0)
                {
                    return c;
                }

                c = a.LotD.CompareTo(b.LotD);
                if (c != 0)
                {
                    return c;
                }

                c = LevelRank(a.Level).CompareTo(LevelRank(b.Level));
                if (c != 0)
                {
                    return c;
                }

                c = (a.IsSignature ? 1 : 0).CompareTo(b.IsSignature ? 1 : 0);
                if (c != 0)
                {
                    return c;
                }

                // 第 7 级：名称按「字母/数字」自然顺序（a 在 b 前、1 在 2 前、2 在 10 前）
                c = NaturalCompare(a.Name, b.Name);
                if (c != 0)
                {
                    return c;
                }

                return string.CompareOrdinal(a.RawName ?? string.Empty, b.RawName ?? string.Empty);
            }

            /// <summary>分区顺序：纯住宅 0、混合居民区 1、商业 2、办公 3、工业 4、未知 5。</summary>
            private static int ZoneRank(CatalogEntry e)
            {
                switch (e.Zone)
                {
                    case "Residential": return e.IsMixed ? 1 : 0;
                    case "Commercial": return 2;
                    case "Office": return 3;
                    case "Industrial": return 4;
                    default: return 5;
                }
            }

            /// <summary>密度顺序：无密度 0、低 1、中 2、高 3、其它 4。</summary>
            private static int DensityRank(string density)
            {
                switch (density)
                {
                    case "Low": return 1;
                    case "Medium": return 2;
                    case "High": return 3;
                    case "": return 0;
                    default: return 4;
                }
            }

            /// <summary>自然顺序比较：字母不区分大小写，连续数字按数值比较（2 &lt; 10）。</summary>
            private static int NaturalCompare(string a, string b)
            {
                a ??= string.Empty;
                b ??= string.Empty;
                int i = 0;
                int j = 0;
                while (i < a.Length && j < b.Length)
                {
                    char ca = a[i];
                    char cb = b[j];
                    bool da = char.IsDigit(ca);
                    bool db = char.IsDigit(cb);

                    if (da && db)
                    {
                        int si = i;
                        int sj = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;

                        // 跳过前导 0 后按长度、再按字典序比较
                        int ai = si;
                        int bj = sj;
                        while (ai < i && a[ai] == '0') ai++;
                        while (bj < j && b[bj] == '0') bj++;
                        int la = i - ai;
                        int lb = j - bj;
                        if (la != lb)
                        {
                            return la < lb ? -1 : 1;
                        }

                        for (int k = 0; k < la; k++)
                        {
                            if (a[ai + k] != b[bj + k])
                            {
                                return a[ai + k] < b[bj + k] ? -1 : 1;
                            }
                        }

                        continue;
                    }

                    char ua = char.ToUpperInvariant(ca);
                    char ub = char.ToUpperInvariant(cb);
                    if (ua != ub)
                    {
                        return ua < ub ? -1 : 1;
                    }

                    i++;
                    j++;
                }

                int restA = a.Length - i;
                int restB = b.Length - j;
                if (restA == restB)
                {
                    return string.CompareOrdinal(a, b); // 完全同前缀时保持稳定
                }

                return restA < restB ? -1 : 1;
            }

            /// <summary>无等级（0）排最前，其余按等级升序。</summary>
            private static int LevelRank(int level) => level <= 0 ? 0 : level;
        }

        /// <summary>JSON 字符串转义（构建期执行一次，推送时直接拼接）。</summary>
        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            if (s.IndexOf('\\') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0 && s.IndexOf('\r') < 0 && s.IndexOf('\t') < 0)
            {
                return s; // 绝大多数情况无需处理，直接复用原字符串
            }

            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        /// <summary>取 prefab 自带的 SVG 图标 URI（不做缩略图渲染）。</summary>
        private static string SafeIcon(PrefabBase prefab)
        {
            try
            {
                return prefab == null ? "" : (Game.UI.ImageSystem.GetIcon(prefab) ?? "");
            }
            catch
            {
                return "";
            }
        }

        /// <summary>本地化显示名（Find It GetAssetName 同款）：标题键查字典，兜底内部名格式化。</summary>
        private string LocalizedName(BuildingPrefab source)
        {
            try
            {
                if (m_PrefabSystem.TryGetEntity(source, out var srcEntity))
                {
                    m_PrefabUISystem.GetTitleAndDescription(srcEntity, out var titleId, out _);
                    if (titleId != null
                        && GameManager.instance.localizationManager.activeDictionary.TryGetValue(titleId, out var name)
                        && name != null)
                    {
                        return name;
                    }
                }
            }
            catch
            {
            }

            return (source.name ?? "").Replace('_', ' ');
        }

        /// <summary>取原版建筑的 titleId（如 Assets.NAME[EU_CommercialLow01_L2_5x6]）。</summary>
        private string TitleIdOf(BuildingPrefab source)
        {
            try
            {
                if (m_PrefabSystem.TryGetEntity(source, out var entity))
                {
                    m_PrefabUISystem.GetTitleAndDescription(entity, out var titleId, out _);
                    return titleId;
                }
            }
            catch
            {
            }

            return null;
        }

        private void LoadConfig()
        {
            try
            {
                string path = Path.Combine(UserDataDir, "ModsData", "StaticSceneryPloppables", "config.json");
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    string trimmed = line.Replace(" ", string.Empty);
                    int colon = trimmed.IndexOf(':');
                    if (colon < 0)
                    {
                        continue;
                    }

                    if (trimmed[..colon] == "\"enableVariants\"" && trimmed[(colon + 1)..].TrimEnd(',', '}', ' ') == "false")
                    {
                        m_Enabled = false;
                    }
                }
            }
            catch (System.Exception e)
            {
                Mod.log.Warn($"[SSP] config load failed (using defaults): {e.Message}");
            }
        }

        /// <summary>
        /// 剥离版白名单 BuildingPrefab：保留网格/地皮(ObjectSubAreas)/随机装饰(SpawnableObject)声明
        /// 与基础 UI/主题组件；摘除 SpawnableBuilding（属性来源）与 ObsoleteIdentifiers；asset 置空。
        /// </summary>
        private BuildingPrefab CreateStrippedBuilding(BuildingPrefab src, string newName)
        {
            try
            {
                var clone = (BuildingPrefab)PrefabBase.Create(typeof(BuildingPrefab), newName);
                CopyFields(src, clone, skipComponents: true);

                CopyComponentIfPresent<ObjectSubAreas>(src, clone);       // 地皮表面声明（占位 AreaPrefab+节点）
                CopyComponentIfPresent<ObjectSubObjects>(src, clone);     // 静态装饰子物体声明
                CopyComponentIfPresent<ObjectSubNets>(src, clone);        // 装饰线网声明
                CopyComponentIfPresent<SpawnableObject>(src, clone);      // 随机装饰组（概率+随机组）
                CopyComponentIfPresent<UIObject>(src, clone);             // UI 索引/图标
                CopyComponentIfPresent<ThemeObject>(src, clone);          // 主题
                CopyComponentIfPresent<FloatingObject>(src, clone);
                CopyComponentIfPresent<UnderwaterObject>(src, clone);
                CopyComponentIfPresent<DestructibleObject>(src, clone);
                CopyComponentIfPresent<BuildingTerraformOverride>(src, clone);
                CopyComponentIfPresent<EditorAssetCategoryOverride>(src, clone);

                if (clone.TryGet<UIObject>(out var uiObject) && string.IsNullOrEmpty(uiObject.m_Icon))
                {
                    uiObject.m_Icon = src.thumbnailUrl;
                }

                clone.isDirty = true;
                clone.asset = null; // 落盘由用户数据库接管；继承源引用会写官方 .cok 包
                return clone;
            }
            catch (System.Exception e)
            {
                Mod.log.Warn($"[SSP] CreateStrippedBuilding failed for {src.name}: {e.GetType().Name} {e.Message}");
                return null;
            }
        }

        private void CopyComponentIfPresent<T>(BuildingPrefab src, BuildingPrefab dst) where T : ComponentBase
        {
            if (!src.TryGet<T>(out var srcComp))
            {
                return;
            }

            var newComp = (ComponentBase)ScriptableObject.CreateInstance(typeof(T));
            newComp.name = srcComp.name;
            CopyFields(srcComp, newComp, skipComponents: false);
            newComp.prefab = dst;
            dst.components.Add(newComp);
        }

        /// <summary>
        /// 反射字段缓存：<c>Type.GetFields</c> 每次都会返回新数组，而目录构建要跑 3 万多次拷贝，
        /// 逐个类型缓存（含继承链展开）是启动耗时的主要优化点。
        /// </summary>
        private static readonly Dictionary<System.Type, FieldInfo[]> FieldsCache =
            new Dictionary<System.Type, FieldInfo[]>();

        private static FieldInfo[] GetFieldsFor(System.Type type)
        {
            if (FieldsCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var list = new List<FieldInfo>();
            var walk = type;
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
            while (walk != null && walk != typeof(object))
            {
                foreach (var field in walk.GetFields(Flags))
                {
                    if (!field.IsStatic && !field.IsInitOnly)
                    {
                        list.Add(field);
                    }
                }

                walk = walk.BaseType;
            }

            var fields = list.ToArray();
            FieldsCache[type] = fields;
            return fields;
        }

        /// <summary>反射逐字段浅拷贝（公有实例字段，含继承链；跳过 name/components）。</summary>
        private static void CopyFields(object src, object dst, bool skipComponents)
        {
            var fields = GetFieldsFor(src.GetType());
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.Name == "name" || (skipComponents && field.Name == "components"))
                {
                    continue;
                }

                try
                {
                    field.SetValue(dst, field.GetValue(src));
                }
                catch
                {
                    // 个别特殊字段跳过，不阻塞整体
                }
            }
        }

        private PrefabBase SafeGetPrefab(Entity entity)
        {
            try
            {
                return m_PrefabSystem.GetPrefab<PrefabBase>(entity);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>诊断：打印排序后前 15 条，核对六级排序规则（仅 DiagnosticsEnabled）。</summary>
        private void LogSortSample()
        {
            int n = System.Math.Min(15, Catalog.Count);
            for (int i = 0; i < n; i++)
            {
                var e = Catalog[i];
                Mod.log.Warn($"[SSP] sort[{i}] {e.Zone} {e.LotW}x{e.LotD} L{e.Level} sig={e.IsSignature} {e.RawName}");
            }
        }
    }
}
