using Colossal.Logging;
using Game.Prefabs;
using Game.Tools;

namespace StaticSceneryPloppables.Tools
{
    /// <summary>
    /// 造景放置工具：直接把「剥离版建筑 prefab」放下去（Create 模式）。
    ///
    /// 为什么不再用图章（2026-11-13 结论，依据 Plop the Growables README + Find It 源码）：
    /// - CS2 允许直接放置建筑 prefab（Find It 就是 `ToolSystem.ActivatePrefabTool(prefab)` 一行）；
    /// - 图章的 stamping 分支只创建 SubObjects，**不会**按建筑声明建地皮，只能靠在图章上再复制一份
    ///   `ObjectSubAreas` 声明去补 → 双份声明、拆除/迁移时地皮归属错乱；
    /// - 直接放置走标准 Create 管线：`ObjectInitializeSystem` 按 prefab 声明登记地皮槽位，
    ///   随后 `CreateAreas` 建实体，归属/缓冲/清理全部由引擎维护，与迁移同源。
    /// </summary>
    public partial class SceneryPlacementTool : ObjectToolSystem
    {
        public static ILog log = LogManager.GetLogger($"{nameof(StaticSceneryPloppables)}.{nameof(SceneryPlacementTool)}")
            .SetShowsErrorsInUI(false);

        /// <summary>UI 轮询的待处理事件（"closePanel"）；由 UISystem 消费后清空。</summary>
        public static string PendingUIEvent = string.Empty;

        /// <summary>本工具是否仍处于「已激活且在放置」状态（UI 侧据此判断 Esc 是否已取消工具）。</summary>
        public bool IsPlacementActive => Enabled && m_ToolSystem != null && m_ToolSystem.activeTool == this;

        /// <summary>激活放置（直接放剥离建筑，Create 模式）。</summary>
        public void ActivateTool(BuildingPrefab buildingPrefab)
        {
            Enabled = true;
            if (buildingPrefab != null && TrySetPrefab(buildingPrefab))
            {
                base.mode = Mode.Create; // 标准创建管线（与 Find It / 原生放置一致）
                m_ToolSystem.activeTool = this;
                log.Warn($"[SSP] tool activated for {buildingPrefab.name} (Mode.Create)");
            }
            else
            {
                log.Warn($"[SSP] TrySetPrefab failed for {buildingPrefab?.name ?? "<null>"}");
                Enabled = false;
            }
        }

        public void DeactivateTool()
        {
            Enabled = false;
            if (m_ToolSystem != null && m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
            log.Info("SceneryPlacementTool created");
        }
    }
}
