/**
 * @spaarke/events-components — context barrel
 *
 * Shared React Context for Events + Tasks state (filters, active event,
 * calendar dates, grid refresh). Originally `EventsPageContext` — kept the
 * existing name to preserve symbol stability for the standalone EventsPage
 * consumer; the Calendar widget consumes the same exports.
 *
 * `useCalendarFilter`, `useAssignedToFilter`, `useStatusFilter`, `useActiveEvent`,
 * `useGridRefresh` selector hooks — DELETED (task 087, C-25, 2026-10-03). Zero
 * consumers outside this barrel; the EventsPage-migration fallout left them
 * exported with no caller. See
 * projects/spaarke-ontology-platform-r1/notes/reuse-verification-2026-10-02.md §8.8 X21.
 */

export { EventsPageContext, EventsPageProvider, useEventsPageContext } from './EventsPageContext';

export type {
  EventsPageFilters,
  EventsPageState,
  EventsPageActions,
  EventsPageContextValue,
  EventsPageProviderProps,
} from './EventsPageContext';
