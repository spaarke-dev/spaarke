/**
 * GridSection Component
 *
 * Displays Events data in a grid format within the Events Custom Page.
 * Fetches data via Dataverse WebAPI and applies calendar filter.
 *
 * Features:
 * - Fetches Events from Dataverse WebAPI
 * - Applies date filter from CalendarSection
 * - Row click opens EventDetailSidePane
 * - Checkbox selection for bulk actions
 * - Matches Power Apps grid styling (per spec)
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/061-events-page-integrate-calendar.poml
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Text,
  Spinner,
  Link,
  Checkbox,
  Badge,
} from "@fluentui/react-components";
import {
  CalendarFilterOutput,
  CalendarFilterSingle,
  CalendarFilterRange,
} from "./CalendarSection";

// ─────────────────────────────────────────────────────────────────────────────
// Xrm Type Declaration
// ─────────────────────────────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Event record from Dataverse
 */
export interface IEventRecord {
  sprk_eventid: string;
  sprk_eventname: string;
  sprk_duedate: string | null;
  statecode: number;
  statuscode: number;
  sprk_priority: number | null;
  _ownerid_value: string | null;
  "_ownerid_value@OData.Community.Display.V1.FormattedValue"?: string;
  "_sprk_eventtype_value"?: string;
  "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue"?: string;
  "statuscode@OData.Community.Display.V1.FormattedValue"?: string;
  "sprk_priority@OData.Community.Display.V1.FormattedValue"?: string;
  /** Parent record name (Matter, Project, etc.) */
  sprk_regardingrecordname?: string;
  /** Parent record entity logical name */
  sprk_regardingrecordtype?: string;
  /** Parent record GUID for navigation */
  sprk_regardingrecordid?: string;
}

/**
 * Props for GridSection component
 */
export interface GridSectionProps {
  /** Calendar filter to apply */
  calendarFilter: CalendarFilterOutput | null;
  /** Assigned-to filter - array of user IDs (Task 063) */
  assignedToFilter?: string[];
  /** Event type filter - array of event type IDs (Task 064) */
  eventTypeFilter?: string[];
  /** Status filter - array of statuscode values (Task 065) */
  statusFilter?: number[];
  /** Callback when a row is clicked (to open side pane) */
  onRowClick?: (eventId: string, eventTypeId?: string) => void;
  /** Callback when row selection changes */
  onSelectionChange?: (selectedIds: string[]) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    overflow: "hidden",
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
  },
  errorContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    ...shorthands.padding("24px"),
    color: tokens.colorPaletteRedForeground1,
  },
  emptyContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    ...shorthands.padding("24px"),
    color: tokens.colorNeutralForeground3,
  },
  gridContainer: {
    flex: 1,
    overflow: "auto",
  },
  table: {
    width: "100%",
    borderCollapse: "collapse",
    fontSize: tokens.fontSizeBase200,
  },
  tableHeader: {
    backgroundColor: tokens.colorNeutralBackground2,
    position: "sticky",
    top: 0,
    zIndex: 1,
  },
  th: {
    ...shorthands.padding("10px", "12px"),
    textAlign: "left",
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
    whiteSpace: "nowrap",
  },
  thCheckbox: {
    width: "40px",
    ...shorthands.padding("10px", "8px"),
  },
  tr: {
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    cursor: "pointer",
  },
  trSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Selected,
    },
  },
  td: {
    ...shorthands.padding("10px", "12px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
    verticalAlign: "middle",
  },
  tdCheckbox: {
    width: "40px",
    ...shorthands.padding("10px", "8px"),
  },
  eventNameLink: {
    fontWeight: tokens.fontWeightSemibold,
    cursor: "pointer",
    color: tokens.colorBrandForeground1,
    textDecoration: "none",
    ":hover": {
      textDecoration: "underline",
    },
  },
  statusBadge: {
    textTransform: "capitalize",
  },
  priorityHigh: {
    color: tokens.colorPaletteRedForeground1,
  },
  priorityNormal: {
    color: tokens.colorNeutralForeground1,
  },
  priorityLow: {
    color: tokens.colorNeutralForeground3,
  },
  footer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground2,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format date for display
 */
function formatDate(dateStr: string | null): string {
  if (!dateStr) return "—";
  try {
    const date = new Date(dateStr);
    return date.toLocaleDateString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
    });
  } catch {
    return "—";
  }
}

