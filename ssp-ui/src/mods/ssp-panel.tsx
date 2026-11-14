import React from "react";
import classNames from "classnames";
import { Button, Panel, Portal } from "cs2/ui";
import { getModule, ModuleRegistryExtend } from "cs2/modding";
import { Theme } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import sspIcon from "./icons/ssp-icon.svg";
import zoneResidential from "./icons/zone-residential.svg";
import zoneCommercial from "./icons/zone-commercial.svg";
import zoneIndustrial from "./icons/zone-industrial.svg";
import zoneOffice from "./icons/zone-office.svg";
import zoneGeneric from "./icons/zone-generic.svg";

/* ---------- 类型 ---------- */
interface CatalogItem {
    i: number; n: string; rn: string; ic: string; si: boolean;
    z: string; d: string; l: number; w: number; dp: number;
    /** 混合型住宅（住宅 + 可售卖/可制造） */
    mx: boolean;
    /** 风格："EU" / "NA" / "" */
    st: string;
    /** 预拼接的小写搜索串（n + rn）：搜索时不再对 2000 条逐次 toLowerCase（#12） */
    sb: string;
}
interface CatalogPayload { total: number; complete: boolean; items: any[]; }
interface FilterState { z: string; s: string; d: string; l: number; w: number; dp: number; }

const ZONE_TABS: { key: string; labelKey: string }[] = [
    { key: "", labelKey: "All" },
    { key: "Residential", labelKey: "Residential" },
    { key: "MixedHousing", labelKey: "MixedHousing" },
    { key: "Commercial", labelKey: "Commercial" },
    { key: "Office", labelKey: "Office" },
    { key: "Industrial", labelKey: "Industrial" },
];
/** 风格筛选：欧美 / 北美（按 prefab 名前缀，C# 侧已归好类） */
const STYLE_OPTS: { key: string; labelKey: string }[] = [
    { key: "", labelKey: "All" },
    { key: "EU", labelKey: "European" },
    { key: "NA", labelKey: "NorthAmerican" },
];
const DENSITY_OPTS = ["", "Low", "Medium", "High"];
const DENSITY_LABEL = (L: LFn): Record<string, string> =>
    ({ "": L("All"), Low: L("Low"), Medium: L("Medium"), High: L("High") });
// 只保留 1/3/5 级：2 级外观与 1 级相同、4 级与 3 级相同（C# 侧也已按此裁剪目录）
const LEVEL_OPTS = [0, 1, 3, 5];
const SIZE_OPTS = [0, 2, 3, 4, 5, 6];

/* 内置分区图标：随模组打包（coui://ui-mods/images/...），一定加载成功、无渲染开销 */
const ZONE_ICONS: Record<string, string> = {
    Residential: zoneResidential,
    Commercial: zoneCommercial,
    Industrial: zoneIndustrial,
    Office: zoneOffice,
};
const iconForZone = (z: string) => ZONE_ICONS[z] ?? zoneGeneric;

/* ---------- 本地化：走游戏本地化字典（C# 已 AddSource 注入 SSP.LABEL[...]） ---------- */
const FALLBACK_EN: Record<string, string> = {
    Title: "Static Scenery Buildings",
    Search: "Search buildings (display or internal name)",
    Filters: "Filters", FiltersActive: "Filters ●",
    All: "All", Residential: "Residential", Commercial: "Commercial", Industrial: "Industrial", Office: "Office",
    Matched: "Matched {0} / {1} buildings",
    Catalog: "Building catalog… {0} so far",
    Rows: "Scroll to browse (rows {0}-{1} of {2})",
    Hint: "Click an item to place. Left-click to keep placing, right-click/Esc to exit.",
    FilterTitle: "Filters", Style: "Style", Density: "Density", Level: "Level", Width: "Width", Depth: "Depth",
    Low: "Low", Medium: "Medium", High: "High", Cells: "{0}",
    FilterNote: "Applies on top of the search box; closing this panel keeps the selection.",
};

type LFn = (key: string, a0?: any, a1?: any, a2?: any) => string;

/**
 * 用游戏本地化控制器取值：C# 侧把词条注册进游戏字典，
 * 选项里切换语言时字典切换 → translate 返回新语言文本。
 * 组件重新渲染时就会跟着变（面板开关、任何状态变化都会重渲染）。
 */
