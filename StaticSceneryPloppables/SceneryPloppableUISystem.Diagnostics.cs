using Colossal.Logging;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace StaticSceneryPloppables
{
    /// <summary>
    /// 只读诊断：把本模组实体的「实体树」打进日志（默认关闭，仅排障时把
    /// <see cref="Mod.DiagnosticsEnabled"/> 改 true 后重新构建）。
    ///
    /// 历史注记：1.6.2f1 反编译确认地皮 area 由 ReplacePrefabSystem.CreateAreas 以
    /// m_Owner = 正在创建的对象 生成；旧版「迁移后旧地皮残留」源于放置与迁移走了两套
    /// 地皮生成逻辑，现两者已同源（都走引擎 Create 管线），因此残留扫描逻辑已整体移除。
    /// </summary>
    public partial class SceneryPloppableUISystem
    {
        private const string DumpPrefix = "SSP_";

        private EntityQuery m_UpdatedQuery;
        private EntityQuery m_DeletedQuery;

        private readonly System.Collections.Generic.Dictionary<Entity, bool> m_OursCache =
            new System.Collections.Generic.Dictionary<Entity, bool>();
        private PrefabSystem m_OurPrefabSystem;
        private readonly System.Collections.Generic.Dictionary<Entity, string> m_Dumped =
            new System.Collections.Generic.Dictionary<Entity, string>();
        private readonly System.Collections.Generic.Dictionary<Entity, System.Collections.Generic.List<Entity>> m_LastAreas =
            new System.Collections.Generic.Dictionary<Entity, System.Collections.Generic.List<Entity>>();

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
        /// <summary>初始化诊断用的查询与依赖系统（只服务实体树 dump）。</summary>
        private void InitDiagnostics()
        {
            m_OurPrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
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
