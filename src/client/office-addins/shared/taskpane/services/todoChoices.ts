/**
 * todoChoices.ts — add-in-local mirror of the `sprk_todo` Priority/Effort
 * choice → score tables.
 *
 * ⚠️ SANCTIONED DUPLICATION (CLAUDE.md §11): the canonical source of truth is
 * `@spaarke/ui-components` → `src/utils/todoScoreMappings.ts` (smart-todo-r5
 * task 011, FR-02/FR-03). The Office add-in intentionally does NOT depend on
 * `@spaarke/ui-components` (an Xrm/PCF-oriented package whose barrel would bloat
 * and break the Office.js webpack build), so — exactly like the `sprk_todo`
 * OnChange webresource mirror documented in that module — these two tables are
 * duplicated here as literals with a cross-reference. **If either table changes
 * in `todoScoreMappings.ts`, update this mirror to match.**
 *
 * The add-in resolves the choice → score CLIENT-SIDE (same as the wizard) and
 * sends the 0-100 score to `POST /api/office/todo`, keeping the BFF write a
 * plain integer set.
 */

export type TodoPriorityChoice = 'Urgent' | 'High' | 'Medium' | 'Low';
export type TodoEffortChoice = 'Low' | 'Medium' | 'High' | 'Very High' | 'None';

/** `sprk_priority` → `sprk_priorityscore`. Mirrors PRIORITY_TO_SCORE. */
const PRIORITY_TO_SCORE: Record<TodoPriorityChoice, number> = {
  Urgent: 100,
  High: 75,
  Medium: 50,
  Low: 25,
};

/** `sprk_effort` → `sprk_effortscore` (Option B, quick-wins-first). Mirrors EFFORT_TO_SCORE. */
const EFFORT_TO_SCORE: Record<TodoEffortChoice, number> = {
  Low: 25,
  Medium: 50,
  High: 75,
  'Very High': 100,
  None: 50,
};

export const TODO_PRIORITY_CHOICES: readonly TodoPriorityChoice[] = ['Urgent', 'High', 'Medium', 'Low'];
export const TODO_EFFORT_CHOICES: readonly TodoEffortChoice[] = ['Low', 'Medium', 'High', 'Very High', 'None'];

/** Default when nothing selected — reproduces the historical 50 defaults. */
export const DEFAULT_PRIORITY_CHOICE: TodoPriorityChoice = 'Medium';
export const DEFAULT_EFFORT_CHOICE: TodoEffortChoice = 'None';

/** Resolve a Priority choice to its 0-100 score (falls back to Medium=50). */
export function priorityChoiceToScore(choice: string): number {
  return PRIORITY_TO_SCORE[choice as TodoPriorityChoice] ?? PRIORITY_TO_SCORE[DEFAULT_PRIORITY_CHOICE];
}

/** Resolve an Effort choice to its 0-100 score (falls back to None=50). */
export function effortChoiceToScore(choice: string): number {
  return EFFORT_TO_SCORE[choice as TodoEffortChoice] ?? EFFORT_TO_SCORE[DEFAULT_EFFORT_CHOICE];
}
