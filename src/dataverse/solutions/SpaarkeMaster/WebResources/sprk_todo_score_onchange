/**
 * Smart To Do — Priority/Effort Auto-Score OnChange Handler
 *
 * Form script for the OOB `sprk_todo` main form. Smart To Do R5 task 011
 * (FR-02 / FR-03).
 *
 * # Purpose
 *
 * `sprk_priority` and `sprk_effort` are Choice columns on `sprk_todo` (task
 * 010) that let a user pick a labeled priority/effort instead of typing a
 * raw 0-100 number. This handler keeps `sprk_priorityscore` /
 * `sprk_effortscore` (the existing Number fields the composite To Do Score
 * formula reads — see cross-reference below) in sync whenever the user
 * changes either choice, directly on the form.
 *
 * # SINGLE SOURCE OF TRUTH — cross-reference (READ BEFORE EDITING)
 *
 * The two lookup tables below (`PRIORITY_TO_SCORE`, `EFFORT_TO_SCORE`) are a
 * LITERAL MIRROR of the canonical TypeScript mapping module:
 *
 *   src/client/shared/Spaarke.UI.Components/src/utils/todoScoreMappings.ts
 *
 * That module is the single source of truth, consumed directly by
 * `CreateTodoStep.tsx` (CreateTodoWizard) and by the SmartTodo Code Page
 * quick-add flow (`SmartToDo.tsx` `handleAdd`) via the `@spaarke/ui-components`
 * barrel. Dataverse web resources CANNOT `import` an npm package, so this
 * file duplicates the STATIC VALUE TABLE ONLY (not any logic) — this is the
 * ONE sanctioned literal duplication per smart-todo-r5 task 011 constraints.
 *
 * >>> IF EITHER TABLE IN `todoScoreMappings.ts` CHANGES, UPDATE THE TABLES
 * >>> BELOW TO MATCH. A mismatch here silently breaks form/wizard/quick-add
 * >>> parity (FR-02/FR-03 acceptance criteria).
 *
 * Composite To Do Score formula/weights are LOCKED in a separate file
 * (`Spaarke.SmartTodo.Components/src/utils/todoScoring.ts`) and are NOT
 * touched or mirrored here — this handler only supplies the two score INPUT
 * fields; it does not compute the composite score itself.
 *
 * # Form events to register
 *
 * 1. **OnLoad** — `Spaarke.SmartTodo.ScoreOnChange.onLoad`
 *    (registers the two field OnChange handlers; pass execution context: Yes)
 * 2. OnChange handlers are registered programmatically by `onLoad` — DO NOT
 *    also wire `sprk_priority`/`sprk_effort` OnChange directly in the form
 *    designer.
 *
 * # Behavior
 *
 * - On `sprk_priority` change: sets `sprk_priorityscore` from
 *   `PRIORITY_TO_SCORE[selectedValue]`, or the Medium null-default (50) if
 *   the field is cleared or holds an unrecognized value.
 * - On `sprk_effort` change: sets `sprk_effortscore` from
 *   `EFFORT_TO_SCORE[selectedValue]`, or the None null-default (50 — Option
 *   B quick-wins-first) if the field is cleared or holds an unrecognized
 *   value.
 * - Never throws / never blocks the form: any failure logs to console and
 *   returns silently (mirrors `sprk_todo_regarding_presave.js` convention).
 *
 * # Version
 *
 * v1.0.0 — initial implementation (smart-todo-r5 task 011, 2026-08-16)
 *
 * @namespace Spaarke.SmartTodo.ScoreOnChange
 * @see src/client/shared/Spaarke.UI.Components/src/utils/todoScoreMappings.ts (canonical TS source of truth)
 * @see src/client/webresources/js/sprk_todo_regarding_presave.js (sibling convention this file mirrors)
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = window.Spaarke || {};
Spaarke.SmartTodo = Spaarke.SmartTodo || {};
Spaarke.SmartTodo.ScoreOnChange = Spaarke.SmartTodo.ScoreOnChange || {};

(function (ns) {
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /** Version for diagnostic logging. */
    ns.VERSION = "1.0.0";

    /**
     * `sprk_priority` (Choice) -> `sprk_priorityscore`. LITERAL MIRROR of
     * `PRIORITY_TO_SCORE` in todoScoreMappings.ts — see file header.
     * Dataverse option-set integer values per task 010 schema:
     *   Urgent=100000000, High=100000001, Medium=100000002, Low=100000003.
     */
    var PRIORITY_TO_SCORE = {
        100000000: 100, // Urgent
        100000001: 75,  // High
        100000002: 50,  // Medium
        100000003: 25   // Low
    };

    /**
     * `sprk_effort` (Choice) -> `sprk_effortscore` (Option B, quick-wins-first).
     * LITERAL MIRROR of `EFFORT_TO_SCORE` in todoScoreMappings.ts — see file
     * header. Dataverse option-set integer values per task 010 schema:
     *   None=100000000, Very High=100000001, High=100000002, Medium=100000003, Low=100000004.
     */
    var EFFORT_TO_SCORE = {
        100000000: 50,  // None (null-default)
        100000001: 100, // Very High
        100000002: 75,  // High
        100000003: 50,  // Medium
        100000004: 25   // Low
    };

    /** Null-default `sprk_priorityscore` — mirrors NULL_DEFAULT_PRIORITY_SCORE. */
    var NULL_DEFAULT_PRIORITY_SCORE = 50;

    /** Null-default `sprk_effortscore` — mirrors NULL_DEFAULT_EFFORT_SCORE (Option B). */
    var NULL_DEFAULT_EFFORT_SCORE = 50;

    // -----------------------------------------------------------------------
    // OnLoad — register OnChange handlers
    // -----------------------------------------------------------------------

    /**
     * Form OnLoad handler. Registers the two OnChange bridges programmatically
     * so the form designer only needs to wire this one entry point.
     *
     * @param {object} executionContext - Form execution context (pass first param: Yes)
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext = executionContext.getFormContext();
            if (!formContext || !formContext.data || !formContext.data.entity) {
                console.warn("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] onLoad — formContext unavailable, skipping");
                return;
            }

            var priorityAttr = formContext.getAttribute("sprk_priority");
            if (priorityAttr) {
                priorityAttr.addOnChange(ns.onPriorityChange);
            } else {
                console.warn("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] sprk_priority attribute not on form");
            }

            var effortAttr = formContext.getAttribute("sprk_effort");
            if (effortAttr) {
                effortAttr.addOnChange(ns.onEffortChange);
            } else {
                console.warn("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] sprk_effort attribute not on form");
            }

            console.log("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] OnChange handlers registered");
        } catch (err) {
            console.error("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] onLoad error:", err);
        }
    };

    // -----------------------------------------------------------------------
    // OnChange handlers
    // -----------------------------------------------------------------------

    /**
     * `sprk_priority` OnChange — sets `sprk_priorityscore` from the mirrored
     * lookup table. Falls back to the Medium null-default (50) for a cleared
     * or unrecognized value; never throws.
     *
     * @param {object} executionContext - Form execution context
     */
    ns.onPriorityChange = function (executionContext) {
        try {
            var formContext = executionContext.getFormContext();
            if (!formContext) {
                return;
            }
            var priorityAttr = formContext.getAttribute("sprk_priority");
            var scoreAttr = formContext.getAttribute("sprk_priorityscore");
            if (!scoreAttr) {
                console.warn("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] sprk_priorityscore attribute not on form");
                return;
            }
            var selected = priorityAttr ? priorityAttr.getValue() : null;
            var score = (selected !== null && selected !== undefined && Object.prototype.hasOwnProperty.call(PRIORITY_TO_SCORE, selected))
                ? PRIORITY_TO_SCORE[selected]
                : NULL_DEFAULT_PRIORITY_SCORE;
            scoreAttr.setValue(score);
        } catch (err) {
            // NEVER block the form: log and return.
            console.error("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] onPriorityChange error:", err);
        }
    };

    /**
     * `sprk_effort` OnChange — sets `sprk_effortscore` from the mirrored
     * lookup table (Option B, quick-wins-first). Falls back to the None
     * null-default (50) for a cleared or unrecognized value; never throws.
     *
     * @param {object} executionContext - Form execution context
     */
    ns.onEffortChange = function (executionContext) {
        try {
            var formContext = executionContext.getFormContext();
            if (!formContext) {
                return;
            }
            var effortAttr = formContext.getAttribute("sprk_effort");
            var scoreAttr = formContext.getAttribute("sprk_effortscore");
            if (!scoreAttr) {
                console.warn("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] sprk_effortscore attribute not on form");
                return;
            }
            var selected = effortAttr ? effortAttr.getValue() : null;
            var score = (selected !== null && selected !== undefined && Object.prototype.hasOwnProperty.call(EFFORT_TO_SCORE, selected))
                ? EFFORT_TO_SCORE[selected]
                : NULL_DEFAULT_EFFORT_SCORE;
            scoreAttr.setValue(score);
        } catch (err) {
            // NEVER block the form: log and return.
            console.error("[SmartTodo.ScoreOnChange v" + ns.VERSION + "] onEffortChange error:", err);
        }
    };

    // -----------------------------------------------------------------------
    // Exports for test harnesses (no-op in MDA runtime)
    // -----------------------------------------------------------------------

    if (typeof module !== "undefined" && module.exports) {
        module.exports = {
            onLoad: ns.onLoad,
            onPriorityChange: ns.onPriorityChange,
            onEffortChange: ns.onEffortChange,
            _internals: {
                PRIORITY_TO_SCORE: PRIORITY_TO_SCORE,
                EFFORT_TO_SCORE: EFFORT_TO_SCORE,
                NULL_DEFAULT_PRIORITY_SCORE: NULL_DEFAULT_PRIORITY_SCORE,
                NULL_DEFAULT_EFFORT_SCORE: NULL_DEFAULT_EFFORT_SCORE
            },
            VERSION: ns.VERSION
        };
    }
})(Spaarke.SmartTodo.ScoreOnChange);
