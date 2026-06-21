/**
 * formatDueDate — render an ISO-8601 due-date string as a short relative phrase.
 *
 * R2.2: per-item due-date rendering for task notifications. Output is meant
 * for the inline due-date hint shown in NarrativeBullet (single-item) and
 * SubRow (aggregated per-item). Format favours quick scanning over precision —
 * "Due tomorrow" / "Overdue by 3d" rather than the raw "2026-06-22T17:00:00Z".
 *
 * Buckets:
 *   - Past:           "Overdue by Nd"  (or "Overdue" when N = 0 but past)
 *   - Today:          "Due today"
 *   - Tomorrow:       "Due tomorrow"
 *   - Future ≤ 7d:    "Due in Nd"
 *   - Future > 7d:    "Due Mon DD"     (locale-aware short date)
 *
 * Returns null for null / unparseable input — callers should skip rendering
 * the due-date hint entirely in that case.
 */
export function formatDueDate(isoTimestamp: string | null | undefined, now: Date = new Date()): string | null {
  if (!isoTimestamp) return null;

  const due = new Date(isoTimestamp);
  if (isNaN(due.getTime())) return null;

  // Compare on day boundaries (local time) — a task due "today at 4pm" should
  // still read "Due today" even at 5pm, not "Overdue by 0d".
  const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const dueDay = startOfDay(due).getTime();
  const todayDay = startOfDay(now).getTime();
  const diffDays = Math.round((dueDay - todayDay) / 86_400_000);

  if (diffDays < 0) {
    const days = Math.abs(diffDays);
    return days === 0 ? 'Overdue' : `Overdue by ${days}d`;
  }
  if (diffDays === 0) return 'Due today';
  if (diffDays === 1) return 'Due tomorrow';
  if (diffDays <= 7) return `Due in ${diffDays}d`;

  // > 7 days out: short locale date (e.g. "Due Jun 28")
  return `Due ${due.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}`;
}
