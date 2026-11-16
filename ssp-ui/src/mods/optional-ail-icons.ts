import React from "react";

// AIL is optional: this module only reads URLs that it has already written to source prefabs.
// No guessed coui path, external assembly reference, full-catalog scan or thumbnail:// request.
const STEP_MS = 60;            // half the previous 120ms: visible pictures appear faster
const RECHECK_MS = 2400;       // per-item cadence while AIL may still be initialising
const MAX_ATTEMPTS = 24;       // bounded per item per panel session
const RESPONSE_TIMEOUT_MS = 1500;
const IMAGE_TIMEOUT_MS = 3000;
const CACHE_LIMIT = 400;
let nextSerial = 1;

/** One visible tile: catalog index plus the stable original prefab name used as cache identity. */
export interface OptionalIconTarget {
    i: number;
    key: string;
}

// Module scope on purpose: closing the panel or switching saves must not reload the same picture.
const verified = new Map<string, string>();

function remember(key: string, url: string): void {
    verified.delete(key);
    verified.set(key, url);
    if (verified.size > CACHE_LIMIT) verified.delete(verified.keys().next().value);
}

export function useOptionalAilIcons(
    visible: OptionalIconTarget[],
    buffer: OptionalIconTarget[]
): ReadonlyMap<number, string> {
    const [, refresh] = React.useState(0);
    const targets = React.useRef<OptionalIconTarget[]>([]);
    const pump = React.useRef<() => void>(() => {});
    targets.current = [...visible, ...buffer];
    const targetKey = targets.current.map((t) => `${t.i}:${t.key}`).join(",");

    // Any window change (scroll, filter, save switch) wakes the pump. There is deliberately no
    // wall-clock expiry: a filter applied much later must still load its newly visible rows.
    React.useEffect(() => {
        const timer = setTimeout(() => pump.current(), STEP_MS);
        return () => clearTimeout(timer);
    }, [targetKey]);

    React.useEffect(() => {
        const engine = (window as any).engine;
        if (!engine) return;
        let disposed = false;
        let active: OptionalIconTarget & { serial: number } | null = null;
        let image: HTMLImageElement | null = null;
        let timer: ReturnType<typeof setTimeout> | null = null;
        let wake: ReturnType<typeof setTimeout> | null = null;
        let wakeAt = 0;
        let lastDispatchAt = 0;
        // Per panel session so reopening retries still-missing pictures, while verified ones stay cached.
        const tried = new Map<string, number>();
        const lastTry = new Map<string, number>();

        const clearTimer = () => { if (timer) clearTimeout(timer); timer = null; };
        const schedule = (delay: number) => {
            if (disposed) return;
            const due = Date.now() + delay;
            if (wake && wakeAt <= due) return;
            if (wake) clearTimeout(wake);
            wakeAt = due;
            wake = setTimeout(() => { wake = null; wakeAt = 0; pump.current(); }, delay);
        };

        const next = () => {
            if (disposed || active) return;
            if (wake) { clearTimeout(wake); wake = null; wakeAt = 0; }
            const now = Date.now();
            if (now - lastDispatchAt < STEP_MS) { schedule(STEP_MS); return; }

            let pick: OptionalIconTarget | null = null;
            let earliest = Infinity;
            for (const target of targets.current) {
                if (verified.has(target.key)) continue;
                if ((tried.get(target.key) ?? 0) >= MAX_ATTEMPTS) continue;
                const due = (lastTry.get(target.key) ?? 0) + RECHECK_MS;
                if (now >= due) { pick = target; break; }
                if (due < earliest) earliest = due;
            }

            if (!pick) {
                // Everything visible is resolved or still waiting out its retry interval.
                if (earliest !== Infinity) schedule(Math.max(STEP_MS, earliest - now));
                return;
            }

            const serial = nextSerial++;
            const selected = pick;
            tried.set(selected.key, (tried.get(selected.key) ?? 0) + 1);
            lastTry.set(selected.key, now);
            lastDispatchAt = now;
            active = { ...selected, serial };
            timer = setTimeout(() => {
                if (active?.serial !== serial) return;
                active = null;
                clearTimer();
                schedule(RECHECK_MS);
            }, RESPONSE_TIMEOUT_MS);
            engine.trigger("ssp.requestOptionalIcon", `${selected.i}:${serial}`);
        };
        pump.current = next;

        const onResult = (payload: any) => {
            let result: { i: number; s: number; url: string };
            try { result = JSON.parse(typeof payload === "string" ? payload : payload?.value); }
            catch { return; } // getter subscription may replay an empty/stale payload
            if (!active || disposed || result.i !== active.i || result.s !== active.serial) return;
            const target = active;
            clearTimer();

            // While scrolling, drop a response whose tile left the window: do not start its image.
            const stillVisible = targets.current.some((t) => t.key === target.key && t.i === target.i);
            if (!stillVisible) { active = null; schedule(STEP_MS); return; }
            if (typeof result.url !== "string" || !result.url.toLowerCase().startsWith("coui://ail/")) {
                active = null;
                schedule(RECHECK_MS);
                return;
            }

            image = document.createElement("img");
            const done = (success: boolean) => {
                if (disposed || active?.serial !== target.serial) return;
                clearTimer();
                if (image) { image.onload = null; image.onerror = null; image = null; }
                if (success) {
                    remember(target.key, result.url);
                    refresh((v) => v + 1);
                }
                active = null;
                schedule(success ? STEP_MS : RECHECK_MS);
            };
            image.onload = () => done(true);
            image.onerror = () => done(false);
            timer = setTimeout(() => done(false), IMAGE_TIMEOUT_MS);
            image.src = result.url;
        };

        engine.on("ssp.optionalIconResult.update", onResult);
        engine.trigger("ssp.optionalIconResult.subscribe");
        // First check is started by the window-change effect after this subscription exists.
        return () => {
            disposed = true;
            pump.current = () => {};
            clearTimer();
            if (wake) clearTimeout(wake);
            if (image) { image.onload = null; image.onerror = null; }
            engine.off("ssp.optionalIconResult.update", onResult);
        };
    }, []);

    const shown = new Map<number, string>();
    for (const target of targets.current) {
        const url = verified.get(target.key);
        if (url) shown.set(target.i, url);
    }
    return shown;
}
