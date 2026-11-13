using Unity.Entities;

namespace StaticSceneryPloppables.Components
{
    /// <summary>
    /// 延迟删除计数（照抄 BetterBulldozer 的 <c>DeleteInXFrames</c>）。
    ///
    /// 为什么需要它：直接给地皮 area 加 <c>Deleted</c> 在多帧刷新/重建期间会被吞掉
    /// （BetterBulldozer 实证：还要在 owner 仍带 <c>Updated</c> 时不断重置倒计时，
    /// 等对象稳定后再删）。所以先挂这个倒计时，由
    /// <see cref="StaticSceneryPloppables.Systems.HandleSSPDeleteInXFramesSystem"/> 递减，
    /// 归零后通过 barrier 命令缓冲加 <c>Deleted</c>。
    /// </summary>
    public struct SSPDeleteInXFrames : IComponentData
    {
        public int m_FramesRemaining;

        /// <summary>因 owner 仍在刷新而重置的次数（上限保护：避免永不删除）。</summary>
        public int m_Resets;
    }
}
