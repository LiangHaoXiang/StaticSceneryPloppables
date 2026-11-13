using Colossal.Logging;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace StaticSceneryPloppables
{
    /// <summary>
    /// 只读诊断：把我们图章对象的「实体树」打进日志，用于定位
    /// 「原版迁移后旧地皮残留 / 地皮归属谁」这类问题。
    ///
    /// 依赖事实（1.6.2f1 反编译）：
    /// - 地皮 area 实体由 ReplacePrefabSystem.CreateAreas 用 m_Owner = 正在创建的对象 生成；
    /// - SubObjectSystem.CreateSubObjects 里没有任何 SubArea 处理 → 子物体不会自带地皮；
    /// - 因此地皮必然挂在「声明了 ObjectSubAreas 的那个实体」上（对我们=图章根）。
    ///
    /// 每次新出现/被更新/被删除的 SSP_ 实体只打印一次，纯日志、不改任何状态。
    /// </summary>
    public partial class SceneryPloppableUISystem
    {
        private const string DumpPrefix = "SSP_";

        private EntityQuery m_UpdatedQuery;
        private EntityQuery m_DeletedQuery;
        /// <summary>所有带地皮的建筑（用于按查询登记追踪，不依赖 dump 触发）。</summary>
        private EntityQuery m_SubAreaOwnerQuery;

        /// <summary>
        /// 反查用：所有「有 Owner 且是 Surface/Space」的地皮。
        /// 修复前就残留的地皮已经不在任何建筑的 SubArea 缓冲里，
        /// 只能从地皮一侧反查它属于谁，再看主人缓冲里有没有它。
        /// </summary>
        private EntityQuery m_OwnedAreaQuery;
        private readonly System.Collections.Generic.Dictionary<Entity, bool> m_OursCache =
            new System.Collections.Generic.Dictionary<Entity, bool>();
        private PrefabSystem m_OurPrefabSystem;
        private readonly System.Collections.Generic.Dictionary<Entity, string> m_Dumped =
            new System.Collections.Generic.Dictionary<Entity, string>();
        private readonly System.Collections.Generic.Dictionary<Entity, System.Collections.Generic.List<Entity>> m_LastAreas =
            new System.Collections.Generic.Dictionary<Entity, System.Collections.Generic.List<Entity>>();

        // ---- 孤儿地皮清理（原版迁移换新 area 实体、旧的不回收 → 视觉残留）----
        // m_SweepAreas：每栋自有建筑「见过的所有地皮」并集（只增，删除后才移除）
        private readonly System.Collections.Generic.Dictionary<Entity, System.Collections.Generic.HashSet<Entity>> m_SweepAreas =
            new System.Collections.Generic.Dictionary<Entity, System.Collections.Generic.HashSet<Entity>>();
        private readonly System.Collections.Generic.Dictionary<Entity, int> m_OrphanPending =
            new System.Collections.Generic.Dictionary<Entity, int>();
        private int m_SweepFrame;
        private int m_SummaryTick;

        /// <summary>
        /// 结构变更必须走 barrier 的命令缓冲，不能在系统里直接 EntityManager.AddComponent。
        /// 依据 cs2-modding-guide「Sub-element removal」：直接改 ECS 会破坏帧内管线，
        /// `Deleted` 只是标记，由游戏清理系统在安全相位处理。（BetterBulldozer 同款做法）
        /// </summary>
        private Game.Common.ModificationEndBarrier m_EndBarrier;

        /// <summary>判决原因日志去重（每个 area 只打第一条）。</summary>
        private readonly System.Collections.Generic.HashSet<Entity> m_VerdictLogged =
            new System.Collections.Generic.HashSet<Entity>();

        private void VerdictOnce(Entity area, string reason)
        {
            if (m_VerdictLogged.Add(area))
            {
                Mod.log.Warn($"[SSP] orphan verdict {area.Index}:{area.Version}: {reason}");
            }
        }

        /// <summary>每 30 帧一扫：地皮连续两次不在建筑的 SubArea 缓冲里且实体仍存活 → Deleted。</summary>
        private void CleanupOrphans()
        {
            if (++m_SweepFrame < 60)
            {
                return;
            }

            m_SweepFrame = 0;

            if (Systems.GrowableVariantSystem.Catalog.Count == 0)
            {
                return; // 目录还没建好，无需扫描城市
            }

            SeedTrackedBuildings();
            SweepOwnedAreas();

            var keys = new System.Collections.Generic.List<Entity>(m_SweepAreas.Keys);
            for (int k = 0; k < keys.Count; k++)
            {
                var building = keys[k];
                if (!EntityManager.Exists(building))
                {
                    m_SweepAreas.Remove(building);
                    continue;
                }

                if (!EntityManager.HasBuffer<Game.Areas.SubArea>(building))
                {
                    continue;
                }

                var buffer = EntityManager.GetBuffer<Game.Areas.SubArea>(building, true);
                if (buffer.Length == 0)
                {
                    continue; // 地皮尚未生成（放置中间态）
                }

                var seen = m_SweepAreas[building];
                var current = new System.Collections.Generic.HashSet<Entity>();
                for (int i = 0; i < buffer.Length; i++)
                {
                    current.Add(buffer[i].m_Area);
                    seen.Add(buffer[i].m_Area); // 并集：新地皮入册
                }

                var check = new System.Collections.Generic.List<Entity>(seen);
                for (int i = 0; i < check.Count; i++)
                {
                    var area = check[i];
                    if (current.Contains(area))
                    {
                        m_OrphanPending.Remove(area);
                        continue;
                    }

                    if (!EntityManager.Exists(area))
                    {
                        m_OrphanPending.Remove(area);
                        seen.Remove(area);
                        continue;
                    }

                    // Owner 仍指向本建筑才算「这栋楼的残留」；被引擎改嫁的不碰
                    if (EntityManager.HasComponent<Owner>(area)
                        && !EntityManager.GetComponentData<Owner>(area).m_Owner.Equals(building))
                    {
                        VerdictOnce(area, $"owner moved to {EntityManager.GetComponentData<Owner>(area).m_Owner.Index}:{EntityManager.GetComponentData<Owner>(area).m_Owner.Version}");
                        seen.Remove(area);
                        continue;
                    }

                    m_OrphanPending.TryGetValue(area, out var misses);
                    misses++;
                    VerdictOnce(area, $"absent #{misses}, surface={EntityManager.HasComponent<Game.Areas.Surface>(area)}, space={EntityManager.HasComponent<Game.Areas.Space>(area)}");
                    if (misses < 2)
                    {
                        m_OrphanPending[area] = misses;
                        continue;
                    }

                    // 连续两轮缺席 → 判定孤儿：先挂倒计时（BetterBulldozer 做法），
                    // 由 HandleSSPDeleteInXFramesSystem 在对象稳定后经 barrier 真正 Deleted
                    m_OrphanPending.Remove(area);
                    seen.Remove(area);
                    if (!EntityManager.HasComponent<Deleted>(area)
                        && !EntityManager.HasComponent<Components.SSPDeleteInXFrames>(area))
                    {
                        try
                        {
                            var ecb = m_EndBarrier.CreateCommandBuffer();
                            ecb.AddComponent(area, new Components.SSPDeleteInXFrames { m_FramesRemaining = 5 });
                            Mod.log.Warn($"[SSP] queue orphan lot area {area.Index}:{area.Version} for deletion (building {building.Index}:{building.Version})");
                        }
                        catch (System.Exception)
                        {
                            // barrier 未就绪：下轮重试
                            seen.Add(area);
                        }
                    }
                }
            }
        }

        /// <summary>按查询把「自有且带地皮的建筑」登记进追踪表（载入存档也能覆盖）。</summary>
        private void SeedTrackedBuildings()
        {
            if (m_SubAreaOwnerQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            var entities = m_SubAreaOwnerQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    if (!IsOursBuilding(e))
                    {
                        continue;
                    }

                    var buffer = EntityManager.GetBuffer<Game.Areas.SubArea>(e, true);
                    if (buffer.Length == 0)
                    {
                        continue;
                    }

                    if (!m_SweepAreas.TryGetValue(e, out var set))
                    {
                        m_SweepAreas[e] = set = new System.Collections.Generic.HashSet<Entity>();
                    }

                    for (int k = 0; k < buffer.Length; k++)
                    {
                        set.Add(buffer[k].m_Area);
                    }

                    // 诊断（默认关闭）：实体状态变化时打实体树
                    if (Mod.DiagnosticsEnabled)
                    {
                    string sig = Signature(e);
                    if (!m_Dumped.TryGetValue(e, out var prevSig) || prevSig != sig)
                    {
                        m_Dumped[e] = sig;
                        DumpOne(e, PrefabNameOf(e), "SEED");
                    }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.log.Warn($"[SSP] seed tracked buildings failed: {ex.GetType().Name} {ex.Message}");
            }
            finally
            {
                entities.Dispose();
            }
        }

        /// <summary>实体挂的 prefab 名（诊断用）。</summary>
        private string PrefabNameOf(Entity e)
        {
            try
            {
                if (EntityManager.HasComponent<PrefabRef>(e)
                    && m_OurPrefabSystem.TryGetPrefab<PrefabBase>(
                        EntityManager.GetComponentData<PrefabRef>(e).m_Prefab, out var prefab))
                {
                    return prefab?.name ?? "?";
                }
            }
            catch
            {
                // 忽略
            }

            return "?";
        }

        /// <summary>该实体挂的 prefab 是否本模组的（结果按 prefab 缓存）。</summary>
        private bool IsOursBuilding(Entity e)
        {
            if (!EntityManager.HasComponent<PrefabRef>(e))
            {
                return false;
            }

            var prefabEntity = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            if (m_OursCache.TryGetValue(prefabEntity, out var known))
            {
                return known;
            }

            bool ours = false;
            try
            {
                if (m_OurPrefabSystem.TryGetPrefab<PrefabBase>(prefabEntity, out var prefab)
                    && prefab?.name != null
                    && prefab.name.StartsWith(DumpPrefix))
                {
                    ours = true;
                }
            }
            catch
            {
                // 解析失败按非自有处理
            }

            m_OursCache[prefabEntity] = ours;
            return ours;
        }

        /// <summary>
        /// 反查残留地皮：遍历「有 Owner 的 Surface/Space」，若主人是我们的建筑，
        /// 但主人的 SubArea 缓冲里已经不包含它 → 孤儿（含修复前就遗留的、任何追踪表里没有的）。
        /// 连续两轮确认后才排队删除。
        /// </summary>
        /// <summary>地皮质心（取 Game.Areas.Node 平均位置）。</summary>
        private bool TryAreaCentroid(Entity area, out Unity.Mathematics.float3 centroid)
        {
            centroid = default;
            try
            {
                if (!EntityManager.HasBuffer<Game.Areas.Node>(area))
                {
                    return false;
                }

                var nodes = EntityManager.GetBuffer<Game.Areas.Node>(area, true);
                if (nodes.Length == 0)
                {
                    return false;
                }

                Unity.Mathematics.float3 sum = default;
                for (int i = 0; i < nodes.Length; i++)
                {
                    sum += nodes[i].m_Position;
                }

                centroid = sum / nodes.Length;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判定「僵尸地皮」：仍在建筑 SubArea 缓冲里，但质心离建筑本体过远。
        /// 实测：原版迁移会把新地皮加进缓冲，却把旧地皮**留在缓冲里**（不是孤儿），
        /// 所以「不在缓冲里」的判据抓不到它们，必须按距离识别。
        /// </summary>
        private const float StaleDistance = 60f;

        private void SweepOwnedAreas()
        {
            if (m_OwnedAreaQuery.IsEmptyIgnoreFilter)
            {
                LogSweepSummary(0, 0, m_SweepAreas.Count, 0, 0, 0);
                return;
            }

            int scanned = 0;
            int oursAreas = 0;
            int orphans = 0;
            int listedFar = 0;
            int notListed = 0;

            var areas = m_OwnedAreaQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < areas.Length; i++)
                {
                    var area = areas[i];
                    scanned++;
                    if (!EntityManager.HasComponent<Owner>(area))
                    {
                        continue;
                    }

                    var owner = EntityManager.GetComponentData<Owner>(area).m_Owner;
                    if (!EntityManager.Exists(owner))
                    {
                        // 主人已被销毁 → 拆除后残留的地皮（实测：拆除时建筑的 SubArea 缓冲已空，
                        // 原版清理不会删它们，area 的 Owner 变成悬空引用）。排除地块/区域边框。
                        if (EntityManager.HasComponent<Game.Areas.Lot>(area)
                            || EntityManager.HasComponent<Game.Areas.District>(area)
                            || EntityManager.HasComponent<Game.Areas.CurrentDistrict>(area))
                        {
                            continue;
                        }

                        m_OrphanPending.TryGetValue(area, out var danglingMisses);
                        danglingMisses++;
                        if (danglingMisses < 2)
                        {
                            m_OrphanPending[area] = danglingMisses;
                            VerdictOnce(area, $"owner {owner.Index}:{owner.Version} is gone (#{danglingMisses})");
                            continue;
                        }

                        QueueDelete(area, Entity.Null);
                        orphans++;
                        continue;
                    }

                    if (!IsOursBuilding(owner))
                    {
                        continue;
                    }

                    // 预览（Temp）建筑的地皮不计入统计，避免放置预览污染计数
                    if (EntityManager.HasComponent<Game.Tools.Temp>(owner))
                    {
                        continue;
                    }

                    oursAreas++;

                    // 分区地块（Lot）不在 SubArea 缓冲里，只按距离判定：
                    // 离开建筑 >60m 的旧地块 = 迁移遗留。
                    if (EntityManager.HasComponent<Game.Areas.Lot>(area))
                    {
                        if (TryAreaCentroid(area, out var lotCenter)
                            && EntityManager.HasComponent<Game.Objects.Transform>(owner))
                        {
                            var lotOwnerPos = EntityManager.GetComponentData<Game.Objects.Transform>(owner).m_Position;
                            float lotDist = Unity.Mathematics.math.distance(lotCenter, lotOwnerPos);
                            if (lotDist > StaleDistance)
                            {
                                m_OrphanPending.TryGetValue(area, out var lotMisses);
                                lotMisses++;
                                if (lotMisses < 2)
                                {
                                    m_OrphanPending[area] = lotMisses;
                                    VerdictOnce(area, $"Lot far: d={lotDist:F1}m from owner {owner.Index}:{owner.Version} (#{lotMisses})");
                                }
                                else
                                {
                                    QueueDelete(area, owner);
                                    orphans++;
                                }
                            }
                        }

                        continue;
                    }

                    if (!EntityManager.HasBuffer<Game.Areas.SubArea>(owner))
                    {
                        continue;
                    }

                    var buffer = EntityManager.GetBuffer<Game.Areas.SubArea>(owner, true);
                    if (buffer.Length == 0)
                    {
                        continue; // 缓冲暂时为空（放置/迁移中间态）不要误判
                    }

                    bool listed = false;
                    for (int k = 0; k < buffer.Length; k++)
                    {
                        if (buffer[k].m_Area.Equals(area))
                        {
                            listed = true;
                            break;
                        }
                    }

                    if (listed)
                    {
                        m_OrphanPending.Remove(area);

                        // 僵尸判定：在缓冲里但离建筑太远 → 迁移遗留
                        if (EntityManager.HasComponent<Game.Objects.Transform>(owner)
                            && TryAreaCentroid(area, out var centroid))
                        {
                            var ownerPos = EntityManager.GetComponentData<Game.Objects.Transform>(owner).m_Position;
                            float dist = Unity.Mathematics.math.distance(centroid, ownerPos);
                            if (dist > StaleDistance)
                            {
                                listedFar++;
                                m_OrphanPending.TryGetValue(area, out var farMisses);
                                farMisses++;
                                if (farMisses < 2)
                                {
                                    m_OrphanPending[area] = farMisses;
                                    VerdictOnce(area, $"listed but far: d={dist:F1}m from owner {owner.Index}:{owner.Version} (#{farMisses})");
                                }
                                else
                                {
                                    QueueDelete(area, owner);
                                    orphans++;
                                }
                            }
                        }

                        continue;
                    }

                    notListed++;
                    m_OrphanPending.TryGetValue(area, out var misses);
                    misses++;
                    if (misses < 2)
                    {
                        m_OrphanPending[area] = misses;
                        VerdictOnce(area, $"owned by {owner.Index}:{owner.Version} but NOT in its SubArea buffer (#{misses})");
                        continue;
                    }

                    QueueDelete(area, owner);
                    orphans++;
                }
            }
            catch (System.Exception ex)
            {
                Mod.log.Warn($"[SSP] sweep owned areas failed: {ex.GetType().Name} {ex.Message}");
            }
            finally
            {
                areas.Dispose();
            }

            LogSweepSummary(scanned, oursAreas, m_SweepAreas.Count, orphans, listedFar, notListed);
        }

        /// <summary>每 ~10 秒一条扫描摘要，方便定位「扫描到底跑没跑/命中没命中」。</summary>
        private void LogSweepSummary(int scanned, int oursAreas, int trackedBuildings, int orphans, int listedFar, int notListed)
        {
            if (!Mod.DiagnosticsEnabled || ++m_SummaryTick < 20)
            {
                return;
            }

            m_SummaryTick = 0;
            Mod.log.Warn($"[SSP] sweep: scannedAreas={scanned}, oursAreas={oursAreas}, trackedBuildings={trackedBuildings}, orphansQueued={orphans}, listedFar={listedFar}, notListed={notListed}, pending={m_OrphanPending.Count}");
        }

        /// <summary>排队删除（BetterBulldozer 的 DeleteInXFrames 模式）。owner 可为 Entity.Null（悬空）。</summary>
        private void QueueDelete(Entity area, Entity owner)
        {
            m_OrphanPending.Remove(area);
            if (!owner.Equals(Entity.Null) && m_SweepAreas.TryGetValue(owner, out var set))
            {
                set.Remove(area);
            }

            if (EntityManager.HasComponent<Deleted>(area)
                || EntityManager.HasComponent<Components.SSPDeleteInXFrames>(area))
            {
                return;
            }

            try
            {
                var ecb = m_EndBarrier.CreateCommandBuffer();
                ecb.AddComponent(area, new Components.SSPDeleteInXFrames { m_FramesRemaining = 5 });
                Mod.log.Warn($"[SSP] queue orphan lot area {area.Index}:{area.Version} for deletion (owner {owner.Index}:{owner.Version}{(owner.Equals(Entity.Null) ? ", dangling" : string.Empty)})");
            }
            catch (System.Exception)
            {
                // barrier 未就绪：下轮重试
                m_OrphanPending[area] = 1;
            }
        }

        private void InitDiagnostics()
        {            m_OurPrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_EndBarrier = World.GetOrCreateSystemManaged<Game.Common.ModificationEndBarrier>();
            m_UpdatedQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Updated>(),
                    ComponentType.ReadOnly<PrefabRef>()
                }
            });
            m_DeletedQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<PrefabRef>()
                }
            });
            // 关键：载入存档时既没有 dump、也没有 Updated 事件，必须靠查询登记
            m_SubAreaOwnerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.SubArea>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Tools.Temp>()
                }
            });
            // 反查：地皮一侧（Owner + Surface/Space），能发现「修复前就已成孤儿」的残留
            m_OwnedAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Owner>()
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<Game.Areas.Space>(),
                    // 分区地块：迁移后旧地块不会跟着走，也不在 SubArea 缓冲里，
                    // 只能按「离建筑过远」判定为遗留。
                    ComponentType.ReadOnly<Game.Areas.Lot>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Components.SSPDeleteInXFrames>()
                }
            });
        }

        private void DumpObjectTrees()
        {
            DumpQuery(m_UpdatedQuery, "LIVE");
            DumpQuery(m_DeletedQuery, "DELETED");
        }

        private void DumpQuery(EntityQuery query, string tag)
        {
            if (query.IsEmptyIgnoreFilter)
            {
                return;
            }

            var entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                // Updated 每帧都很多，超过阈值就跳过，避免刷日志
                if (entities.Length > 400)
                {
                    return;
                }

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    if (!EntityManager.HasComponent<PrefabRef>(e))
                    {
                        continue;
                    }

                    // 预览幽灵/Temp 实体每帧重建，会刷爆日志；只看落地后的实体
                    if (EntityManager.HasComponent<Game.Tools.Temp>(e))
                    {
                        continue;
                    }

                    var prefabEntity = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!m_OurPrefabSystem.TryGetPrefab<PrefabBase>(prefabEntity, out var prefab)
                        || prefab == null
                        || prefab.name == null
                        || !prefab.name.StartsWith(DumpPrefix))
                    {
                        continue;
                    }

                    // 同一个实体在「迁移」时会被原地修改（TempFlags.Modify），地皮集合会变，
                    // 所以按「实体 + 地皮指纹」去重，指纹变了就再打一次。
                    string signature = tag + Signature(e);
                    if (m_Dumped.TryGetValue(e, out var previous) && previous == signature)
                    {
                        continue;
                    }

                    m_Dumped[e] = signature;
                    DumpOne(e, prefab.name, tag);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        /// <summary>地皮指纹：位置 + 子地皮实体列表，用于判断「同一实体是否发生了变化」。</summary>
        private string Signature(Entity e)
        {
            var sb = new System.Text.StringBuilder(128);
            if (EntityManager.HasComponent<Game.Objects.Transform>(e))
            {
                var t = EntityManager.GetComponentData<Game.Objects.Transform>(e);
                sb.Append($"@{t.m_Position.x:F1},{t.m_Position.z:F1}");
            }

            if (EntityManager.HasBuffer<Game.Areas.SubArea>(e))
            {
                var areas = EntityManager.GetBuffer<Game.Areas.SubArea>(e, true);
                sb.Append($"#a{areas.Length}:");
                for (int i = 0; i < areas.Length; i++)
                {
                    sb.Append(areas[i].m_Area.Index).Append(',');
                }
            }

            return sb.ToString();
        }

        private void DumpOne(Entity root, string prefabName, string tag)
        {
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                sb.Append($"[SSP] DUMP({tag}) {prefabName} entity={root.Index}:{root.Version}");
                sb.Append($", Building={EntityManager.HasComponent<Game.Buildings.Building>(root)}");
                sb.Append($", AssetStampData={EntityManager.HasComponent<AssetStampData>(root)}");
                sb.Append($", Owner={(EntityManager.HasComponent<Owner>(root) ? EntityManager.GetComponentData<Owner>(root).m_Owner.ToString() : "-")}");
                if (EntityManager.HasComponent<Game.Objects.Transform>(root))
                {
                    var t = EntityManager.GetComponentData<Game.Objects.Transform>(root);
                    sb.Append($", pos={t.m_Position.x:F1},{t.m_Position.y:F1},{t.m_Position.z:F1}");
                }

                // 自身的地皮
                var currentAreaIds = new System.Collections.Generic.List<Entity>();
                if (EntityManager.HasBuffer<Game.Areas.SubArea>(root))
                {
                    var areas = EntityManager.GetBuffer<Game.Areas.SubArea>(root, true);
                    sb.Append($", SubAreas({areas.Length})");
                    for (int i = 0; i < areas.Length; i++)
                    {
                        var a = areas[i].m_Area;
                        currentAreaIds.Add(a);
                        sb.Append($"\n    area[{i}] {a.Index}:{a.Version}");
                        if (!EntityManager.Exists(a))
                        {
                            sb.Append(" (missing)");
                            continue;
                        }

                        sb.Append($", nodes={EntityManager.GetBuffer<Game.Areas.Node>(a, true).Length}");
                        sb.Append($", owner={(EntityManager.HasComponent<Owner>(a) ? EntityManager.GetComponentData<Owner>(a).m_Owner.ToString() : "-")}");
                        sb.Append($", Surface={EntityManager.HasComponent<Game.Areas.Surface>(a)}");
                        sb.Append($", Space={EntityManager.HasComponent<Game.Areas.Space>(a)}");
                        sb.Append($", Deleted={EntityManager.HasComponent<Deleted>(a)}");
                        sb.Append($", Lot={EntityManager.HasComponent<Game.Areas.Lot>(a)}");
                        if (EntityManager.HasComponent<Game.Objects.Transform>(root) && TryAreaCentroid(a, out var c))
                        {
                            var p = EntityManager.GetComponentData<Game.Objects.Transform>(root).m_Position;
                            sb.Append($", d={Unity.Mathematics.math.distance(c, p):F1}m");
                        }
                    }
                }
                else
                {
                    sb.Append(", SubAreas(none)");
                }

                // 与上一轮对比：旧地皮实体是否还活着（残留判定）
                if (m_LastAreas.TryGetValue(root, out var lastAreas))
                {
                    int goneCount = 0;
                    int aliveCount = 0;
                    int listedCount = 0;
                    for (int i = 0; i < lastAreas.Count; i++)
                    {
                        var old = lastAreas[i];
                        if (currentAreaIds.Contains(old))
                        {
                            continue;
                        }

                        bool alive = EntityManager.Exists(old);
                        if (alive)
                        {
                            aliveCount++;
                        }
                        else
                        {
                            goneCount++;
                        }

                        // 旧地皮若仍登记在缓冲里，说明引擎没把它移出缓冲（只能靠距离规则）
                        if (alive && EntityManager.HasBuffer<Game.Areas.SubArea>(root))
                        {
                            var b = EntityManager.GetBuffer<Game.Areas.SubArea>(root, true);
                            for (int k = 0; k < b.Length; k++)
                            {
                                if (b[k].m_Area.Equals(old))
                                {
                                    listedCount++;
                                    break;
                                }
                            }
                        }

                        sb.Append($"\n    orphan? oldArea={old.Index}:{old.Version} alive={alive}");
                    }

                    if (goneCount + aliveCount > 0)
                    {
                        sb.Append($"\n    [SSP] relocate check: entity={root.Index}:{root.Version}, oldAreas={goneCount + aliveCount}, gone={goneCount}, stillAlive={aliveCount}, listedInBuffer={listedCount}");
                        if (aliveCount > 0 && listedCount == 0)
                        {
                            sb.Append(" -> 旧地皮已变孤儿，由扫描/排队删除接手");
                        }
                        else if (listedCount > 0)
                        {
                            sb.Append(" -> 旧地皮仍在缓冲里，由距离规则清理");
                        }
                        else
                        {
                            sb.Append(" -> 引擎已清理干净");
                        }
                    }
                }

                m_LastAreas[root] = currentAreaIds;

                // 并入清理追踪集（迁移 dump 触发时新地皮即时入册，旧地皮由扫描判定）
                if (!m_SweepAreas.TryGetValue(root, out var seenSet))
                {
                    m_SweepAreas[root] = seenSet = new System.Collections.Generic.HashSet<Entity>();
                }

                for (int i = 0; i < currentAreaIds.Count; i++)
                {
                    seenSet.Add(currentAreaIds[i]);
                }

                // 子物体（建筑的真正载体）
                if (EntityManager.HasBuffer<Game.Objects.SubObject>(root))
                {
                    var subs = EntityManager.GetBuffer<Game.Objects.SubObject>(root, true);
                    sb.Append($", SubObjects({subs.Length})");
                    for (int i = 0; i < subs.Length && i < 8; i++)
                    {
                        var s = subs[i].m_SubObject;
                        if (!EntityManager.Exists(s))
                        {
                            continue;
                        }

                        string subName = "?";
                        if (EntityManager.HasComponent<PrefabRef>(s)
                            && m_OurPrefabSystem.TryGetPrefab<PrefabBase>(
                                EntityManager.GetComponentData<PrefabRef>(s).m_Prefab, out var subPrefab))
                        {
                            subName = subPrefab?.name ?? "?";
                        }

                        sb.Append($"\n    sub[{i}] {subName} {s.Index}:{s.Version}");
                        sb.Append($", SubAreas={(EntityManager.HasBuffer<Game.Areas.SubArea>(s) ? EntityManager.GetBuffer<Game.Areas.SubArea>(s, true).Length : 0)}");
                        sb.Append($", Building={EntityManager.HasComponent<Game.Buildings.Building>(s)}");
                        if (EntityManager.HasComponent<Game.Objects.Transform>(s))
                        {
                            var t2 = EntityManager.GetComponentData<Game.Objects.Transform>(s);
                            sb.Append($", pos={t2.m_Position.x:F1},{t2.m_Position.y:F1},{t2.m_Position.z:F1}");
                        }
                    }
                }

                Mod.log.Warn(sb.ToString());
            }
            catch (System.Exception e)
            {
                Mod.log.Warn($"[SSP] DUMP failed for {prefabName}: {e.GetType().Name} {e.Message}");
            }
        }
    }
}
