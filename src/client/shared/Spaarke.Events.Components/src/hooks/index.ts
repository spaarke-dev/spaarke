/**
 * @spaarke/events-components — hooks barrel
 *
 * Reusable hooks for Events surfaces. Per ADR-012, all hooks are
 * context-agnostic — dependencies are injected via the hook's arguments.
 *
 * Current exports:
 *  - `useEventsBulkActions` (R4 task 063, B-7) — bulk status update +
 *    archive operations for Events. Consumed by EventsPage standalone
 *    and CalendarWorkspaceWidget.
 *
 * Selector hooks for data + filter state are exposed via
 * `context/EventsPageContext.tsx` (not re-exported here — they're host-
 * driven, not standalone hooks).
 */

export * from './useEventsBulkActions';
