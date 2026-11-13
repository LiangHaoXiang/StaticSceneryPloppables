using System.Text;
using Colossal.UI.Binding;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI;
using StaticSceneryPloppables.Systems;
using StaticSceneryPloppables.Tools;

namespace StaticSceneryPloppables
{
    /// <summary>
    /// Gameface 桥：搜索+筛选+分页（ssp.search(query,offset) / ssp.setFilter(json) → ssp.results），
    /// 选择放置（ssp.select → SceneryPlacementTool）。
    /// 绑定协议：GetterValueBinding 推送事件名为 ssp.results.update，JS 需先 trigger ssp.results.subscribe。
    /// </summary>
    public partial class SceneryPloppableUISystem : UISystemBase
    {
        private const int PageSize = 120;

        private GetterValueBinding<string> m_ResultsBinding;
        private GetterValueBinding<string> m_EventBinding;
        private string m_Query = string.Empty;
        private int m_Offset;
        private int m_SearchToken;
        private int m_PushedCatalogCount = -1;
        private string m_PushedEvent = string.Empty;
        private SceneryPlacementTool m_Tool;
        private bool m_ToolWasActive;

        // 筛选状态（UI 分开触发设置；0/空 = 不限）
        private string m_FilterZone = "";
        private string m_FilterDensity = "";
        private int m_FilterLevel;
        private int m_FilterLotW;
        private int m_FilterLotD;

        protected override void OnCreate()
        {
            base.OnCreate();
            InitDiagnostics();

            m_ResultsBinding = new GetterValueBinding<string>("ssp", "results", BuildResultsJson);
            AddBinding(m_ResultsBinding);
            m_EventBinding = new GetterValueBinding<string>("ssp", "uievent", () => Tools.SceneryPlacementTool.PendingUIEvent);
            AddBinding(m_EventBinding);
            // 注：语言不再由本模组推送——UI 文案走游戏本地化字典（LocaleSSP + AddSource），
            // 选项里切换语言时字典随之切换，translate() 自动取到新语言。
            AddBinding(new TriggerBinding<string, int>("ssp", "search", (q, offset) =>
            {
                m_Query = q ?? string.Empty;
                m_Offset = System.Math.Max(0, offset);
                // 仅「新一次搜索」（offset==0）才自增 token：UI 据此重置列表；
                // 翻页（offset>0）沿用同一 token → UI 走追加分支，否则每页都覆盖上一页
                if (offset <= 0)
                {
                    m_SearchToken++;
                }

                m_ResultsBinding.Update();
            }));
            // 注：筛选（页签/密度/等级/面宽/进深）是纯前端过滤，C# 侧不再需要 setFilter 绑定。
            AddBinding(new TriggerBinding<int>("ssp", "select", Select));
            // 面板重开命中缓存时调用：把 C# 侧分页游标归零，避免后续推送从旧 offset 开始
            AddBinding(new TriggerBinding("ssp", "resetOffset", () => { m_Offset = 0; }));
            AddBinding(new TriggerBinding("ssp", "closeTool", () =>
            {
                World.GetExistingSystemManaged<SceneryPlacementTool>()?.DeactivateTool();
            }));
        }

        protected override void OnUpdate()
        {
            // 分帧生成期间目录增长：重推（订阅中的面板自动刷新，offset 归零防越界）
            if (GrowableVariantSystem.Catalog.Count != m_PushedCatalogCount)
            {
                m_PushedCatalogCount = GrowableVariantSystem.Catalog.Count;
                m_ResultsBinding.Update();
            }

            // 工具取消（Esc/右键/切换到别的工具）→ 推 closePanel，面板随之关闭
            if (m_Tool == null)
            {
                m_Tool = World.GetExistingSystemManaged<SceneryPlacementTool>();
            }

            bool toolActive = m_Tool != null && m_Tool.IsPlacementActive;
            if (m_ToolWasActive && !toolActive)
            {
                m_Tool?.DeactivateTool();
                Tools.SceneryPlacementTool.PendingUIEvent = "closePanel";
            }

            m_ToolWasActive = toolActive;

            // C# 侧事件 → 推给 UI（面板据此关闭）；推送后清空，保证下一次同类事件仍能触发
            string pending = Tools.SceneryPlacementTool.PendingUIEvent;
            if (pending != m_PushedEvent)
            {
                m_PushedEvent = pending;
                m_EventBinding.Update();
            }

            if (pending.Length != 0)
            {
                Tools.SceneryPlacementTool.PendingUIEvent = string.Empty;
            }

            if (Mod.DiagnosticsEnabled)
            {
                DumpObjectTrees(); // 诊断：每帧扫描 Updated/Deleted 并打实体树，默认关闭
            }
            CleanupOrphans(); // 迁移残留地皮清理（实证在跑的 UIUpdate 里执行）
        }

