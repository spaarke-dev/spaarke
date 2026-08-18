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
    ns.VERSION = "1.6.0";

    /**
     * Form OnLoad handler. Cleans up the form-header chrome via SUPPORTED
     * Unified Interface Client API: (1) hides the single-tab navigator pivot
     * ("General"), (2) blanks the header entity/table name ("To Do"). Each step
     * is independently guarded so a failure in one never blocks the other or
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
            var collectDocs = function () {
                var docs = [];
                var push = function (d) { if (d && docs.indexOf(d) < 0) { docs.push(d); } };
                push(document);
                var roots = [document];
                try {
                    if (window.top && window.top.document) {
                        push(window.top.document);
                        roots.push(window.top.document);
                    }
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
