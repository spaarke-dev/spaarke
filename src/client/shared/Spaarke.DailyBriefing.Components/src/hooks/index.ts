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
 * To be populated by R2 task 014 (FR-06 split of `useNotificationData`):
 *  - `useBriefingNotifications`
 *  - `useBriefingPreferences`
 *  - `useBriefingActions`
 */

export { useInlineTodoCreate } from "./useInlineTodoCreate";
export type { UseInlineTodoCreateResult } from "./useInlineTodoCreate";

export { useBriefingNarration } from "./useBriefingNarration";
export type { UseBriefingNarrationResult } from "./useBriefingNarration";