function useText(): LFn {
    const { translate } = useLocalization();
    return React.useCallback<LFn>((key, a0, a1, a2) => {
        const fallback = FALLBACK_EN[key] ?? key;
        let text = fallback;
        try {
            text = (translate as any)(`SSP.LABEL[${key}]`, fallback) ?? fallback;
        } catch {
            text = fallback;
        }
        // {0}/{1}/{2} 占位替换
        const args = [a0, a1, a2];
        return text.replace(/\{(\d)\}/g, (m, i) => (args[Number(i)] === undefined ? m : String(args[Number(i)])));
    }, [translate]);
}

/* 网格/窗口参数（与 Find It 观感对齐：5 列 × 2 行可见，滚轮翻页） */
const COLS = 5;            // 每行条目数
const VISIBLE_ROWS = 2;    // 可见行数
const GAP = 6;             // 条目间距（rem）
const ROW_H = 150;         // 单行高度（rem）
const VIEW_H = VISIBLE_ROWS * ROW_H;
const PANEL_W = 700;       // 面板宽度（rem，Find It 量级）
const GRID_W = PANEL_W - 20 - 14; // 去掉内边距 20 与滚动条 8+间距 6
// 固定格子宽度：最后一行不足 5 个时也不允许被拉宽（保持与文本换行格子同样大小）
const TILE_W = (GRID_W - (COLS - 1) * GAP) / COLS;

/* ---------- 模块级持久状态：面板关闭重开不丢（搜索词 + 筛选 + 整份目录缓存） ---------- */
const persisted = {
    query: "" as string,
    filter: { z: "", s: "", d: "", l: 0, w: 0, dp: 0 } as FilterState,
    // 目录只由 C# 推送一次；这里缓存整份，重开面板零等待
    items: [] as CatalogItem[],
    total: 0,
    complete: false,
};

/** 目录原始条目 → UI 条目（预拼小写搜索串，避免每次按键对 2000 条反复 toLowerCase） */
function toItem(raw: any): CatalogItem {
    const name = raw?.n ?? "";
    const rawName = raw?.rn ?? "";
    return {
        i: raw?.i ?? 0, n: name, rn: rawName, ic: raw?.ic ?? "", si: !!raw?.si,
        z: raw?.z ?? "", d: raw?.d ?? "", l: raw?.l ?? 0, w: raw?.w ?? 0, dp: raw?.dp ?? 0,
        mx: !!raw?.mx, st: raw?.st ?? "",
        sb: (name + "\n" + rawName).toLowerCase(),
    };
}

/** 前端筛选：搜索词 + 页签(z) + 密度(d) + 等级(l) + 面宽(w) + 进深(dp) */
function matchesFilter(it: CatalogItem, f: FilterState, query: string): boolean {
    // 「住宅」只显示纯住宅，「混合型住宅」只显示混合型（C# 已按 IsMixed 分类）
    if (f.z === "Residential") { if (it.z !== "Residential" || it.mx) return false; }
    else if (f.z === "MixedHousing") { if (it.z !== "Residential" || !it.mx) return false; }
    else if (f.z && it.z !== f.z) return false;
    if (f.s && it.st !== f.s) return false;
    if (f.d && it.d !== f.d) return false;
    if (f.l > 0 && it.l !== f.l) return false;
    if (f.w > 0 && it.w !== f.w) return false;
    if (f.dp > 0 && it.dp !== f.dp) return false;
    if (query && it.sb.indexOf(query) < 0) return false;
    return true;
}

/* ---------- 原版 TextInput 取用 ---------- */
function useVanillaTextInput(): any {
    const [comp, setComp] = React.useState<any>(null);
    React.useEffect(() => {
        try {
            const modding = (window as any)["cs2/modding"];
            const mr = (modding as any)?.moduleRegistry ?? (window as any).moduleRegistry;
            const mod = mr?.registry?.get?.("game-ui/common/input/text/text-input.tsx");
            if (mod?.TextInput) setComp(() => mod.TextInput);
        } catch (e) {
            console.error("[SSP] registry TextInput lookup failed: " + e);
        }
    }, []);
    return comp;
}

