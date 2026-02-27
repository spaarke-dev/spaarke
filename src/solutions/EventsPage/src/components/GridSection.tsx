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
 * - Column-level filtering with filter icons in headers (Task 094)
 * - Matches Power Apps grid styling (per spec)
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/061-events-page-integrate-calendar.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/094-add-column-filters-to-grid.poml
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
import { ColumnHeaderMenu, ColumnFilterOption, SortDirection } from "./ColumnHeaderMenu";
import {
  executeFetchXml,
  getViewById,
  mergeDateFilterIntoFetchXml,
  ensureRequiredAttributes,
  parseLayoutXml,
  type ViewDefinition,
  type LayoutColumn,
} from "../services";

// ─────────────────────────────────────────────────────────────────────────────
// Xrm Type Declaration and Access
// ─────────────────────────────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;

/**
 * Get the Xrm object from the appropriate context.
 * Custom Pages run in an iframe, so Xrm may be on window.parent.
 * PCF controls have Xrm directly on window.
 */
function getXrm(): any | undefined {
  // Try window.Xrm first (PCF controls or direct access)
  if (typeof Xrm !== "undefined" && Xrm?.WebApi) {
    return Xrm;
  }
  // Try parent.Xrm for Custom Pages running in iframe
  try {
    if (typeof window !== "undefined" && window.parent && (window.parent as any).Xrm?.WebApi) {
      return (window.parent as any).Xrm;
    }
  } catch (e) {
    // Cross-origin access denied - expected in some environments
    console.debug("[GridSection] Cannot access parent.Xrm:", e);
  }
  return undefined;
}
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
  /** Event Status (custom field - primary status indicator) */
  sprk_eventstatus: number;
  /** @deprecated Kept for backward compatibility */
  statecode?: number;
  sprk_priority: number | null;
  _ownerid_value: string | null;
  "_ownerid_value@OData.Community.Display.V1.FormattedValue"?: string;
  "_sprk_eventtype_ref_value"?: string;
  "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue"?: string;
  "sprk_eventstatus@OData.Community.Display.V1.FormattedValue"?: string;
  "sprk_priority@OData.Community.Display.V1.FormattedValue"?: string;
  /** Parent record name (text field) */
  sprk_regardingrecordname?: string;
  /** Parent record type (lookup to Record Type) */
  "_sprk_regardingrecordtype_value"?: string;
  "_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"?: string;
  /** Parent record GUID (text field) */
  sprk_regardingrecordid?: string;
  /** Parent record entity logical name (text field) */
  sprk_regardingrecordtypelogicalname?: string;
}

/**
 * Context filter for drill-through scenarios.
 * Filters the grid to records matching a specific field value (e.g., regarding record).
 */
