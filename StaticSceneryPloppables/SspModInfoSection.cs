using Colossal.Entities;
using Colossal.UI.Binding;
using Game.Prefabs;
using Game.UI.InGame;
using UnityEngine.Scripting;

namespace StaticSceneryPloppables
{
    /// <summary>
    /// 信息面板「用户创作内容」文本卡（#35）：选中 SSP 剥离建筑时，在原生信息面板中
    /// 显示模组名称/作者/模组ID/版本四行静态信息。
    /// 注册：Mod.cs 中 AddMiddleSection 挂入 SelectedInfoUISystem 的中区 section 列表
    /// （PdxModsSection 同位），并 UpdateAt(UIUpdate) 驱动 OnUpdate 刷新可见性；
    /// 前端在 selectedInfoSectionComponents 注册表按本类全名注册渲染器
    /// （见 ssp-ui/src/mods/ssp-info-section.tsx，InfoLoom 同款机制）。
    /// </summary>
    public partial class SspModInfoSection : InfoSectionBase
    {
        protected override string group => "SspModInfo";

        [Preserve]
        protected override void OnUpdate()
        {
            // 选中实体的 prefab 是 SSP 剥离建筑（SSP_S_ 前缀）时才显示
            visible = m_PrefabSystem.TryGetPrefab<PrefabBase>(selectedPrefab, out var prefab)
                && prefab.name != null
                && prefab.name.StartsWith(Systems.GrowableVariantSystem.StrippedPrefix);
        }

        protected override void Reset()
        {
        }

        protected override void OnProcess()
        {
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
            // 原生 PdxMods 渲染器只需 modId：卡片全部信息（名称/作者/版本/订阅数）由其按 modId 实时取
            writer.PropertyName("modId");
            writer.Write(Mod.ModIdString);
        }

        [Preserve]
        public SspModInfoSection()
        {
        }
    }
}
