/**
 * relativeTime — "N minutes/hours/days ago" formatting for INSTANTS.
 *
 * Hoisted into `@spaarke/ui-components` (spaarke-ontology-platform-r1 task
 * 081 / C-13). Before this, the idiom was independently reimplemented five
 * times (LegalWorkspace `utils/formatRelativeTime.ts` and
 * `NotificationPanel/notificationTypes.ts`, DailyBriefing `TldrSection.tsx`,
 * SpaarkeAi `HistoryOverlay.tsx` and `ManageWorkspacesPane.tsx`). It starts
 * from the deleted AI.Outputs `ChatSessionCard.tsx` version (62277d50a), the
 * only one using `Intl.RelativeTimeFormat`.
 *
 * NOT for date-only values. A Dataverse DateOnly column (e.g. a due date,
 * `"2026-10-05"`) is a calendar day, not an instant: `new Date("2026-10-05")`
 * is UTC midnight, i.e. the previous evening in every US zone, so an elapsed
 * formatter would render a due date of today as "14 hours ago". Format those
 * with a day-granular due label instead (`parseDueDate` from `dateLocal.ts`
 * plus e.g. DailyBriefing's `formatDueDate`).
 *
 * ## Bucketing (what the function actually does)
 *
 * Let `elapsed` be |then - now|. Buckets are tried in this order:
 *   1. elapsed < 60 s (past OR future) → "just now". Clock skew between the
 *      server stamp and the browser must never read "in 3 seconds"; SpaarkeAi
 *      FR-07 relies on "just now" for a freshly saved layout.
 *   2. elapsed < 60 min → minutes, truncated ("1 minute ago" at 60-119 s).
 *   3. elapsed < 24 h → hours, truncated ("23 hours ago" at 23h59m).
 *      These sub-day buckets are ELAPSED-time based and run first, so
 *      something stamped "yesterday at 11pm" reads "10 hours ago" at 9am —
 *      not "yesterday".
 *   4. Otherwise CALENDAR days between local midnights
 *      ({@link daysBetweenLocalMidnight}), not elapsed 24h periods: something
 *      stamped Saturday 11pm reads "2 days ago" at Monday 00:30 (25.5 h
 *      elapsed), where 24h-period math would say "1 day ago".
 *      |days| < 7 → days ("yesterday" / "tomorrow" in the long style).
 *   5. |days| < 30 → weeks, ROUNDED (7-10 → 1, 11-17 → 2, 18-24 → 3,
 *      25-29 → 4). Rounding rather than truncating keeps 13 days at
 *      "2 weeks ago" and 28-29 days at "4 weeks ago".
 *   6. |days| < 365 and rounded months < 12 → months (days / 30.4375, rounded,
 *      minimum 1).
 *   7. Otherwise → years (days / 365.25, rounded, minimum 1).
 * Future instants use the same buckets with "in …" phrasing.
 *
 * ## Styles
 *   - `'long'` (default): `Intl.RelativeTimeFormat` style `long`, numeric
 *     `auto` → "5 minutes ago", "yesterday", "in 3 days", "last month".
 *   - `'compact'`: "5m ago", "3h ago", "1d ago", "2w ago", "3mo ago",
 *     "1y ago", "in 3d" — the abbreviated form the replaced SpaarkeAi History
 *     and Manage Workspaces copies and the DailyBriefing Tl;dr "Generated …"
 *     line rendered (those are its only callers; no feed caller uses it). For
 *     English it is built from FIXED abbreviations (m / h / d / w / mo / y),
 *     not `Intl` `narrow`, whose English output varies by ICU version ("5 min.
 *     ago" in older browsers). For any other locale it falls back to `Intl`
 *     style `narrow`, numeric `always`.
 *
 * ## Locale
 * Defaults to `'en'`, NOT the browser language: every surrounding UI string
 * is English, so a relative time must not switch language on its own
 * ("Modified vor 5 Minuten"). Callers that localise their whole surface pass
 * `locale` explicitly. "just now" is English-only; other locales get their
 * own `Intl` "now".
 */

import { daysBetweenLocalMidnight } from './dateLocal';

/** Output style for {@link formatRelativeTime}. */
export type RelativeTimeStyle = 'long' | 'compact';

