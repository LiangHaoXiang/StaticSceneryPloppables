using System.Collections.Generic;
using Colossal;
using Colossal.Logging;
using Game.Prefabs;

namespace StaticSceneryPloppables.Localization
{
    /// <summary>
    /// 给「剥离版建筑」补上 <c>Assets.NAME[...]</c> 词条。
    ///
    /// 背景：游戏的信息面板/悬停提示按 <c>"Assets.NAME[" + prefab.name + "]"</c> 查字典，
    /// 查不到就把 key 原样显示（截图里因此出现 <c>Assets.NAME[SSP_S_EU_Commercial...]</c>）。
    /// 我们的 prefab 名字是 <c>SSP_S_&lt;原版名&gt;</c>，原版没有对应词条，
    /// 所以在目录构建完成后，把「剥离版名 → 已本地化的原版建筑名」注册进当前语言的字典。
    ///
    /// 说明：词条在目录构建完成后一次性写入，语言切换后若要跟随需要重新进图（面板自身文案不受影响，
    /// 面板走的是 LocaleSSP 的独立词条）。
    /// </summary>
    public static class PrefabNamePatcher
    {
        private static ILog Log => Mod.log;

        /// <summary>把映射写入当前语言字典并触发生效。</summary>
        public static void Apply(Dictionary<string, string> strippedToDisplayName)
        {
            if (strippedToDisplayName == null || strippedToDisplayName.Count == 0)
            {
                return;
            }

            try
            {
                var lm = Game.SceneFlow.GameManager.instance?.localizationManager;
                if (lm == null)
                {
                    return;
                }

                var entries = new Dictionary<string, string>(strippedToDisplayName.Count);
                foreach (var kv in strippedToDisplayName)
                {
                    entries["Assets.NAME[" + kv.Key + "]"] = kv.Value;
                }

                string localeId = lm.activeLocaleId;
                if (string.IsNullOrEmpty(localeId))
                {
                    return;
                }

                lm.AddSource(localeId, new DictSource(entries));

                // AddSource 只是登记来源；触发一次语言重设让活动字典重建，条目才会生效。
                try
                {
                    lm.SetActiveLocale(localeId);
                    Log.Warn($"[SSP] registered {entries.Count} prefab name entries for locale {localeId}");
                }
                catch (System.Exception e)
                {
                    Log.Warn($"[SSP] prefab names registered but refresh failed: {e.GetType().Name} {e.Message}");
                }
            }
            catch (System.Exception e)
            {
                Log.Warn($"[SSP] PrefabNamePatcher failed: {e.GetType().Name} {e.Message}");
            }
        }

        private sealed class DictSource : IDictionarySource
        {
            private readonly Dictionary<string, string> m_Entries;

            public DictSource(Dictionary<string, string> entries)
            {
                m_Entries = entries;
            }

            public IEnumerable<KeyValuePair<string, string>> ReadEntries(
                IList<IDictionaryEntryError> errors,
                Dictionary<string, int> indexCounts)
            {
                return m_Entries;
            }

            public void Unload()
            {
            }
        }
    }
}
