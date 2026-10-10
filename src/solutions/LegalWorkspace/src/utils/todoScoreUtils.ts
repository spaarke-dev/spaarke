/**
 * todoScoreUtils — To Do Score computation for Smart To Do list sorting.
 *
 * Combines priority, effort, and due-date urgency into a single 0-100
 * composite score used to rank items in the To Do list. Higher scores
 * surface the most important / time-sensitive / achievable items first.
 *
 * Formula:
 *   todoScore = (priorityScore * 0.50)
 *             + (invertedEffort * 0.20)
 *             + (dueDateUrgency * 0.30)
 *
 * Component weights:
 *   Priority (0.50) — user-stated importance is the primary driver.
 *   Effort inverted (0.20) — lower effort = quick wins bubble up.
 *   Due date urgency (0.30) — time pressure adds urgency bonus.
 *
 * All inputs come from existing `sprk_todo` (ITodo) fields — this is a
 * client-side convenience sort key and does NOT replace the BFF-computed
 * sprk_priorityscore / sprk_effortscore on the record itself.
 *
 * Per R3 FR-29 / OS-1, the legacy IEvent-based scoring path is removed.
 */

import type { ITodo } from '../types/entities';
import { computeTodoScoreBreakdown, type ITodoScoreBreakdown } from '@spaarke/ui-components';

// Task 067 / D-29 (FR-63): the composite score and its urgency component are ONE
// shared function, `computeTodoScoreBreakdown` in `@spaarke/ui-components`
// (`utils/dateLocal.ts`), counting CALENDAR days like the due label. This module
// used to keep a private copy of the weights and the millisecond-based urgency
// math; it is now only the ITodo-typed entry point existing importers use.

export type { ITodoScoreBreakdown };

/**
 * Compute the To Do Score for a `sprk_todo` record.
 *
 * @param todo - The to-do record with priority/effort/due-date fields.
 * @returns Breakdown with the final todoScore and per-component values.
 */
export function computeTodoScore(todo: ITodo): ITodoScoreBreakdown {
  return computeTodoScoreBreakdown(todo);
}