/* ---------- 错误边界 ---------- */
class ErrorBoundary extends React.Component<{ children: React.ReactNode }, { error: string | null }> {
    state = { error: null as string | null };
    static getDerivedStateFromError(err: any) { return { error: String(err?.message ?? err) }; }
    componentDidCatch(err: any) { console.error("[SSP] panel render error: " + (err?.stack ?? err)); }
    render() {
        if (this.state.error) {
            return <div style={{ padding: "10rem", color: "#ffcb00", fontSize: "14rem" }}>SSP 面板错误: {this.state.error}</div>;
        }
        return this.props.children as any;
    }
}

/* ---------- 小控件 ---------- */
function Chip(props: { label: string; active: boolean; onClick: () => void }) {
    const { label, active, onClick } = props;
    const [hover, setHover] = React.useState(false);
    const bg = active ? "#4bc3f1" : hover ? "rgba(75,195,241,0.25)" : "rgba(34,49,65,0.9)";
    return (
        <div
            onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)} onClick={onClick}
            style={{
                padding: "4rem 12rem", borderRadius: "12rem", cursor: "pointer",
                backgroundColor: bg, color: active ? "#141b22" : "#f0fbff",
                fontSize: "12rem", whiteSpace: "nowrap", border: "1rem solid rgba(75,195,241,0.35)",
            }}
        >{label}</div>
    );
}

/* 行组件：memo 化，避免筛选/翻页时重渲染所有已有条目 */
const ResultRow = React.memo(function ResultRow(props: { item: CatalogItem; onPick: (i: number) => void }) {
    const { item, onPick } = props;
    const [hover, setHover] = React.useState(false);
    // 图标候选链：C# 给的 SVG 图标 → 内置分区图标（必定存在）
    const candidates = React.useMemo(
        () => (item.ic ? [item.ic, iconForZone(item.z)] : [iconForZone(item.z)]),
        [item.ic, item.z]
    );
    const [idx, setIdx] = React.useState(0);
    React.useEffect(() => { setIdx(0); }, [candidates]);
    const iconSrc = candidates[Math.min(idx, candidates.length - 1)];
    return (
        <div
            title={item.n}
            onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
            onClick={() => onPick(item.i)}
            style={{
                display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "flex-start",
                width: "100%", height: "100%", boxSizing: "border-box",
                padding: "6rem", borderRadius: "4rem", cursor: "pointer",
                backgroundColor: hover ? "rgba(75,195,241,0.18)" : "rgba(34,49,65,0.9)",
            }}
        >
            <img
                src={iconSrc}
                onError={() => {
                    if (idx < candidates.length - 1) {
                        console.error("[SSP] icon load failed: " + iconSrc);
                        setIdx(idx + 1);
                    }
                }}
                style={{ width: "84rem", height: "84rem", objectFit: "contain" }}
            />
            <div style={{
                marginTop: "4rem", width: "100%", height: "28rem", color: "#f0fbff", fontSize: "10rem",
                textAlign: "center", lineHeight: "14rem",
                display: "-webkit-box", WebkitBoxOrient: "vertical", WebkitLineClamp: 2,
                overflow: "hidden", wordBreak: "break-word",
            }}>{item.n}</div>
            {item.si && (
                <div style={{ marginTop: "2rem", color: "#7d9bb5", fontSize: "9rem", whiteSpace: "nowrap" }}>
                    L{item.l}　{item.w}x{item.dp}
                </div>
            )}
        </div>
    );
}, (a, b) => a.item.i === b.item.i && a.item.ic === b.item.ic && a.item.n === b.item.n && a.onPick === b.onPick);

