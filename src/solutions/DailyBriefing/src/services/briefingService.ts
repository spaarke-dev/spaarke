/**
 * briefingService.ts — re-export shim.
 *
 * The canonical `briefingService` was hoisted to the new shared package
 * `@spaarke/daily-briefing-components/services` in R2 task 012 (FR-09) per
 * Calendar (`@spaarke/events-components`) precedent — BFF clients live
 * package-local in their dual-use widget package.
 *
 * This shim preserves the original import paths from this Code Page's hooks
 * and components so they continue to build during the hoist transition.
 * Cleanup (replace consumer imports with the canonical package path, then
 * delete this shim) is tracked by R2 tasks 017 / 018.
 *
 * DO NOT add new code here. New work lives in
 * `src/client/shared/Spaarke.DailyBriefing.Components/src/services/briefingService.ts`.
 */

export {
  fetchAiBriefing,
  fetchBriefingNarration,
  type BriefingResult,
  type DailyBriefingSummaryResponse,
  type NarrationResult,
  type NarrateResponse,
  type TldrResult,
  type ChannelNarrationResult,
  type NarrativeBulletResult,
} from "@spaarke/daily-briefing-components/services";
