/**
 * Events Custom Page - Root Application Component
 *
 * System-level Events page that replaces the OOB Events entity main view.
 * Combines EventCalendarFilter (left panel) and UniversalDatasetGrid (main area)
 * with EventDetailSidePane integration.
 *
 * Layout:
 * ┌────────────────────────────────────────────────────────────────────┐
 * │ Header: "Events" title + filters toolbar                           │
 * ├─────────────┬──────────────────────────────────────────────────────┤
 * │  Calendar   │  Grid                                                │
 * │  Filter     │  (Events data grid with date filtering)              │
 * │  (left)     │                                                      │
 * │             │                                                      │
 * │             │                                                      │
 * └─────────────┴──────────────────────────────────────────────────────┘
 *                                                 + Side Pane (right, on row click)
 *
 * Task 061: Integrated CalendarSection and GridSection components
 * Task 063: Added AssignedToFilter
 * Task 064: Added RecordTypeFilter
 * Task 065: Added StatusFilter
 * Task 066: Migrated to React Context for state management
 * - State managed via EventsPageContext
 * - Calendar, Grid, Filters communicate through context
 * - Side Pane integration via context callbacks
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/060-events-custompage-scaffolding.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/061-events-page-integrate-calendar.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/066-events-page-react-context.poml
 */

import * as React from "react";
import {
  FluentProvider,
  tokens,
  makeStyles,
  shorthands,
  Text,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  Divider,
} from "@fluentui/react-components";
import {
  Filter24Regular,
  ArrowSync24Regular,
  Settings24Regular,
  DismissCircle20Regular,
} from "@fluentui/react-icons";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import {
  EventsPageProvider,
  useEventsPageContext,
} from "./context";
import {
  CalendarSection,
  CalendarFilterOutput,
  GridSection,
  IEventDateInfo,
  AssignedToFilter,
  RecordTypeFilter,
  StatusFilter,
  getActionableStatuses,
} from "./components";

// ─────────────────────────────────────────────────────────────────────────────
// Xrm Type Declaration for Side Pane
// ─────────────────────────────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const VERSION = "1.5.0";
const CALENDAR_PANEL_WIDTH = 280; // px - width of left calendar panel
const EVENT_DETAIL_PANE_ID = "eventDetailPane";
const EVENT_DETAIL_PAGE_NAME = "sprk_eventdetailsidepane";
const PANE_WIDTH = 400;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("12px", "16px"),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
    minHeight: "48px",
  },
  headerLeft: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("16px"),
  },
  headerTitle: {
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  headerRight: {
    display: "flex",
    alignItems: "center",
  },
  filterToolbar: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
  },
  filterLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    marginRight: "4px",
  },
  clearFiltersButton: {
    fontSize: tokens.fontSizeBase200,
  },
  mainContent: {
    display: "flex",
    flexDirection: "row",
    flexGrow: 1,
    overflow: "hidden",
  },
  calendarPanel: {
    width: `${CALENDAR_PANEL_WIDTH}px`,
    minWidth: `${CALENDAR_PANEL_WIDTH}px`,
    display: "flex",
    flexDirection: "column",
    ...shorthands.borderRight("1px", "solid", tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
    overflow: "hidden",
  },
  gridPanel: {
    flexGrow: 1,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
  },
  footer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "flex-end",
    ...shorthands.padding("4px", "16px"),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke1),
    minHeight: "24px",
  },
  footerVersion: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Side Pane Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Check if Xrm.App.sidePanes API is available
 */
function isSidePanesAvailable(): boolean {
  return !!(
    typeof Xrm !== "undefined" &&
    Xrm.App &&
    Xrm.App.sidePanes
  );
}

/**
 * Open the Event Detail Side Pane for a specific event.
 * Reuses existing pane if open, otherwise creates a new one.
 *
 * @param eventId - GUID of the Event record
 * @param eventTypeId - GUID of the Event Type (optional)
 */