/* ---------- 面板主体 ---------- */
function PanelBody(props: {
    onPick: (i: number) => void;
    onClose: () => void;
    showFilter: boolean;
    onToggleFilter: () => void;
    filter: FilterState;
    onFilterChange: (next: FilterState) => void;
}) {
    const TextInput = useVanillaTextInput();
    const L = useText(); // 本地化：走游戏本地化字典
    const filter = props.filter;
    // 目录由 C# 一次性推送；重开面板直接用模块级缓存，零等待、无请求
    const [items, setItems] = React.useState<CatalogItem[]>(persisted.items);
    const [total, setTotal] = React.useState(persisted.total);
    const [complete, setComplete] = React.useState(persisted.complete);
    const [query, setQuery] = React.useState(persisted.query);

    React.useEffect(() => {
        const engine = (window as any).engine;
        let timer: any = null;
        const stop = () => { if (timer) { clearInterval(timer); timer = null; } };

        const onUpdate = (payload: string) => {
            try {
                const data: CatalogPayload = JSON.parse(payload);
                const list = (data?.items ?? []).map(toItem);
                const done = !!data?.complete;
                setItems(list);
                setTotal(data?.total ?? list.length);
                setComplete(done);
                persisted.items = list;
                persisted.total = data?.total ?? list.length;
                persisted.complete = done;
                if (done) stop();
            } catch (e) {
                console.error("[SSP] catalog parse failed: " + e);
            }
        };

        engine.on("ssp.catalog.update", onUpdate);
        engine.trigger("ssp.catalog.subscribe");
        // 目录仍在构建（约 4 秒）：每 400ms 重新订阅，直到拿到 complete=true 为止
        timer = setInterval(() => {
            if (persisted.complete) { stop(); return; }
            engine.trigger("ssp.catalog.subscribe");
        }, 400);

        return () => { stop(); engine.off("ssp.catalog.update", onUpdate); };
    }, []);

    // Esc（工具取消）时由 C# 推事件关闭面板
    React.useEffect(() => {
        const onUIEvent = (payload: any) => {
            const ev = typeof payload === "string" ? payload : (payload?.value ?? "");
            if (ev === "closePanel" && props.onClose) {
                props.onClose();
            }
        };
        const engine = (window as any).engine;
        engine.on("ssp.uievent.update", onUIEvent);
        engine.trigger("ssp.uievent.subscribe");
        // 上报面板可见性：关闭后 C# 侧不再推送 uievent（避免 "no handler" 告警）
        engine.trigger("ssp.panelVisible", true);
        return () => {
            engine.trigger("ssp.panelVisible", false);
            engine.off("ssp.uievent.update", onUIEvent);
        };
    }, [props.onClose]);

    // 工具未激活时 Esc 也应关闭面板（并尽量拦截原版暂停菜单）
    React.useEffect(() => {
        const onKeyDown = (e: any) => {
            if (e?.key !== "Escape") return;
            e.preventDefault?.();
            e.stopPropagation?.();
            e.stopImmediatePropagation?.();
            if (props.onClose) props.onClose();
        };
        window.addEventListener("keydown", onKeyDown, true);
        document.addEventListener("keydown", onKeyDown, true);
        return () => {
            window.removeEventListener("keydown", onKeyDown, true);
            document.removeEventListener("keydown", onKeyDown, true);
        };
    }, [props.onClose]);

    // 搜索纯前端：整份目录已在内存里，打字即时过滤，不触发任何 C# 调用
    const onChange = (q: string) => {
        setQuery(q);
        persisted.query = q;
    };

    const inputEl = TextInput ? (
        <TextInput value={query} onChange={onChange} placeholder={L("Search")} />
    ) : (
        <input
            value={query}
            onChange={(e: any) => onChange(e.currentTarget.value)}
            placeholder={L("Search")}
            style={{
                width: "100%", boxSizing: "border-box", padding: "8rem 12rem", fontSize: "13rem",
                color: "#f0fbff", backgroundColor: "#141b22", border: "1rem solid #3d5064",
                borderRadius: "4rem", outline: "none",
            }}
        />
    );

    const filterActive = filter.z || filter.s || filter.d || filter.l || filter.w || filter.dp;
    // 页签/筛选纯前端过滤：不重新请求、不等待，切换即时生效
    const needle = query.trim().toLowerCase();
    const visible = React.useMemo(
        () => items.filter((it) => matchesFilter(it, filter, needle)),
        [items, filter, needle]);

    // ★ 窗口化渲染（Find It 同款）：DOM 里永远只有 2 行可见 + 1 行缓冲
    const rows = React.useMemo(() => chunk(visible, COLS), [visible]);
    const totalRows = rows.length;
    const maxScroll = Math.max(0, totalRows - VISIBLE_ROWS);
    const [scrollIndex, setScrollIndex] = React.useState(0);
    // 页签/筛选/搜索变化 → 回到顶部
    React.useEffect(() => { setScrollIndex(0); }, [filter.z, filter.s, filter.d, filter.l, filter.w, filter.dp, query]);
    React.useEffect(() => {
        if (scrollIndex > maxScroll) setScrollIndex(maxScroll);
    }, [maxScroll]);

    const firstRow = Math.max(0, Math.min(Math.floor(scrollIndex), maxScroll));
    const windowRows = rows.slice(firstRow, firstRow + VISIBLE_ROWS + 1);
    const offsetY = -(scrollIndex - firstRow) * ROW_H;

    const scroll = (deltaRows: number) => {
        setScrollIndex((s) => Math.max(0, Math.min(maxScroll, s + deltaRows)));
    };

    // 自绘滚动条（Gameface 里原生滚动条不可控，Find It 也是自绘）
    const thumbH = totalRows <= VISIBLE_ROWS
        ? VIEW_H
        : Math.max(28, Math.round((VIEW_H * VISIBLE_ROWS) / totalRows));
    const trackSpace = Math.max(1, VIEW_H - thumbH);
    const thumbTop = maxScroll <= 0 ? 0 : Math.round((scrollIndex / maxScroll) * trackSpace);
    const trackRef = React.useRef<any>(null);

    // 拖拽：rect 只在按下时取一次（每次 mousemove 都 getBoundingClientRect 会强制布局 → 卡顿根源）
    const dragRef = React.useRef<{ y0: number; s0: number; travelPx: number } | null>(null);
    const [dragging, setDragging] = React.useState(false);
    const wasDragRef = React.useRef(false);

    const onThumbDown = (e: any) => {
        const rect = trackRef.current?.getBoundingClientRect();
        if (!rect) return;
        const scale = rect.height / VIEW_H || 1; // rem → px
        dragRef.current = {
            y0: e.clientY,
            s0: scrollIndex,
            travelPx: Math.max(1, (trackSpace - thumbH) * scale),
        };
        wasDragRef.current = false;
        setDragging(true);
        e.stopPropagation?.();
        e.preventDefault?.();
    };

    const onDragMove = (e: any) => {
        const d = dragRef.current;
        if (!d) return;
        const next = Math.max(0, Math.min(maxScroll, Math.round(d.s0 + ((e.clientY - d.y0) * maxScroll) / d.travelPx)));
        if (next !== scrollIndex) {
            wasDragRef.current = true;
            setScrollIndex(next);
        }
    };

    const endDrag = () => {
        dragRef.current = null;
        setDragging(false);
    };

    return (
        /* 自绘面板容器（不用 cs2/ui Panel：它会给内容套一层滚动 → 双滚动条） */
        <div style={{
            width: `${PANEL_W}rem`, display: "flex", flexDirection: "column",
            borderRadius: "4rem", overflow: "hidden",
            backgroundColor: "rgba(16,19,29,0.88)",
            backdropFilter: "brightness(60%) contrast(90%) blur(7px)",
            color: "#f0fbff", pointerEvents: "auto",
        }}>
            <div style={{
                display: "flex", alignItems: "center", justifyContent: "space-between",
                padding: "8rem 12rem", backgroundColor: "rgba(19,25,36,0.95)",
            }}>
                <span style={{ fontSize: "14rem", fontWeight: 600, color: "#4bc3f1" }}>{L("Title")}</span>
                <Button variant="icon" src="Media/Glyphs/Close.svg" onSelect={props.onClose} />
            </div>
            <div style={{ display: "flex", flexDirection: "column", gap: "8rem", padding: "10rem" }}>
                <div style={{ display: "flex", gap: "8rem", alignItems: "center" }}>
                    <div style={{ flex: 1 }}>{inputEl}</div>
                    <Chip label={filterActive ? L("FiltersActive") : L("Filters")} active={props.showFilter}
                        onClick={props.onToggleFilter} />
                </div>

                {/* 类型页签 */}
                <div style={{ display: "flex", gap: "6rem" }}>
                    {ZONE_TABS.map((t) => (
                        <Chip key={t.key} label={L(t.labelKey)} active={filter.z === t.key}
                            onClick={() => props.onFilterChange({ ...filter, z: t.key })} />
                    ))}
                </div>

                <div style={{ color: "#7d9bb5", fontSize: "11rem" }}>
                    {complete
                        ? L("Matched", visible.length, items.length)
                        : L("Catalog", total)}
                </div>

                <div style={{ display: "flex", gap: "6rem", width: "100%" }}>
                    <div
                        onWheel={(e: any) => { scroll((e?.deltaY ?? 0) > 0 ? 1 : -1); e.stopPropagation?.(); }}
                        style={{ height: `${VIEW_H}rem`, width: `${GRID_W}rem`, overflow: "hidden", position: "relative" }}
                    >
                        <div style={{ position: "absolute", left: 0, right: 0, top: `${offsetY}rem` }}>
                            {windowRows.map((row, ri) => (
                                <div key={ri} style={{ display: "flex", gap: `${GAP}rem`, height: `${ROW_H - GAP}rem`, marginBottom: `${GAP}rem` }}>
                                    {row.map((it) => (
                                        <div key={it.i} style={{ width: `${TILE_W}rem`, flex: "0 0 auto" }}>
                                            <ResultRow item={it} onPick={props.onPick} />
                                        </div>
                                    ))}
                                </div>
                            ))}
                        </div>
                    </div>

                    {/* 滚动条 */}
                    <div
                        ref={trackRef}
                        onClick={(e: any) => {
                            if (wasDragRef.current) return; // 拖完松手不触发点击跳页
                            const rect = trackRef.current?.getBoundingClientRect();
                            if (!rect) return;
                            scroll(e.clientY < rect.y + thumbTop ? -VISIBLE_ROWS : VISIBLE_ROWS);
                        }}
                        style={{
                            width: "8rem", height: `${VIEW_H}rem`, borderRadius: "4rem",
                            backgroundColor: "rgba(240,251,255,0.10)", position: "relative", flex: "0 0 auto",
                        }}
                    >
                        <div
                            onMouseDown={onThumbDown}
                            style={{
                                position: "absolute", left: 0, right: 0, top: `${thumbTop}rem`, height: `${thumbH}rem`,
                                borderRadius: "4rem", backgroundColor: "rgba(75,195,241,0.75)", cursor: "pointer",
                            }}
                        />
                    </div>
                    {dragging && (
                        <div
                            onMouseMove={onDragMove}
                            onMouseUp={endDrag}
                            onMouseLeave={endDrag}
                            style={{ position: "fixed", left: 0, top: 0, right: 0, bottom: 0, zIndex: 50, cursor: "ns-resize" }}
                        />
                    )}
                </div>

                <div style={{ color: "#7d9bb5", fontSize: "10rem" }}>
                    {L("Rows", Math.min(totalRows, firstRow + 1), Math.min(totalRows, firstRow + VISIBLE_ROWS), totalRows)}
                </div>
                <div style={{ color: "#7d9bb5", fontSize: "10rem" }}>{L("Hint")}</div>
            </div>
        </div>
    );
}

