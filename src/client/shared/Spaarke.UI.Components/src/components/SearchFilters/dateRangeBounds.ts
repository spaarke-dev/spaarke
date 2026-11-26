/**
 * dateRangeBounds — turn a DateRangeFilter value (LOCAL calendar days, `YYYY-MM-DD`) into the UTC
 * instants the search API compares `createdAt` against (#3093).
 *
 * The filter's days are the user's local days (`calculatePresetRange` uses `formatDateOnly`), so the
 * bounds are local midnight and local 23:59:59.999, as UTC instants. The two former host
 * conversions disagreed with that: the PCF appended `T00:00:00Z` / `T23:59:59Z` (UTC days, so an
 * evening search west of UTC missed the last hours of "today"), and the code page sent the bare
 * date (read as midnight, so the whole `to` day was excluded).
 *
 * A value that already carries a time (e.g. an ISO instant passed in through URL parameters) is a
 * real instant and passes through unchanged. Same local-day maths as the DataGrid chip's
 * `localDateToUtcBounds`, per side, because either side may be open.
 */

import { isDateOnlyString, parseDueDate } from '../../utils/dateLocal';

/** UTC instant bounds for the search API; an open side is `undefined`. */
export interface DateRangeUtcBounds {
  from?: string;
  to?: string;
}

/** Start of the local day `YYYY-MM-DD`, or the instant itself, as an ISO string. */
function toFromInstant(value: string): string | undefined {
  // parseDueDate reads a bare date as LOCAL midnight and leaves an instant as it is.
  return parseDueDate(value)?.toISOString();
}

/** End of the local day `YYYY-MM-DD` (23:59:59.999), or the instant itself, as an ISO string. */
function toToInstant(value: string): string | undefined {
  const parsed = parseDueDate(value);
  if (!parsed) return undefined;
  if (!isDateOnlyString(value)) return parsed.toISOString();
  return new Date(parsed.getFullYear(), parsed.getMonth(), parsed.getDate(), 23, 59, 59, 999).toISOString();
}

/**
 * The UTC instant bounds for a date range of local calendar days.
 *
 * @example
 * // In New York (EST on 1 March, EDT from 8 March 2026):
 * toUtcDateRangeBounds({ from: '2026-03-01', to: '2026-03-10' })
 * // → { from: '2026-03-01T05:00:00.000Z', to: '2026-03-11T03:59:59.999Z' }
 */
export function toUtcDateRangeBounds(
  value: { from?: string | null; to?: string | null } | null | undefined
): DateRangeUtcBounds {
  return {
    from: value?.from ? toFromInstant(value.from) : undefined,
    to: value?.to ? toToInstant(value.to) : undefined,
  };
}
