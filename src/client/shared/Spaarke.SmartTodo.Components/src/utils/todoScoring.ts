/**
 * todoScoring — Shared scoring + due-date helpers used by the hoisted
 * SmartTodo Kanban surface (R4 task 102 / E-1, 2026-06-18).
 *
 * Why this file exists: the peer package is host-agnostic by design, so the
 * composite-score math lives here rather than in a `src/solutions/...` Code
 * Page (reaching into one would invert the dependency direction). The Code
 * Page's `utils/todoScoreUtils.ts` and `utils/dueLabelUtils.ts` now re-export
 * from this package instead of keeping copies.
 *
 *   - Composite score (task 067 / D-29): ONE implementation, `computeTodoScoreBreakdown`
 *     in `@spaarke/ui-components` `utils/dateLocal.ts` (weights priority 0.50, effort
 *     inverted 0.20, urgency 0.30; urgency points overdue=100, 0-3d=80, 4-7d=50,
 *     8-10d=25, else=0, counted in CALENDAR days like the due label). Moved there
 *     because `TodoDetail` lives in that package and cannot import this one.
 *   - DueLabel COLOUR tiers (`urgency`): from the shared `dueUrgencyForDays`
 *     (`@spaarke/ui-components` `utils/dateLocal.ts`, task 081 / C-17) — the
 *     ONE 3/7/10 tier function every due-date surface calls.
 *     NOTE (smart-todo-r5 UAT 2026-08-17, item #6): the badge LABEL text is a
 *     real calendar-day countdown ("Overdue" / "Today" / "{n}d"), not the tier
 *     name — so a task due today reads "Today" instead of a misleading "3d".
 *
 * `parseDueDate`'s local-midnight fix (spaarke-ontology-platform-r1 task 080
 * / C-10, 2026-10-03): this file no longer defines its own copy. Two other
 * copies of this exact function had drifted out of sync with it — one in
 * `hooks/useKanbanColumns.ts` (this package), one in
 * `Spaarke.UI.Components/.../TodoDetail/TodoDetail.tsx` — so a To Do's
 * Kanban bucket and its own detail-view score disagreed by a day in every
 * negative-UTC-offset zone. The fix is now the single copy in
 * `@spaarke/ui-components` (`utils/dateLocal.ts`), which both packages
 * already depend on; the composite-score FORMULA/WEIGHTS below remain
 * locked in THIS file per `todoScoreMappings.ts`'s own doc comment — only
 * the date-parsing primitive moved.
 *
 * @see hooks/useKanbanColumns.ts (imports the same parseDueDate)
 */

import {
  parseDueDate,
  daysBetweenLocalMidnight,
  dueUrgencyForDays,
  computeTodoScoreBreakdown,
  type DueUrgency,
  type ITodoScoreBreakdown,
} from '@spaarke/ui-components';
import type { IKanbanTodoLike } from '../types/kanban';

export { parseDueDate };
export type { DueUrgency, ITodoScoreBreakdown };

// ---------------------------------------------------------------------------
// Due-date label types (`DueUrgency` is re-exported from `@spaarke/ui-components`)
// ---------------------------------------------------------------------------

/** Computed due label with a short display string and urgency tier. */
export interface IDueLabel {
  /** Short badge text shown in the UI (empty string when urgency is 'none'). */
  label: string;
  /** Urgency tier used to select badge colour. */
  urgency: DueUrgency;
}

// ---------------------------------------------------------------------------
// Parsing — `parseDueDate` is imported from `@spaarke/ui-components` above
// and re-exported for existing consumers of this module; see the task-080
// doc comment at the top of this file for why.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Display label (discrete tier used by the card's urgency badge)
// ---------------------------------------------------------------------------

/**
 * Compute the due label and urgency tier for a given due date.
 *
 * Returns `{ label: '', urgency: 'none' }` when no due date is present.
 */
export function computeDueLabel(dueDate: Date | null | undefined): IDueLabel {
  if (!dueDate) {
    return { label: '', urgency: 'none' };
  }

  // Compare CALENDAR days (both normalized to local midnight) so the label is a
  // real countdown, independent of the time-of-day component — a task due today
  // reads "Today", not "3d". (smart-todo-r5 UAT 2026-08-17 — item #6: the old
  // labels were the URGENCY-TIER names 3d/7d/10d, which misread as "3 days left"
  // for anything in the 0–3-day tier, including today.) Math.round absorbs the
  // ±1h DST skew that makes a "day" 23 or 25 hours. Colour tiers (`urgency`) are
  // UNCHANGED, so badge colours match the prior behaviour exactly.
  // (task 081 / U5: the shared `daysBetweenLocalMidnight` replaces this
  // function's former inline copy of the same local-midnight idiom.)
  const diffDays = daysBetweenLocalMidnight(new Date(), dueDate);
  // task 081 / C-17: the tier comes from the ONE shared tier function, so the
  // feed card and the event due-date card get the same tier for the same day.
  const urgency = dueUrgencyForDays(diffDays);

  if (urgency === 'none') {
    return { label: '', urgency };
  }
  if (urgency === 'overdue') {
    return { label: 'Overdue', urgency };
  }
  return { label: diffDays === 0 ? 'Today' : `${diffDays}d`, urgency };
}

// ---------------------------------------------------------------------------
// Composite To Do Score — 0–100 clamped
// ---------------------------------------------------------------------------

/**
 * Compute the To Do Score for a sprk_todo-shaped item. Works on any
 * structural supertype of `IKanbanTodoLike` (the same minimum surface the
 * `useKanbanColumns` hook depends on).
 *
 * Task 067 / D-29: this delegates to the ONE implementation in
 * `@spaarke/ui-components` (`computeTodoScoreBreakdown`), whose urgency
 * component counts calendar days like `computeDueLabel` above. It stays here as
 * the package's public entry point.
 */
export function computeTodoScore(todo: IKanbanTodoLike): ITodoScoreBreakdown {
  return computeTodoScoreBreakdown(todo);
}
