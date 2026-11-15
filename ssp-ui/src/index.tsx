import { ModRegistrar } from "cs2/modding";
import { SSPToolbarIcon } from "mods/ssp-panel";

// Gameface 只把 console.error 转发到 Player.log，因此排障日志沿用 error 级别，
// 但只保留 1 条并明确标注「非错误」，避免日志里出现容易误判的 [UI] [ERROR]。
console.error("[SSP] info: UI module loaded (not an error)");

const register: ModRegistrar = (moduleRegistry) => {
    // 工具栏入口（Find It 同款位置：包装右下角工具栏的 PhotoModeToggle，按钮排在它前面）
    moduleRegistry.extend("game-ui/game/components/toolbar/top/toggles.tsx", "PhotoModeToggle", SSPToolbarIcon);
};

export default register;