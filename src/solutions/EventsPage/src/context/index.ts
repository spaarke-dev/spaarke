/**
 * Context module exports
 *
 * Exports for Events Page state management via React Context.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/066-events-page-react-context.poml
 */

export {
  EventsPageContext,
  EventsPageProvider,
  useEventsPageContext,
  // Selector hooks for performance optimization
  useCalendarFilter,
  useAssignedToFilter,
  useStatusFilter,
  useActiveEvent,
  useGridRefresh,
} from "./EventsPageContext";

export type {
  EventsPageFilters,
  EventsPageState,
  EventsPageActions,
  EventsPageContextValue,
  EventsPageProviderProps,
} from "./EventsPageContext";
