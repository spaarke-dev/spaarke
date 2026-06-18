/**
 * ChannelHeading — re-export shim.
 *
 * The canonical `ChannelHeading` was hoisted to the new shared package
 * `@spaarke/daily-briefing-components/components` in R2 task 011 (FR-04)
 * per Calendar (`@spaarke/events-components`) precedent.
 *
 * Cleanup (replace consumer imports with the canonical package path, then
 * delete this shim) is tracked by R2 task 017.
 *
 * DO NOT add new code here. New work lives in
 * `src/client/shared/Spaarke.DailyBriefing.Components/src/components/ChannelHeading.tsx`.
 */

export {
  ChannelHeading,
  type ChannelHeadingProps,
} from "@spaarke/daily-briefing-components/components";
