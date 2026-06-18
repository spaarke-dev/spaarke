/**
 * App.tsx — re-export shim.
 *
 * The canonical top-level Daily Briefing composer was hoisted to the new
 * shared package `@spaarke/daily-briefing-components/components` and renamed
 * `DailyBriefingApp` in R2 task 011 (FR-04) per Calendar
 * (`@spaarke/events-components`) precedent.
 *
 * `main.tsx` (and any other consumer importing `./App`) continues to receive
 * the same component value via the canonical re-export — `App` is preserved
 * as a named-alias re-export so the bootstrap entry needs no change in this
 * wave. Full consumer cleanup (replace `./App` with the canonical package
 * import + delete this shim) is tracked by R2 task 017.
 *
 * DO NOT add new code here. New work lives in
 * `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx`.
 */

export {
  DailyBriefingApp,
  DailyBriefingApp as App,
  type DailyBriefingAppProps,
  type DailyBriefingAppProps as AppProps,
} from "@spaarke/daily-briefing-components/components";
