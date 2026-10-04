/**
 * relativeTime — locale-aware "N minutes/hours/days ago" formatting.
 *
 * Hoisted into `@spaarke/ui-components` (spaarke-ontology-platform-r1 task
 * 081 / C-13). Before this, the "format a timestamp as relative time" idiom
 * was independently reimplemented five times:
 *   - `Spaarke.AI.Outputs/src/chat-history/ChatSessionCard.tsx` (deleted by
 *     #1120, zero consumers — but the most-correct starting point: it used
 *     `Intl.RelativeTimeFormat`, guarded invalid dates, and handled future
 *     timestamps)
 *   - `src/solutions/LegalWorkspace/src/utils/formatRelativeTime.ts`
 *   - `src/solutions/LegalWorkspace/src/components/NotificationPanel/notificationTypes.ts`
 *   - `Spaarke.DailyBriefing.Components/src/components/TldrSection.tsx`
 *   - `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx`
 *     (`formatRelative`) and `.../workspace/ManageWorkspacesPane.tsx`
 *     (`formatModifiedOn`, which explicitly documented itself as "mirroring"
 *     the HistoryOverlay copy rather than sharing it)
 *
 * This implementation starts from the `ChatSessionCard.tsx` version
 * (62277d50a) and fixes its two documented weaknesses:
 *   1. Hard-coded `'en'` locale → resolves `navigator.language` (overridable
 *      via the `locale` param), so `Intl.RelativeTimeFormat` actually
 *      localizes for non-English users instead of always rendering English.
 *   2. Day/week/month buckets computed from elapsed 24h *periods* (`diffMs /
 *      86_400_000`) → now computed from CALENDAR-day differences via
 *      {@link daysBetweenLocalMidnight}. The elapsed-period version reads
 *      "yesterday at 11pm" as "10 hours ago" at 9am the next morning, instead
 *      of "yesterday" — the same local-midnight boundary bug `dateLocal.ts`
 *      exists to fix (task 084 / C-10's `todoScoring.ts` precedent). Sub-day
 *      buckets (second/minute/hour) stay elapsed-time based, since "how long
 *      ago" below a day genuinely is an elapsed-time question.
 *
 * Convergence note: the five former copies disagreed only in surface
 * FORMATTING (compact "Xm ago" vs prose "X minutes ago" vs a "Modified "
 * prefix vs a short calendar-date fallback for old items) — not in what a
 * caller needs ("how long ago was this"). None of that is a load-bearing
 * behavior difference, so callers that want their own prefix/suffix text
 * wrap this function's output (e.g. `` `Modified ${formatRelativeTime(iso)}` ``)
 * rather than reimplementing the date math. One exception was NOT cosmetic:
 * `LegalWorkspace/utils/formatRelativeTime.ts` returned `"just now"` for any
 * FUTURE timestamp (`diffMs < 0`) — including a due date days away — which
 * this function fixes by correctly rendering future dates via
 * `Intl.RelativeTimeFormat`'s "in N days" phrasing.
 */

import { daysBetweenLocalMidnight } from './dateLocal';

/**
 * Format an ISO 8601 timestamp as a locale-aware relative-time string
 * (e.g. "5 minutes ago", "yesterday", "in 3 days", "2 months ago").
 *
 * Never throws: an unparseable `isoTimestamp` returns the input string
 * unchanged (mirrors the `ChatSessionCard.tsx` precedent) so a bad value is
 * visible instead of silently blank.
 *
 * @param isoTimestamp - ISO 8601 date string.
 * @param locale - BCP 47 locale tag. Defaults to `navigator.language` when
 *   available (browser contexts), falling back to `'en'` (e.g. Node/tests).
 *   Passing a locale explicitly never silently reverts to `'en'`.
 */
export function formatRelativeTime(isoTimestamp: string, locale?: string): string {
  const now = new Date();
  const thenMs = new Date(isoTimestamp).getTime();

  if (Number.isNaN(thenMs)) {
    return isoTimestamp;
  }

  const then = new Date(thenMs);
  const diffMs = thenMs - now.getTime(); // negative = in the past
  const diffSec = Math.round(diffMs / 1000);
  const diffMin = Math.round(diffMs / (1000 * 60));
  const diffHr = Math.round(diffMs / (1000 * 60 * 60));

  const resolvedLocale = locale ?? (typeof navigator !== 'undefined' ? navigator.language : undefined) ?? 'en';
  const rtf = new Intl.RelativeTimeFormat(resolvedLocale, { numeric: 'auto' });

  if (Math.abs(diffSec) < 60) return rtf.format(diffSec, 'second');
  if (Math.abs(diffMin) < 60) return rtf.format(diffMin, 'minute');
  if (Math.abs(diffHr) < 24) return rtf.format(diffHr, 'hour');

  // Calendar-day difference (local midnight to local midnight), not elapsed
  // 24h periods — see module header.
  const diffDays = daysBetweenLocalMidnight(now, then);

  if (Math.abs(diffDays) < 7) return rtf.format(diffDays, 'day');

  const diffWeeks = Math.trunc(diffDays / 7);
  if (Math.abs(diffWeeks) < 4) return rtf.format(diffWeeks, 'week');

  const diffMonths = Math.trunc(diffDays / 30);
  return rtf.format(diffMonths, 'month');
}
