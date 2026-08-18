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
 * NOTE (2026-08-18 UAT): a second attempt to blank the header entity-name
 * SUBTITLE ("To Do") via `formContext.ui.setFormEntityName(" ")` was REVERTED —
 * empirically that API prefixes the record TITLE (renders "{name}: {primary}"),
 * so a space produced a stray ": " colon in front of the record name and did
 * NOT touch the "To Do" subtitle. No supported Client API hides that subtitle.
 *
 * MUST run from a FORM OnLoad handler so it also fires when the form is opened
 * as an `Xrm.Navigation.navigateTo` modal dialog (target:2).
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
 * - On form load: hides the tab navigator so the single-tab form reads clean.
 * - Never throws / never blocks the form: guarded; failures log and continue.
 *
 * # Version
 *
 * v1.2.0 — revert setFormEntityName (caused a ": " colon; wrong element) (2026-08-18)
 * v1.1.0 — add header entity-name blanking (later reverted) (2026-08-18)
 * v1.0.0 — initial: hide single-tab navigator (UAT item #3, 2026-08-18)
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
    ns.VERSION = "1.2.0";

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

        // 2) (Reverted 2026-08-18 UAT) — `setFormEntityName(" ")` was NOT the
        //    right lever for the "To Do" entity-name SUBTITLE: empirically it
        //    PREFIXES the record TITLE (rendered "{name}: {primary}"), so a
        //    single space produced a stray ": " colon in front of the record
        //    name AND left the "To Do" subtitle untouched. Removed. The subtitle
        //    hide (if pursued) needs a different, non-title mechanism.
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