        private void ApplyFilter(string json)
        {
            // 极简解析 {"z":"Residential","d":"High","l":3,"w":4,"dp":5}
            m_FilterZone = ReadStr(json, "z");
            m_FilterDensity = ReadStr(json, "d");
            m_FilterLevel = ReadInt(json, "l");
            m_FilterLotW = ReadInt(json, "w");
            m_FilterLotD = ReadInt(json, "dp");
        }

        private static string ReadStr(string json, string key)
        {
            string token = "\"" + key + "\":\"";
            int i = json?.IndexOf(token, System.StringComparison.Ordinal) ?? -1;
            if (i < 0)
            {
                return "";
            }

            int start = i + token.Length;
            int end = json.IndexOf('"', start);
            return end < 0 ? "" : json[start..end];
        }

        private static int ReadInt(string json, string key)
        {
            string token = "\"" + key + "\":";
            int i = json?.IndexOf(token, System.StringComparison.Ordinal) ?? -1;
            if (i < 0)
            {
                return 0;
            }

            int start = i + token.Length;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end])))
            {
                end++;
            }

            return end > start && int.TryParse(json[start..end], out var v) ? v : 0;
        }

        private bool Matches(GrowableVariantSystem.CatalogEntry e)
        {
            if (m_FilterZone.Length > 0 && e.Zone != m_FilterZone)
            {
                return false;
            }

            if (m_FilterDensity.Length > 0 && e.Density != m_FilterDensity)
            {
                return false;
            }

            if (m_FilterLevel > 0 && e.Level != m_FilterLevel)
            {
                return false;
            }

            if (m_FilterLotW > 0 && e.LotW != m_FilterLotW)
            {
                return false;
            }

            if (m_FilterLotD > 0 && e.LotD != m_FilterLotD)
            {
                return false;
            }

            if (m_Query.Trim().Length > 0
                && e.Name.IndexOf(m_Query, System.StringComparison.OrdinalIgnoreCase) < 0
                && e.RawName.IndexOf(m_Query, System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }

        private void Select(int index)
        {
            var catalog = GrowableVariantSystem.Catalog;
            if (index < 0 || index >= catalog.Count)
            {
                return;
            }

            if (catalog[index].Building != null)
            {
                World.GetOrCreateSystemManaged<SceneryPlacementTool>().ActivateTool(catalog[index].Building);
            }
        }

        private string BuildResultsJson()
        {
            var catalog = GrowableVariantSystem.Catalog;
            var sb = new StringBuilder(8192);
            sb.Append("{\"total\":").Append(catalog.Count)
              .Append(",\"token\":").Append(m_SearchToken)
              .Append(",\"complete\":").Append(GrowableVariantSystem.BuildComplete ? "true" : "false")
              .Append(",\"items\":[");

            int matchedTotal = 0;
            int written = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                var e = catalog[i];
                if (!Matches(e))
                {
                    continue;
                }

                int rank = matchedTotal++;
                if (rank < m_Offset || written >= PageSize)
                {
                    continue;
                }

                if (written > 0)
                {
                    sb.Append(',');
                }
                // si=false：本地化名已含等级/尺寸（如“北美高层住宅01-L5 6x6”），UI 不再重复显示小字
                bool nameHasInfo = System.Text.RegularExpressions.Regex.IsMatch(
                    e.Name ?? string.Empty, @"(L\d|\d+x\d)");
                sb.Append("{\"i\":").Append(i)
                  .Append(",\"n\":\"").Append(Escape(e.Name)).Append('"')
                  .Append(",\"ic\":\"").Append(Escape(e.IconUrl)).Append('"')
                  .Append(",\"si\":").Append(nameHasInfo ? "false" : "true")
                  .Append(",\"z\":\"").Append(e.Zone).Append('"')
                  .Append(",\"d\":\"").Append(e.Density).Append('"')
                  .Append(",\"l\":").Append(e.Level)
                  .Append(",\"w\":").Append(e.LotW)
                  .Append(",\"dp\":").Append(e.LotD)
                  .Append('}');
                written++;
            }

            sb.Append("],\"matched\":").Append(matchedTotal)
              .Append(",\"hasMore\":").Append(m_Offset + written < matchedTotal ? "true" : "false")
              .Append('}');
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            return (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
