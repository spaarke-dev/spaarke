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
 *   - Weights: priority 0.50, effort (inverted) 0.20, urgency 0.30
 *   - Composite-score urgency points: overdue=100, ≤3d=80, ≤7d=50, ≤10d=25, else=0
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

import { parseDueDate, daysBetweenLocalMidnight, dueUrgencyForDays, type DueUrgency } from '@spaarke/ui-components';
import type { IKanbanTodoLike } from '../types/kanban';

export { parseDueDate };
export type { DueUrgency };

// ---------------------------------------------------------------------------
// Weights — locked (see `todoScoreMappings.ts`)
// ---------------------------------------------------------------------------

const W_PRIORITY = 0.5;
const W_EFFORT = 0.2;
const W_URGENCY = 0.3;

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
// Urgency scoring (continuous 0–100 raw value used by composite score)
// ---------------------------------------------------------------------------

/** Convert days-until-due into a 0-100 urgency raw score. */
function computeDueDateUrgencyRaw(dueDate: Date | null): number {
  if (!dueDate) return 0;
  const now = new Date();
  const diffMs = dueDate.getTime() - now.getTime();
  const diffDays = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
  if (diffDays < 0) return 100;
  if (diffDays <= 3) return 80;
  if (diffDays <= 7) return 50;
  if (diffDays <= 10) return 25;
  return 0;
}

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

/** Breakdown of the To Do Score components for transparency / debugging. */
export interface ITodoScoreBreakdown {
  todoScore: number;
  priorityComponent: number;
  effortComponent: number;
  urgencyComponent: number;
}

/**
 * Compute the To Do Score for a sprk_todo-shaped item. Works on any
 * structural supertype of `IKanbanTodoLike` (the same minimum surface the
 * `useKanbanColumns` hook depends on).
 */
export function computeTodoScore(todo: IKanbanTodoLike): ITodoScoreBreakdown {
  const rawPriority = todo.sprk_priorityscore ?? 50;
  const priorityComponent = rawPriority * W_PRIORITY;

  const rawEffort = todo.sprk_effortscore ?? 50;
  const invertedEffort = 100 - rawEffort;
  const effortComponent = invertedEffort * W_EFFORT;

  const dueDate = parseDueDate(todo.sprk_duedate);
  const rawUrgency = computeDueDateUrgencyRaw(dueDate);
  const urgencyComponent = rawUrgency * W_URGENCY;

  const raw = priorityComponent + effortComponent + urgencyComponent;
  const todoScore = Math.max(0, Math.min(100, Math.round(raw)));

  return { todoScore, priorityComponent, effortComponent, urgencyComponent };
}
