/**
 * Events Custom Page - Root Application Component
 *
 * System-level Events page that replaces the OOB Events entity main view.
 * Combines Calendar filter (right-side drawer) and data grid (main area)
 * with EventDetailSidePane integration for event editing.
 *
 * Phase 10 OOB Visual Parity: Calendar in Xrm.App.sidePanes (default open),
 * mutual exclusivity with Event side pane - clicking Event row deselects Calendar,
 * Calendar and Event appear as icons in the side pane menu.
 *
 * Layout (OOB Power Apps Visual Parity - Task 089):
 * ┌────────────────────────────────────────────────────────────────────────────────┐
 * │ CommandBar: [+New] [Delete(n)] [Refresh] [More] ...              (OOB-style)  │
 * ├────────────────────────────────────────────────────────────────────────────────┤
 * │ ViewToolbar: [ViewSelector ▼] (42 records)     [Edit filters] [Edit columns]  │
 * ├──────────────────────────────────────────────────────────────────────────────┬─┤
 * │  Filter Toolbar: Assigned to: [▼] | Type: [▼] | Status: [▼]   [Clear] [📅]   │
 * ├──────────────────────────────────────────────────────────────┬─────────────────┤
 * │  Grid                                                        │  Calendar       │
 * │  (Events data grid - view-driven columns)                    │  Panel          │
 * │                                                              │  (toggleable)   │
 * └──────────────────────────────────────────────────────────────┴─────────────────┘
 *                                                 + Side Pane (Xrm.App.sidePanes, on row click)
 *
 * Task 061: Integrated CalendarSection and GridSection components
 * Task 063: Added AssignedToFilter
 * Task 064: Added RecordTypeFilter
 * Task 065: Added StatusFilter
 * Task 066: Migrated to React Context for state management
 * Task 089: Refactored to use shared components (CommandBar, ViewToolbar)
 *           Added Event Type → Form GUID mapping for side pane navigation
 * Task 096: Moved Calendar to Xrm.App.sidePanes as CalendarSidePane web resource
 *           Calendar appears in side pane menu alongside Event
 *           Calendar opens by default on page load
 *           Mutual exclusivity: Calendar closes when Event opens
 * Task 097: Column headers with OOB-style dropdown menu (sort, filter)
 * Task 098: OOB-style bordered/shadowed containers for CommandBar and List View
 * - State managed via EventsPageContext
 * - Calendar communicates via postMessage with CalendarSidePane web resource
 * - Side Pane integration via Xrm.App.sidePanes
 * - Event Type determines which form is used in side pane
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/060-events-custompage-scaffolding.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/061-events-page-integrate-calendar.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/066-events-page-react-context.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/089-refactor-eventspage-grid.poml
 * @see projects/events-workspace-apps-UX-r1/notes/Event-Form-GUID.md
 * @see projects/events-workspace-apps-UX-r1/notes/Events-View-GUIDS.md
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
} from "@fluentui/react-components";
import {
  Add24Regular,
  Delete24Regular,
  ArrowClockwise24Regular,
  CalendarLtr24Regular,
} from "@fluentui/react-icons";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import {
  EventsPageProvider,
  useEventsPageContext,
} from "./context";
import {
  CalendarFilterOutput,
  GridSection,
  ViewSelectorDropdown,
  useViewSelection,
} from "./components";
import {
  getFormGuidForEventType,
  EVENT_DETAIL_PANE_ID,
  CALENDAR_PANE_ID,
  PANE_WIDTH,
  CALENDAR_PANE_WIDTH,
  EVENT_ENTITY_NAME,
  CALENDAR_WEB_RESOURCE_NAME,
} from "./config";

// ─────────────────────────────────────────────────────────────────────────────
// Xrm Type Declaration for Side Pane
// ─────────────────────────────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const VERSION = "2.16.0"; // Dialog mode: skip calendar pane, apply context filter

// ─────────────────────────────────────────────────────────────────────────────
// Drill-Through Parameter Parsing
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parameters passed from VisualHost PCF drill-through navigation.
 * Encoded as URL-encoded key=value pairs in the ?data= query parameter.
 */
interface DrillThroughParams {
  mode: string | null;
  entityName: string | null;
  filterField: string | null;
  filterValue: string | null;
  viewId: string | null;
}

/**
 * Parse drill-through parameters from the URL.
 * VisualHost passes context via: ?data=entityName=X&filterField=Y&filterValue=Z&mode=dialog
 */
