/**
 * Smart To Do — Hide Single-Tab Navigator (OnLoad)
 *
 * Form script for the OOB `sprk_todo` main form. Smart To Do R5 UAT
 * 2026-08-18 (item #3, round 2).
 *
 * # Purpose
 *
 * The To Do main form has a SINGLE tab ("General"). In Unified Interface the
 * tab NAME renders as a navigator pivot at the top of the form body — this is
 * chrome rendered independently of the tab's `ShowLabel`/`Label` properties
 * (which is why setting `showlabel="false"` in formxml AND `"ShowLabel":false`
 * in formjson had NO effect on it). The ONLY supported way to remove it is the
 * Client API call `formContext.ui.headerSection.setTabNavigatorVisible(false)`,
 * which MUST run from a form OnLoad handler so it also fires when the form is
 * opened as an `Xrm.Navigation.navigateTo` modal dialog (the SmartTodo
 * "+ New Task" / open path, target:2).
 *
 * Ref: Microsoft Learn — formContext.ui.headerSection.setTabNavigatorVisible
 * (Unified Interface only). The property is documented as the mechanism for the
 * single-tab-that-isn't-used-for-navigation case.
 *
 * # Form events to register
 *
 * 1. **OnLoad** — `Spaarke.SmartTodo.HideTabNav.onLoad`
 *    (pass execution context: Yes)
 *
 * # Behavior
 *
 * - On form load: hides the tab navigator so the single-tab form reads as a
 *   one-page form (no "General" pivot).
 * - Never throws / never blocks the form: any failure logs to console and
 *   returns silently (mirrors the sibling form-script convention).
 *
 * # Version
 *
 * v1.0.0 — initial implementation (smart-todo-r5 UAT item #3, 2026-08-18)
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
    ns.VERSION = "1.0.0";

    /**
     * Form OnLoad handler. Hides the single-tab navigator pivot ("General")
     * via the supported Unified Interface Client API. Defensive: if the API
     * isn't available (older host, unexpected context), it logs and returns
     * without ever blocking the form.
     *
     * @param {object} executionContext - Form execution context (pass first param: Yes)
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext = executionContext && executionContext.getFormContext
                ? executionContext.getFormContext()
                : null;
            if (!formContext || !formContext.ui || !formContext.ui.headerSection ||
                typeof formContext.ui.headerSection.setTabNavigatorVisible !== "function") {
                console.warn("[SmartTodo.HideTabNav v" + ns.VERSION + "] setTabNavigatorVisible unavailable, skipping");
                return;
            }
            formContext.ui.headerSection.setTabNavigatorVisible(false);
        } catch (err) {
            // NEVER block the form: log and return.
            console.error("[SmartTodo.HideTabNav v" + ns.VERSION + "] onLoad error:", err);
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
