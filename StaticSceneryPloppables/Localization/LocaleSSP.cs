using System.Collections.Generic;
using Colossal;

namespace StaticSceneryPloppables.Localization
{
    /// <summary>
    /// 模组 UI 词条，经 <c>localizationManager.AddSource(localeId, source)</c> 注入游戏本地化字典
    /// （Find It / BetterBulldozer / PowerlineCapacityMod 同款），UI 侧 <c>translate("SSP.LABEL[Key]")</c> 取值，
    /// 因此游戏选项切语言时文案即时跟随。
    ///
    /// 覆盖语言：en / zh-HANS / zh-HANT / de / fr / es / pt-BR / ru / pl / ja / ko；
    /// 未覆盖的语言会注册英文兜底（见 Mod.OnLoad）。
    /// </summary>
    public class LocaleSSP : IDictionarySource
    {
        public enum Lang
        {
            EN,
            ZH_HANS,
            ZH_HANT,
            DE,
            FR,
            ES,
            PT_BR,
            RU,
            PL,
            JA,
            KO
        }

        private static readonly string[] Keys =
        {
            "Title", "Search", "Filters", "FiltersActive",
            "All", "Residential", "Commercial", "Industrial", "Office",
            "Matched", "LoadingItems", "LoadingMore", "Catalog", "Rows", "Hint",
            "FilterTitle", "Density", "Level", "Width", "Depth",
            "Low", "Medium", "High", "Cells", "FilterNote",
            "MixedHousing", "Style", "European", "NorthAmerican",
        };

