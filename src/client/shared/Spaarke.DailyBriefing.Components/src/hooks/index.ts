/**
 * @spaarke/daily-briefing-components — hooks barrel
 *
 * Reusable hooks for Daily Briefing surfaces. Per ADR-012, all hooks are
 * context-agnostic — dependencies are injected via the hook's arguments.
 *
 * Populated by R2 task 013 (Wave 3 hoist, FR-05):
 *  - `useBriefingNarration` — TL;DR + per-channel narrative bullets via BFF `/narrate`.
 *  - `useInlineTodoCreate` — `sprk_todo` creation with multi-entity regarding
 *    resolution per ADR-024 (TODO_REGARDING_CATALOG + applyResolverFields
 *    preserved verbatim from the original location).
 *
 * Populated by R2 task 014 (FR-06 split of `useNotificationData`):
 *  - `useBriefingNotifications` — fetches + groups appnotification records.
 *  - `useBriefingPreferences` — fetches + persists Daily Digest user preferences.
 *  - `useBriefingActions` — mark-as-read / mark-all-as-read / dismiss-all / refresh.
 *
 * Cross-hook coordination (e.g., "refetch notifications when preferences change")
 * happens at the CONSUMER layer via effects (Option A per FR-06 / design.md).
 * The three hooks intentionally share NO internal state, NO singleton, NO context.
 */

export { useInlineTodoCreate } from './useInlineTodoCreate';
export type { UseInlineTodoCreateResult } from './useInlineTodoCreate';

export { useBriefingNarration } from './useBriefingNarration';
export type { UseBriefingNarrationResult } from './useBriefingNarration';

export { useBriefingNotifications } from './useBriefingNotifications';
export type { UseBriefingNotificationsResult } from './useBriefingNotifications';

export { useBriefingPreferences } from './useBriefingPreferences';
export type { UseBriefingPreferencesResult } from './useBriefingPreferences';

export { useBriefingActions } from './useBriefingActions';
export type { UseBriefingActionsResult } from './useBriefingActions';