function chunk<T>(arr: T[], count: number): T[][] {
    const rows: T[][] = [];
    for (let i = 0; i < arr.length; i += count) {
        rows.push(arr.slice(i, i + count));
    }
    return rows;
}

/* ---------- 独立筛选面板（与主面板并排，不遮挡不裁剪；由外层容器定位） ---------- */
function FilterPanel(props: { filter: FilterState; apply: (next: FilterState) => void; onClose: () => void }) {
    const { filter, apply } = props;
    const L = useText();
    return (
        <Panel header={L("FilterTitle")} onClose={props.onClose} style={{ width: "470rem" }}>
            <div style={{ display: "flex", flexDirection: "column", gap: "6rem", padding: "10rem" }}>
                <div style={{ display: "flex", gap: "6rem", alignItems: "center" }}>
                    <span style={{ color: "#7d9bb5", fontSize: "11rem", width: "52rem" }}>{L("Style")}</span>
                    {STYLE_OPTS.map((s) => (
                        <Chip key={s.key} label={L(s.labelKey)} active={filter.s === s.key}
                            onClick={() => apply({ ...filter, s: s.key })} />
                    ))}
                </div>
                <div style={{ display: "flex", gap: "6rem", alignItems: "center" }}>
                    <span style={{ color: "#7d9bb5", fontSize: "11rem", width: "52rem" }}>{L("Density")}</span>
                    {DENSITY_OPTS.map((d) => (
                        <Chip key={d} label={DENSITY_LABEL(L)[d]} active={filter.d === d}
                            onClick={() => apply({ ...filter, d })} />
                    ))}
                </div>
                <div style={{ display: "flex", gap: "6rem", alignItems: "center" }}>
                    <span style={{ color: "#7d9bb5", fontSize: "11rem", width: "52rem" }}>{L("Level")}</span>
                    {LEVEL_OPTS.map((l) => (
                        <Chip key={l} label={l === 0 ? L("All") : `L${l}`} active={filter.l === l}
                            onClick={() => apply({ ...filter, l })} />
                    ))}
                </div>
                <div style={{ display: "flex", gap: "6rem", alignItems: "center" }}>
                    <span style={{ color: "#7d9bb5", fontSize: "11rem", width: "52rem" }}>{L("Width")}</span>
                    {SIZE_OPTS.map((w) => (
                        <Chip key={w} label={w === 0 ? L("All") : L("Cells", w)} active={filter.w === w}
                            onClick={() => apply({ ...filter, w })} />
                    ))}
                </div>
                <div style={{ display: "flex", gap: "6rem", alignItems: "center" }}>
                    <span style={{ color: "#7d9bb5", fontSize: "11rem", width: "52rem" }}>{L("Depth")}</span>
                    {SIZE_OPTS.map((dp) => (
                        <Chip key={dp} label={dp === 0 ? L("All") : L("Cells", dp)} active={filter.dp === dp}
                            onClick={() => apply({ ...filter, dp })} />
                    ))}
                </div>
                <div style={{ color: "#7d9bb5", fontSize: "10rem" }}>{L("FilterNote")}</div>
            </div>
        </Panel>
    );
}