        private static readonly Dictionary<Lang, string[]> Values = new Dictionary<Lang, string[]>
        {
            [Lang.EN] = new[]
            {
                "Static Scenery Buildings",
                "Search buildings (display or internal name)",
                "Filters", "Filters ●",
                "All", "Residential", "Commercial", "Industrial", "Office",
                "Matched {0} / {1} buildings",
                "Matched {0} / {1} buildings (loading {1}/{2})",
                "Loaded {0}/{1}, loading the rest…",
                "Building catalog… {0} so far",
                "Scroll to browse (rows {0}-{1} of {2})",
                "Click an item to place. Left-click to keep placing, right-click/Esc to exit.",
                "Filters", "Density", "Level", "Width", "Depth",
                "Low", "Medium", "High", "{0}",
                "Applies on top of the search box; closing this panel keeps the selection.",
                "Mixed Housing", "Style", "European", "North American",
            },
            [Lang.ZH_HANS] = new[]
            {
                "静态造景建筑",
                "搜索建筑（原版名称或内部名）",
                "筛选", "筛选 ●",
                "全部", "住宅", "商业", "工业", "办公",
                "匹配 {0} / 共 {1} 个建筑",
                "匹配 {0} / 共 {1} 个建筑（载入中 {1}/{2}）",
                "已载入 {0}/{1} 个建筑，其余自动载入中…",
                "目录生成中… 已 {0} 个",
                "滚轮/拖滚动条翻页（第 {0}-{1} 行 / 共 {2} 行）",
                "点击条目进入放置；左键连放，右键/Esc 退出。地皮与装饰按原版随机生成。",
                "筛选", "密度", "等级", "面宽", "进深",
                "低密度", "中密度", "高密度", "{0}格",
                "与主面板搜索叠加生效；关闭筛选面板不影响已选条件。",
                "混合型住宅", "风格", "欧美", "北美",
            },
            [Lang.ZH_HANT] = new[]
            {
                "靜態造景建築",
                "搜尋建築（原版名稱或內部名稱）",
                "篩選", "篩選 ●",
                "全部", "住宅", "商業", "工業", "辦公",
                "符合 {0} / 共 {1} 棟建築",
                "符合 {0} / 共 {1} 棟建築（載入中 {1}/{2}）",
                "已載入 {0}/{1} 棟建築，其餘自動載入中…",
                "目錄產生中… 已 {0} 棟",
                "滾輪／拖曳捲軸翻頁（第 {0}-{1} 列 / 共 {2} 列）",
                "點選項目即可放置；左鍵連續放置，右鍵/Esc 離開。地坪與裝飾依原版隨機產生。",
                "篩選", "密度", "等級", "面寬", "進深",
                "低密度", "中密度", "高密度", "{0}格",
                "與主面板搜尋疊加生效；關閉篩選面板不影響已選條件。",
                "混合型住宅", "風格", "歐美", "北美",
            },
            [Lang.DE] = new[]
            {
                "Statische Kulissen-Gebäude",
                "Gebäude suchen (Anzeige- oder interner Name)",
                "Filter", "Filter ●",
                "Alle", "Wohngebiet", "Gewerbe", "Industrie", "Büro",
                "Treffer {0} / {1} Gebäude",
                "Treffer {0} / {1} Gebäude (lädt {1}/{2})",
                "Geladen {0}/{1}, Rest wird geladen…",
                "Gebäudekatalog… bisher {0}",
                "Scrollen zum Blättern (Zeilen {0}-{1} von {2})",
                "Objekt anklicken zum Platzieren. Linksklick setzt weiter, Rechtsklick/Esc beendet.",
                "Filter", "Dichte", "Stufe", "Breite", "Tiefe",
                "Niedrig", "Mittel", "Hoch", "{0}",
                "Wirkt zusätzlich zur Suche; Schließen behält die Auswahl.",
                "Gemischtwohnen", "Stil", "Europäisch", "Nordamerikanisch",
            },
            [Lang.FR] = new[]
            {
                "Bâtiments de décor statique",
                "Rechercher un bâtiment (nom affiché ou interne)",
                "Filtres", "Filtres ●",
                "Tous", "Résidentiel", "Commercial", "Industriel", "Bureaux",
                "Correspondances {0} / {1} bâtiments",
                "Correspondances {0} / {1} bâtiments (chargement {1}/{2})",
                "Chargés {0}/{1}, chargement du reste…",
                "Catalogue de bâtiments… {0} pour l’instant",
                "Faites défiler pour parcourir (lignes {0}-{1} sur {2})",
                "Cliquez sur un élément pour le placer. Clic gauche pour continuer, clic droit/Échap pour quitter.",
                "Filtres", "Densité", "Niveau", "Largeur", "Profondeur",
                "Faible", "Moyenne", "Élevée", "{0}",
                "S’applique en plus de la recherche ; fermer ce panneau conserve la sélection.",
                "Logements mixtes", "Style", "Européen", "Nord-américain",
            },
            [Lang.ES] = new[]
            {
                "Edificios de decorado estático",
                "Buscar edificios (nombre visible o interno)",
                "Filtros", "Filtros ●",
                "Todos", "Residencial", "Comercial", "Industrial", "Oficinas",
                "Coincidencias {0} / {1} edificios",
                "Coincidencias {0} / {1} edificios (cargando {1}/{2})",
                "Cargados {0}/{1}, cargando el resto…",
                "Catálogo de edificios… {0} hasta ahora",
                "Desplázate para navegar (filas {0}-{1} de {2})",
                "Haz clic en un elemento para colocarlo. Clic izquierdo para seguir, clic derecho/Esc para salir.",
                "Filtros", "Densidad", "Nivel", "Anchura", "Profundidad",
                "Baja", "Media", "Alta", "{0}",
                "Se aplica junto a la búsqueda; cerrar este panel mantiene la selección.",
                "Vivienda mixta", "Estilo", "Europeo", "Norteamericano",
            },
            [Lang.PT_BR] = new[]
            {
                "Edifícios de cenário estático",
                "Pesquisar edifícios (nome exibido ou interno)",
                "Filtros", "Filtros ●",
                "Todos", "Residencial", "Comercial", "Industrial", "Escritórios",
                "Correspondências {0} / {1} edifícios",
                "Correspondências {0} / {1} edifícios (carregando {1}/{2})",
                "Carregados {0}/{1}, carregando o restante…",
                "Catálogo de edifícios… {0} até agora",
                "Role para navegar (linhas {0}-{1} de {2})",
                "Clique em um item para posicionar. Clique esquerdo continua, clique direito/Esc sai.",
                "Filtros", "Densidade", "Nível", "Largura", "Profundidade",
                "Baixa", "Média", "Alta", "{0}",
                "Aplica-se junto à pesquisa; fechar este painel mantém a seleção.",
                "Habitação mista", "Estilo", "Europeu", "Norte-americano",
            },
            [Lang.RU] = new[]
            {
                "Статичные декоративные здания",
                "Поиск зданий (отображаемое или внутреннее имя)",
                "Фильтры", "Фильтры ●",
                "Все", "Жилая", "Коммерческая", "Промышленная", "Офисная",
                "Совпадений {0} / {1} зданий",
                "Совпадений {0} / {1} зданий (загрузка {1}/{2})",
                "Загружено {0}/{1}, остальное загружается…",
                "Каталог зданий… пока {0}",
                "Прокручивайте для просмотра (строки {0}-{1} из {2})",
                "Нажмите на элемент, чтобы разместить. ЛКМ — продолжать, ПКМ/Esc — выйти.",
                "Фильтры", "Плотность", "Уровень", "Ширина", "Глубина",
                "Низкая", "Средняя", "Высокая", "{0}",
                "Действует вместе с поиском; закрытие панели сохраняет выбор.",
                "Смешанное жильё", "Стиль", "Европейский", "Североамериканский",
            },
            [Lang.PL] = new[]
            {
                "Statyczne budynki dekoracyjne",
                "Szukaj budynków (nazwa wyświetlana lub wewnętrzna)",
                "Filtry", "Filtry ●",
                "Wszystkie", "Mieszkalne", "Komercyjne", "Przemysłowe", "Biurowe",
                "Dopasowania {0} / {1} budynków",
                "Dopasowania {0} / {1} budynków (ładowanie {1}/{2})",
                "Załadowano {0}/{1}, ładowanie reszty…",
                "Katalog budynków… dotąd {0}",
                "Przewiń, aby przeglądać (wiersze {0}-{1} z {2})",
                "Kliknij element, aby go postawić. LPM — stawiaj dalej, PPM/Esc — wyjdź.",
                "Filtry", "Gęstość", "Poziom", "Szerokość", "Głębokość",
                "Niska", "Średnia", "Wysoka", "{0}",
                "Działa razem z wyszukiwaniem; zamknięcie panelu zachowuje wybór.",
                "Zabudowa mieszana", "Styl", "Europejski", "Północnoamerykański",
            },
            [Lang.JA] = new[]
            {
                "静的景観ビル",
                "建物を検索（表示名または内部名）",
                "フィルター", "フィルター ●",
                "すべて", "住宅", "商業", "工業", "オフィス",
                "該当 {0} / 全 {1} 棟",
                "該当 {0} / 全 {1} 棟（読み込み中 {1}/{2}）",
                "読み込み済み {0}/{1}、残りを読み込み中…",
                "建物カタログ… 現在 {0} 件",
                "スクロールで閲覧（{0}-{1} 行 / 全 {2} 行）",
                "項目をクリックで設置。左クリックで連続設置、右クリック/Esc で終了。",
                "フィルター", "密度", "レベル", "間口", "奥行き",
                "低密度", "中密度", "高密度", "{0}マス",
                "検索と併用して適用されます。閉じても選択は保持されます。",
                "複合住宅", "スタイル", "ヨーロッパ", "北米",
            },
            [Lang.KO] = new[]
            {
                "정적 조경 건물",
                "건물 검색(표시 이름 또는 내부 이름)",
                "필터", "필터 ●",
                "전체", "주거", "상업", "산업", "사무",
                "일치 {0} / 전체 {1}개 건물",
                "일치 {0} / 전체 {1}개 건물(로드 중 {1}/{2})",
                "로드됨 {0}/{1}, 나머지 로드 중…",
                "건물 목록… 현재 {0}개",
                "스크롤하여 탐색(행 {0}-{1} / 전체 {2})",
                "항목을 클릭하여 배치합니다. 좌클릭으로 계속 배치, 우클릭/Esc로 종료.",
                "필터", "밀도", "레벨", "너비", "깊이",
                "저밀도", "중밀도", "고밀도", "{0}칸",
                "검색과 함께 적용됩니다. 패널을 닫아도 선택은 유지됩니다.",
                "복합 주거", "스타일", "유럽", "북미",
            },
        };

