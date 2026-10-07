/**
 * formatDueDate — render an ISO-8601 due-date string as a short relative phrase.
 *
 * R2.2: per-item due-date rendering for task notifications. Output is meant
 * for the inline due-date hint shown in NarrativeBullet (single-item) and
 * SubRow (aggregated per-item). Format favours quick scanning over precision —
 * "Due tomorrow" / "Overdue by 3d" rather than the raw "2026-06-22T17:00:00Z".
 *
 * Buckets:
 *   - Past:           "Overdue by Nd"  (N >= 1 calendar days)
 *   - Today:          "Due today"
 *   - Tomorrow:       "Due tomorrow"
 *   - Future ≤ 7d:    "Due in Nd"
 *   - Future > 7d:    "Due Mon DD"     (locale-aware short date)
 *
 * Returns null for null / unparseable input — callers should skip rendering
 * the due-date hint entirely in that case.
 *
 * Day-granular by design: a due date is a calendar day, so this never says
 * "in 14 hours". Date-only values (`YYYY-MM-DD`, e.g. a Dataverse DateOnly
 * `sprk_duedate`) are parsed as LOCAL midnight via the shared `parseDueDate`
 * (task 081 / C-13 + F4: `new Date("2026-10-05")` is UTC midnight, i.e. the
 * previous evening in every US zone, so a task due today read "Overdue by 1d"),
 * and the day difference uses the shared `daysBetweenLocalMidnight` (U5).
 * Full ISO timestamps are real instants and parse exactly as before.
 */
import { parseDueDate, daysBetweenLocalMidnight } from '@spaarke/ui-components';

export function formatDueDate(isoTimestamp: string | null | undefined, now: Date = new Date()): string | null {
  const due = parseDueDate(isoTimestamp);
  if (!due) return null;

  // Compare on day boundaries (local time) — a task due "today at 4pm" should
  // still read "Due today" even at 5pm, not "Overdue by 0d".
  const diffDays = daysBetweenLocalMidnight(now, due);

  if (diffDays < 0) {
    return `Overdue by ${Math.abs(diffDays)}d`;
  }
  if (diffDays === 0) return 'Due today';
  if (diffDays === 1) return 'Due tomorrow';
  if (diffDays <= 7) return `Due in ${diffDays}d`;

  // > 7 days out: short locale date (e.g. "Due Jun 28")
  return `Due ${due.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}`;
}