/**
 * Build OData filter for date filtering
 */
function buildDateFilter(filter: CalendarFilterOutput | null): string {
  if (!filter || filter.type === "clear") {
    return "";
  }

  if (filter.type === "single") {
    const singleFilter = filter as CalendarFilterSingle;
    // Filter events where due date matches the selected date
    // Using date-only comparison
    return `Microsoft.Dynamics.CRM.On(PropertyName='sprk_duedate',PropertyValue=${singleFilter.date})`;
  }

  if (filter.type === "range") {
    const rangeFilter = filter as CalendarFilterRange;
    // Filter events where due date is within the range
    return `Microsoft.Dynamics.CRM.Between(PropertyName='sprk_duedate',PropertyValues=['${rangeFilter.start}','${rangeFilter.end}'])`;
  }

  return "";
}

/**
 * Build OData filter for assigned-to (owner) filtering
 * Task 063: Filter by ownerid when user(s) selected
 */
function buildOwnerFilter(userIds: string[] | undefined): string {
  if (!userIds || userIds.length === 0) {
    return "";
  }

  if (userIds.length === 1) {
    // Single user - simple equality filter
    return `_ownerid_value eq ${userIds[0]}`;
  }

  // Multiple users - use 'in' filter
  // Note: Dataverse uses Microsoft.Dynamics.CRM.In for multi-value lookups
  const values = userIds.map((id) => `'${id}'`).join(",");
  return `Microsoft.Dynamics.CRM.In(PropertyName='ownerid',PropertyValues=[${values}])`;
}

/**
 * Build OData filter for event type filtering
 * Task 064: Filter by sprk_eventtype lookup when type(s) selected
 */
function buildEventTypeFilter(typeIds: string[] | undefined): string {
  if (!typeIds || typeIds.length === 0) {
    return "";
  }

  if (typeIds.length === 1) {
    // Single type - simple equality filter
    return `_sprk_eventtype_value eq ${typeIds[0]}`;
  }

  // Multiple types - use 'in' filter
  // Note: Dataverse uses Microsoft.Dynamics.CRM.In for multi-value lookups
  const values = typeIds.map((id) => `'${id}'`).join(",");
  return `Microsoft.Dynamics.CRM.In(PropertyName='sprk_eventtype',PropertyValues=[${values}])`;
}

/**
 * Build OData filter for status filtering
 * Task 065: Filter by statuscode when status(es) selected
 *
 * Status values:
 * - 1: Draft
 * - 2: Planned
 * - 3: Open
 * - 4: On Hold
 * - 5: Completed
 * - 6: Cancelled
 */
function buildStatusFilter(statusCodes: number[] | undefined): string {
  if (!statusCodes || statusCodes.length === 0) {
    return "";
  }

  if (statusCodes.length === 1) {
    // Single status - simple equality filter
    return `statuscode eq ${statusCodes[0]}`;
  }

  // Multiple statuses - use 'in' filter with numeric values
  // Dataverse uses Microsoft.Dynamics.CRM.In for multi-value comparisons
  const values = statusCodes.join(",");
  return `Microsoft.Dynamics.CRM.In(PropertyName='statuscode',PropertyValues=[${values}])`;
}

/**
 * Get status badge appearance
 */
function getStatusAppearance(
  statusCode: number
): "filled" | "outline" | "tint" | "ghost" {
  switch (statusCode) {
    case 5: // Completed
      return "filled";
    case 6: // Cancelled
      return "ghost";
    default:
      return "tint";
  }
}

/**
 * Get status badge color
 */
function getStatusColor(
  statusCode: number
): "brand" | "success" | "warning" | "danger" | "informative" | "subtle" {
  switch (statusCode) {
    case 1: // Draft
      return "subtle";
    case 2: // Planned
      return "informative";
    case 3: // Open
      return "brand";
    case 4: // On Hold
      return "warning";
    case 5: // Completed
      return "success";
    case 6: // Cancelled
      return "danger";
    default:
      return "subtle";
  }
}

