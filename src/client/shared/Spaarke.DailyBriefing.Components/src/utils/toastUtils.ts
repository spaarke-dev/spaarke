/**
 * toastUtils.ts — Shared constants for Fluent UI v9 Toaster integration.
 *
 * The TOASTER_ID is used by DailyBriefingApp to create the Toaster provider
 * and by action handlers to dispatch toast notifications (e.g., To Do creation).
 *
 * Hoist note (R2 task 015 / FR-07):
 *   Originally lived at `src/solutions/DailyBriefing/src/utils/toastUtils.ts`.
 *   Hoisted to `@spaarke/daily-briefing-components/utils` to close the last
 *   solution-local back-pointer from DailyBriefingApp. Logic is byte-identical
 *   from the original — no behavior change.
 */

export const TOASTER_ID = "daily-briefing-toaster";
