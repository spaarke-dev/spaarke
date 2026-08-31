/**
 * todoScoreMappings.ts
 *
 * SINGLE SOURCE OF TRUTH for the `sprk_todo` Priority/Effort choice -> score
 * mapping (spec FR-02/FR-03, smart-todo-r5 task 011).
 *
 * This module supplies ONLY the choice -> score INPUT tables. It does NOT
 * touch, duplicate, or reimplement the composite To Do Score formula/weights,
 * which remain locked in `Spaarke.SmartTodo.Components/src/utils/todoScoring.ts`
 * (`computeTodoScore` — priority 0.50 / effort 0.20 (inverted) / urgency 0.30).
 *
 * # Three consumers (parity requirement)
 *
 * 1. **CreateTodoWizard** (`../components/CreateTodoWizard/CreateTodoStep.tsx`)
 *    — imports this module directly (same package).
 * 2. **SmartTodo Code Page quick-add** (`src/solutions/SmartTodo/src/components/SmartToDo.tsx`
 *    `handleAdd`) — imports this module via the `@spaarke/ui-components` barrel
 *    (SmartToDo already depends on `@spaarke/ui-components` for `KanbanBoard` /
 *    `OrientationToggle`, so no new cross-package dependency edge is introduced).
 * 3. **`sprk_todo` form OnChange webresource**
 *    (`src/client/webresources/js/sprk_todo_score_onchange.js`) — Dataverse
 *    webresources cannot import npm packages, so that file MIRRORS the two
 *    tables below as a literal object with an explicit cross-reference comment
 *    back to this module. That mirror is the ONLY sanctioned literal
 *    duplication (CLAUDE.md §11 / task 011 constraints) — if either table
 *    below changes, the webresource literal MUST be updated to match.
 *
 * # Option B (quick-wins-first) — owner-locked 2026-08-15
 *
 * Effort scoring is inverted relative to naive intuition: LOW effort scores
 * LOW (25) and HIGH effort scores HIGH (75) — i.e., the choice value and its
 * score move together. The *inversion that produces quick-wins-first
 * behavior* happens downstream in the locked composite formula
 * (`effortComponent = (100 - rawEffort) * 0.20`), NOT in this table. Do not
 * "double invert" by flipping this table.
 *
 * @see spec.md FR-02, FR-03, and the "F-5 effort direction" Owner Clarification row
 * @see ../../../Spaarke.SmartTodo.Components/src/utils/todoScoring.ts (locked formula — untouched by this module)
 */

// ---------------------------------------------------------------------------
// Choice types
// ---------------------------------------------------------------------------

/** `sprk_priority` choice labels (Dataverse option set — task 010 schema). */
export type TodoPriorityChoice = 'Urgent' | 'High' | 'Medium' | 'Low';

/** `sprk_effort` choice labels (Dataverse option set — task 010 schema). */
export type TodoEffortChoice = 'Low' | 'Medium' | 'High' | 'Very High' | 'None';

// ---------------------------------------------------------------------------
// Canonical mapping tables (FR-02 / FR-03 — EXACT values, no additions)
// ---------------------------------------------------------------------------

/** `sprk_priority` -> `sprk_priorityscore`. Unset/unrecognized falls back to Medium (50). */
export const PRIORITY_TO_SCORE: Readonly<Record<TodoPriorityChoice, number>> = Object.freeze({
  Urgent: 100,
  High: 75,
  Medium: 50,
  Low: 25,
});

/**
 * `sprk_effort` -> `sprk_effortscore` (Option B, quick-wins-first — owner-locked).
 * Unset/unrecognized falls back to None (50) — reproduces today's null-default.
 */
export const EFFORT_TO_SCORE: Readonly<Record<TodoEffortChoice, number>> = Object.freeze({
  Low: 25,
  Medium: 50,
  High: 75,
  'Very High': 100,
  None: 50,
});

// ---------------------------------------------------------------------------
// Null-defaults (reproduce today's pre-choice-field behavior — no regression)
// ---------------------------------------------------------------------------

