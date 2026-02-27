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
 * All inputs come from existing IEvent fields — this is a client-side
 * convenience sort key and does NOT replace the BFF-computed
 * sprk_priorityscore / sprk_effortscore.
 */

import type { IEvent } from '../types/entities';
import { parseDueDate } from './dueLabelUtils';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Breakdown of the To Do Score components for transparency / debugging. */
export interface ITodoScoreBreakdown {
  /** Final composite score (0-100, clamped). */
  todoScore: number;
  /** Weighted priority component. */
  priorityComponent: number;
  /** Weighted inverted-effort component. */
  effortComponent: number;
  /** Weighted due-date urgency component. */
  urgencyComponent: number;
}

// ---------------------------------------------------------------------------
// Weights
// ---------------------------------------------------------------------------

const W_PRIORITY = 0.50;
const W_EFFORT   = 0.20;
const W_URGENCY  = 0.30;

// ---------------------------------------------------------------------------
// Due-date urgency raw score
// ---------------------------------------------------------------------------

/**
 * Map days-until-due into a 0-100 raw urgency score.
 *
 *   Overdue     → 100
 *   ≤ 3 days    →  80
 *   ≤ 7 days    →  50
 *   ≤ 10 days   →  25
 *   > 10 days   →   0
 *   No due date →   0
 */
export function computeDueDateUrgencyRaw(dueDate: Date | null): number {
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
// Main computation
// ---------------------------------------------------------------------------

/**
 * Compute the To Do Score for an event.
 *
 * @param event - The event record with priority/effort/due-date fields.
 * @returns Breakdown with the final todoScore and per-component values.
 */
export function computeTodoScore(event: IEvent): ITodoScoreBreakdown {
  // Priority: use sprk_priorityscore (0-100), default 50 (Normal)
  const rawPriority = event.sprk_priorityscore ?? 50;
  const priorityComponent = rawPriority * W_PRIORITY;

  // Effort inverted: lower effort → higher score (quick wins)
  const rawEffort = event.sprk_effortscore ?? 50;
  const invertedEffort = 100 - rawEffort;
  const effortComponent = invertedEffort * W_EFFORT;

  // Due-date urgency
  const dueDate = parseDueDate(event.sprk_duedate);
  const rawUrgency = computeDueDateUrgencyRaw(dueDate);
  const urgencyComponent = rawUrgency * W_URGENCY;

  // Composite (clamped 0-100)
  const raw = priorityComponent + effortComponent + urgencyComponent;
  const todoScore = Math.max(0, Math.min(100, Math.round(raw)));

  return { todoScore, priorityComponent, effortComponent, urgencyComponent };
}
