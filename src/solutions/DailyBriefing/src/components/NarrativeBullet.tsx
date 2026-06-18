/**
 * NarrativeBullet — re-export shim.
 *
 * The canonical `NarrativeBullet` was hoisted to the new shared package
 * `@spaarke/daily-briefing-components/components` in R2 task 011 (FR-04)
 * per Calendar (`@spaarke/events-components`) precedent.
 *
 * NOTE: Existing logic was preserved verbatim by task 011. The P2a sub-list
 * rendering enhancement (FR-11..FR-14) is R2 task 020 and modifies the
 * canonical component in the new package directly.
 *
 * Cleanup (replace consumer imports with the canonical package path, then
 * delete this shim) is tracked by R2 task 017.
 *
 * DO NOT add new code here. New work lives in
 * `src/client/shared/Spaarke.DailyBriefing.Components/src/components/NarrativeBullet.tsx`.
 */

export {
  NarrativeBullet,
  type NarrativeBulletProps,
} from "@spaarke/daily-briefing-components/components";
