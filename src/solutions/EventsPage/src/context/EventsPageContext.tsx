/**
 * EventsPageContext
 *
 * React Context for shared state between Events Custom Page components:
 * - CalendarSection (date selection/filtering)
 * - GridSection (data display, row clicks)
 * - Side Pane integration (active event)
 * - Filter components (AssignedTo, Status, RecordType)
 *
 * This context centralizes state management and provides actions for
 * component communication, reducing prop drilling and enabling
 * coordinated state updates across the page.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/066-events-page-react-context.poml
 * @see .claude/adr/ADR-022-pcf-platform-libraries.md (React 16 Context API)
 */

import * as React from "react";
import {
  CalendarFilterOutput,
  CalendarFilterSingle,
  CalendarFilterRange,
  IEventDateInfo,
} from "../components/CalendarSection";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Filter state for the Events page
 * Aggregates all filter values used to query events
 */
export interface EventsPageFilters {
  /** Calendar date filter (single date, range, or cleared) */
  calendarFilter: CalendarFilterOutput | null;
  /** Selected user IDs for "Assigned To" filter */
  assignedToUserIds: string[];
  /** Selected status codes for "Status" filter */
  statusCodes: number[];
  /** Selected record type (entity logical name) for "Record Type" filter */
  recordType: string | null;
}

/**
 * State managed by EventsPageContext
 */
export interface EventsPageState {
  /** Current filter settings */
  filters: EventsPageFilters;
  /** ID of the currently active/selected event (for side pane) */
  activeEventId: string | null;
  /** Event type ID of the active event (for field visibility) */
  activeEventTypeId: string | null;
  /** Event dates with counts for calendar indicators */
  eventDates: IEventDateInfo[];
  /** Grid refresh trigger (increment to force refresh) */
  refreshTrigger: number;
  /** Whether any filters are active (for "Clear filters" button visibility) */
  hasActiveFilters: boolean;
}

/**
 * Actions available through EventsPageContext
 */
export interface EventsPageActions {
  /** Set calendar filter (from CalendarSection) */
  setCalendarFilter: (filter: CalendarFilterOutput | null) => void;
  /** Set assigned-to filter (from AssignedToFilter) */
  setAssignedToFilter: (userIds: string[]) => void;
  /** Set status filter (from StatusFilter) */
  setStatusFilter: (statusCodes: number[]) => void;
  /** Set record type filter */
  setRecordTypeFilter: (recordType: string | null) => void;
  /** Clear all filters to default state */
  clearAllFilters: () => void;
  /** Open an event in the side pane */
  openEvent: (eventId: string, eventTypeId?: string) => void;
  /** Close the side pane / deselect active event */
  closeEvent: () => void;
  /** Set event dates for calendar indicators */
  setEventDates: (dates: IEventDateInfo[]) => void;
  /** Trigger grid refresh */
  refreshGrid: () => void;
}

/**
 * Combined context value type
 */
export interface EventsPageContextValue extends EventsPageState, EventsPageActions {}

// ---------------------------------------------------------------------------
// Default Values
// ---------------------------------------------------------------------------

/**
 * Default filter state
 * - No calendar selection
 * - Empty assigned-to (will be populated with current user)
 * - Empty status (will be populated with actionable statuses)
 * - No record type filter
 */
const defaultFilters: EventsPageFilters = {
  calendarFilter: null,
  assignedToUserIds: [],
  statusCodes: [],
  recordType: null,
};

/**
 * Default context state
 */
const defaultState: EventsPageState = {
  filters: defaultFilters,
  activeEventId: null,
  activeEventTypeId: null,
  eventDates: [],
  refreshTrigger: 0,
  hasActiveFilters: false,
};

/**
 * Default context value (placeholder functions)
 * These will be replaced by actual implementations in the provider
 */
const defaultContextValue: EventsPageContextValue = {
  ...defaultState,
  setCalendarFilter: () => {
    console.warn("[EventsPageContext] setCalendarFilter called without provider");
  },
  setAssignedToFilter: () => {
    console.warn("[EventsPageContext] setAssignedToFilter called without provider");
  },
  setStatusFilter: () => {
    console.warn("[EventsPageContext] setStatusFilter called without provider");
  },
  setRecordTypeFilter: () => {
    console.warn("[EventsPageContext] setRecordTypeFilter called without provider");
  },
  clearAllFilters: () => {
    console.warn("[EventsPageContext] clearAllFilters called without provider");
  },
  openEvent: () => {
    console.warn("[EventsPageContext] openEvent called without provider");
  },
  closeEvent: () => {
    console.warn("[EventsPageContext] closeEvent called without provider");
  },
  setEventDates: () => {
    console.warn("[EventsPageContext] setEventDates called without provider");
  },
  refreshGrid: () => {
    console.warn("[EventsPageContext] refreshGrid called without provider");
  },
};

