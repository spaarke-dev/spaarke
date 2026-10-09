/**
 * Smart To Do — Form Header Chrome Cleanup (OnLoad)
 *
 * Form script for the OOB `sprk_todo` main form. Smart To Do R5 UAT 2026-08-18.
 *
 * # Purpose
 *
 * Two header-chrome cleanups, both via SUPPORTED Unified Interface Client API
 * (no DOM manipulation), so the To Do modal reads as a clean one-page form:
 *
 * 1. **Hide the single-tab navigator pivot ("General")** — the tab NAME renders
 *    independently of the tab's `ShowLabel`/`Label` (which is why formxml
 *    `showlabel="false"` and formjson `"ShowLabel":false` had NO effect). Fixed
 *    with `formContext.ui.headerSection.setTabNavigatorVisible(false)`.
 *
 * 2. **Hide the header entity-name SUBTITLE ("To Do")** — the
 *    `entity_name_span`. There is NO supported Client API for it
 *    (`setFormEntityName` targets the record-title PREFIX, not this span — it
 *    added a stray ": " colon and was reverted). So this uses an UNSUPPORTED,
 *    operator-approved DOM hide. As of v1.5.0 the hide is UNSCOPED (every
 *    `entity_name_span`): the earlier dialog-scoped guard skipped the span
 *    because its ancestor chain does not expose `[aria-modal]`/`[role=dialog]`.
 *    Safe because this handler runs ONLY on the sprk_todo form, which opens over
 *    a code-page / widget host with no background entity form.
 *
 * Both MUST run from a FORM OnLoad handler so they also fire when the form is
 * opened as an `Xrm.Navigation.navigateTo` modal dialog (target:2).
 *
 * Ref (Microsoft Learn, Unified Interface only):
 *   - formContext.ui.headerSection.setTabNavigatorVisible
 *
 * # Form events to register
 *
 * 1. **OnLoad** — `Spaarke.SmartTodo.HideTabNav.onLoad`
 *    (pass execution context: Yes)
 *
 * # Behavior
 *
 * - On form load: hides the tab navigator AND (DOM-hide) the modal's entity-name
 *   subtitle so the single-tab form reads clean.
 * - Never throws / never blocks the form: each step guarded; failures log and continue.
 *
 * # Version
 *
 * v1.7.0 — modal theme polish: (3) inject the Spaarke thin scrollbar into the
 *          modal (form-scoped, theme-gated color) + (4) dark dialog-chrome
 *          recolor (dark bar, WHITE title/icons, dark divider) gated on the app
 *          theme (shell-luminance, "follow app theme" per operator). (2026-08-18)
 * v1.6.0 — entity-name hide made CROSS-FRAME (root cause: the span lives in the
 *          shell/window.top document, NOT the form iframe this handler queried —
 *          confirmed by live console test). Now hides + observes across the form
 *          doc + window.top.document + iframes of both. (2026-08-18)
 * v1.5.0 — entity-name hide made UNSCOPED (dialog-scope skipped the span; the
 *          entity_name_span ancestor chain does not carry [aria-modal]/[role=dialog]
 *          where closest() looked). Safe: this OnLoad fires only on sprk_todo forms,
 *          and To Do opens over a code-page/widget host (no background entity form). (2026-08-18)
 * v1.4.0 — entity-name hide made durable: closest()-scoped + MutationObserver re-hide (2026-08-18)
 * v1.3.0 — hide entity-name subtitle via scoped DOM hide (UAT #2, operator-approved) (2026-08-18)
 * v1.2.0 — revert setFormEntityName (caused a ": " colon; wrong element) (2026-08-18)
 * v1.1.0 — add header entity-name blanking (later reverted) (2026-08-18)
 * v1.0.0 — initial: hide single-tab navigator (2026-08-18)
 *
 * @namespace Spaarke.SmartTodo.HideTabNav
 * @see src/client/webresources/js/sprk_todo_score_onchange.js (sibling convention this file mirrors)
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = window.Spaarke || {};
Spaarke.SmartTodo = Spaarke.SmartTodo || {};
Spaarke.SmartTodo.HideTabNav = Spaarke.SmartTodo.HideTabNav || {};

(function (ns) {
    /** Version for diagnostic logging. */
    ns.VERSION = "1.7.0";

    /**
     * Collect the document set the form chrome can live in: this handler's own
     * (form iframe) document + the shell (`window.top`) document + the iframes
     * of both. UCI paints the record header AND the dialog chrome in the SHELL
     * document, not the form iframe — so anything touching chrome MUST look here,
     * not just at `document`. All same-origin (*.crm.dynamics.com); each access
     * is try-guarded so a cross-origin frame is silently skipped.
     */
    var collectDocs = function () {
        var docs = [];
        var push = function (d) { if (d && docs.indexOf(d) < 0) { docs.push(d); } };
        push(document);
        var roots = [document];
        try {
            if (window.top && window.top.document) { push(window.top.document); roots.push(window.top.document); }
        } catch (e) { /* cross-origin top — skip */ }
        for (var r = 0; r < roots.length; r++) {
            try {
                var frames = roots[r].querySelectorAll("iframe");
                for (var f = 0; f < frames.length; f++) {
                    try { push(frames[f].contentDocument); } catch (e2) { /* cross-origin frame */ }
                }
            } catch (e3) { /* no-op */ }
        }
        return docs;
    };

    /**
     * "Follow the app/Dataverse theme" (operator decision 2026-08-18): sample the
     * shell's own chrome luminance rather than the OS `prefers-color-scheme`, so
     * the modal chrome tracks whatever theme the model-driven app is actually
     * rendering per-user. Falls back to `prefers-color-scheme` only when the
     * shell background is indeterminate (transparent / unreadable).
     */
    var isAppDark = function () {
        var lumOf = function (win, el) {
            try {
                var m = /rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/.exec(win.getComputedStyle(el).backgroundColor);
                if (!m) { return null; }
                if (m[4] !== undefined && parseFloat(m[4]) === 0) { return null; } // transparent — no signal
                return 0.299 * +m[1] + 0.587 * +m[2] + 0.114 * +m[3];
            } catch (e) { return null; }
        };
        try {
            var win = window.top || window;
            var doc = win.document;
            // Sample the shell body + main app region; first readable one wins.
            var candidates = [doc.body];
            var main = doc.querySelector("#shell-container, [data-id='shell-container'], main, #ApplicationShell");
            if (main) { candidates.unshift(main); }
            for (var i = 0; i < candidates.length; i++) {
                if (!candidates[i]) { continue; }
                var lum = lumOf(win, candidates[i]);
                if (lum !== null) { return lum < 128; }
            }
        } catch (e) { /* fall through */ }
        try { return !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches); }
        catch (e) { return false; }
    };

    /**
     * Form OnLoad handler. Cleans up the form-header chrome via SUPPORTED
     * Unified Interface Client API: (1) hides the single-tab navigator pivot
     * ("General"), (2) hides the header entity/table name ("To Do"); plus two
     * theme polishes: (3) the Spaarke thin scrollbar inside the modal, and
     * (4) a dark dialog-chrome recolor when the app is in dark theme. Each step
     * is independently guarded so a failure in one never blocks the others or
     * the form.
     *
     * @param {object} executionContext - Form execution context (pass first param: Yes)
     */
    ns.onLoad = function (executionContext) {
        var formContext = executionContext && executionContext.getFormContext
            ? executionContext.getFormContext()
            : null;
        if (!formContext || !formContext.ui) {
            console.warn("[SmartTodo.HideTabNav v" + ns.VERSION + "] formContext.ui unavailable, skipping");
            return;
        }

        // 1) Hide the single-tab navigator pivot ("General").
        try {
            if (formContext.ui.headerSection &&
                typeof formContext.ui.headerSection.setTabNavigatorVisible === "function") {
                formContext.ui.headerSection.setTabNavigatorVisible(false);
            } else {
                console.warn("[SmartTodo.HideTabNav v" + ns.VERSION + "] setTabNavigatorVisible unavailable");
            }
        } catch (err) {
            console.error("[SmartTodo.HideTabNav v" + ns.VERSION + "] setTabNavigatorVisible error:", err);
        }

        // 2) Hide the header entity-name SUBTITLE ("To Do").
        //
        //    ⚠ UNSUPPORTED DOM approach — used deliberately (operator-approved,
        //    2026-08-18 UAT) because there is NO supported Client API for this
        //    span: `setFormEntityName` targets the record-title PREFIX (it added
        //    a stray ": " colon — reverted in v1.2.0), and `headerSection`
        //    exposes no entity-name setter. Microsoft discourages DOM
        //    manipulation of UCI; the `data-id="entity_name_span"` hook is
        //    reasonably stable but MAY break on a platform UI update.
        //
        //    v1.4.0 rewrite: the v1.3.0 one-shot descendant-selector hide DID
        //    NOT stick — UCI renders/refreshes the header in PHASES (the form
        //    loads to "- Saved"), so a single early hide is undone by a later
        //    re-render. The v1.4.0 fix installed a MutationObserver but SCOPED
        //    the hide to spans inside a `closest('[aria-modal],[role=dialog]')`
        //    ancestor — and that scope SKIPPED the span (UAT 2026-08-18: the
        //    entity_name_span's ancestor chain does not expose those attributes
        //    where closest() looked, so the guard never matched).
        //
        //    v1.5.0: dropped the dialog scope — but STILL failed in UAT.
        //
        //    v1.6.0 (ROOT CAUSE): every prior version queried only THIS handler's
        //    `document` — i.e. the FORM's iframe document. But UCI paints the
        //    record header (title + entity-name subtitle) in a DIFFERENT document
        //    (the shell / `window.top`), NOT inside the form iframe. So the query
        //    found ZERO spans and hid nothing. A live console test (2026-08-18)
        //    confirmed the span IS reachable from `window.top.document`.
        //
        //    Fix: collect the same document set the working console snippet used —
        //    the form's own doc + `window.top.document` + the iframes of both —
        //    and hide/observe across all of them. All same-origin
        //    (*.crm.dynamics.com), so cross-frame access is allowed; each access
        //    is try-guarded so a cross-origin frame is silently skipped.
        try {
            var hideEntityNames = function () {
                var docs = collectDocs();
                for (var d = 0; d < docs.length; d++) {
                    var spans;
                    try { spans = docs[d].querySelectorAll('[data-id="entity_name_span"]'); }
                    catch (e) { continue; }
                    for (var i = 0; i < spans.length; i++) {
                        spans[i].style.display = "none";
                    }
                }
            };

            hideEntityNames(); // immediate pass

            if (typeof MutationObserver === "function") {
                // Observe every collected doc's body so a late header re-render
                // (in any frame) is re-hidden. Disconnect all after 30s.
                var observers = [];
                var obsDocs = collectDocs();
                for (var k = 0; k < obsDocs.length; k++) {
                    try {
                        if (obsDocs[k].body) {
                            var mo = new MutationObserver(hideEntityNames);
                            mo.observe(obsDocs[k].body, { childList: true, subtree: true });
                            observers.push(mo);
                        }
                    } catch (e) { /* frame not observable — skip */ }
                }
                window.setTimeout(function () {
                    for (var m = 0; m < observers.length; m++) {
                        try { observers[m].disconnect(); } catch (e) { /* no-op */ }
                    }
                }, 30000);
            } else {
                // No observer — fall back to a bounded retry for async render.
                var attempts = 0;
                var retry = function () {
                    attempts++;
                    hideEntityNames();
                    if (attempts < 30) { window.setTimeout(retry, 100); }
                };
                retry();
            }
        } catch (err) {
            console.error("[SmartTodo.HideTabNav v" + ns.VERSION + "] entity-name hide error:", err);
        }

        // 3) Thin Spaarke scrollbar INSIDE the modal.
        //
        //    Mirrors the canonical numbers from @spaarke/ui-components
        //    theme/scrollbar.ts (8px, 4px radius, transparent track); thumb color
        //    follows the app theme. Injected as a <style> ONLY into the FORM's
        //    own document (+ its child iframes) — NOT window.top — so it scopes
        //    to the modal's form content and never restyles the whole MDA shell.
        try {
            var dark3 = isAppDark();
            var thumb = dark3 ? "#5a5a5a" : "#c7c7c7";
            var thumbHover = dark3 ? "#6f6f6f" : "#b0b0b0";
            var css =
                "*::-webkit-scrollbar{width:8px;height:8px;}" +
                "*::-webkit-scrollbar-track{background:transparent;}" +
                "*::-webkit-scrollbar-thumb{background:" + thumb + ";border-radius:4px;}" +
                "*::-webkit-scrollbar-thumb:hover{background:" + thumbHover + ";}" +
                "*{scrollbar-width:thin;scrollbar-color:" + thumb + " transparent;}";
            // FORM-scoped docs only (this iframe + its child iframes), never top.
            var sbDocs = [document];
            try {
                var sbFrames = document.querySelectorAll("iframe");
                for (var sf = 0; sf < sbFrames.length; sf++) {
                    try { if (sbFrames[sf].contentDocument && sbDocs.indexOf(sbFrames[sf].contentDocument) < 0) { sbDocs.push(sbFrames[sf].contentDocument); } } catch (e) { /* cross-origin */ }
                }
            } catch (e) { /* no-op */ }
            for (var sd = 0; sd < sbDocs.length; sd++) {
                try {
                    var sbDoc = sbDocs[sd];
                    var head = sbDoc.head || (sbDoc.getElementsByTagName("head")[0]);
                    if (!head) { continue; }
                    var prior = sbDoc.getElementById("sprk-todo-thin-scrollbar");
                    if (prior && prior.parentNode) { prior.parentNode.removeChild(prior); } // refresh (theme may have changed)
                    var styleEl = sbDoc.createElement("style");
                    styleEl.id = "sprk-todo-thin-scrollbar";
                    styleEl.textContent = css;
                    head.appendChild(styleEl);
                } catch (e) { /* frame not writable — skip */ }
            }
        } catch (err) {
            console.error("[SmartTodo.HideTabNav v" + ns.VERSION + "] scrollbar inject error:", err);
        }

        // 4) Dark dialog-chrome recolor — ONLY when the app is in dark theme.
        //
        //    The navigateTo dialog chrome (top bar: title + pop-out/close icons)
        //    is painted WHITE by UCI in the shell (window.top) even in dark mode,
        //    and there is NO supported API to theme it. So we recolor via the
        //    same geometry approach a live console test confirmed (2026-08-18):
        //    within the dialog element, find the wide light bar near the top →
        //    dark background + WHITE title/icons; find the thin light divider →
        //    dark. SCOPED to the dialog element so the shell behind it is
        //    untouched. Runs as BOUNDED timed passes (chrome re-renders a few
        //    times right after open) rather than a permanent observer, to keep
        //    layout cost near zero. ⚠ UNSUPPORTED DOM — operator-approved; the
        //    geometry heuristic MAY need revisiting on a platform UI update.
        try {
            if (isAppDark()) {
                var DARK = "#1f1f1f", LIGHT = "#ffffff";
                var isLightColor = function (c) {
                    var m = /rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(c);
                    return m && +m[1] > 220 && +m[2] > 220 && +m[3] > 220;
                };
                var recolorChrome = function () {
                    var win;
                    try { win = window.top || window; } catch (e) { win = window; }
                    var cdoc;
                    try { cdoc = win.document; } catch (e) { return; }
                    var dlg = cdoc.querySelector('[aria-modal="true"], [role="dialog"]');
                    if (!dlg) { return; }
                    var dr = dlg.getBoundingClientRect();
                    var els = dlg.querySelectorAll("*");
                    for (var i = 0; i < els.length; i++) {
                        var el = els[i];
                        var r = el.getBoundingClientRect();
                        var cs;
                        try { cs = win.getComputedStyle(el); } catch (e) { continue; }
                        // Wide bar near the top → dark bg + white text/icons.
                        if (r.top <= dr.top + 80 && r.width > dr.width * 0.6 && r.height >= 8 && r.height < 80 && isLightColor(cs.backgroundColor)) {
                            el.style.setProperty("background-color", DARK, "important");
                            el.style.setProperty("box-shadow", "none", "important");
                            el.style.setProperty("border-color", DARK, "important");
                            var kids = el.querySelectorAll("*");
                            for (var k = 0; k < kids.length; k++) {
                                kids[k].style.setProperty("color", LIGHT, "important");
                                kids[k].style.setProperty("fill", LIGHT, "important");
                            }
                        }
                        // Thin light divider near the top → dark.
                        if (r.top <= dr.top + 130 && r.width > dr.width * 0.6 && r.height <= 6 &&
                            (isLightColor(cs.backgroundColor) || isLightColor(cs.borderTopColor) || isLightColor(cs.borderBottomColor))) {
                            el.style.setProperty("background-color", DARK, "important");
                            el.style.setProperty("border-color", DARK, "important");
                            el.style.setProperty("box-shadow", "none", "important");
                        }
                    }
                };
                var delays = [0, 150, 400, 800, 1500, 3000];
                for (var t = 0; t < delays.length; t++) {
                    (function (ms) { window.setTimeout(recolorChrome, ms); })(delays[t]);
                }
            }
        } catch (err) {
            console.error("[SmartTodo.HideTabNav v" + ns.VERSION + "] dark chrome error:", err);
        }
    };

    // -----------------------------------------------------------------------
    // Exports for test harnesses (no-op in MDA runtime)
    // -----------------------------------------------------------------------

    if (typeof module !== "undefined" && module.exports) {
        module.exports = {
            onLoad: ns.onLoad,
            VERSION: ns.VERSION
        };
    }
})(Spaarke.SmartTodo.HideTabNav);
