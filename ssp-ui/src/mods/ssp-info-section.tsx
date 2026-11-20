/**
 * 信息面板「用户创作内容」卡（#35）：把原生 PdxModsSection 渲染器（带内容框的原生卡）
 * 按类型键映射到我们的 SspModInfoSection —— C# 侧只写 modId=167410，
 * 卡片的标题/名称/作者/版本/订阅数全部由原生渲染器按 modId 从平台实时取。
 * 注册键 = C# section 类全名（InfoSectionBase.Write 序列化的 $type）。
 */
export const SSP_INFO_SECTION_TYPE = "StaticSceneryPloppables.SspModInfoSection";
const NATIVE_PDX_TYPE = "Game.UI.InGame.PdxModsSection";

export const ExtendSelectedInfoSections = (registry: Record<string, any>) => {
    try {
        const nativePdx = registry?.[NATIVE_PDX_TYPE];
        if (nativePdx) {
            registry[SSP_INFO_SECTION_TYPE] = nativePdx;
        } else {
            // 原生渲染器缺失（游戏更新改名等）：不注册，section 静默不渲染，不影响信息面板其它部分
            console.error("[SSP] info: native PdxMods section renderer not found, card disabled (not an error)");
        }
    } catch (e) {
        console.error("[SSP] info section registry extend failed (not an error): " + e);
    }
    return registry;
};
