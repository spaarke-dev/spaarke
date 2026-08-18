/**
 * dueLabelUtils — Due date label computation for Smart To Do list (Block 4).
 *
 * Computes a short urgency label and urgency tier from a due date.
 * Used by TodoItem to render a colour-coded due label badge.
 *
 * Urgency tiers:
 *   overdue  — dueDate is in the past
 *   3d       — due within 3 calendar days (0-3 days remaining)
 *   7d       — due within 7 calendar days (4-7 days remaining)
 *   10d      — due within 10 calendar days (8-10 days remaining)
 *   none     — more than 10 days away or no dueDate
 *
 * Label text (smart-todo-r5 UAT 2026-08-17, item #6): the badge shows a real
 * calendar-day countdown ("Overdue" / "Today" / "{n}d"), NOT the colour-tier
 * name — a task due today reads "Today", not a misleading "3d". The COLOUR
 * tiers above are unchanged. Kept in lock-step with the shared-lib
 * `@spaarke/smart-todo-components` `utils/todoScoring.ts::computeDueLabel`
 * (which the Kanban cards use); this local copy feeds the List + Dismissed
 * views only.
 */

// ---------------------------------------------------------------------------
// Types
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
// Utility
// ---------------------------------------------------------------------------

/**
 * Compute the due label and urgency tier for a given due date.
 *
 * @param dueDate - The due date, or null/undefined if not set.
 * @returns IDueLabel with label and urgency. When no due date, returns
 *          `{ label: '', urgency: 'none' }`.
 *
 * @example
 *   computeDueLabel(null)
 *   // => { label: '', urgency: 'none' }
 *
 *   computeDueLabel(new Date(Date.now() - 86400000))  // yesterday
 *   // => { label: 'Overdue', urgency: 'overdue' }
 *
 *   computeDueLabel(new Date(Date.now() + 2 * 86400000))  // 2 days ahead
 *   // => { label: '3d', urgency: '3d' }
 */
export function computeDueLabel(dueDate: Date | null | undefined): IDueLabel {
  if (!dueDate) {
    return { label: '', urgency: 'none' };
  }

  // Calendar-day countdown (both dates normalized to local midnight) so the
  // label is time-of-day independent — see module header + the shared-lib
  // todoScoring.ts twin. Colour tiers unchanged.
  const startOfToday = new Date();
  startOfToday.setHours(0, 0, 0, 0);
  const startOfDue = new Date(dueDate.getFullYear(), dueDate.getMonth(), dueDate.getDate());
  const diffDays = Math.round((startOfDue.getTime() - startOfToday.getTime()) / (1000 * 60 * 60 * 24));

  if (diffDays < 0) {
    return { label: 'Overdue', urgency: 'overdue' };
  }
  if (diffDays === 0) {
    return { label: 'Today', urgency: '3d' };
  }
  if (diffDays <= 3) {
    return { label: `${diffDays}d`, urgency: '3d' };
  }
  if (diffDays <= 7) {
    return { label: `${diffDays}d`, urgency: '7d' };
  }
  if (diffDays <= 10) {
    return { label: `${diffDays}d`, urgency: '10d' };
  }

  return { label: '', urgency: 'none' };
}

/**
 * Parse an ISO date string from Dataverse into a Date object.
 *
 * Dataverse returns dates as ISO strings (e.g. "2026-03-15T00:00:00Z").
 * Returns null if the string is undefined, null, or invalid.
 *
 * @param isoString - The ISO date string from Dataverse, or undefined/null.
 * @returns Parsed Date, or null.
 */
export function parseDueDate(isoString: string | undefined | null): Date | null {
  if (!isoString) return null;
  // Date-only values (`YYYY-MM-DD`) are calendar dates: parse as LOCAL midnight
  // so western timezones don't read them a day early. Full ISO timestamps are
  // unaffected. (smart-todo-r5 UAT 2026-08-17, item #6 — mirrors the shared-lib
  // todoScoring.ts twin.)
  const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoString.trim());
  if (dateOnly) {
    const dt = new Date(Number(dateOnly[1]), Number(dateOnly[2]) - 1, Number(dateOnly[3]));
    return isNaN(dt.getTime()) ? null : dt;
  }
  const d = new Date(isoString);
  // Guard against invalid Date (e.g. malformed string)
  return isNaN(d.getTime()) ? null : d;
}
