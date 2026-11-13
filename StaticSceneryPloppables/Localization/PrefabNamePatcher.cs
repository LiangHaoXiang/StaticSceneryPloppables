using System.Collections.Generic;
using Colossal;
using Colossal.Localization;
using Colossal.Logging;

namespace StaticSceneryPloppables.Localization
{
    /// <summary>
    /// 给「剥离版建筑」补 <c>Assets.NAME[...]</c> 词条，并在**切换语言时重新注入**。
    ///
    /// 背景：游戏的信息面板/悬停提示按 <c>"Assets.NAME[" + prefab.name + "]"</c> 查字典，
    /// 查不到就把 key 原样显示（曾出现 <c>Assets.NAME[SSP_S_EU_Commercial...]</c>）。
    /// 我们的 prefab 名字是 <c>SSP_S_&lt;原版名&gt;</c>，原版没有对应词条。
    ///
    /// 做法：记录「剥离版名 → 原版 prefab 的 titleId（如 Assets.NAME[EU_CommercialLow01_L2_5x6]）」，
    /// 注入时用当前语言的字典把 titleId 解析成文本，因此每次切换语言都能重新解析成新语言。
    /// </summary>
    public static class PrefabNamePatcher
    {
        private static ILog Log => Mod.log;

        /// <summary>剥离版 prefab 名 → 原版 prefab 的 titleId。</summary>
        private static Dictionary<string, string> s_TitleIds;

        /// <summary>已经为哪个 locale 注入过（同时用于防止 SetActiveLocale 递归触发）。</summary>
        private static string s_AppliedLocale;

        private static LocalizationManager s_Manager;

        /// <summary>订阅语言切换事件（Mod.OnLoad 时调用一次）。</summary>
        public static void Attach(LocalizationManager manager)
        {
            if (manager == null || s_Manager == manager)
            {
                return;
            }

            if (s_Manager != null)
            {
                s_Manager.onActiveDictionaryChanged -= OnActiveDictionaryChanged;
            }

            s_Manager = manager;
            s_Manager.onActiveDictionaryChanged += OnActiveDictionaryChanged;
        }

        /// <summary>目录构建完成后调用：记录映射并立即为当前语言注入。</summary>
        public static void SetMapping(Dictionary<string, string> titleIds)
        {
            s_TitleIds = titleIds;
            s_AppliedLocale = null; // 映射是新的，强制重注入
            ApplyForActiveLocale();
        }

        private static void OnActiveDictionaryChanged()
        {
            // 语言被切换 → 用新语言的字典重新解析并注入
            ApplyForActiveLocale();
        }

        private static void ApplyForActiveLocale()
        {
            if (s_TitleIds == null || s_TitleIds.Count == 0)
            {
                return;
            }

            try
            {
                var lm = s_Manager ?? Game.SceneFlow.GameManager.instance?.localizationManager;
                if (lm == null)
                {
                    return;
                }

                string localeId = lm.activeLocaleId;
                if (string.IsNullOrEmpty(localeId) || localeId == s_AppliedLocale)
                {
                    return;
                }

                var dictionary = lm.activeDictionary;
                var entries = new Dictionary<string, string>(s_TitleIds.Count);
                foreach (var kv in s_TitleIds)
                {
                    string text = null;
                    if (dictionary != null && !string.IsNullOrEmpty(kv.Value))
                    {
                        dictionary.TryGetValue(kv.Value, out text);
                    }

                    entries["Assets.NAME[" + kv.Key + "]"] = string.IsNullOrEmpty(text) ? kv.Key : text;
                }

                // 先标记，再 SetActiveLocale：避免事件回调递归
                s_AppliedLocale = localeId;
                lm.AddSource(localeId, new DictSource(entries));

                try
                {
                    lm.SetActiveLocale(localeId);
                    Log.Warn($"[SSP] injected {entries.Count} prefab name entries for locale {localeId}");
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
