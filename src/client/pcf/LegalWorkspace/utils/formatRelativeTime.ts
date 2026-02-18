/**
 * formatRelativeTime — compact relative-time formatter for the Updates Feed.
 *
 * Format rules:
 *   < 1 hour  → "Xm ago"
 *   < 24 hours → "Xh ago"
 *   Yesterday  → "Yesterday"
 *   < 7 days   → "Xd ago"
 *   Otherwise  → Short date string, e.g. "Jan 15"
 *
 * The output is intentionally compact to fit the FeedItemCard right-column
 * without wrapping.  For notification-panel prose formatting (e.g. "2 min ago",
 * "1 hour ago") see NotificationPanel/notificationTypes.ts.
 */

const MONTH_ABBR = [
  "Jan", "Feb", "Mar", "Apr", "May", "Jun",
  "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
];

/**
 * Formats an ISO timestamp string into a compact relative time string for the
 * Updates Feed card.
 *
 * @param isoTimestamp - ISO 8601 date string (e.g. event.modifiedon)
 * @returns Compact relative time label, e.g. "5m ago", "3h ago", "Yesterday", "4d ago", "Jan 15"
 */
export function formatRelativeTime(isoTimestamp: string): string {
  const now = new Date();
  const then = new Date(isoTimestamp);
  const diffMs = now.getTime() - then.getTime();

  // Guard: future timestamps (data anomaly) → treat as "just now"
  if (diffMs < 0) return "just now";

  const diffMinutes = Math.floor(diffMs / (1000 * 60));

  // < 1 hour → "Xm ago"
  if (diffMinutes < 60) {
    return diffMinutes <= 1 ? "1m ago" : `${diffMinutes}m ago`;
  }

  const diffHours = Math.floor(diffMinutes / 60);

  // < 24 hours → "Xh ago"
  if (diffHours < 24) {
    return `${diffHours}h ago`;
  }

  // Check if timestamp falls on the calendar day before today ("Yesterday")
  const todayMidnight = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const yesterdayMidnight = new Date(todayMidnight.getTime() - 24 * 60 * 60 * 1000);
  const thenMidnight = new Date(then.getFullYear(), then.getMonth(), then.getDate());

  if (thenMidnight.getTime() === yesterdayMidnight.getTime()) {
    return "Yesterday";
  }

  const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

  // < 7 days → "Xd ago"
  if (diffDays < 7) {
    return `${diffDays}d ago`;
  }

  // ≥ 7 days → short date "Mon DD" e.g. "Jan 15"
  return `${MONTH_ABBR[then.getMonth()]} ${then.getDate()}`;
}