function parseDrillThroughParams(): DrillThroughParams {
  try {
    const urlParams = new URLSearchParams(window.location.search);
    const data = urlParams.get("data");
    if (!data) {
      return { mode: null, entityName: null, filterField: null, filterValue: null, viewId: null };
    }
    const dataParams = new URLSearchParams(data);
    const params: DrillThroughParams = {
      mode: dataParams.get("mode"),
      entityName: dataParams.get("entityName"),
      filterField: dataParams.get("filterField"),
      filterValue: dataParams.get("filterValue"),
      viewId: dataParams.get("viewId"),
    };
    console.log("[EventsPage] Drill-through params:", params);
    return params;
  } catch (e) {
    console.warn("[EventsPage] Failed to parse drill-through params:", e);
    return { mode: null, entityName: null, filterField: null, filterValue: null, viewId: null };
  }
}

/** Parsed once at module load — immutable for the page lifecycle */
const DRILL_THROUGH_PARAMS = parseDrillThroughParams();

/** True when opened as a drill-through dialog from VisualHost */
const IS_DIALOG_MODE = DRILL_THROUGH_PARAMS.mode === "dialog";

// Session storage key for calendar filter (must match CalendarSidePane)
const CALENDAR_FILTER_STATE_KEY = "sprk_calendar_filter_state";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    backgroundColor: tokens.colorNeutralBackground2, // Subtle background to show container separation
    color: tokens.colorNeutralForeground1,
    ...shorthands.padding("12px", "16px"),
    ...shorthands.gap("12px"), // Spacing between CommandBar container and List View container
    boxSizing: "border-box",
  },
  // OOB-style CommandBar container (Task 098)
  // Distinct visual section with border, shadow, and background
  commandBarWrapper: {
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    boxShadow: tokens.shadow4, // Subtle shadow like OOB
  },
  // OOB-style List View container (Task 098)
  // Contains ViewToolbar + Grid as a distinct visual section
  listViewContainer: {
    display: "flex",
    flexDirection: "column",
    flexGrow: 1,
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    boxShadow: tokens.shadow4, // Subtle shadow like OOB
  },
  // OOB-style ViewToolbar (Task 089)
  viewToolbarWrapper: {
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
  },
  // Filter toolbar row (below ViewToolbar)
  filterToolbarRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("8px", "16px"),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
    minHeight: "40px",
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
    flexDirection: "column",
    flexGrow: 1,
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
    ...shorthands.padding("4px", "0"), // Reduced padding since root already has horizontal padding
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

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Get Xrm object from current window, parent, or top frame.
 * Web resources loaded via URL navigation are in iframes and need
 * to access the parent frame's Xrm object.
 */
function getXrm(): any | null {
  // Try current window first
  if (typeof Xrm !== "undefined" && Xrm?.App?.sidePanes) {
    console.log("[EventsPage] Found Xrm in current window");
    return Xrm;
  }

  // Try parent window (web resource in iframe)
  try {
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.App?.sidePanes) {
      console.log("[EventsPage] Found Xrm in parent window");
      return parentXrm;
    }
  } catch (e) {
    console.log("[EventsPage] Cannot access parent window (cross-origin)");
  }

  // Try top window
  try {
    const topXrm = (window.top as any)?.Xrm;
    if (topXrm?.App?.sidePanes) {
      console.log("[EventsPage] Found Xrm in top window");
      return topXrm;
    }
  } catch (e) {
    console.log("[EventsPage] Cannot access top window (cross-origin)");
  }

  console.log("[EventsPage] Xrm not found in any accessible frame");
  return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Open the Event Detail Side Pane for a specific event.
 * Uses native Dataverse form for editing, selecting the appropriate form
 * based on the Event Type.
 *
 * Task 089: Now uses Event Type → Form GUID mapping from configuration.
 * Task 096: Implements mutual exclusivity with Calendar pane.
 *           Re-registers Calendar pane if it was closed with 'x'.
 *
 * @param eventId - GUID of the Event record
 * @param eventTypeId - GUID of the Event Type (determines which form to use)
 */
