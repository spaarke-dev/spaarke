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
 *    operator-approved DOM hide, SCOPED to the `navigateTo` modal dialog
 *    (`[aria-modal="true"]`) so it never affects a background full-page form.
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
    ns.VERSION = "1.3.0";

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
        //    SCOPED to the `navigateTo` modal dialog (`[aria-modal="true"]`) so
        //    it NEVER hides a background full-page form's entity name (which
        //    would stay hidden after the modal closes). Bounded retry (~2s)
        //    covers the header's async render inside the dialog.
        try {
            var attempts = 0;
            var hideEntityName = function () {
                attempts++;
                var spans = document.querySelectorAll('[aria-modal="true"] [data-id="entity_name_span"]');
                if (spans.length > 0) {
                    for (var i = 0; i < spans.length; i++) {
                        spans[i].style.display = "none";
                    }
                    return;
                }
                if (attempts < 20) {
                    window.setTimeout(hideEntityName, 100);
                }
            };
            hideEntityName();
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
