/**
 * todoScoring — Shared scoring + due-date helpers used by the hoisted
 * SmartTodo Kanban surface (R4 task 102 / E-1, 2026-06-18).
 *
 * Why this file exists (and why the helpers aren't simply imported from the
 * Code Page's `utils/todoScoreUtils.ts` / `utils/dueLabelUtils.ts`):
 *
 *   - The peer package is host-agnostic by design. Reaching into a
 *     `src/solutions/...` source path would invert the dependency direction
 *     and tie the peer package to one specific app's file layout.
 *   - The previously-hoisted `useKanbanColumns` (R4-101) already keeps
 *     LOCAL copies of `computeTodoScore` + `parseDueDate` inside its module
 *     for the same reason. Centralising those copies here lets the hoisted
 *     `KanbanCard` re-use the identical math without further duplication.
 *
 * Bit-for-bit parity with the Code Page implementation is required (no
 * regression for existing users of `SmartToDo.tsx`):
 *   - Weights: priority 0.50, effort (inverted) 0.20, urgency 0.30
 *   - Urgency tiers: overdue=100, ≤3d=80, ≤7d=50, ≤10d=25, else=0
 *   - DueLabel tiers: overdue / 3d / 7d / 10d / none (matches dueLabelUtils.ts)
 *
 * @see src/solutions/SmartTodo/src/utils/todoScoreUtils.ts (original)
 * @see src/solutions/SmartTodo/src/utils/dueLabelUtils.ts (original)
 * @see hooks/useKanbanColumns.ts (local copies for hook bucketing)
 */

import type { IKanbanTodoLike } from '../types/kanban';

// ---------------------------------------------------------------------------
// Weights — locked to match Code Page `todoScoreUtils.ts`
// ---------------------------------------------------------------------------

const W_PRIORITY = 0.5;
const W_EFFORT = 0.2;
const W_URGENCY = 0.3;

// ---------------------------------------------------------------------------
// Due-date label types — mirrors Code Page `dueLabelUtils.ts` exactly
// ---------------------------------------------------------------------------

/** The urgency tier for a due date. */
export type DueUrgency = 'overdue' | '3d' | '7d' | '10d' | 'none';

/** Computed due label with a short display string and urgency tier. */
export interface IDueLabel {
  /** Short badge text shown in the UI (empty string when urgency is 'none'). */
  label: string;
  /** Urgency tier used to select badge colour. */
  urgency: DueUrgency;
}

// ---------------------------------------------------------------------------
// Parsing
// ---------------------------------------------------------------------------

/** Parse an ISO date string defensively. Returns null for null/undefined/invalid. */
export function parseDueDate(isoString: string | undefined | null): Date | null {
  if (!isoString) return null;
  const d = new Date(isoString);
  return Number.isNaN(d.getTime()) ? null : d;
}

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

  const now = new Date();
  const diffMs = dueDate.getTime() - now.getTime();
  const diffDays = Math.ceil(diffMs / (1000 * 60 * 60 * 24));

  if (diffDays < 0) {
    return { label: 'Overdue', urgency: 'overdue' };
  }
  if (diffDays <= 3) {
    return { label: '3d', urgency: '3d' };
  }
  if (diffDays <= 7) {
    return { label: '7d', urgency: '7d' };
  }
  if (diffDays <= 10) {
    return { label: '10d', urgency: '10d' };
  }
  return { label: '', urgency: 'none' };
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
