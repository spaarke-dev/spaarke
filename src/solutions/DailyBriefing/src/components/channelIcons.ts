/**
 * channelIcons — re-export shim.
 *
 * The canonical `getChannelIcon` helper was hoisted alongside the 9 Daily
 * Briefing components to the new shared package
 * `@spaarke/daily-briefing-components/components` in R2 task 011 (FR-04).
 * ActivityNotesSection depends on it so it followed the same hoist.
 *
 * Cleanup (replace consumer imports with the canonical package path, then
 * delete this shim) is tracked by R2 task 017.
 *
 * DO NOT add new code here. New work lives in
 * `src/client/shared/Spaarke.DailyBriefing.Components/src/components/channelIcons.ts`.
 */

export { getChannelIcon } from "@spaarke/daily-briefing-components/components";