        private readonly Dictionary<string, string> m_Entries;

        public LocaleSSP(Lang lang)
        {
            m_Entries = new Dictionary<string, string>(Keys.Length);
            if (!Values.TryGetValue(lang, out var values) || values.Length != Keys.Length)
            {
                values = Values[Lang.EN];
            }

            for (int i = 0; i < Keys.Length; i++)
            {
                m_Entries["SSP.LABEL[" + Keys[i] + "]"] = values[i];
            }
        }

        /// <summary>该语言应注册到的 localeId 列表（含常见变体）。</summary>
        public static string[] LocaleIds(Lang lang)
        {
            switch (lang)
            {
                case Lang.ZH_HANS: return new[] { "zh", "zh-HANS", "zh-Hans", "zh-CN", "zh-SG" };
                case Lang.ZH_HANT: return new[] { "zh-HANT", "zh-Hant", "zh-TW", "zh-HK", "zh-MO" };
                case Lang.DE: return new[] { "de", "de-DE", "de-AT", "de-CH" };
                case Lang.FR: return new[] { "fr", "fr-FR", "fr-CA" };
                case Lang.ES: return new[] { "es", "es-ES", "es-MX", "es-419" };
                case Lang.PT_BR: return new[] { "pt", "pt-BR", "pt-PT" };
                case Lang.RU: return new[] { "ru", "ru-RU" };
                case Lang.PL: return new[] { "pl", "pl-PL" };
                case Lang.JA: return new[] { "ja", "ja-JP" };
                case Lang.KO: return new[] { "ko", "ko-KR" };
                default: return new[] { "en", "en-US", "en-GB", "en-UK" };
            }
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