export interface ContextFilter {
  /** Dataverse field name to filter on (e.g., "sprk_regardingrecordid") */
  fieldName: string;
  /** Value to filter by (e.g., matter record GUID) */
  value: string;
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
  /** Status filter - array of sprk_eventstatus values (Task 065) */
  statusFilter?: number[];
  /** Selected view ID for FetchXML query (Task 093) */
  viewId?: string;
  /** Context filter for drill-through — limits records to a specific parent (v2.16.0) */
  contextFilter?: ContextFilter;
  /** Callback when a row is clicked (to open side pane) */
  onRowClick?: (eventId: string, eventTypeId?: string) => void;
  /** Callback when row selection changes */
  onSelectionChange?: (selectedIds: string[]) => void;
  /** Callback when data is loaded (returns record count) (Task 089) */
  onDataLoaded?: (recordCount: number) => void;
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
    // OOB-style grid font: 12px Segoe UI
    fontSize: "12px",
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    color: tokens.colorNeutralForeground1,
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
    fontWeight: tokens.fontWeightRegular, // OOB uses regular weight for cell text
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
 * Supports filtering by multiple date fields (OR logic)
 *
 * Note: Dataverse OData date filtering requires specific formats:
 * - Microsoft.Dynamics.CRM.On uses PropertyValue as ISO date (YYYY-MM-DD)
 * - For datetime fields, we need to compare the date portion using ge/lt operators
 */
function buildDateFilter(filter: CalendarFilterOutput | null): string {
  if (!filter || filter.type === "clear") {
    return "";
  }

  // Get date fields to filter by (default to sprk_duedate if not specified)
  const dateFields = ("dateFields" in filter && filter.dateFields?.length)
    ? filter.dateFields
    : ["sprk_duedate"];

  console.log("[GridSection] Building date filter:", filter, "fields:", dateFields);

  if (filter.type === "single") {
    const singleFilter = filter as CalendarFilterSingle;
    // For single date, filter events on that specific date
    // Use ge/lt to match any time on that date (handles datetime fields)
    const dateStart = `${singleFilter.date}T00:00:00Z`;
    const dateEnd = `${singleFilter.date}T23:59:59Z`;

    if (dateFields.length === 1) {
      const field = dateFields[0].toLowerCase();
      return `(${field} ge ${dateStart} and ${field} le ${dateEnd})`;
    }
    // Multiple fields - OR logic
    const conditions = dateFields.map((field) => {
      const f = field.toLowerCase();
      return `(${f} ge ${dateStart} and ${f} le ${dateEnd})`;
    });
    return `(${conditions.join(" or ")})`;
  }

  if (filter.type === "range") {
    const rangeFilter = filter as CalendarFilterRange;
    // For range, filter events from start date 00:00 to end date 23:59
    const rangeStart = `${rangeFilter.start}T00:00:00Z`;
    const rangeEnd = `${rangeFilter.end}T23:59:59Z`;

    if (dateFields.length === 1) {
      const field = dateFields[0].toLowerCase();
      return `(${field} ge ${rangeStart} and ${field} le ${rangeEnd})`;
    }
    // Multiple fields - OR logic
    const conditions = dateFields.map((field) => {
      const f = field.toLowerCase();
      return `(${f} ge ${rangeStart} and ${f} le ${rangeEnd})`;
    });
    return `(${conditions.join(" or ")})`;
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
    return `_sprk_eventtype_ref_value eq ${typeIds[0]}`;
  }

  // Multiple types - use 'in' filter
  // Note: Dataverse uses Microsoft.Dynamics.CRM.In for multi-value lookups
  const values = typeIds.map((id) => `'${id}'`).join(",");
  return `Microsoft.Dynamics.CRM.In(PropertyName='sprk_eventtype',PropertyValues=[${values}])`;
}

/**
 * Build OData filter for status filtering
 * Task 065: Filter by sprk_eventstatus when status(es) selected
 *
 * Event Status values (sprk_eventstatus):
 * - 0: Draft
 * - 1: Open
 * - 2: Completed
 * - 3: Closed
 * - 4: On Hold
 * - 5: Cancelled
 * - 6: Reassigned
 * - 7: Archived
 */
function buildStatusFilter(statusCodes: number[] | undefined): string {
  if (!statusCodes || statusCodes.length === 0) {
    return "";
  }

  if (statusCodes.length === 1) {
    // Single status - simple equality filter (string field)
    return `sprk_eventstatus eq '${statusCodes[0]}'`;
  }

  // Multiple statuses - use 'in' filter with string values
  // Dataverse uses Microsoft.Dynamics.CRM.In for multi-value comparisons
  const values = statusCodes.map((code) => `'${code}'`).join(",");
  return `Microsoft.Dynamics.CRM.In(PropertyName='sprk_eventstatus',PropertyValues=[${values}])`;
}

/**
 * Get status badge appearance based on sprk_eventstatus value
 */
function getStatusAppearance(
  eventStatus: number
): "filled" | "outline" | "tint" | "ghost" {
  switch (eventStatus) {
    case 2: // Completed
      return "filled";
    case 3: // Closed
    case 5: // Cancelled
    case 7: // Archived
      return "ghost";
    default:
      return "tint";
  }
}

/**
 * Get status badge color based on sprk_eventstatus value
 */
function getStatusColor(
  eventStatus: number
): "brand" | "success" | "warning" | "danger" | "informative" | "subtle" {
  switch (eventStatus) {
    case 0: // Draft
      return "subtle";
    case 1: // Open
      return "brand";
    case 2: // Completed
      return "success";
    case 3: // Closed
      return "informative";
    case 4: // On Hold
      return "warning";
    case 5: // Cancelled
      return "danger";
    case 6: // Reassigned
      return "informative";
    case 7: // Archived
      return "subtle";
    default:
      return "subtle";
  }
}

/**
 * Get the OData field accessor for a column (inline to avoid bundler issues).
 */
function getFieldAccessor(column: LayoutColumn): string {
  const fieldName = column.name.toLowerCase();

  // Special cases for lookups that need _xxx_value format
  if (column.isLookup) {
    if (fieldName.startsWith("_") && fieldName.endsWith("_value")) {
      return fieldName;
    }
    return `_${fieldName}_value`;
  }

  // Handle special field mappings
  const fieldMappings: Record<string, string> = {
    "sprk_eventtype": "_sprk_eventtype_ref_value",
    "sprk_eventtype_ref": "_sprk_eventtype_ref_value",
    "sprk_regardingrecordtype": "_sprk_regardingrecordtype_value",
  };

  if (fieldMappings[fieldName]) {
    return fieldMappings[fieldName];
  }

  return column.name;
}

/**
 * Get the value to display for a dynamic column.
 * Handles lookups, option sets, dates, and regular fields.
 */
function getColumnDisplayValue(
  record: IEventRecord,
  column: LayoutColumn
): string {
  const fieldName = column.name.toLowerCase();

  // Special handling for known fields with formatted values
  if (column.formattedValueField) {
    const formattedValue = (record as Record<string, unknown>)[column.formattedValueField];
    if (formattedValue) return String(formattedValue);
  }

  // Get the raw value using field accessor
  const accessor = getFieldAccessor(column);
  const rawValue = (record as Record<string, unknown>)[accessor];

  // Handle null/undefined
  if (rawValue === null || rawValue === undefined) {
    return "—";
  }

  // Special handling for dates
  if (fieldName.includes("date") || fieldName === "createdon" || fieldName === "modifiedon") {
    return formatDate(rawValue as string);
  }

  // Special handling for status with formatted value
  if (fieldName === "sprk_eventstatus") {
    const formatted = record["sprk_eventstatus@OData.Community.Display.V1.FormattedValue"];
    if (formatted) return formatted;
  }

  // Special handling for priority with formatted value
  if (fieldName === "sprk_priority") {
    const formatted = record["sprk_priority@OData.Community.Display.V1.FormattedValue"];
    if (formatted) return formatted;
  }

  // For lookups, try to get the formatted value
  if (column.isLookup) {
    const lookupFormatted = (record as Record<string, unknown>)[
      `${accessor}@OData.Community.Display.V1.FormattedValue`
    ];
    if (lookupFormatted) return String(lookupFormatted);
  }

  // Default: return string value
  return String(rawValue);
}

/**
 * Check if a column is the Event Name column (for link rendering).
 */
function isEventNameColumn(column: LayoutColumn): boolean {
  return column.name.toLowerCase() === "sprk_eventname" ||
         column.name.toLowerCase() === "sprk_name";
}

/**
 * Check if a column is the Regarding column (for link rendering).
 */
function isRegardingColumn(column: LayoutColumn): boolean {
  return column.name.toLowerCase() === "sprk_regardingrecordname" ||
         column.name.toLowerCase().includes("regarding");
}

/**
 * Check if a column is a Status column (for badge rendering).
 */
function isStatusColumn(column: LayoutColumn): boolean {
  return column.name.toLowerCase() === "sprk_eventstatus" ||
         column.name.toLowerCase() === "statecode" ||
         column.name.toLowerCase() === "statuscode";
}

/**
 * Check if a column is a Priority column (for styled rendering).
 */
function isPriorityColumn(column: LayoutColumn): boolean {
  return column.name.toLowerCase() === "sprk_priority";
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

  const xrm = getXrm();
  if (!xrm || !xrm.Navigation) {
    console.warn(
      "[GridSection] Xrm.Navigation not available. Cannot navigate."
    );
    return;
  }

  try {
    xrm.Navigation.openForm({
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

const VERSION = "2.12.0"; // Dynamic columns from layoutXml

// ─────────────────────────────────────────────────────────────────────────────
// Column Filter Options (Task 094)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Status filter options for the status column filter.
 * These match the sprk_eventstatus option set values.
 */
const STATUS_FILTER_OPTIONS: ColumnFilterOption[] = [
  { value: 0, label: "Draft" },
  { value: 1, label: "Open" },
  { value: 2, label: "Completed" },
  { value: 3, label: "Closed" },
  { value: 4, label: "On Hold" },
  { value: 5, label: "Cancelled" },
  { value: 6, label: "Reassigned" },
  { value: 7, label: "Archived" },
];

/**
 * Priority filter options for the priority column filter.
 */
const PRIORITY_FILTER_OPTIONS: ColumnFilterOption[] = [
  { value: 1, label: "High" },
  { value: 2, label: "Normal" },
  { value: 3, label: "Low" },
];

/**
 * Default columns when no layoutXml is available.
 * Provides a fallback for backward compatibility.
 */
const DEFAULT_COLUMNS: LayoutColumn[] = [
  { name: "sprk_eventname", width: 200, label: "Event Name", isLookup: false },
  { name: "sprk_regardingrecordname", width: 180, label: "Regarding", isLookup: false },
  { name: "sprk_duedate", width: 120, label: "Due Date", isLookup: false },
  { name: "sprk_eventstatus", width: 100, label: "Status", isLookup: false },
  { name: "sprk_priority", width: 100, label: "Priority", isLookup: false },
  { name: "ownerid", width: 150, label: "Owner", isLookup: true, formattedValueField: "_ownerid_value@OData.Community.Display.V1.FormattedValue" },
  { name: "sprk_eventtype_ref", width: 120, label: "Event Type", isLookup: true, formattedValueField: "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue" },
];

export const GridSection: React.FC<GridSectionProps> = ({
  calendarFilter,
  assignedToFilter,
  eventTypeFilter,
  statusFilter,
  viewId,
  contextFilter,
  onRowClick,
  onSelectionChange,
  onDataLoaded,
}) => {
  const styles = useStyles();

  // State
  const [events, setEvents] = React.useState<IEventRecord[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [selectedIds, setSelectedIds] = React.useState<Set<string>>(new Set());

  // View definition from savedquery (for FetchXML queries)
  const [viewDefinition, setViewDefinition] = React.useState<ViewDefinition | null>(null);

  // Dynamic columns parsed from layoutXml
  const [dynamicColumns, setDynamicColumns] = React.useState<LayoutColumn[]>([]);

  // Load view definition when viewId changes
  React.useEffect(() => {
    if (viewId) {
      console.log("[GridSection] Loading view definition for:", viewId);
      getViewById(viewId).then((view) => {
        if (view) {
          console.log("[GridSection] View loaded:", view.name);
          setViewDefinition(view);

          // Parse layoutXml to get dynamic columns
          if (view.layoutXml) {
            const parsedColumns = parseLayoutXml(view.layoutXml);
            if (parsedColumns.length > 0) {
              console.log("[GridSection] Dynamic columns:", parsedColumns.map(c => c.name));
              setDynamicColumns(parsedColumns);
            } else {
              console.warn("[GridSection] No columns parsed from layoutXml, using defaults");
              setDynamicColumns([]);
            }
          }
        } else {
          console.warn("[GridSection] View not found, using fallback query");
          setViewDefinition(null);
          setDynamicColumns([]);
        }
      });
    }
  }, [viewId]);

  // Column filter state (Task 094)
  const [columnFilters, setColumnFilters] = React.useState<{
    eventName: string | null;
    regarding: string | null;
    status: number[];
    priority: number[];
    owner: string | null;
    eventType: string | null;
    dueDate: string | null; // Column-level date filter
  }>({
    eventName: null,
    regarding: null,
    status: [],
    priority: [],
    owner: null,
    eventType: null,
    dueDate: null,
  });

  // Sorting state (Task 097)
  const [sortConfig, setSortConfig] = React.useState<{
    column: string | null;
    direction: SortDirection;
  }>({
    column: null,
    direction: null,
  });

  /**
   * Fetch events from Dataverse using FetchXML from saved view.
   *
   * Uses the view's FetchXML with date filter merged in.
   * Falls back to OData query if view FetchXML is not available.
   */
  const fetchEvents = React.useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      // Get Xrm from appropriate context (window or parent for iframe)
      const xrm = getXrm();
      if (!xrm) {
        // Mock data for development/testing outside Dataverse
        console.warn(
          "[GridSection] Xrm.WebApi not available (checked window.Xrm and parent.Xrm). Using mock data."
        );
        const mockData = getMockEvents(calendarFilter, assignedToFilter, eventTypeFilter, statusFilter);
        setEvents(mockData);
        onDataLoaded?.(mockData.length);
        setLoading(false);
        return;
      }

      // Log which context provided Xrm
      const xrmSource = (typeof Xrm !== "undefined" && xrm === Xrm) ? "window" : "parent";
      console.log("[GridSection] Using Xrm.WebApi from:", xrmSource);

      // ─────────────────────────────────────────────────────────────────────────
      // FetchXML Query (from saved view)
      // ─────────────────────────────────────────────────────────────────────────

      if (viewDefinition?.fetchXml) {
        console.log("[GridSection] Using FetchXML from view:", viewDefinition.name);

        // Ensure all required grid columns are in the FetchXML
        // (the saved view may not include all columns we need)
        let fetchXml = ensureRequiredAttributes(viewDefinition.fetchXml);

        // Merge date filter into the view's FetchXML
        const dateFilterInput = calendarFilter ? {
          type: calendarFilter.type,
          date: calendarFilter.type === "single" ? (calendarFilter as CalendarFilterSingle).date : undefined,
          start: calendarFilter.type === "range" ? (calendarFilter as CalendarFilterRange).start : undefined,
          end: calendarFilter.type === "range" ? (calendarFilter as CalendarFilterRange).end : undefined,
          dateFields: "dateFields" in calendarFilter ? calendarFilter.dateFields : undefined,
        } : null;

        fetchXml = mergeDateFilterIntoFetchXml(fetchXml, dateFilterInput);

        // v3.0.1: Merge context filter as a separate <filter> at entity level.
        // Always adds a NEW filter before </entity> instead of injecting into
        // existing filters — avoids misplacement when date/link-entity filters
        // create nested </filter> tags (replace only matches the first occurrence).
        if (contextFilter) {
          console.log("[GridSection] Applying context filter:", contextFilter);
          const contextFilterXml = `<filter type="and"><condition attribute="${contextFilter.fieldName}" operator="eq" value="${contextFilter.value}" /></filter>`;
          fetchXml = fetchXml.replace("</entity>", `${contextFilterXml}</entity>`);
        }

        console.log("[GridSection] Final FetchXML:", fetchXml);

        // Execute FetchXML query
        const result = await executeFetchXml<IEventRecord>("sprk_event", fetchXml);

        const loadedEvents = result.entities || [];
        setEvents(loadedEvents);
        onDataLoaded?.(loadedEvents.length);
        console.log(`[GridSection] FetchXML returned ${loadedEvents.length} records`);
        return;
      }

      // ─────────────────────────────────────────────────────────────────────────
      // Fallback: OData Query (when view FetchXML not available)
      // ─────────────────────────────────────────────────────────────────────────

      console.log("[GridSection] No view FetchXML, using OData fallback");

      // Build query
      const select = [
        "sprk_eventid",
        "sprk_eventname",
        "sprk_duedate",
        "sprk_eventstatus",
        "statecode",
        "sprk_priority",
        "_ownerid_value",
        "_sprk_eventtype_ref_value",
        "_sprk_regardingrecordtype_value",
        "sprk_regardingrecordid",
        "sprk_regardingrecordname",
        "sprk_regardingrecordtypelogicalname",
      ].join(",");

      // Build filters
      const filters: string[] = ["statecode eq 0"];

      // v2.16.0: Context filter for drill-through
      if (contextFilter) {
        filters.push(`${contextFilter.fieldName} eq '${contextFilter.value}'`);
      }

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

      const statusFilterStr = buildStatusFilter(statusFilter);
      if (statusFilterStr) {
        filters.push(statusFilterStr);
      }

      const filter = filters.join(" and ");
      const orderBy = "sprk_duedate asc";

      // Execute OData query
      const result = await xrm.WebApi.retrieveMultipleRecords(
        "sprk_event",
        `?$select=${select}&$filter=${filter}&$orderby=${orderBy}&$top=100`
      );

      const loadedEvents = result.entities || [];
      setEvents(loadedEvents);
      onDataLoaded?.(loadedEvents.length);
    } catch (err) {
      console.error("[GridSection] Error fetching events:", err);
      setError(err instanceof Error ? err.message : "Failed to load events");
      onDataLoaded?.(0);
    } finally {
      setLoading(false);
    }
  }, [calendarFilter, assignedToFilter, eventTypeFilter, statusFilter, viewDefinition, contextFilter, onDataLoaded]);

  // Fetch events on mount and when filter changes
  React.useEffect(() => {
    fetchEvents();
  }, [fetchEvents]);

  // ─────────────────────────────────────────────────────────────────────────
  // Client-side Column Filtering (Task 094)
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Apply column filters and sorting client-side after data is fetched.
   * This allows for responsive filtering/sorting without re-querying the server.
   */
  const filteredEvents = React.useMemo(() => {
    let result = events;

    // Event Name filter (contains, case-insensitive)
    if (columnFilters.eventName) {
      const search = columnFilters.eventName.toLowerCase();
      result = result.filter((e) =>
        e.sprk_eventname?.toLowerCase().includes(search)
      );
    }

    // Regarding filter (contains, case-insensitive)
    if (columnFilters.regarding) {
      const search = columnFilters.regarding.toLowerCase();
      result = result.filter((e) =>
        e.sprk_regardingrecordname?.toLowerCase().includes(search)
      );
    }

    // Status filter (multi-select)
    if (columnFilters.status.length > 0) {
      result = result.filter((e) =>
        columnFilters.status.includes(e.sprk_eventstatus)
      );
    }

    // Priority filter (multi-select)
    if (columnFilters.priority.length > 0) {
      result = result.filter((e) =>
        e.sprk_priority !== null && columnFilters.priority.includes(e.sprk_priority)
      );
    }

    // Owner filter (contains, case-insensitive)
    if (columnFilters.owner) {
      const search = columnFilters.owner.toLowerCase();
      result = result.filter((e) =>
        e["_ownerid_value@OData.Community.Display.V1.FormattedValue"]
          ?.toLowerCase()
          .includes(search)
      );
    }

    // Event Type filter (contains, case-insensitive)
    if (columnFilters.eventType) {
      const search = columnFilters.eventType.toLowerCase();
      result = result.filter((e) =>
        e["_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue"]
          ?.toLowerCase()
          .includes(search)
      );
    }

    // Due Date column filter (matches date part only)
    if (columnFilters.dueDate) {
      const filterDate = columnFilters.dueDate; // YYYY-MM-DD format
      result = result.filter((e) => {
        if (!e.sprk_duedate) return false;
        const eventDate = e.sprk_duedate.split("T")[0];
        return eventDate === filterDate;
      });
    }

    // Apply sorting (Task 097)
    if (sortConfig.column && sortConfig.direction) {
      result = [...result].sort((a, b) => {
        let aValue: string | number | null = null;
        let bValue: string | number | null = null;

        // Get sortable values based on column
        switch (sortConfig.column) {
          case "eventName":
            aValue = a.sprk_eventname?.toLowerCase() || "";
            bValue = b.sprk_eventname?.toLowerCase() || "";
            break;
          case "regarding":
            aValue = a.sprk_regardingrecordname?.toLowerCase() || "";
            bValue = b.sprk_regardingrecordname?.toLowerCase() || "";
            break;
          case "dueDate":
            aValue = a.sprk_duedate || "";
            bValue = b.sprk_duedate || "";
            break;
          case "status":
            aValue = a.sprk_eventstatus;
            bValue = b.sprk_eventstatus;
            break;
          case "priority":
            aValue = a.sprk_priority ?? 999; // Null priorities sort last
            bValue = b.sprk_priority ?? 999;
            break;
          case "owner":
            aValue =
              a["_ownerid_value@OData.Community.Display.V1.FormattedValue"]?.toLowerCase() || "";
            bValue =
              b["_ownerid_value@OData.Community.Display.V1.FormattedValue"]?.toLowerCase() || "";
            break;
          case "eventType":
            aValue =
              a["_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue"]?.toLowerCase() || "";
            bValue =
              b["_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue"]?.toLowerCase() || "";
            break;
          default:
            return 0;
        }

        // Compare values
        if (aValue === null || aValue === "") return sortConfig.direction === "asc" ? 1 : -1;
        if (bValue === null || bValue === "") return sortConfig.direction === "asc" ? -1 : 1;

        if (aValue < bValue) return sortConfig.direction === "asc" ? -1 : 1;
        if (aValue > bValue) return sortConfig.direction === "asc" ? 1 : -1;
        return 0;
      });
    }

    return result;
  }, [events, columnFilters, sortConfig]);

  /**
   * Get active columns - use dynamic columns from layoutXml if available,
   * otherwise fall back to default columns.
   */
  const activeColumns = dynamicColumns.length > 0 ? dynamicColumns : DEFAULT_COLUMNS;

  /**
   * Get filter type for a column based on its name.
   */
  const getColumnFilterType = React.useCallback(
    (column: LayoutColumn): "text" | "date" | "choice" => {
      const name = column.name.toLowerCase();
      if (name.includes("date")) return "date";
      if (name === "sprk_eventstatus" || name === "statecode") return "choice";
      if (name === "sprk_priority") return "choice";
      return "text";
    },
    []
  );

  /**
   * Get filter options for choice columns.
   */
  const getColumnFilterOptions = React.useCallback(
    (column: LayoutColumn): ColumnFilterOption[] => {
      const name = column.name.toLowerCase();
      if (name === "sprk_eventstatus" || name === "statecode") {
        return STATUS_FILTER_OPTIONS;
      }
      if (name === "sprk_priority") {
        return PRIORITY_FILTER_OPTIONS;
      }
      return [];
    },
    []
  );

  /**
   * Get current filter value for a column.
   */
  const getColumnFilterValue = React.useCallback(
    (column: LayoutColumn): string | number[] => {
      const name = column.name.toLowerCase();
      if (name === "sprk_eventname" || name === "sprk_name") {
        return columnFilters.eventName || "";
      }
      if (name.includes("regarding")) {
        return columnFilters.regarding || "";
      }
      if (name.includes("date")) {
        return columnFilters.dueDate || "";
      }
      if (name === "sprk_eventstatus" || name === "statecode") {
        return columnFilters.status;
      }
      if (name === "sprk_priority") {
        return columnFilters.priority;
      }
      if (name === "ownerid") {
        return columnFilters.owner || "";
      }
      if (name.includes("eventtype")) {
        return columnFilters.eventType || "";
      }
      return "";
    },
    [columnFilters]
  );

  /**
   * Check if a column has an active filter.
   */
  const hasColumnActiveFilter = React.useCallback(
    (column: LayoutColumn): boolean => {
      const name = column.name.toLowerCase();
      if (name === "sprk_eventname" || name === "sprk_name") {
        return !!columnFilters.eventName;
      }
      if (name.includes("regarding")) {
        return !!columnFilters.regarding;
      }
      if (name.includes("date")) {
        return !!columnFilters.dueDate;
      }
      if (name === "sprk_eventstatus" || name === "statecode") {
        return columnFilters.status.length > 0;
      }
      if (name === "sprk_priority") {
        return columnFilters.priority.length > 0;
      }
      if (name === "ownerid") {
        return !!columnFilters.owner;
      }
      if (name.includes("eventtype")) {
        return !!columnFilters.eventType;
      }
      return false;
    },
    [columnFilters]
  );

  /**
   * Get sort key for a dynamic column.
   */
  const getColumnSortKey = React.useCallback((column: LayoutColumn): string => {
    const name = column.name.toLowerCase();
    if (name === "sprk_eventname" || name === "sprk_name") return "eventName";
    if (name.includes("regarding")) return "regarding";
    if (name.includes("date")) return "dueDate";
    if (name === "sprk_eventstatus" || name === "statecode") return "status";
    if (name === "sprk_priority") return "priority";
    if (name === "ownerid") return "owner";
    if (name.includes("eventtype")) return "eventType";
    return name;
  }, []);

  /**
   * Handle row click (on event name)
   */
  const handleEventNameClick = React.useCallback(
    (event: React.MouseEvent, record: IEventRecord) => {
      event.preventDefault();
      event.stopPropagation();
      onRowClick?.(record.sprk_eventid, record._sprk_eventtype_ref_value);
    },
    [onRowClick]
  );

  /**
   * Handle row click (on entire row, excluding checkbox)
   */
  const handleRowClick = React.useCallback(
    (record: IEventRecord) => {
      onRowClick?.(record.sprk_eventid, record._sprk_eventtype_ref_value);
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

      // Navigate to parent record using entity logical name
      if (record.sprk_regardingrecordtypelogicalname && record.sprk_regardingrecordid) {
        navigateToRegardingRecord(
          record.sprk_regardingrecordtypelogicalname,
          record.sprk_regardingrecordid
        );
      }
    },
    []
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Column Filter Handlers (Task 094)
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle text column filter change (Event Name, Regarding, Owner, Event Type)
   */
  const handleTextColumnFilter = React.useCallback(
    (column: "eventName" | "regarding" | "owner" | "eventType", value: string | (string | number)[] | null) => {
      console.log(`[GridSection] Column filter changed: ${column}`, value);
      setColumnFilters((prev) => ({
        ...prev,
        [column]: typeof value === "string" ? value : null,
      }));
    },
    []
  );

  /**
   * Handle choice column filter change (Status, Priority)
   */
  const handleChoiceColumnFilter = React.useCallback(
    (column: "status" | "priority", value: string | (string | number)[] | null) => {
      console.log(`[GridSection] Column filter changed: ${column}`, value);
      setColumnFilters((prev) => ({
        ...prev,
        [column]: Array.isArray(value) ? value.map((v) => Number(v)) : [],
      }));
    },
    []
  );

  /**
   * Handle date column filter change (Due Date)
   */
  const handleDateColumnFilter = React.useCallback(
    (value: string | (string | number)[] | null) => {
      console.log("[GridSection] Due Date column filter changed:", value);
      setColumnFilters((prev) => ({
        ...prev,
        dueDate: typeof value === "string" ? value : null,
      }));
    },
    []
  );

  /**
   * Handle sort change (Task 097)
   */
  const handleSortChange = React.useCallback(
    (column: string, direction: SortDirection) => {
      console.log(`[GridSection] Sort changed: ${column}`, direction);
      setSortConfig({
        column: direction ? column : null,
        direction,
      });
    },
    []
  );

  /**
   * Handle filter change for a dynamic column.
   * NOTE: Must be defined AFTER handleTextColumnFilter, handleDateColumnFilter, handleChoiceColumnFilter
   */
  const handleDynamicColumnFilter = React.useCallback(
    (column: LayoutColumn, value: string | (string | number)[] | null) => {
      const name = column.name.toLowerCase();
      console.log(`[GridSection] Dynamic column filter: ${name}`, value);

      if (name === "sprk_eventname" || name === "sprk_name") {
        handleTextColumnFilter("eventName", value);
      } else if (name.includes("regarding")) {
        handleTextColumnFilter("regarding", value);
      } else if (name.includes("date")) {
        handleDateColumnFilter(value);
      } else if (name === "sprk_eventstatus" || name === "statecode") {
        handleChoiceColumnFilter("status", value);
      } else if (name === "sprk_priority") {
        handleChoiceColumnFilter("priority", value);
      } else if (name === "ownerid") {
        handleTextColumnFilter("owner", value);
      } else if (name.includes("eventtype")) {
        handleTextColumnFilter("eventType", value);
      }
    },
    [handleTextColumnFilter, handleDateColumnFilter, handleChoiceColumnFilter]
  );

  // All selected? (based on filtered events, not all events)
  const allSelected =
    filteredEvents.length > 0 && selectedIds.size === filteredEvents.length;
  const someSelected = selectedIds.size > 0 && selectedIds.size < filteredEvents.length;

  // Check if any column filters are active (Task 094)
  const hasColumnFilters =
    !!columnFilters.eventName ||
    !!columnFilters.regarding ||
    columnFilters.status.length > 0 ||
    columnFilters.priority.length > 0 ||
    !!columnFilters.owner ||
    !!columnFilters.eventType ||
    !!columnFilters.dueDate;

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
              {/* Dynamic column headers from layoutXml (Task 097) */}
              {activeColumns.map((column) => {
                const filterType = getColumnFilterType(column);
                const sortKey = getColumnSortKey(column);
                const filterValue = getColumnFilterValue(column);

                return (
                  <ColumnHeaderMenu
                    key={column.name}
                    columnKey={sortKey}
                    title={column.label}
                    filterType={filterType}
                    options={filterType === "choice" ? getColumnFilterOptions(column) : undefined}
                    filterValue={filterType === "text" || filterType === "date" ? (filterValue as string) : undefined}
                    selectedValues={filterType === "choice" ? (filterValue as number[]) : undefined}
                    hasActiveFilter={hasColumnActiveFilter(column)}
                    onFilterChange={(v) => handleDynamicColumnFilter(column, v)}
                    sortDirection={sortConfig.column === sortKey ? sortConfig.direction : null}
                    onSortChange={(dir) => handleSortChange(sortKey, dir)}
                    sortable={true}
                  />
                );
              })}
            </tr>
          </thead>
          <tbody>
            {filteredEvents.map((record) => {
              const isSelected = selectedIds.has(record.sprk_eventid);

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
                  {/* Dynamic cell rendering based on layoutXml columns */}
                  {activeColumns.map((column) => {
                    // Event Name column - render as link
                    if (isEventNameColumn(column)) {
                      return (
                        <td key={column.name} className={styles.td}>
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
                      );
                    }

                    // Regarding column - render as link if navigable
                    if (isRegardingColumn(column)) {
                      return (
                        <td key={column.name} className={styles.td}>
                          {record.sprk_regardingrecordname ? (
                            record.sprk_regardingrecordid &&
                            record.sprk_regardingrecordtypelogicalname ? (
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
                              record.sprk_regardingrecordname
                            )
                          ) : (
                            "—"
                          )}
                        </td>
                      );
                    }

                    // Status column - render as badge
                    if (isStatusColumn(column)) {
                      return (
                        <td key={column.name} className={styles.td}>
                          <Badge
                            appearance={getStatusAppearance(record.sprk_eventstatus)}
                            color={getStatusColor(record.sprk_eventstatus)}
                            className={styles.statusBadge}
                          >
                            {record[
                              "sprk_eventstatus@OData.Community.Display.V1.FormattedValue"
                            ] || "Unknown"}
                          </Badge>
                        </td>
                      );
                    }

                    // Priority column - render with color styling
                    if (isPriorityColumn(column)) {
                      const priorityClass =
                        record.sprk_priority === 1
                          ? styles.priorityHigh
                          : record.sprk_priority === 3
                          ? styles.priorityLow
                          : styles.priorityNormal;
                      return (
                        <td key={column.name} className={`${styles.td} ${priorityClass}`}>
                          {record[
                            "sprk_priority@OData.Community.Display.V1.FormattedValue"
                          ] || "—"}
                        </td>
                      );
                    }

                    // Default cell rendering
                    return (
                      <td key={column.name} className={styles.td}>
                        {getColumnDisplayValue(record, column)}
                      </td>
                    );
                  })}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <div className={styles.footer}>
        <Text>
          {filteredEvents.length} event{filteredEvents.length !== 1 ? "s" : ""}
          {hasColumnFilters && ` (${events.length} total)`} •{" "}
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
  // Mock event GUIDs for development/testing (valid GUID format required for side pane)
  const mockEvents: IEventRecord[] = [
    {
      sprk_eventid: "00000000-0000-0000-0001-000000000001",
      sprk_eventname: "Contract Review Deadline",
      sprk_duedate: new Date(today.getTime() + 86400000 * 2).toISOString(),
      sprk_eventstatus: 1, // Open
      statecode: 0,
      sprk_priority: 1,
      _ownerid_value: "00000000-0000-0000-0000-000000000001", // Current user (mock)
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Current User",
      "_sprk_eventtype_ref_value": "00000000-0000-0000-0000-000000000101",
      "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue":
        "Filing Deadline",
      "sprk_eventstatus@OData.Community.Display.V1.FormattedValue": "Open",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "High",
      // Regarding: Matter record
      sprk_regardingrecordname: "Smith vs. Johnson Corp",
      "_sprk_regardingrecordtype_value": "00000000-0000-0000-0000-000000000001",
      sprk_regardingrecordtypelogicalname: "sprk_matter",
      sprk_regardingrecordid: "00000000-0000-0000-0000-000000000201",
    },
    {
      sprk_eventid: "00000000-0000-0000-0001-000000000002",
      sprk_eventname: "Client Meeting",
      sprk_duedate: new Date(today.getTime() + 86400000 * 5).toISOString(),
      sprk_eventstatus: 1, // Open
      statecode: 0,
      sprk_priority: 2,
      _ownerid_value: "00000000-0000-0000-0000-000000000002",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Jane Doe",
      "_sprk_eventtype_ref_value": "00000000-0000-0000-0000-000000000102",
      "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue":
        "Meeting",
      "sprk_eventstatus@OData.Community.Display.V1.FormattedValue": "Open",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Normal",
      // Regarding: Project record
      sprk_regardingrecordname: "Website Redesign Project",
      "_sprk_regardingrecordtype_value": "00000000-0000-0000-0000-000000000002",
      sprk_regardingrecordtypelogicalname: "sprk_project",
      sprk_regardingrecordid: "00000000-0000-0000-0000-000000000301",
    },
    {
      sprk_eventid: "00000000-0000-0000-0001-000000000003",
      sprk_eventname: "Document Submission",
      sprk_duedate: new Date(today.getTime() + 86400000 * 7).toISOString(),
      sprk_eventstatus: 0, // Draft
      statecode: 0,
      sprk_priority: 3,
      _ownerid_value: "00000000-0000-0000-0000-000000000003",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "John Smith",
      "_sprk_eventtype_ref_value": "00000000-0000-0000-0000-000000000103",
      "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue": "Task",
      "sprk_eventstatus@OData.Community.Display.V1.FormattedValue": "Draft",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Low",
      // No regarding record for this event (standalone event)
    },
    {
      sprk_eventid: "00000000-0000-0000-0001-000000000004",
      sprk_eventname: "Project Kickoff",
      sprk_duedate: new Date(today.getTime() + 86400000 * 10).toISOString(),
      sprk_eventstatus: 1, // Open
      statecode: 0,
      sprk_priority: 2,
      _ownerid_value: "00000000-0000-0000-0000-000000000001", // Current user (mock)
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Current User",
      "_sprk_eventtype_ref_value": "00000000-0000-0000-0000-000000000102",
      "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue":
        "Meeting",
      "sprk_eventstatus@OData.Community.Display.V1.FormattedValue": "Open",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Normal",
      sprk_regardingrecordname: "Alpha Project",
      "_sprk_regardingrecordtype_value": "00000000-0000-0000-0000-000000000002",
      sprk_regardingrecordtypelogicalname: "sprk_project",
      sprk_regardingrecordid: "00000000-0000-0000-0000-000000000302",
    },
    // Additional mock events for status filter testing (Task 065)
    {
      sprk_eventid: "00000000-0000-0000-0001-000000000005",
      sprk_eventname: "Completed Task Example",
      sprk_duedate: new Date(today.getTime() - 86400000 * 3).toISOString(), // 3 days ago
      sprk_eventstatus: 2, // Completed
      statecode: 0,
      sprk_priority: 2,
      _ownerid_value: "00000000-0000-0000-0000-000000000003",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "John Smith",
      "_sprk_eventtype_ref_value": "00000000-0000-0000-0000-000000000103",
      "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue": "Task",
      "sprk_eventstatus@OData.Community.Display.V1.FormattedValue": "Completed",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Normal",
      sprk_regardingrecordname: "Beta Project",
      "_sprk_regardingrecordtype_value": "00000000-0000-0000-0000-000000000002",
      sprk_regardingrecordtypelogicalname: "sprk_project",
      sprk_regardingrecordid: "00000000-0000-0000-0000-000000000303",
    },
    {
      sprk_eventid: "00000000-0000-0000-0001-000000000006",
      sprk_eventname: "Cancelled Meeting",
      sprk_duedate: new Date(today.getTime() + 86400000 * 1).toISOString(), // Tomorrow
      sprk_eventstatus: 5, // Cancelled
      statecode: 0,
      sprk_priority: 1,
      _ownerid_value: "00000000-0000-0000-0000-000000000002",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Jane Doe",
      "_sprk_eventtype_ref_value": "00000000-0000-0000-0000-000000000102",
      "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue": "Meeting",
      "sprk_eventstatus@OData.Community.Display.V1.FormattedValue": "Cancelled",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "High",
    },
    {
      sprk_eventid: "00000000-0000-0000-0001-000000000007",
      sprk_eventname: "On Hold Review",
      sprk_duedate: new Date(today.getTime() + 86400000 * 14).toISOString(), // 2 weeks out
      sprk_eventstatus: 4, // On Hold
      statecode: 0,
      sprk_priority: 2,
      _ownerid_value: "00000000-0000-0000-0000-000000000001",
      "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Current User",
      "_sprk_eventtype_ref_value": "00000000-0000-0000-0000-000000000101",
      "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue": "Filing Deadline",
      "sprk_eventstatus@OData.Community.Display.V1.FormattedValue": "On Hold",
      "sprk_priority@OData.Community.Display.V1.FormattedValue": "Normal",
      sprk_regardingrecordname: "Gamma Matter",
      "_sprk_regardingrecordtype_value": "00000000-0000-0000-0000-000000000001",
      sprk_regardingrecordtypelogicalname: "sprk_matter",
      sprk_regardingrecordid: "00000000-0000-0000-0000-000000000202",
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
      eventTypeFilter.includes(event._sprk_eventtype_ref_value || "")
    );
  }

  // Apply status filter to mock data (Task 065)
  if (statusFilter && statusFilter.length > 0) {
    filteredEvents = filteredEvents.filter((event) =>
      statusFilter.includes(event.sprk_eventstatus)
    );
  }

  return filteredEvents;
}

export default GridSection;