async function openEventDetailPane(
  eventId: string,
  eventTypeId?: string
): Promise<void> {
  // Get the appropriate form based on Event Type (Task 089)
  const formId = getFormGuidForEventType(eventTypeId);
  console.log("[EventsPage] Opening side pane for event:", eventId, "eventTypeId:", eventTypeId, "formId:", formId);

  const xrm = getXrm();
  if (!xrm) {
    console.warn(
      "[EventsPage] Xrm.App.sidePanes not available. Cannot open side pane."
    );
    return;
  }

  try {
    const sidePanes = xrm.App.sidePanes;
    console.log("[EventsPage] sidePanes object:", sidePanes);

    // Task 096: Re-register Date Filter pane if it was closed with 'x'
    // This ensures Date Filter icon stays in the menu when opening Event pane
    const calendarPane = sidePanes.getPane(CALENDAR_PANE_ID);
    if (!calendarPane) {
      console.log("[EventsPage] Date Filter pane was closed, re-registering");
      // Re-register Date Filter pane (but don't select it - Event pane will be selected)
      await sidePanes.createPane({
        title: "Date Filter: Event",
        paneId: CALENDAR_PANE_ID,
        canClose: true,
        width: CALENDAR_PANE_WIDTH,
        isSelected: false, // Don't select - Event pane will be selected
        imageSrc: "WebResources/sprk_calendarline_24", // Calendar icon web resource
      });
      // Navigate to the CalendarSidePane web resource
      const newCalendarPane = sidePanes.getPane(CALENDAR_PANE_ID);
      if (newCalendarPane) {
        await newCalendarPane.navigate({
          pageType: "webresource",
          webresourceName: CALENDAR_WEB_RESOURCE_NAME,
        });
      }
      console.log("[EventsPage] Calendar pane re-registered");
    } else {
      console.log("[EventsPage] Calendar pane exists, will deselect for Event pane");
    }

    // Clean the event ID (remove braces if present)
    const cleanEventId = eventId.replace(/[{}]/g, "");

    // Check for existing pane
    console.log("[EventsPage] Checking for existing pane with ID:", EVENT_DETAIL_PANE_ID);
    const existingPane = sidePanes.getPane(EVENT_DETAIL_PANE_ID);
    console.log("[EventsPage] existingPane:", existingPane);

    // Navigation options for native Event form
    // Task 089: Form ID is now determined by Event Type
    const navigationOptions = {
      pageType: "entityrecord",
      entityName: EVENT_ENTITY_NAME,
      entityId: cleanEventId,
      formId: formId,
    };

    if (existingPane) {
      // Navigate existing pane to new event
      console.log("[EventsPage] Reusing existing side pane, navigating to:", cleanEventId);
      await existingPane.navigate(navigationOptions);
      existingPane.select();
    } else {
      // Create new pane
      console.log("[EventsPage] Creating new side pane");
      const newPane = await sidePanes.createPane({
        title: "Event",
        paneId: EVENT_DETAIL_PANE_ID,
        canClose: true,
        width: PANE_WIDTH,
        isSelected: true,
        imageSrc: "WebResources/sprk_tabaddline_24", // Event icon web resource
      });

      await newPane.navigate(navigationOptions);
    }

    console.log("[EventsPage] Side pane opened successfully with form:", formId);
  } catch (error) {
    console.error("[EventsPage] Failed to open side pane:", error);
  }
}

/**
 * Navigate to a new Event form to create a new record.
 */
function openNewEventForm(): void {
  console.log("[EventsPage] Opening new Event form");
  const xrm = getXrm();
  if (!xrm?.Navigation) {
    console.warn("[EventsPage] Xrm.Navigation not available. Cannot open new form.");
    return;
  }

  try {
    xrm.Navigation.openForm({
      entityName: EVENT_ENTITY_NAME,
    });
  } catch (error) {
    console.error("[EventsPage] Failed to open new Event form:", error);
  }
}

/**
 * Close the Event Detail Side Pane if it's open.
 * Used for mutual exclusivity with Calendar pane.
 */
