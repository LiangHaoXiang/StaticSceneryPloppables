import { ModRegistrar } from "cs2/modding";
import { SSPToolbarIcon } from "mods/ssp-panel";

// console.error 转发到 Player.log：留下本模块执行过的痕迹（UI 手册坑5）
console.error("[SSP] UI module executed");

const register: ModRegistrar = (moduleRegistry) => {
    // 工具栏入口（Find It 同款位置：包装右下角工具栏的 PhotoModeToggle，按钮排在它前面）
    moduleRegistry.extend("game-ui/game/components/toolbar/top/toggles.tsx", "PhotoModeToggle", SSPToolbarIcon);
    console.error("[SSP] toolbar icon registered (extend PhotoModeToggle)");
};

export default register;
