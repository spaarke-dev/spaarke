/**
 * @spaarke/events-components — context barrel
 *
 * Shared React Context for Events + Tasks state (filters, active event,
 * calendar dates, grid refresh). Originally `EventsPageContext` — kept the
 * existing name to preserve symbol stability for the standalone EventsPage
 * consumer; the Calendar widget consumes the same exports.
 */

export {
  EventsPageContext,
  EventsPageProvider,
  useEventsPageContext,
  // Selector hooks (perf-optimized subscriptions)
  useCalendarFilter,
  useAssignedToFilter,
  useStatusFilter,
  useActiveEvent,
  useGridRefresh,
} from './EventsPageContext';

export type {
  EventsPageFilters,
  EventsPageState,
  EventsPageActions,
  EventsPageContextValue,
  EventsPageProviderProps,
} from './EventsPageContext';
