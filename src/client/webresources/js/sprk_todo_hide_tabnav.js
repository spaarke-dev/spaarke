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
 * 2. **Blank the header entity/table name ("To Do")** — the `entity_name_span`
 *    in the form header. Fixed with `formContext.ui.setFormEntityName(" ")`
 *    (a single space blanks it reliably; the empty string can be ignored). This
 *    is the documented supported method (Microsoft Learn: "Sets the name of the
 *    table to be displayed on the form"). smart-todo-r5 UAT 2026-08-18 item #2.
 *
 * Both MUST run from a FORM OnLoad handler so they also fire when the form is
 * opened as an `Xrm.Navigation.navigateTo` modal dialog (the SmartTodo
 * "+ New Task" / open path, target:2).
 *
 * Refs (Microsoft Learn, Unified Interface only):
 *   - formContext.ui.headerSection.setTabNavigatorVisible
 *   - formContext.ui.setFormEntityName
 *
 * # Form events to register
 *
 * 1. **OnLoad** — `Spaarke.SmartTodo.HideTabNav.onLoad`
 *    (pass execution context: Yes)
 *
 * # Behavior
 *
 * - On form load: hides the tab navigator AND blanks the header entity name.
 * - Never throws / never blocks the form: each step is independently guarded;
 *   any failure logs to console and continues (mirrors the sibling convention).
 *
 * # Version
 *
 * v1.1.0 — add header entity-name blanking (UAT item #2, 2026-08-18)
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
    ns.VERSION = "1.1.0";

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

        // 2) Blank the header entity/table name ("To Do"). A single space blanks
        //    it reliably (the empty string can be ignored by the renderer).
        try {
            if (typeof formContext.ui.setFormEntityName === "function") {
                formContext.ui.setFormEntityName(" ");
            } else {
                console.warn("[SmartTodo.HideTabNav v" + ns.VERSION + "] setFormEntityName unavailable");
            }
        } catch (err) {
            console.error("[SmartTodo.HideTabNav v" + ns.VERSION + "] setFormEntityName error:", err);
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