/* ---------- 主容器：工具栏按钮 + 主面板 + 筛选面板 ---------- */
const ToolBarButtonTheme: Theme | any = getModule(
    "game-ui/game/components/toolbar/components/feature-button/toolbar-feature-button-new.module.scss",
    "classes"
);
const ToolBarTheme: Theme | any = getModule("game-ui/game/components/toolbar/toolbar.module.scss", "classes");

export const SSPToolbarIcon: ModuleRegistryExtend = (Component) => {
    return (props: any) => {
        const { children, ...otherProps } = props || {};
        const [open, setOpen] = React.useState(false);
        const [showFilter, setShowFilter] = React.useState(false);
        // 筛选状态提到这里：面板与筛选弹层共用同一份 state，点选即刻高亮
        const [filter, setFilter] = React.useState<FilterState>(persisted.filter);
        const pick = React.useCallback((i: number) => {
            (window as any).engine.trigger("ssp.select", i);
            // 保持面板打开（Find It 同款），可继续换选/连放
        }, []);
        const applyFilter = React.useCallback((next: FilterState) => {
            // 纯前端过滤：只改本地状态，不触发任何 C# 请求（缓存与列表都不动）
            persisted.filter = next;
            setFilter(next);
        }, []);
        const closeAll = React.useCallback(() => { setOpen(false); setShowFilter(false); }, []);
        return (
            <>
                <Button
                    src={sspIcon}
                    className={classNames(ToolBarButtonTheme?.button, open && ToolBarButtonTheme?.selected)}
                    variant="icon"
                    selected={open}
                    onSelect={() => setOpen(!open)}
                />
                <div className={ToolBarTheme?.divider}></div>
                <Component {...otherProps}></Component>
                {open && (
                    <Portal>
                        {/* Find It 同款：水平居中、贴着工具栏上方；pointer-events 只开在面板上 */}
                        <div style={{
                            position: "absolute",
                            left: 0, right: 0,
                            bottom: "calc(2 * var(--toolbarHeight))",
                            display: "flex", justifyContent: "center",
                            pointerEvents: "none",
                        }}>
                            <div style={{ position: "relative", pointerEvents: "auto" }}>
                                <ErrorBoundary>
                                    <PanelBody
                                        onPick={pick}
                                        onClose={closeAll}
                                        showFilter={showFilter}
                                        onToggleFilter={() => setShowFilter((s) => !s)}
                                        filter={filter}
                                        onFilterChange={applyFilter}
                                    />
                                </ErrorBoundary>
                                {showFilter && (
                                    <div style={{ position: "absolute", left: `${PANEL_W + 16}rem`, bottom: 0 }}>
                                        <FilterPanel
                                            filter={filter}
                                            apply={applyFilter}
                                            onClose={() => setShowFilter(false)}
                                        />
                                    </div>
                                )}
                            </div>
                        </div>
                    </Portal>
                )}
            </>
        );
    };
}
