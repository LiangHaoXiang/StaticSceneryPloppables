using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace StaticSceneryPloppables
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(StaticSceneryPloppables)}.{nameof(Mod)}")
            .SetShowsErrorsInUI(false);

        /// <summary>
        /// 详细诊断（实体树 dump、每轮扫描摘要、逐块地皮距离）开关。
        /// 默认关闭：这些日志每轮都要拼字符串并写盘，会明显拖慢帧率。
        /// 排查问题时临时改为 true 再编译。
        /// </summary>
        public static bool DiagnosticsEnabled = false;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            // 全量图章工厂 + 自有放置工具（ctrlC 同款注册相位）
            updateSystem.UpdateAt<Systems.GrowableVariantSystem>(SystemUpdatePhase.Modification4B);
            updateSystem.UpdateAt<Tools.SceneryPlacementTool>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<SceneryPloppableUISystem>(SystemUpdatePhase.UIUpdate);
            // 孤儿地皮延迟删除（BetterBulldozer 同款 DeleteInXFrames 模式）
            updateSystem.UpdateAt<Systems.HandleSSPDeleteInXFramesSystem>(SystemUpdatePhase.Modification4B);
            // 注：放置阶段的地皮由「直接放置 + 引擎 Create 管线」创建；若个别建筑仍空地皮，
            // 由 SceneryPloppableUISystem 的低频扫描补建（不再用每帧遍历全城的独立系统）。

            // 本地化：注册自己的词条（多语言），游戏切语言时字典随之切换
            try
            {
                var lm = GameManager.instance?.localizationManager;
                if (lm != null)
                {
                    var langs = (Localization.LocaleSSP.Lang[])System.Enum.GetValues(typeof(Localization.LocaleSSP.Lang));
                    var known = new System.Collections.Generic.HashSet<string>();
                    foreach (var lang in langs)
                    {
                        var source = new Localization.LocaleSSP(lang);
                        foreach (var id in Localization.LocaleSSP.LocaleIds(lang))
                        {
                            if (TryAddSource(lm, id, source))
                            {
                                known.Add(id.ToLowerInvariant());
                            }
                        }
                    }

                    // 当前语言若不在覆盖范围内 → 注册英文兜底，避免出现空白 key
                    try
                    {
                        var active = lm.activeLocaleId;
                        if (!string.IsNullOrEmpty(active) && !known.Contains(active.ToLowerInvariant()))
                        {
                            TryAddSource(lm, active, new Localization.LocaleSSP(Localization.LocaleSSP.Lang.EN));
                            log.Warn($"locale {active} not covered, using English fallback");
                        }
                    }
                    catch (System.Exception e)
                    {
                        log.Warn($"locale fallback check failed: {e.Message}");
                    }
                }
            }
            catch (System.Exception e)
            {
                log.Warn($"locale registration failed: {e.Message}");
            }

            // TODO M4: 发布打磨（首启性能/存档安全回归/PDX 发布）
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
        }

        private static bool TryAddSource(Colossal.Localization.LocalizationManager lm, string localeId, Colossal.IDictionarySource source)
        {
            try
            {
                lm.AddSource(localeId, source);
                return true;
            }
            catch (System.Exception)
            {
                // 某些语言本就未安装，忽略
                return false;
            }
        }
    }
}
