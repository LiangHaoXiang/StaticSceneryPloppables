using System.Text;
using Colossal.UI.Binding;
using Game;
using Game.SceneFlow;
using Game.UI;
using StaticSceneryPloppables.Systems;
using StaticSceneryPloppables.Tools;

namespace StaticSceneryPloppables
{
    /// <summary>
    /// Gameface 桥：**一次性推送整份目录**（ssp.catalog）+ 选择放置（ssp.select）+ 工具取消事件（ssp.uievent）。
    ///
    /// 设计变更（2026-11-14，优化 #9/#11）：
    /// 旧实现是「分页 + C# 侧搜索」，前端打开面板后要拉 18 页，每页都重新全表扫描一次并构造 JSON；
    /// 现在目录只推送一次（约 250 KB），搜索/筛选/排序全部在前端完成——打字不再触发任何 C# 计算。
    /// 绑定协议：GetterValueBinding 推送事件名为 &lt;name&gt;.update，JS 需先 trigger &lt;name&gt;.subscribe。
    /// </summary>
    public partial class SceneryPloppableUISystem : UISystemBase
    {
        private GetterValueBinding<string> m_CatalogBinding;
        private GetterValueBinding<string> m_EventBinding;
        private bool m_CatalogPushed;
        private string m_PushedEvent = string.Empty;
        private bool m_PanelVisible;
        private SceneryPlacementTool m_Tool;
        private bool m_ToolWasActive;

        protected override void OnCreate()
        {
            base.OnCreate();
#if SSP_DIAG
            InitDiagnostics();
#endif

            m_CatalogBinding = new GetterValueBinding<string>("ssp", "catalog", BuildCatalogJson);
            AddBinding(m_CatalogBinding);
            m_EventBinding = new GetterValueBinding<string>("ssp", "uievent", () => Tools.SceneryPlacementTool.PendingUIEvent);
            AddBinding(m_EventBinding);
            // 注：语言由游戏本地化字典提供（LocaleSSP + AddSource），筛选/排序由前端完成，
            // 因此不再需要 ssp.lang / ssp.setFilter / ssp.search / ssp.resetOffset。
            AddBinding(new TriggerBinding<int>("ssp", "select", Select));
            // 面板挂载/卸载上报：面板关闭时不再推送 uievent（否则 UI 侧没有 handler，会刷 WARN）
            AddBinding(new TriggerBinding<bool>("ssp", "panelVisible", visible => { m_PanelVisible = visible; }));
            AddBinding(new TriggerBinding("ssp", "closeTool", () =>
            {
                World.GetExistingSystemManaged<SceneryPlacementTool>()?.DeactivateTool();
            }));
        }

        protected override void OnUpdate()
        {
            // 目录构建完成 → 一次性推送（此前前端订阅会拿到 complete=false 并自行重试）
            if (!m_CatalogPushed && GrowableVariantSystem.BuildComplete)
            {
                m_CatalogPushed = true;
                m_CatalogBinding.Update();
                Mod.log.Warn($"[SSP] catalog pushed to UI: {GrowableVariantSystem.Catalog.Count} entries");
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

            // C# 侧事件 → 推给 UI（面板据此关闭）
            // 只在「有事件 且 面板可见」时推送：空字符串也会触发一次 Update，面板已关闭时就是
            // Player.log 里那条 "ssp.uievent.update ... no handler" 告警的来源。
            string pending = Tools.SceneryPlacementTool.PendingUIEvent;
            if (pending.Length == 0)
            {
                m_PushedEvent = string.Empty; // 事件已消费，允许下次同类事件再次触发
            }
            else if (!m_PanelVisible)
            {
                // 面板已关闭：没有订阅者，直接丢弃
                Tools.SceneryPlacementTool.PendingUIEvent = string.Empty;
                m_PushedEvent = string.Empty;
            }
            else if (pending != m_PushedEvent)
            {
                m_PushedEvent = pending;
                Tools.SceneryPlacementTool.PendingUIEvent = string.Empty;
                m_EventBinding.Update();
            }
            else
            {
                Tools.SceneryPlacementTool.PendingUIEvent = string.Empty;
            }

#if SSP_DIAG
            if (Mod.DiagnosticsEnabled)
            {
                DumpObjectTrees(); // 诊断：实体树 dump，默认关闭
            }
#endif
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

        /// <summary>整份目录序列化（构建完成后只调用一次；字段已在构建期转义）。</summary>
        private string BuildCatalogJson()
        {
            var catalog = GrowableVariantSystem.Catalog;
            var sb = new StringBuilder(256 * 1024);
            sb.Append("{\"total\":").Append(catalog.Count)
              .Append(",\"complete\":").Append(GrowableVariantSystem.BuildComplete ? "true" : "false")
              .Append(",\"items\":[");

            for (int i = 0; i < catalog.Count; i++)
            {
                var e = catalog[i];
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"i\":").Append(i)
                  .Append(",\"n\":\"").Append(e.NameJson).Append('"')
                  .Append(",\"rn\":\"").Append(e.RawNameJson).Append('"')
                  .Append(",\"ic\":\"").Append(e.IconJson).Append('"')
                  .Append(",\"si\":").Append(e.NameHasInfo ? "false" : "true")
                  .Append(",\"z\":\"").Append(e.Zone).Append('"')
                  .Append(",\"d\":\"").Append(e.Density).Append('"')
                  .Append(",\"l\":").Append(e.Level)
                  .Append(",\"w\":").Append(e.LotW)
                  .Append(",\"dp\":").Append(e.LotD)
                  .Append('}');
            }

            sb.Append("]}");
            return sb.ToString();
        }
    }
}
