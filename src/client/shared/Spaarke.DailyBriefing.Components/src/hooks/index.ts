/**
 * @spaarke/daily-briefing-components — hooks barrel
 *
 * Reusable hooks for Daily Briefing surfaces. Per ADR-012, all hooks are
 * context-agnostic — dependencies are injected via the hook's arguments.
 *
 * Populated by R2 task 013 (Wave 3 hoist, FR-05):
 *  - `useInlineTodoCreate` — `sprk_todo` creation with multi-entity regarding
 *    resolution per ADR-024 (TODO_REGARDING_CATALOG + applyResolverFields
 *    preserved verbatim from the original location).
 *
 * Populated by R2 task 014 (FR-06 split of `useNotificationData`):
 *  - `useBriefingPreferences` — fetches + persists Daily Digest user preferences.
 *
 * ontology-platform-r1 task 010 / C-1 (2026-10-03): the appnotification-driven
 * `useBriefingNotifications` / `useBriefingNarration` / `useBriefingActions`
 * hooks (and the `notificationService.ts` module that backed them) were
 * deleted as dead code — they had no live call site and their only
 * importers were their own now-deleted unit tests. The current data path is
 * `useBriefingRender`, which fires `POST /api/ai/daily-briefing/render`
 * (behind `USE_LIVE_RENDER` in `briefingService.ts`) unconditionally on
 * mount; it does not gate on an `appnotification` load.
 */

export { useInlineTodoCreate } from './useInlineTodoCreate';
export type { UseInlineTodoCreateResult } from './useInlineTodoCreate';

export { useBriefingRender } from './useBriefingRender';
export type { UseBriefingRenderResult, BriefingRenderStatus } from './useBriefingRender';

export { useBriefingPreferences } from './useBriefingPreferences';
export type { UseBriefingPreferencesResult } from './useBriefingPreferences';