async function openEventDetailPane(
  eventId: string,
  eventTypeId?: string
): Promise<void> {
  console.log("[EventsPage] Opening side pane for event:", eventId);

  if (!isSidePanesAvailable()) {
    console.warn(
      "[EventsPage] Xrm.App.sidePanes not available. Cannot open side pane."
    );
    return;
  }

  try {
    const sidePanes = Xrm.App.sidePanes;

    // Build page name with query parameters
    const params = new URLSearchParams();
    params.set("eventId", eventId);
    if (eventTypeId) {
      params.set("eventType", eventTypeId);
    }
    const pageName = `${EVENT_DETAIL_PAGE_NAME}?${params.toString()}`;

    // Check for existing pane
    const existingPane = sidePanes.getPane(EVENT_DETAIL_PANE_ID);

    if (existingPane) {
      // Navigate existing pane to new event
      console.log("[EventsPage] Reusing existing side pane");
      await existingPane.navigate({
        pageType: "custom",
        name: pageName,
      });
      existingPane.select();
    } else {
      // Create new pane
      console.log("[EventsPage] Creating new side pane");
      const newPane = await sidePanes.createPane({
        title: "Event Details",
        paneId: EVENT_DETAIL_PANE_ID,
        canClose: true,
        width: PANE_WIDTH,
        isSelected: true,
      });

      await newPane.navigate({
        pageType: "custom",
        name: pageName,
      });
    }

    console.log("[EventsPage] Side pane opened successfully");
  } catch (error) {
    console.error("[EventsPage] Failed to open side pane:", error);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// EventsPageContent Component (uses context)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * EventsPageContent - Internal component that uses EventsPageContext.
 *
 * This component consumes the context and renders the page UI.
 * It is wrapped by EventsPageProvider in the App component.
 *
 * Task 066: Migrated from local state to context-based state management.
 */
const EventsPageContent: React.FC = () => {
  const styles = useStyles();

  // Get state and actions from context (Task 066)
  const {
    filters,
    eventDates,
    refreshTrigger,
    hasActiveFilters,
    setCalendarFilter,
    setAssignedToFilter,
    setStatusFilter,
    setRecordTypeFilter,
    clearAllFilters,
    openEvent,
    refreshGrid,
  } = useEventsPageContext();

  // ─────────────────────────────────────────────────────────────────────────
  // Event Handlers (now delegate to context)
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle calendar filter change from CalendarSection
   * Updates context state for GridSection to consume
   */
  const handleCalendarFilterChange = React.useCallback(
    (filter: CalendarFilterOutput | null) => {
      console.log("[EventsPage] Calendar filter changed:", filter);
      setCalendarFilter(filter);
    },
    [setCalendarFilter]
  );

  /**
   * Handle grid row click to open side pane
   * Updates context state and opens EventDetailSidePane
   */
  const handleRowClick = React.useCallback(
    (eventId: string, eventTypeId?: string) => {
      console.log("[EventsPage] Row clicked, eventId:", eventId);
      openEvent(eventId, eventTypeId);
    },
    [openEvent]
  );

  /**
   * Handle grid selection change
   * Could be used for bulk actions toolbar (future task)
   */
  const handleSelectionChange = React.useCallback((selectedIds: string[]) => {
    console.log("[EventsPage] Selection changed:", selectedIds.length, "selected");
    // Future: Update toolbar state for bulk actions
  }, []);

  /**
   * Handle refresh button click
   * Triggers grid data refresh via context
   */
  const handleRefresh = React.useCallback(() => {
    console.log("[EventsPage] Refresh requested");
    refreshGrid();
  }, [refreshGrid]);

  /**
   * Handle assigned-to filter change (Task 063)
   * Updates context state for GridSection filtering
   */
  const handleAssignedToChange = React.useCallback(
    (userIds: string[]) => {
      console.log("[EventsPage] Assigned-to filter changed:", userIds);
      setAssignedToFilter(userIds);
    },
    [setAssignedToFilter]
  );

  /**
   * Handle event type filter change (Task 064)
   * Updates context state for GridSection filtering
   * Note: RecordTypeFilter uses string[] but context allows flexible handling
   */
  const handleEventTypeChange = React.useCallback(
    (typeIds: string[]) => {
      console.log("[EventsPage] Event type filter changed:", typeIds);
      // Context expects string | null, use first selected or null
      setRecordTypeFilter(typeIds.length > 0 ? typeIds[0] : null);
    },
    [setRecordTypeFilter]
  );

  /**
   * Handle status filter change (Task 065)
   * Updates context state for GridSection filtering
   */
  const handleStatusChange = React.useCallback(
    (statusCodes: number[]) => {
      console.log("[EventsPage] Status filter changed:", statusCodes);
      setStatusFilter(statusCodes);
    },
    [setStatusFilter]
  );

  /**
   * Handle clear all filters
   * Resets all filters via context
   */
  const handleClearFilters = React.useCallback(() => {
    console.log("[EventsPage] Clearing all filters");
    clearAllFilters();
  }, [clearAllFilters]);

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  // Convert context recordType (string | null) to array for RecordTypeFilter
  const eventTypeFilterArray = filters.recordType ? [filters.recordType] : [];

  return (
    <div className={styles.root}>
      {/* Header: Title + Toolbar */}
      <header className={styles.header}>
        <div className={styles.headerLeft}>
          <Text className={styles.headerTitle}>Events</Text>
          <Divider vertical style={{ height: "24px" }} />
          {/* Filter Toolbar (Task 063, Task 064, Task 065, Task 066) */}
          <div className={styles.filterToolbar}>
            <Filter24Regular style={{ verticalAlign: "middle", marginRight: "4px" }} />
            <Text className={styles.filterLabel}>Assigned to:</Text>
            <AssignedToFilter
              selectedUserIds={filters.assignedToUserIds}
              onSelectionChange={handleAssignedToChange}
              placeholder="All users"
            />
            <ToolbarDivider />
            <Text className={styles.filterLabel}>Type:</Text>
            <RecordTypeFilter
              selectedTypeIds={eventTypeFilterArray}
              onSelectionChange={handleEventTypeChange}
              placeholder="All types"
            />
            <ToolbarDivider />
            <Text className={styles.filterLabel}>Status:</Text>
            <StatusFilter
              selectedStatuses={filters.statusCodes}
              onSelectionChange={handleStatusChange}
              placeholder="Status"
              autoSelectActionable={false}
            />
            {hasActiveFilters && (
              <ToolbarButton
                icon={<DismissCircle20Regular />}
                aria-label="Clear all filters"
                onClick={handleClearFilters}
                className={styles.clearFiltersButton}
                size="small"
              >
                Clear filters
              </ToolbarButton>
            )}
          </div>
        </div>
        <div className={styles.headerRight}>
          <Toolbar size="small">
            <ToolbarButton
              icon={<ArrowSync24Regular />}
              aria-label="Refresh"
              onClick={handleRefresh}
            />
            <ToolbarButton
              icon={<Settings24Regular />}
              aria-label="Settings"
            />
          </Toolbar>
        </div>
      </header>

      {/* Main Content: Calendar Panel + Grid Panel */}
      <main className={styles.mainContent}>
        {/* Left Panel: Calendar Filter (Task 061, Task 066) */}
        <aside className={styles.calendarPanel}>
          <CalendarSection
            eventDates={eventDates}
            onFilterChange={handleCalendarFilterChange}
          />
        </aside>

        {/* Main Panel: Events Grid (Task 061, Task 063, Task 064, Task 065, Task 066) */}
        <section className={styles.gridPanel}>
          <GridSection
            key={refreshTrigger}
            calendarFilter={filters.calendarFilter}
            assignedToFilter={filters.assignedToUserIds}
            eventTypeFilter={eventTypeFilterArray.length > 0 ? eventTypeFilterArray : undefined}
            statusFilter={filters.statusCodes}
            onRowClick={handleRowClick}
            onSelectionChange={handleSelectionChange}
          />
        </section>
      </main>

      {/* Footer: Version */}
      <footer className={styles.footer}>
        <Text className={styles.footerVersion}>v{VERSION}</Text>
      </footer>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// App Component (Provider wrapper)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * App - Root component that provides context and theme.
 *
 * Task 066: Wraps EventsPageContent with EventsPageProvider
 * to enable centralized state management via React Context.
 */
export const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveTheme);

  // Listen for theme changes
  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  /**
   * Handle opening event in side pane (called from context)
   * This callback is passed to EventsPageProvider
   */
  const handleOpenEvent = React.useCallback(
    (eventId: string, eventTypeId?: string) => {
      openEventDetailPane(eventId, eventTypeId);
    },
    []
  );

  return (
    <FluentProvider theme={theme}>
      <EventsPageProvider
        onOpenEvent={handleOpenEvent}
        initialFilters={{
          // Initialize with actionable statuses by default
          statusCodes: getActionableStatuses(),
        }}
      >
        <EventsPageContent />
      </EventsPageProvider>
    </FluentProvider>
  );
};

// Export for callback type definitions used by future integrations
export type CalendarFilterChangeHandler = (filter: CalendarFilterOutput | null) => void;
export type RowClickHandler = (eventId: string, eventTypeId?: string) => void;