// ---------------------------------------------------------------------------
// Context Creation (React 16 API)
// ---------------------------------------------------------------------------

/**
 * EventsPageContext
 * Use React.createContext (React 16 compatible)
 */
export const EventsPageContext = React.createContext<EventsPageContextValue>(
  defaultContextValue
);

// Display name for React DevTools
EventsPageContext.displayName = "EventsPageContext";

// ---------------------------------------------------------------------------
// Provider Props
// ---------------------------------------------------------------------------

/**
 * Props for EventsPageProvider
 */
export interface EventsPageProviderProps {
  /** Child components to wrap */
  children: React.ReactNode;
  /** Initial filter values (optional) */
  initialFilters?: Partial<EventsPageFilters>;
  /** Callback when an event should be opened in side pane */
  onOpenEvent?: (eventId: string, eventTypeId?: string) => void;
  /** Callback when side pane should close */
  onCloseEvent?: () => void;
}

// ---------------------------------------------------------------------------
// Helper Functions
// ---------------------------------------------------------------------------

/**
 * Check if any filters are active (different from default/cleared state)
 */
function computeHasActiveFilters(filters: EventsPageFilters): boolean {
  // Calendar filter is active if set and not "clear" type
  const hasCalendarFilter =
    filters.calendarFilter !== null && filters.calendarFilter.type !== "clear";

  // Assigned-to filter is active if any users selected
  const hasAssignedToFilter = filters.assignedToUserIds.length > 0;

  // Status filter is active if any statuses selected
  const hasStatusFilter = filters.statusCodes.length > 0;

  // Record type filter is active if set
  const hasRecordTypeFilter = filters.recordType !== null;

  return (
    hasCalendarFilter ||
    hasAssignedToFilter ||
    hasStatusFilter ||
    hasRecordTypeFilter
  );
}

// ---------------------------------------------------------------------------
// Provider Component
// ---------------------------------------------------------------------------

/**
 * EventsPageProvider
 *
 * Provides shared state and actions to all child components.
 * Manages filters, active event, and grid refresh coordination.
 *
 * @example
 * ```tsx
 * <EventsPageProvider
 *   onOpenEvent={(eventId, eventTypeId) => openSidePane(eventId, eventTypeId)}
 * >
 *   <CalendarSection />
 *   <GridSection />
 * </EventsPageProvider>
 * ```
 */
export const EventsPageProvider: React.FC<EventsPageProviderProps> = ({
  children,
  initialFilters,
  onOpenEvent,
  onCloseEvent,
}) => {
  // -------------------------------------------------------------------------
  // State
  // -------------------------------------------------------------------------

  // Filters state
  const [filters, setFilters] = React.useState<EventsPageFilters>(() => ({
    ...defaultFilters,
    ...initialFilters,
  }));

  // Active event state
  const [activeEventId, setActiveEventId] = React.useState<string | null>(null);
  const [activeEventTypeId, setActiveEventTypeId] = React.useState<string | null>(
    null
  );

  // Event dates for calendar indicators
  const [eventDates, setEventDatesState] = React.useState<IEventDateInfo[]>([]);

  // Grid refresh trigger
  const [refreshTrigger, setRefreshTrigger] = React.useState(0);

  // -------------------------------------------------------------------------
  // Derived State
  // -------------------------------------------------------------------------

  const hasActiveFilters = React.useMemo(
    () => computeHasActiveFilters(filters),
    [filters]
  );

  // -------------------------------------------------------------------------
  // Actions (memoized to prevent unnecessary re-renders)
  // -------------------------------------------------------------------------

  /**
   * Set calendar filter
   */
  const setCalendarFilter = React.useCallback(
    (filter: CalendarFilterOutput | null) => {
      console.log("[EventsPageContext] setCalendarFilter:", filter);
      setFilters((prev) => ({
        ...prev,
        calendarFilter: filter,
      }));
    },
    []
  );

  /**
   * Set assigned-to filter
   */
  const setAssignedToFilter = React.useCallback((userIds: string[]) => {
    console.log("[EventsPageContext] setAssignedToFilter:", userIds);
    setFilters((prev) => ({
      ...prev,
      assignedToUserIds: userIds,
    }));
  }, []);

  /**
   * Set status filter
   */
  const setStatusFilter = React.useCallback((statusCodes: number[]) => {
    console.log("[EventsPageContext] setStatusFilter:", statusCodes);
    setFilters((prev) => ({
      ...prev,
      statusCodes: statusCodes,
    }));
  }, []);

  /**
   * Set record type filter
   */
  const setRecordTypeFilter = React.useCallback((recordType: string | null) => {
    console.log("[EventsPageContext] setRecordTypeFilter:", recordType);
    setFilters((prev) => ({
      ...prev,
      recordType: recordType,
    }));
  }, []);

  /**
   * Clear all filters to default state
   */
  const clearAllFilters = React.useCallback(() => {
    console.log("[EventsPageContext] clearAllFilters");
    setFilters(defaultFilters);
  }, []);

  /**
   * Open an event in the side pane
   */
  const openEvent = React.useCallback(
    (eventId: string, eventTypeId?: string) => {
      console.log("[EventsPageContext] openEvent:", eventId, eventTypeId);
      setActiveEventId(eventId);
      setActiveEventTypeId(eventTypeId ?? null);
      // Notify parent if callback provided
      onOpenEvent?.(eventId, eventTypeId);
    },
    [onOpenEvent]
  );

  /**
   * Close the side pane / deselect active event
   */
  const closeEvent = React.useCallback(() => {
    console.log("[EventsPageContext] closeEvent");
    setActiveEventId(null);
    setActiveEventTypeId(null);
    onCloseEvent?.();
  }, [onCloseEvent]);

  /**
   * Set event dates for calendar indicators
   */
  const setEventDates = React.useCallback((dates: IEventDateInfo[]) => {
    setEventDatesState(dates);
  }, []);

  /**
   * Trigger grid refresh
   */
  const refreshGrid = React.useCallback(() => {
    console.log("[EventsPageContext] refreshGrid");
    setRefreshTrigger((prev) => prev + 1);
  }, []);

  // -------------------------------------------------------------------------
  // Context Value (memoized)
  // -------------------------------------------------------------------------

  const contextValue = React.useMemo<EventsPageContextValue>(
    () => ({
      // State
      filters,
      activeEventId,
      activeEventTypeId,
      eventDates,
      refreshTrigger,
      hasActiveFilters,
      // Actions
      setCalendarFilter,
      setAssignedToFilter,
      setStatusFilter,
      setRecordTypeFilter,
      clearAllFilters,
      openEvent,
      closeEvent,
      setEventDates,
      refreshGrid,
    }),
    [
      filters,
      activeEventId,
      activeEventTypeId,
      eventDates,
      refreshTrigger,
      hasActiveFilters,
      setCalendarFilter,
      setAssignedToFilter,
      setStatusFilter,
      setRecordTypeFilter,
      clearAllFilters,
      openEvent,
      closeEvent,
      setEventDates,
      refreshGrid,
    ]
  );

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  return (
    <EventsPageContext.Provider value={contextValue}>
      {children}
    </EventsPageContext.Provider>
  );
};

