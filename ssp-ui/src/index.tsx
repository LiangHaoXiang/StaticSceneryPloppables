import { ModRegistrar } from "cs2/modding";
import { SSPToolbarIcon } from "mods/ssp-panel";
import { ExtendSelectedInfoSections } from "mods/ssp-info-section";

// Gameface 只把 console.error 转发到 Player.log，因此排障日志沿用 error 级别，
// 但只保留 1 条并明确标注「非错误」，避免日志里出现容易误判的 [UI] [ERROR]。
console.error("[SSP] info: UI module loaded (not an error)");

const register: ModRegistrar = (moduleRegistry) => {
    // 工具栏入口（Find It 同款位置：包装右下角工具栏的 PhotoModeToggle，按钮排在它前面）
    moduleRegistry.extend("game-ui/game/components/toolbar/top/toggles.tsx", "PhotoModeToggle", SSPToolbarIcon);
    // 信息面板「用户创作内容」卡：向原版 section 渲染器注册表登记我们的渲染器（#35）。
    // 注：该导出是「注册表对象」而非组件，extend 回调收到的是原注册表、返回修改后的注册表
    //（InfoLoom 同款运行时行为）；类型声明只覆盖组件包装，故 as any。
    moduleRegistry.extend(
        "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx",
        "selectedInfoSectionComponents",
        ExtendSelectedInfoSections as any
    );
};

export default register;