/**
 * feedDueAccent — the Updates Feed card's due-date tier and accent colour.
 *
 * The tier comes from THE shared tier function `dueUrgencyForDays`
 * (`@spaarke/ui-components` `utils/dateLocal.ts`; owner decision 2026-10-03,
 * 3/7/10 calendar days), over calendar days between local midnights — so a
 * DateOnly `sprk_duedate` of today is '3d', never 'overdue', in every US zone.
 * This module keeps only the feed's tier → accent colour, which follows
 * SmartTodo's badge palette (owner decision 2026-10-05, task 081 / H1):
 *   overdue → red · 3d → dark orange · 7d → yellow · 10d and none → neutral.
 *
 * Its own module (rather than inside `FeedItemCard.tsx`) so the cross-surface
 * tier test in `@spaarke/smart-todo-components` can import it without the card.
 *
 * Before task 081 the feed had a private copy of the boundaries (3/10 days,
 * ceil of elapsed 24h periods over a UTC-midnight parse) and coloured 4-10
 * days green.
 */

import { tokens } from "@fluentui/react-components";
import { daysBetweenLocalMidnight, dueUrgencyForDays, parseDueDate, type DueUrgency } from "@spaarke/ui-components";

/** Feed card left-accent colour per tier. */
export const FEED_DUE_ACCENT: Record<DueUrgency, string> = {
  overdue: tokens.colorPaletteRedBorder2,
  "3d": tokens.colorPaletteDarkOrangeBorder2,
  "7d": tokens.colorPaletteYellowBorder2,
  "10d": tokens.colorNeutralStroke2,
  none: tokens.colorNeutralStroke2,
};

/** Tier of a feed event's `sprk_duedate` (date-only or ISO), relative to `now`. */
export function feedDueUrgency(dueDate: string | null | undefined, now: Date = new Date()): DueUrgency {
  const due = parseDueDate(dueDate);
  return dueUrgencyForDays(due ? daysBetweenLocalMidnight(now, due) : null);
}

/**
 * Whether a feed event is overdue: its tier is `'overdue'` (due before today,
 * calendar days, local time). The feed's Overdue FILTER, its Overdue BADGE
 * count and the card's red accent all use this, so they agree (task 081
 * round 4, review F5). The filter and the badge used to compare
 * `new Date("YYYY-MM-DD")` (UTC midnight) with local midnight, so in every US
 * zone a task due TODAY was counted and listed as overdue while its card showed
 * it as due today.
 */
export function isFeedEventOverdue(dueDate: string | null | undefined, now: Date = new Date()): boolean {
  return feedDueUrgency(dueDate, now) === "overdue";
}
