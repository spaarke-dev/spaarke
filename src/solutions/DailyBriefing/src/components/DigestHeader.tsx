/**
 * DigestHeader — re-export shim.
 *
 * The canonical `DigestHeader` was hoisted to the new shared package
 * `@spaarke/daily-briefing-components/components` in R2 task 011 (FR-04)
 * per Calendar (`@spaarke/events-components`) precedent — Daily Briefing
 * UI components live package-local in their dual-use widget package.
 *
 * This shim preserves the original import paths from this Code Page's
 * components/App.tsx so they continue to build during the hoist transition.
 * Cleanup (replace consumer imports with the canonical package path, then
 * delete this shim) is tracked by R2 task 017.
 *
 * DO NOT add new code here. New work lives in
 * `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DigestHeader.tsx`.
 */

export {
  DigestHeader,
  type DigestHeaderProps,
} from "@spaarke/daily-briefing-components/components";
