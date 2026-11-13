using Colossal.Logging;
using Game;
using Game.Common;
using Game.Tools;
using StaticSceneryPloppables.Components;
using Unity.Collections;
using Unity.Entities;

namespace StaticSceneryPloppables.Systems
{
    /// <summary>
    /// 递减 <see cref="SSPDeleteInXFrames"/>，归零后经 <c>ModificationEndBarrier</c> 加 <c>Deleted</c>。
    /// 逻辑照抄 BetterBulldozer 的 HandleDeleteInXFramesSystem（源验证过的做法）：
    /// - owner 仍带 <c>Updated</c>（对象还在刷新/重建）→ 重置倒计时，等它稳定；
    /// - 否则递减，归零才 marker 删除；
    /// - 全程走命令缓冲，不在系统里直接改 EntityManager。
    /// </summary>
    public partial class HandleSSPDeleteInXFramesSystem : GameSystemBase
    {
        public static ILog log = LogManager.GetLogger($"{nameof(StaticSceneryPloppables)}.{nameof(HandleSSPDeleteInXFramesSystem)}")
            .SetShowsErrorsInUI(false);

        private const int ResetFrames = 30;
        private const int MaxResets = 3; // 上限：owner 长期 Updated 时也最终删除

        private EntityQuery m_Query;
        private ModificationEndBarrier m_Barrier;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Barrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<SSPDeleteInXFrames>(),
                    ComponentType.ReadOnly<Owner>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>()
                }
            });
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            var entities = m_Query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0)
            {
                entities.Dispose();
                return;
            }

            var ecb = m_Barrier.CreateCommandBuffer();
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                var data = EntityManager.GetComponentData<SSPDeleteInXFrames>(e);

                if (EntityManager.Exists(owner) && EntityManager.HasComponent<Updated>(owner) && data.m_Resets < MaxResets)
                {
                    // 对象还在被刷新 → 推迟，等稳定（最多推迟 MaxResets 次）
                    data.m_FramesRemaining = ResetFrames;
                    data.m_Resets++;
                    ecb.SetComponent(e, data);
                    continue;
                }

                if (data.m_FramesRemaining <= 0)
                {
                    ecb.AddComponent<Deleted>(e);
                    log.Warn($"[SSP] deleted orphan area {e.Index}:{e.Version} (owner {owner.Index}:{owner.Version})");
                }
                else
                {
                    data.m_FramesRemaining--;
                    ecb.SetComponent(e, data);
                }
            }

            entities.Dispose();
        }
    }
}