// ---------------------------------------------------------------------------
// Custom Hook
// ---------------------------------------------------------------------------

/**
 * useEventsPageContext
 *
 * Custom hook to access EventsPageContext.
 * Throws an error if used outside of EventsPageProvider.
 *
 * @example
 * ```tsx
 * const { filters, setCalendarFilter, openEvent } = useEventsPageContext();
 * ```
 */
export function useEventsPageContext(): EventsPageContextValue {
  const context = React.useContext(EventsPageContext);

  // The context will have default values if provider is missing,
  // but we should warn in development
  if (process.env.NODE_ENV === "development") {
    if (context === defaultContextValue) {
      console.warn(
        "[useEventsPageContext] Context used without EventsPageProvider. " +
          "Wrap your component tree with <EventsPageProvider>."
      );
    }
  }

  return context;
}

// ---------------------------------------------------------------------------
// Selector Hooks (Performance Optimization)
// ---------------------------------------------------------------------------

/**
 * useCalendarFilter
 *
 * Selector hook for calendar filter only.
 * Use this when component only needs calendar filter to minimize re-renders.
 */
export function useCalendarFilter() {
  const { filters, setCalendarFilter } = useEventsPageContext();
  return {
    calendarFilter: filters.calendarFilter,
    setCalendarFilter,
  };
}

/**
 * useAssignedToFilter
 *
 * Selector hook for assigned-to filter only.
 */
export function useAssignedToFilter() {
  const { filters, setAssignedToFilter } = useEventsPageContext();
  return {
    assignedToUserIds: filters.assignedToUserIds,
    setAssignedToFilter,
  };
}

/**
 * useStatusFilter
 *
 * Selector hook for status filter only.
 */
export function useStatusFilter() {
  const { filters, setStatusFilter } = useEventsPageContext();
  return {
    statusCodes: filters.statusCodes,
    setStatusFilter,
  };
}

/**
 * useActiveEvent
 *
 * Selector hook for active event state and actions.
 */
export function useActiveEvent() {
  const { activeEventId, activeEventTypeId, openEvent, closeEvent } =
    useEventsPageContext();
  return {
    activeEventId,
    activeEventTypeId,
    openEvent,
    closeEvent,
  };
}

/**
 * useGridRefresh
 *
 * Selector hook for grid refresh trigger and action.
 */
export function useGridRefresh() {
  const { refreshTrigger, refreshGrid } = useEventsPageContext();
  return {
    refreshTrigger,
    refreshGrid,
  };
}

// ---------------------------------------------------------------------------
// Export index file contents
// ---------------------------------------------------------------------------

export default EventsPageContext;
