/**
 * useBriefingNarration — re-export shim.
 *
 * Per R2 task 013 (FR-05): the canonical implementation moved to
 * `@spaarke/daily-briefing-components/hooks`. This file is preserved as a
 * thin re-export so existing consumers (App.tsx and any other importers in
 * `src/solutions/DailyBriefing/src/`) keep building until task 017/018
 * redirects them to the package import directly.
 */

export {
  useBriefingNarration,
  type UseBriefingNarrationResult,
} from "@spaarke/daily-briefing-components/hooks";