/** Options for {@link formatRelativeTime}. */
export interface FormatRelativeTimeOptions {
  /** BCP 47 locale tag. Default `'en'` (see module header). */
  locale?: string;
  /** `'long'` (default) or `'compact'` (see module header). */
  style?: RelativeTimeStyle;
}

const MS_PER_MINUTE = 60_000;
const MS_PER_HOUR = 3_600_000;
const DAYS_PER_MONTH = 30.4375;
const DAYS_PER_YEAR = 365.25;

type RelativeUnit = 'minute' | 'hour' | 'day' | 'week' | 'month' | 'year';

/** Fixed English compact abbreviations (see module header, "Styles"). */
const COMPACT_EN_SUFFIX: Record<RelativeUnit, string> = {
  minute: 'm',
  hour: 'h',
  day: 'd',
  week: 'w',
  month: 'mo',
  year: 'y',
};

function isEnglish(locale: string): boolean {
  return /^en\b/i.test(locale);
}

function createFormatter(locale: string, style: RelativeTimeStyle): Intl.RelativeTimeFormat {
  const intlOptions: Intl.RelativeTimeFormatOptions =
    style === 'compact' ? { style: 'narrow', numeric: 'always' } : { style: 'long', numeric: 'auto' };
  try {
    return new Intl.RelativeTimeFormat(locale, intlOptions);
  } catch {
    // Malformed locale tag (RangeError) — fall back rather than throw in render.
    return new Intl.RelativeTimeFormat('en', intlOptions);
  }
}

/**
 * Format an ISO 8601 INSTANT as a relative-time string (e.g. "5 minutes ago",
 * "yesterday", "in 3 days"; compact: "5m ago", "1d ago"). See the module
 * header for the exact bucketing, styles and locale rules. Do not pass a
 * date-only value (see module header).
 *
 * Never throws: an unparseable `isoTimestamp` is returned unchanged so a bad
 * value is visible instead of silently blank.
 */
export function formatRelativeTime(isoTimestamp: string, options: FormatRelativeTimeOptions = {}): string {
  const thenMs = new Date(isoTimestamp).getTime();
  if (Number.isNaN(thenMs)) {
    return isoTimestamp;
  }

  const locale = options.locale || 'en';
  const style: RelativeTimeStyle = options.style || 'long';
  const now = new Date();
  const diffMs = thenMs - now.getTime(); // negative = in the past
  const sign = diffMs < 0 ? -1 : 1;
  const absMs = Math.abs(diffMs);
  const english = isEnglish(locale);
  const fixedCompact = style === 'compact' && english;
  const rtf = fixedCompact ? null : createFormatter(locale, style);
  const fmt = (value: number, unit: RelativeUnit): string => {
    if (rtf) return rtf.format(value, unit);
    const text = `${Math.abs(value)}${COMPACT_EN_SUFFIX[unit]}`;
    return value < 0 ? `${text} ago` : `in ${text}`;
  };

  if (absMs < MS_PER_MINUTE) {
    return english ? 'just now' : createFormatter(locale, style).format(0, 'second');
  }

  const absMinutes = Math.floor(absMs / MS_PER_MINUTE);
  if (absMinutes < 60) return fmt(sign * absMinutes, 'minute');

  const absHours = Math.floor(absMs / MS_PER_HOUR);
  if (absHours < 24) return fmt(sign * absHours, 'hour');

  const days = daysBetweenLocalMidnight(now, new Date(thenMs));
  const absDays = Math.abs(days);
  // ≥ 24 h elapsed on the same calendar day only happens on a 25-hour DST
  // day; stay in hours rather than render "today".
  if (absDays === 0) return fmt(sign * absHours, 'hour');
  if (absDays < 7) return fmt(days, 'day');
  if (absDays < 30) return fmt(sign * Math.round(absDays / 7), 'week');

  const absMonths = Math.max(1, Math.round(absDays / DAYS_PER_MONTH));
  if (absDays < 365 && absMonths < 12) return fmt(sign * absMonths, 'month');

  return fmt(sign * Math.max(1, Math.round(absDays / DAYS_PER_YEAR)), 'year');
}