/** Default `sprk_priority` choice when nothing is selected — reproduces the historical 50 default. */
export const DEFAULT_PRIORITY_CHOICE: TodoPriorityChoice = 'Medium';

/** Default `sprk_effort` choice when nothing is selected (Option B null-default). */
export const DEFAULT_EFFORT_CHOICE: TodoEffortChoice = 'None';

/** `PRIORITY_TO_SCORE[DEFAULT_PRIORITY_CHOICE]` — always 50. Exported for readability at call sites. */
export const NULL_DEFAULT_PRIORITY_SCORE: number = PRIORITY_TO_SCORE[DEFAULT_PRIORITY_CHOICE];

/** `EFFORT_TO_SCORE[DEFAULT_EFFORT_CHOICE]` — always 50 (Option B). Exported for readability at call sites. */
export const NULL_DEFAULT_EFFORT_SCORE: number = EFFORT_TO_SCORE[DEFAULT_EFFORT_CHOICE];

// ---------------------------------------------------------------------------
// Helpers — defensive, never throw (AC: unrecognized value falls back, no crash)
// ---------------------------------------------------------------------------

/**
 * Resolve a `sprk_priority` choice value to its score. Falls back to the
 * Medium null-default (50) for null/undefined/unrecognized input — never throws.
 */
export function priorityChoiceToScore(choice: string | null | undefined): number {
  if (choice && Object.prototype.hasOwnProperty.call(PRIORITY_TO_SCORE, choice)) {
    return PRIORITY_TO_SCORE[choice as TodoPriorityChoice];
  }
  return NULL_DEFAULT_PRIORITY_SCORE;
}

/**
 * Resolve a `sprk_effort` choice value to its score (Option B). Falls back to
 * the None null-default (50) for null/undefined/unrecognized input — never throws.
 */
export function effortChoiceToScore(choice: string | null | undefined): number {
  if (choice && Object.prototype.hasOwnProperty.call(EFFORT_TO_SCORE, choice)) {
    return EFFORT_TO_SCORE[choice as TodoEffortChoice];
  }
  return NULL_DEFAULT_EFFORT_SCORE;
}

/**
 * Reverse-lookup: given a raw `sprk_priorityscore` value, find the choice
 * whose mapped score matches exactly. Used by the wizard to pre-select a
 * Dropdown option when initializing from a numeric score (e.g. edit flows).
 * Returns `DEFAULT_PRIORITY_CHOICE` when no exact match is found.
 */
export function scoreToPriorityChoice(score: number | null | undefined): TodoPriorityChoice {
  if (score === null || score === undefined) return DEFAULT_PRIORITY_CHOICE;
  const match = (Object.keys(PRIORITY_TO_SCORE) as TodoPriorityChoice[]).find(key => PRIORITY_TO_SCORE[key] === score);
  return match ?? DEFAULT_PRIORITY_CHOICE;
}

/**
 * Reverse-lookup: given a raw `sprk_effortscore` value, find the choice whose
 * mapped score matches exactly. Returns `DEFAULT_EFFORT_CHOICE` when no exact
 * match is found.
 */
export function scoreToEffortChoice(score: number | null | undefined): TodoEffortChoice {
  if (score === null || score === undefined) return DEFAULT_EFFORT_CHOICE;
  const match = (Object.keys(EFFORT_TO_SCORE) as TodoEffortChoice[]).find(key => EFFORT_TO_SCORE[key] === score);
  return match ?? DEFAULT_EFFORT_CHOICE;
}

/** Ordered list of priority choices, for rendering Dropdown/RadioGroup options. */
export const TODO_PRIORITY_CHOICES: readonly TodoPriorityChoice[] = Object.freeze(['Urgent', 'High', 'Medium', 'Low']);

/** Ordered list of effort choices, for rendering Dropdown/RadioGroup options. */
export const TODO_EFFORT_CHOICES: readonly TodoEffortChoice[] = Object.freeze([
  'Low',
  'Medium',
  'High',
  'Very High',
  'None',
]);