function closeEventDetailPane(): void {
  const xrm = getXrm();
  if (!xrm?.App?.sidePanes) {
    return;
  }

  try {
    const sidePanes = xrm.App.sidePanes;
    const existingPane = sidePanes.getPane(EVENT_DETAIL_PANE_ID);
    if (existingPane) {
      console.log("[EventsPage] Closing Event side pane for Calendar");
      existingPane.close();
    }
  } catch (error) {
    console.error("[EventsPage] Failed to close side pane:", error);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Calendar Side Pane Functions (Task 096)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Message types for CalendarSidePane communication
 */
const CALENDAR_MESSAGE_TYPES = {
  CALENDAR_FILTER_CHANGED: "CALENDAR_FILTER_CHANGED",
  CALENDAR_EVENTS_UPDATE: "CALENDAR_EVENTS_UPDATE",
  CALENDAR_CLOSE: "CALENDAR_CLOSE",
  CALENDAR_READY: "CALENDAR_READY",
} as const;

/**
 * BroadcastChannel name for Events page communication.
 * Used for communication between sibling iframes (CalendarSidePane and EventsPage).
 */
const EVENTS_CHANNEL_NAME = "spaarke-events-page-channel";

/**
 * Register the Calendar side pane with Xrm.App.sidePanes.
 * Called on page load to add Calendar to the side pane menu.
 *
 * @returns Promise<void>
 */
async function registerCalendarPane(): Promise<void> {
  const xrm = getXrm();
  if (!xrm?.App?.sidePanes) {
    console.warn("[EventsPage] Xrm.App.sidePanes not available. Cannot register Calendar pane.");
    return;
  }

  try {
    const sidePanes = xrm.App.sidePanes;
    console.log("[EventsPage] Registering Calendar side pane");

    // Check if already registered
    const existingPane = sidePanes.getPane(CALENDAR_PANE_ID);
    if (existingPane) {
      console.log("[EventsPage] Calendar pane already registered, selecting it");
      existingPane.select();
      return;
    }

    // Create the Date Filter side pane
    // Note: imageSrc uses web resource path for side pane menu icon
    const calendarPane = await sidePanes.createPane({
      title: "Date Filter: Event",
      paneId: CALENDAR_PANE_ID,
      canClose: true,
      width: CALENDAR_PANE_WIDTH,
      isSelected: true, // Open by default
      imageSrc: "WebResources/sprk_calendarline_24", // Calendar icon web resource
    });

    // Navigate to the CalendarSidePane web resource
    await calendarPane.navigate({
      pageType: "webresource",
      webresourceName: CALENDAR_WEB_RESOURCE_NAME,
    });

    console.log("[EventsPage] Calendar side pane registered and opened");
  } catch (error) {
    console.error("[EventsPage] Failed to register Calendar pane:", error);
  }
}

/**
 * Open the Calendar side pane.
 * Closes the Event side pane for mutual exclusivity.
 */
async function openCalendarPane(): Promise<void> {
  const xrm = getXrm();
  if (!xrm?.App?.sidePanes) {
    console.warn("[EventsPage] Xrm.App.sidePanes not available.");
    return;
  }

  try {
    // Close Event pane for mutual exclusivity
    closeEventDetailPane();

    const sidePanes = xrm.App.sidePanes;
    const calendarPane = sidePanes.getPane(CALENDAR_PANE_ID);

    if (calendarPane) {
      calendarPane.select();
      console.log("[EventsPage] Calendar pane selected");
    } else {
      // If not registered, register it
      await registerCalendarPane();
    }
  } catch (error) {
    console.error("[EventsPage] Failed to open Calendar pane:", error);
  }
}

/**
 * Close the Calendar side pane.
 * Used for mutual exclusivity when Event pane opens.
 */
function closeCalendarPane(): void {
  const xrm = getXrm();
  if (!xrm?.App?.sidePanes) {
    return;
  }

  try {
    const sidePanes = xrm.App.sidePanes;
    const calendarPane = sidePanes.getPane(CALENDAR_PANE_ID);
    if (calendarPane) {
      console.log("[EventsPage] Closing Calendar side pane");
      // Don't close, just deselect - let user close manually if desired
      // calendarPane.close();
    }
  } catch (error) {
    console.error("[EventsPage] Failed to close Calendar pane:", error);
  }
}

/**
 * Send event dates to the CalendarSidePane for indicator display.
 * Uses BroadcastChannel for cross-iframe communication.
 *
 * @param eventDates - Array of date strings with event counts
 */
function sendEventDatesToCalendar(eventDates: Array<{ date: string; count: number }>): void {
  try {
    const message = {
      type: CALENDAR_MESSAGE_TYPES.CALENDAR_EVENTS_UPDATE,
      payload: { eventDates },
    };

    // PRIMARY: Use BroadcastChannel for cross-iframe communication
    if (typeof BroadcastChannel !== "undefined") {
      const channel = new BroadcastChannel(EVENTS_CHANNEL_NAME);
      channel.postMessage(message);
      channel.close();
      console.log("[EventsPage] Sent event dates via BroadcastChannel:", eventDates.length, "dates");
    }

    // FALLBACK: Send via postMessage to any iframe (CalendarSidePane web resource)
    const iframes = document.querySelectorAll("iframe");
    iframes.forEach((iframe) => {
      try {
        iframe.contentWindow?.postMessage(message, "*");
      } catch {
        // Cross-origin, skip
      }
    });

    // Also dispatch as custom event for same-window scenarios
    window.dispatchEvent(
      new CustomEvent("calendar-events-update", { detail: { eventDates } })
    );
  } catch (error) {
    console.error("[EventsPage] Failed to send event dates:", error);
  }
}

/**
 * Delete selected Event records.
 * @param selectedIds - Array of Event record GUIDs to delete
 */
async function deleteSelectedEvents(selectedIds: string[]): Promise<void> {
  console.log("[EventsPage] Deleting events:", selectedIds);
  const xrm = getXrm();
  if (!xrm?.WebApi) {
    console.warn("[EventsPage] Xrm.WebApi not available. Cannot delete.");
    return;
  }

  // Confirm deletion
  const confirmed = window.confirm(
    `Are you sure you want to delete ${selectedIds.length} event${selectedIds.length !== 1 ? "s" : ""}?`
  );
  if (!confirmed) {
    console.log("[EventsPage] Deletion cancelled by user");
    return;
  }

  try {
    // Delete each record
    for (const id of selectedIds) {
      const cleanId = id.replace(/[{}]/g, "");
      await xrm.WebApi.deleteRecord(EVENT_ENTITY_NAME, cleanId);
    }
    console.log("[EventsPage] Successfully deleted", selectedIds.length, "events");
    // Trigger refresh via context (caller should handle this)
  } catch (error) {
    console.error("[EventsPage] Failed to delete events:", error);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// CommandBar Component (OOB-style)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * EventsCommandBar - OOB-style command bar for Events page.
 *
 * Task 089: Implements command bar with New, Delete, Refresh, Calendar buttons
 * Task 096: Calendar button reopens Date Filter side pane if closed
 */
interface EventsCommandBarProps {
  selectedIds: string[];
  onNew: () => void;
  onDelete: () => void;
  onRefresh: () => void;
  onCalendar: () => void;
}

const useCommandBarStyles = makeStyles({
  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.padding("8px", "16px"),
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("4px"),
  },
  deleteButton: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("4px"),
  },
  badge: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    ...shorthands.borderRadius("50%"),
    minWidth: "18px",
    height: "18px",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    marginLeft: "4px",
  },
});

const EventsCommandBar: React.FC<EventsCommandBarProps> = ({
  selectedIds,
  onNew,
  onDelete,
  onRefresh,
  onCalendar,
}) => {
  const styles = useCommandBarStyles();

  return (
    <Toolbar className={styles.toolbar} size="small">
      {/* OOB-style New button - subtle appearance with + icon */}
      <ToolbarButton
        icon={<Add24Regular />}
        onClick={onNew}
        appearance="subtle"
      >
        New
      </ToolbarButton>
      <ToolbarDivider />
      <ToolbarButton
        icon={<Delete24Regular />}
        onClick={onDelete}
        disabled={selectedIds.length === 0}
        appearance="subtle"
      >
        Delete
      </ToolbarButton>
      <ToolbarDivider />
      <ToolbarButton
        icon={<ArrowClockwise24Regular />}
        appearance="subtle"
        onClick={onRefresh}
      >
        Refresh
      </ToolbarButton>
      <ToolbarDivider />
      <ToolbarButton
        icon={<CalendarLtr24Regular />}
        appearance="subtle"
        onClick={onCalendar}
      >
        Calendar
      </ToolbarButton>
    </Toolbar>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// ViewToolbar Component (OOB-style)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * EventsViewToolbar - OOB-style view toolbar with view selector.
 *
 * Task 089: Displays current view name.
 * Task 093: Added ViewSelectorDropdown for switching between saved views.
 * Fix: Removed record count to match OOB style.
 */
interface EventsViewToolbarProps {
  selectedViewId: string;
  selectedViewName: string;
  onViewChange: (viewId: string, viewName: string) => void;
}

const useViewToolbarStyles = makeStyles({
  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.padding("8px", "16px"),
    display: "flex",
    alignItems: "center",
  },
});

const EventsViewToolbar: React.FC<EventsViewToolbarProps> = ({
  selectedViewId,
  onViewChange,
}) => {
  const styles = useViewToolbarStyles();

  return (
    <div className={styles.toolbar}>
      {/* View Selector Dropdown (Task 093) */}
      <ViewSelectorDropdown
        selectedViewId={selectedViewId}
        onViewChange={onViewChange}
      />
    </div>
  );
};

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
 * Task 089: Refactored to use CommandBar and ViewToolbar.
 * Task 096: Integrated CalendarSidePane via Xrm.App.sidePanes.
 */
const EventsPageContent: React.FC = () => {
  const styles = useStyles();

  // Get state and actions from context (Task 066)
  // Note: Task 094 moved toolbar filters to column headers in GridSection
  const {
    filters,
    eventDates,
    refreshTrigger,
    setCalendarFilter,
    openEvent,
    refreshGrid,
  } = useEventsPageContext();

  // Selected record IDs for CommandBar (Task 089)
  const [selectedIds, setSelectedIds] = React.useState<string[]>([]);

  // View selection state with session storage persistence (Task 093)
  const [selectedViewId, setSelectedViewId, selectedViewName] = useViewSelection();

  // ─────────────────────────────────────────────────────────────────────────
  // Calendar Side Pane Registration (Task 096)
  // ─────────────────────────────────────────────────────────────────────────

  // Register Calendar side pane on page load (default open)
  // v2.16.0: Skip in dialog mode — calendar is not relevant for drill-through
  React.useEffect(() => {
    // Small delay to ensure Xrm is available
    const timer = setTimeout(() => {
      if (IS_DIALOG_MODE) {
        console.log("[EventsPage] Dialog mode — skipping Calendar side pane registration");
      } else {
        registerCalendarPane();
      }
    }, 500);

    /**
     * Close all side panes and clear filter state - used for cleanup on page navigation
     * Prevents panes from persisting when navigating to other modules
     * v2.14.0: More aggressive cleanup with multiple close attempts
     */
    const closeAllSidePanes = () => {
      console.log("[EventsPage] v2.14.0 closeAllSidePanes called");
      const xrm = getXrm();
      if (xrm?.App?.sidePanes) {
        // Close Calendar pane
        try {
          const calendarPane = xrm.App.sidePanes.getPane(CALENDAR_PANE_ID);
          if (calendarPane) {
            calendarPane.close();
            console.log("[EventsPage] Closed Calendar pane on navigation away");
          }
        } catch (err) {
          console.warn("[EventsPage] Could not close Calendar pane:", err);
        }
        // Close Event detail pane (Issue #2 fix - prevent persistence after navigation)
        try {
          const eventPane = xrm.App.sidePanes.getPane(EVENT_DETAIL_PANE_ID);
          if (eventPane) {
            console.log("[EventsPage] v2.14.0 Found Event pane, closing...");
            eventPane.close();
            console.log("[EventsPage] Closed Event pane on navigation away");
          } else {
            console.log("[EventsPage] v2.14.0 Event pane not found in sidePanes");
          }
        } catch (err) {
          console.warn("[EventsPage] Could not close Event pane:", err);
        }
      } else {
        console.log("[EventsPage] v2.14.0 Xrm.App.sidePanes not available");
      }
      // Clear calendar filter session storage when navigating away from Events page
      try {
        sessionStorage.removeItem(CALENDAR_FILTER_STATE_KEY);
        console.log("[EventsPage] Cleared calendar filter state on navigation away");
      } catch (err) {
        console.warn("[EventsPage] Could not clear filter state:", err);
      }
    };

    // Listen for page unload events (navigation away from custom page)
    // This is more reliable than React unmount in Dataverse iframe scenarios
    const handleBeforeUnload = () => {
      console.log("[EventsPage] v2.14.0 beforeunload event fired");
      closeAllSidePanes();
    };

    const handlePageHide = () => {
      console.log("[EventsPage] v2.14.0 pagehide event fired");
      closeAllSidePanes();
    };

    // v2.14.0: Also listen for visibility change (more reliable in some iframe scenarios)
    const handleVisibilityChange = () => {
      if (document.visibilityState === "hidden") {
        console.log("[EventsPage] v2.15.0 visibilitychange (hidden) event fired");
        closeAllSidePanes();
      }
    };

    // v2.15.0: Proactive navigation detection - watch parent URL for changes
    // Dataverse SPA navigation changes the URL without firing unload events
    let lastParentUrl = "";
    let navigationCheckInterval: ReturnType<typeof setInterval> | null = null;

    try {
      lastParentUrl = window.parent?.location?.href || "";
    } catch {
      // Cross-origin, can't access
    }

    const checkForNavigation = () => {
      try {
        const currentUrl = window.parent?.location?.href || "";
        if (lastParentUrl && currentUrl && currentUrl !== lastParentUrl) {
          // URL changed - check if we navigated away from Events
          const isStillEvents = currentUrl.includes("sprk_event") ||
                                currentUrl.includes("/Events") ||
                                currentUrl.includes("etn=sprk_event");
          if (!isStillEvents) {
            console.log("[EventsPage] v2.15.0 Navigation detected away from Events, closing panes");
            closeAllSidePanes();
          }
          lastParentUrl = currentUrl;
        }
      } catch {
        // Cross-origin or error - ignore
      }
    };

    // Check frequently (200ms) for navigation
    navigationCheckInterval = setInterval(checkForNavigation, 200);

    // Also listen for hashchange/popstate on parent window
    const handleParentNavigation = () => {
      console.log("[EventsPage] v2.15.0 Parent navigation event fired");
      checkForNavigation();
    };

    try {
      window.parent?.addEventListener("hashchange", handleParentNavigation);
      window.parent?.addEventListener("popstate", handleParentNavigation);
    } catch {
      // Cross-origin, can't add listeners
    }

    window.addEventListener("beforeunload", handleBeforeUnload);
    window.addEventListener("pagehide", handlePageHide);
    document.addEventListener("visibilitychange", handleVisibilityChange);

    // Cleanup: Close all side panes when navigating away from Events page
    return () => {
      console.log("[EventsPage] v2.15.0 React cleanup running");
      clearTimeout(timer);
      if (navigationCheckInterval) {
        clearInterval(navigationCheckInterval);
      }
      closeAllSidePanes();
      window.removeEventListener("beforeunload", handleBeforeUnload);
      window.removeEventListener("pagehide", handlePageHide);
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      try {
        window.parent?.removeEventListener("hashchange", handleParentNavigation);
        window.parent?.removeEventListener("popstate", handleParentNavigation);
      } catch {
        // Cross-origin, ignore
      }
    };
  }, []);

  // Send event dates to Calendar side pane when they change
  React.useEffect(() => {
    if (eventDates && eventDates.length > 0) {
      // Transform eventDates to the format expected by CalendarSidePane
      const dateInfo = eventDates.map((date) => ({
        date,
        count: 1, // Each date has at least one event
      }));
      sendEventDatesToCalendar(dateInfo);
    }
  }, [eventDates]);

  // Listen for messages from CalendarSidePane via BroadcastChannel and postMessage
  React.useEffect(() => {
    /**
     * Handle incoming calendar messages
     */
    const handleCalendarMessage = (data: { type?: string; payload?: unknown }) => {
      switch (data.type) {
        case CALENDAR_MESSAGE_TYPES.CALENDAR_FILTER_CHANGED: {
          const payload = data.payload as { filter?: CalendarFilterOutput | null };
          console.log("[EventsPage] Received calendar filter change:", payload.filter);
          setCalendarFilter(payload.filter ?? null);
          break;
        }
        case CALENDAR_MESSAGE_TYPES.CALENDAR_READY: {
          // Calendar is ready, send current event dates
          console.log("[EventsPage] Calendar side pane ready, sending event dates");
          if (eventDates && eventDates.length > 0) {
            const dateInfo = eventDates.map((date) => ({
              date,
              count: 1,
            }));
            sendEventDatesToCalendar(dateInfo);
          }
          break;
        }
        case CALENDAR_MESSAGE_TYPES.CALENDAR_CLOSE: {
          // Calendar requested close - no action needed, handled by Xrm
          console.log("[EventsPage] Calendar close request received");
          break;
        }
      }
    };

    // PRIMARY: BroadcastChannel listener for cross-iframe communication
    // (CalendarSidePane and EventsPage are sibling iframes under Dataverse shell)
    let broadcastChannel: BroadcastChannel | null = null;
    if (typeof BroadcastChannel !== "undefined") {
      broadcastChannel = new BroadcastChannel(EVENTS_CHANNEL_NAME);
      broadcastChannel.onmessage = (event) => {
        if (event.data && typeof event.data === "object") {
          console.log("[EventsPage] BroadcastChannel message received:", event.data.type);
          handleCalendarMessage(event.data);
        }
      };
      console.log("[EventsPage] BroadcastChannel listener registered");
    }

    // FALLBACK: postMessage listener
    const handlePostMessage = (event: MessageEvent) => {
      if (!event.data || typeof event.data !== "object") {
        return;
      }
      handleCalendarMessage(event.data);
    };
    window.addEventListener("message", handlePostMessage);

    // Also listen for custom events (same-window scenarios)
    const handleCustomFilterEvent = (event: CustomEvent<{ filter?: CalendarFilterOutput | null }>) => {
      if (event.detail?.filter !== undefined) {
        setCalendarFilter(event.detail.filter);
      }
    };

    window.addEventListener(
      "calendar-filter-changed" as keyof WindowEventMap,
      handleCustomFilterEvent as EventListener
    );

    return () => {
      if (broadcastChannel) {
        broadcastChannel.close();
      }
      window.removeEventListener("message", handlePostMessage);
      window.removeEventListener(
        "calendar-filter-changed" as keyof WindowEventMap,
        handleCustomFilterEvent as EventListener
      );
    };
  }, [eventDates, setCalendarFilter]);

  // ─────────────────────────────────────────────────────────────────────────
  // CommandBar Handlers (Task 089)
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle "New" button click - opens new Event form
   */
  const handleNew = React.useCallback(() => {
    console.log("[EventsPage] New button clicked");
    openNewEventForm();
  }, []);

  /**
   * Handle "Delete" button click - deletes selected events
   */
  const handleDelete = React.useCallback(async () => {
    console.log("[EventsPage] Delete button clicked, selected:", selectedIds);
    if (selectedIds.length > 0) {
      await deleteSelectedEvents(selectedIds);
      setSelectedIds([]); // Clear selection after delete
      refreshGrid(); // Refresh the grid
    }
  }, [selectedIds, refreshGrid]);

  // ─────────────────────────────────────────────────────────────────────────
  // Event Handlers (now delegate to context)
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle grid row click to open side pane
   * Updates context state and opens EventDetailSidePane.
   * Task 096: Mutual exclusivity - opening Event closes Calendar
   */
  const handleRowClick = React.useCallback(
    (eventId: string, eventTypeId?: string) => {
      console.log("[EventsPage] Row clicked, eventId:", eventId);
      // Close Calendar side pane - mutual exclusivity with Event side pane
      closeCalendarPane();
      openEvent(eventId, eventTypeId);
    },
    [openEvent]
  );

  /**
   * Handle grid selection change (Task 089)
   * Updates selected IDs for CommandBar bulk actions
   */
  const handleSelectionChange = React.useCallback((ids: string[]) => {
    console.log("[EventsPage] Selection changed:", ids.length, "selected");
    setSelectedIds(ids);
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
   * Handle calendar button click
   * Opens or selects the Date Filter side pane
   */
  const handleCalendar = React.useCallback(() => {
    console.log("[EventsPage] Calendar button clicked");
    openCalendarPane();
  }, []);

  /**
   * Note: Task 094 moved toolbar filters (Assigned To, Type, Status) to column headers
   * in GridSection. The old handler functions have been removed.
   */

  /**
   * Handle view change from ViewSelectorDropdown (Task 093)
   * Updates selected view and triggers grid refresh
   */
  const handleViewChange = React.useCallback(
    (viewId: string, viewName: string) => {
      console.log("[EventsPage] View changed:", viewId, viewName);
      setSelectedViewId(viewId);
      // Trigger grid refresh to load new view data
      refreshGrid();
    },
    [setSelectedViewId, refreshGrid]
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* CommandBar: New, Delete, Refresh, Calendar (Task 089) */}
      <div className={styles.commandBarWrapper}>
        <EventsCommandBar
          selectedIds={selectedIds}
          onNew={handleNew}
          onDelete={handleDelete}
          onRefresh={handleRefresh}
          onCalendar={handleCalendar}
        />
      </div>

      {/* List View Container: ViewToolbar + Grid in distinct visual container (Task 098) */}
      <div className={styles.listViewContainer}>
        {/* ViewToolbar: View selector dropdown (Task 089, Task 093) */}
        <div className={styles.viewToolbarWrapper}>
          <EventsViewToolbar
            selectedViewId={selectedViewId}
            selectedViewName={selectedViewName}
            onViewChange={handleViewChange}
          />
        </div>

        {/* Task 094: Filter toolbar removed - filtering now done via column headers */}

        {/* Main Content: Full-width Grid */}
        <main className={styles.mainContent}>
          {/* Events Grid - full width */}
          <section className={styles.gridPanel}>
            <GridSection
              key={refreshTrigger}
              calendarFilter={filters.calendarFilter}
              // Task 094: Removed assignedToFilter, eventTypeFilter, statusFilter
              // Filtering now done client-side via column headers
              viewId={selectedViewId}
              onRowClick={handleRowClick}
              onSelectionChange={handleSelectionChange}
              contextFilter={
                DRILL_THROUGH_PARAMS.filterField && DRILL_THROUGH_PARAMS.filterValue
                  ? { fieldName: DRILL_THROUGH_PARAMS.filterField, value: DRILL_THROUGH_PARAMS.filterValue }
                  : undefined
              }
            />
          </section>
        </main>
      </div>

      {/* Calendar is now in Xrm.App.sidePanes menu (Task 096) */}

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
 * Task 089: Updated side pane handler to use Event Type → Form mapping.
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
   * Task 089: Now uses Event Type → Form GUID mapping
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
        // Task 094: Removed initialFilters - filtering now done client-side via column headers
      >
        <EventsPageContent />
      </EventsPageProvider>
    </FluentProvider>
  );
};

// Export for callback type definitions used by future integrations
export type CalendarFilterChangeHandler = (filter: CalendarFilterOutput | null) => void;
export type RowClickHandler = (eventId: string, eventTypeId?: string) => void;
