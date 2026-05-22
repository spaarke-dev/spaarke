/**
 * dateMath.ts — small date helpers used by Calendar widget surfaces.
 *
 * Implemented inline (vs adding `date-fns` to the shared lib's peer-deps)
 * because (a) the helpers are trivial, (b) the events-components package
 * currently has ZERO runtime dependencies beyond React + Fluent v9 (see
 * `package.json` peerDependencies — keeping it that way preserves
 * publish-size attribution + bundle-size baselines), and (c) date-fns is
 * not already in any of the shared lib's existing peers.
 *
 * Task 116 (Round 10, 2026-05-22) — added for the Calendar widget's
 * external ◀ ▶ month-navigation arrows. `CalendarSection` accepts a
 * `viewDate?: Date` controlled prop; the widget owns the state and shifts
 * it by ±1 month per arrow click using `addMonths` here.
 */

/**
 * Return a new Date offset from `date` by `months` calendar months.
 *
 * Preserves the day-of-month where possible; if the target month has fewer
 * days than the source's day-of-month, JS Date naturally rolls into the
 * following month (e.g. Jan 31 + 1 month → Mar 3). That's acceptable for
 * Calendar navigation because the widget anchors its rendered range to
 * `startOfMonth(viewDate)` before computing the month list.
 *
 * Pure / side-effect-free.
 */
export function addMonths(date: Date, months: number): Date {
  const result = new Date(date.getTime());
  result.setMonth(result.getMonth() + months);
  return result;
}

/**
 * Return a new Date representing the first day (00:00 local time) of
 * `date`'s month. Used by the Calendar widget to anchor its rendered
 * month range — passing a mid-month date through `startOfMonth` ensures
 * `CalendarSection`'s month-list computation lines up cleanly.
 *
 * Pure / side-effect-free.
 */
export function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}