/**
 * Navigate to a parent record (Matter, Project, etc.)
 * Uses Xrm.Navigation.openForm to open the record form
 */
function navigateToRegardingRecord(
  entityLogicalName: string,
  recordId: string
): void {
  console.log(
    "[GridSection] Navigating to parent record:",
    entityLogicalName,
    recordId
  );

  if (typeof Xrm === "undefined" || !Xrm.Navigation) {
    console.warn(
      "[GridSection] Xrm.Navigation not available. Cannot navigate."
    );
    return;
  }

  try {
    Xrm.Navigation.openForm({
      entityName: entityLogicalName,
      entityId: recordId,
    });
  } catch (error) {
    console.error("[GridSection] Failed to navigate to parent record:", error);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

const VERSION = "1.5.0";

export const GridSection: React.FC<GridSectionProps> = ({
  calendarFilter,
  assignedToFilter,
  eventTypeFilter,
  statusFilter,
  onRowClick,
  onSelectionChange,
}) => {
  const styles = useStyles();

  // State
  const [events, setEvents] = React.useState<IEventRecord[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [selectedIds, setSelectedIds] = React.useState<Set<string>>(new Set());

  /**
   * Fetch events from Dataverse WebAPI
   */
  const fetchEvents = React.useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      // Check if Xrm is available
      if (typeof Xrm === "undefined" || !Xrm.WebApi) {
        // Mock data for development/testing outside Dataverse
        console.warn(
          "[GridSection] Xrm.WebApi not available. Using mock data."
        );
        setEvents(getMockEvents(calendarFilter, assignedToFilter, eventTypeFilter, statusFilter));
        setLoading(false);
        return;
      }

      // Build query
      const select = [
        "sprk_eventid",
        "sprk_eventname",
        "sprk_duedate",
        "statecode",
        "statuscode",
        "sprk_priority",
        "_ownerid_value",
        "_sprk_eventtype_value",
        "sprk_regardingrecordname",
        "sprk_regardingrecordtype",
        "sprk_regardingrecordid",
      ].join(",");

      // Build filters (Task 063: owner, Task 064: event type, Task 065: status)
      const filters: string[] = ["statecode eq 0"]; // Active events only

      const dateFilter = buildDateFilter(calendarFilter);
      if (dateFilter) {
        filters.push(dateFilter);
      }

      const ownerFilter = buildOwnerFilter(assignedToFilter);
      if (ownerFilter) {
        filters.push(ownerFilter);
      }

      const eventTypeFilterStr = buildEventTypeFilter(eventTypeFilter);
      if (eventTypeFilterStr) {
        filters.push(eventTypeFilterStr);
      }

      // Task 065: Apply status filter
      const statusFilterStr = buildStatusFilter(statusFilter);
      if (statusFilterStr) {
        filters.push(statusFilterStr);
      }

      const filter = filters.join(" and ");
      const orderBy = "sprk_duedate asc";

      // Execute query
      const result = await Xrm.WebApi.retrieveMultipleRecords(
        "sprk_event",
        `?$select=${select}&$filter=${filter}&$orderby=${orderBy}&$top=100`
      );

      setEvents(result.entities || []);
    } catch (err) {
      console.error("[GridSection] Error fetching events:", err);
      setError(err instanceof Error ? err.message : "Failed to load events");
    } finally {
      setLoading(false);
    }
  }, [calendarFilter, assignedToFilter, eventTypeFilter, statusFilter]);

  // Fetch events on mount and when filter changes
  React.useEffect(() => {
    fetchEvents();
  }, [fetchEvents]);

  /**
   * Handle row click (on event name)
   */
  const handleEventNameClick = React.useCallback(
    (event: React.MouseEvent, record: IEventRecord) => {
      event.preventDefault();
      event.stopPropagation();
      onRowClick?.(record.sprk_eventid, record._sprk_eventtype_value);
    },
    [onRowClick]
  );

  /**
   * Handle row click (on entire row, excluding checkbox)
   */
  const handleRowClick = React.useCallback(
    (record: IEventRecord) => {
      onRowClick?.(record.sprk_eventid, record._sprk_eventtype_value);
    },
    [onRowClick]
  );

  /**
   * Handle checkbox change
   */
  const handleCheckboxChange = React.useCallback(
    (eventId: string, checked: boolean) => {
      setSelectedIds((prev) => {
        const next = new Set(prev);
        if (checked) {
          next.add(eventId);
        } else {
          next.delete(eventId);
        }
        onSelectionChange?.(Array.from(next));
        return next;
      });
    },
    [onSelectionChange]
  );

  /**
   * Handle select all
   */
  const handleSelectAll = React.useCallback(
    (checked: boolean) => {
      if (checked) {
        const allIds = new Set(events.map((e) => e.sprk_eventid));
        setSelectedIds(allIds);
        onSelectionChange?.(Array.from(allIds));
      } else {
        setSelectedIds(new Set());
        onSelectionChange?.([]);
      }
    },
    [events, onSelectionChange]
  );

  /**
   * Handle click on "Regarding" link to navigate to parent record
   */
  const handleRegardingClick = React.useCallback(
    (event: React.MouseEvent, record: IEventRecord) => {
      event.preventDefault();
      event.stopPropagation();

      if (record.sprk_regardingrecordtype && record.sprk_regardingrecordid) {
        navigateToRegardingRecord(
          record.sprk_regardingrecordtype,
          record.sprk_regardingrecordid
        );
      }
    },
    []
  );

  // All selected?
  const allSelected =
    events.length > 0 && selectedIds.size === events.length;
  const someSelected = selectedIds.size > 0 && selectedIds.size < events.length;

  // Render loading state
  if (loading) {
    return (
      <div className={styles.container}>
        <div className={styles.loadingContainer}>
          <Spinner size="medium" label="Loading events..." />
        </div>
      </div>
    );
  }

  // Render error state
  if (error) {
    return (
      <div className={styles.container}>
        <div className={styles.errorContainer}>
          <Text weight="semibold">Error loading events</Text>
          <Text size={200}>{error}</Text>
        </div>
      </div>
    );
  }

  // Render empty state
  if (events.length === 0) {
    return (
      <div className={styles.container}>
        <div className={styles.emptyContainer}>
          <Text weight="semibold">No events found</Text>
          <Text size={200}>
            {calendarFilter && calendarFilter.type !== "clear"
              ? "Try selecting a different date or clearing the filter."
              : "No active events are available."}
          </Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.gridContainer}>
        <table className={styles.table}>
          <thead className={styles.tableHeader}>
            <tr>
              <th className={`${styles.th} ${styles.thCheckbox}`}>
                <Checkbox
                  checked={allSelected ? true : someSelected ? "mixed" : false}
                  onChange={(_, data) =>
                    handleSelectAll(data.checked === true)
                  }
                  aria-label="Select all events"
                />
              </th>
              <th className={styles.th}>Event Name</th>
              <th className={styles.th}>Regarding</th>
              <th className={styles.th}>Due Date</th>
              <th className={styles.th}>Status</th>
              <th className={styles.th}>Priority</th>
              <th className={styles.th}>Owner</th>
              <th className={styles.th}>Event Type</th>
            </tr>
          </thead>
          <tbody>
            {events.map((record) => {
              const isSelected = selectedIds.has(record.sprk_eventid);
              const priorityClass =
                record.sprk_priority === 1
                  ? styles.priorityHigh
                  : record.sprk_priority === 3
                  ? styles.priorityLow
                  : styles.priorityNormal;

              return (
                <tr
                  key={record.sprk_eventid}
                  className={`${styles.tr} ${isSelected ? styles.trSelected : ""}`}
                  onClick={() => handleRowClick(record)}
                >
                  <td
                    className={`${styles.td} ${styles.tdCheckbox}`}
                    onClick={(e) => e.stopPropagation()}
                  >
                    <Checkbox
                      checked={isSelected}
                      onChange={(_, data) =>
                        handleCheckboxChange(
                          record.sprk_eventid,
                          data.checked === true
                        )
                      }
                      aria-label={`Select ${record.sprk_eventname}`}
                    />
                  </td>
                  <td className={styles.td}>
                    <Link
                      as="a"
                      href="#"
                      className={styles.eventNameLink}
                      onClick={(e) => handleEventNameClick(e, record)}
                      role="button"
                    >
                      {record.sprk_eventname || "—"}
                    </Link>
                  </td>
                  <td className={styles.td}>
                    {record.sprk_regardingrecordname &&
                    record.sprk_regardingrecordtype &&
                    record.sprk_regardingrecordid ? (
                      <Link
                        as="a"
                        href="#"
                        className={styles.eventNameLink}
                        onClick={(e) => handleRegardingClick(e, record)}
                        role="button"
                        aria-label={`Open ${record.sprk_regardingrecordname}`}
                      >
                        {record.sprk_regardingrecordname}
                      </Link>
                    ) : (
                      "—"
                    )}
                  </td>
                  <td className={styles.td}>
                    {formatDate(record.sprk_duedate)}
                  </td>
                  <td className={styles.td}>
                    <Badge
                      appearance={getStatusAppearance(record.statuscode)}
                      color={getStatusColor(record.statuscode)}
                      className={styles.statusBadge}
                    >
                      {record[
                        "statuscode@OData.Community.Display.V1.FormattedValue"
                      ] || "Unknown"}
                    </Badge>
                  </td>
                  <td className={`${styles.td} ${priorityClass}`}>
                    {record[
                      "sprk_priority@OData.Community.Display.V1.FormattedValue"
                    ] || "—"}
                  </td>
                  <td className={styles.td}>
                    {record[
                      "_ownerid_value@OData.Community.Display.V1.FormattedValue"
                    ] || "—"}
                  </td>
                  <td className={styles.td}>
                    {record[
                      "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue"
                    ] || "—"}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <div className={styles.footer}>
        <Text>
          {events.length} event{events.length !== 1 ? "s" : ""} •{" "}
          {selectedIds.size} selected
        </Text>
        <Text>v{VERSION}</Text>
      </div>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Mock Data (for development outside Dataverse)
// ─────────────────────────────────────────────────────────────────────────────

function getMockEvents(
  filter: CalendarFilterOutput | null,
  assignedToFilter?: string[],
  eventTypeFilter?: string[],
  statusFilter?: number[]
): IEventRecord[] {
  const today = new Date();
  const mockEvents: IEventRecord[] = [
    {
      sprk_eventid: "mock-1",
      sprk_eventname: "Contract Review Deadline",
      sprk_duedate: new Date(today.getTime() + 86400000 * 2).toISOString(),
      statecode: 0,
      statuscode: 3,
      sprk_priority: 1,
      _ownerid_value: "user-current", // Current user (mock)
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Current User",
      "_sprk_eventtype_value": "type-1",
      "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue":
        "Filing Deadline",
      "statuscode@OData.Community.Display.V1.FormattedValue": "Open",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "High",
      // Regarding: Matter record
      sprk_regardingrecordname: "Smith vs. Johnson Corp",
      sprk_regardingrecordtype: "sprk_matter",
      sprk_regardingrecordid: "matter-001",
    },
    {
      sprk_eventid: "mock-2",
      sprk_eventname: "Client Meeting",
      sprk_duedate: new Date(today.getTime() + 86400000 * 5).toISOString(),
      statecode: 0,
      statuscode: 2,
      sprk_priority: 2,
      _ownerid_value: "user-2",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Jane Doe",
      "_sprk_eventtype_value": "type-2",
      "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue":
        "Meeting",
      "statuscode@OData.Community.Display.V1.FormattedValue": "Planned",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Normal",
      // Regarding: Project record
      sprk_regardingrecordname: "Website Redesign Project",
      sprk_regardingrecordtype: "sprk_project",
      sprk_regardingrecordid: "project-001",
    },
    {
      sprk_eventid: "mock-3",
      sprk_eventname: "Document Submission",
      sprk_duedate: new Date(today.getTime() + 86400000 * 7).toISOString(),
      statecode: 0,
      statuscode: 1,
      sprk_priority: 3,
      _ownerid_value: "user-1",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "John Smith",
      "_sprk_eventtype_value": "type-3",
      "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue": "Task",
      "statuscode@OData.Community.Display.V1.FormattedValue": "Draft",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Low",
      // No regarding record for this event (standalone event)
    },
    {
      sprk_eventid: "mock-4",
      sprk_eventname: "Project Kickoff",
      sprk_duedate: new Date(today.getTime() + 86400000 * 10).toISOString(),
      statecode: 0,
      statuscode: 2,
      sprk_priority: 2,
      _ownerid_value: "user-current", // Current user (mock)
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Current User",
      "_sprk_eventtype_value": "type-2",
      "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue":
        "Meeting",
      "statuscode@OData.Community.Display.V1.FormattedValue": "Planned",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Normal",
      sprk_regardingrecordname: "Alpha Project",
      sprk_regardingrecordtype: "sprk_project",
      sprk_regardingrecordid: "project-002",
    },
    // Additional mock events for status filter testing (Task 065)
    {
      sprk_eventid: "mock-5",
      sprk_eventname: "Completed Task Example",
      sprk_duedate: new Date(today.getTime() - 86400000 * 3).toISOString(), // 3 days ago
      statecode: 0,
      statuscode: 5, // Completed
      sprk_priority: 2,
      _ownerid_value: "user-1",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "John Smith",
      "_sprk_eventtype_value": "type-3",
      "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue": "Task",
      "statuscode@OData.Community.Display.V1.FormattedValue": "Completed",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Normal",
      sprk_regardingrecordname: "Beta Project",
      sprk_regardingrecordtype: "sprk_project",
      sprk_regardingrecordid: "project-003",
    },
    {
      sprk_eventid: "mock-6",
      sprk_eventname: "Cancelled Meeting",
      sprk_duedate: new Date(today.getTime() + 86400000 * 1).toISOString(), // Tomorrow
      statecode: 0,
      statuscode: 6, // Cancelled
      sprk_priority: 1,
      _ownerid_value: "user-2",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Jane Doe",
      "_sprk_eventtype_value": "type-2",
      "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue": "Meeting",
      "statuscode@OData.Community.Display.V1.FormattedValue": "Cancelled",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "High",
    },
    {
      sprk_eventid: "mock-7",
      sprk_eventname: "On Hold Review",
      sprk_duedate: new Date(today.getTime() + 86400000 * 14).toISOString(), // 2 weeks out
      statecode: 0,
      statuscode: 4, // On Hold
      sprk_priority: 2,
      _ownerid_value: "user-current",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Current User",
      "_sprk_eventtype_value": "type-1",
      "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue": "Filing Deadline",
      "statuscode@OData.Community.Display.V1.FormattedValue": "On Hold",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Normal",
      sprk_regardingrecordname: "Gamma Matter",
      sprk_regardingrecordtype: "sprk_matter",
      sprk_regardingrecordid: "matter-002",
    },
  ];

  let filteredEvents = mockEvents;

  // Apply date filter to mock data
  if (filter && filter.type !== "clear") {
    filteredEvents = filteredEvents.filter((event) => {
      if (!event.sprk_duedate) return false;
      const dueDate = event.sprk_duedate.split("T")[0];

      if (filter.type === "single") {
        return dueDate === (filter as CalendarFilterSingle).date;
      }

      if (filter.type === "range") {
        const range = filter as CalendarFilterRange;
        return dueDate >= range.start && dueDate <= range.end;
      }

      return true;
    });
  }

  // Apply assigned-to filter to mock data (Task 063)
  if (assignedToFilter && assignedToFilter.length > 0) {
    filteredEvents = filteredEvents.filter((event) =>
      assignedToFilter.includes(event._ownerid_value || "")
    );
  }

  // Apply event type filter to mock data (Task 064)
  if (eventTypeFilter && eventTypeFilter.length > 0) {
    filteredEvents = filteredEvents.filter((event) =>
      eventTypeFilter.includes(event._sprk_eventtype_value || "")
    );
  }

  // Apply status filter to mock data (Task 065)
  if (statusFilter && statusFilter.length > 0) {
    filteredEvents = filteredEvents.filter((event) =>
      statusFilter.includes(event.statuscode)
    );
  }

  return filteredEvents;
}

export default GridSection;
