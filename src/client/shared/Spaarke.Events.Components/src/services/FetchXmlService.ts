/**
 * FetchXmlService
 *
 * Service for executing FetchXML queries against Dataverse via Xrm.WebApi.
 * Used by EventsPage to load data from saved views.
 *
 * @see projects/events-workspace-apps-UX-r1/notes/design/universal-datagrid-enhancement.md
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface FetchXmlResult<T> {
  entities: T[];
  moreRecords: boolean;
  pagingCookie?: string;
  totalRecordCount?: number;
}

export interface ViewDefinition {
  id: string;
  name: string;
  fetchXml: string;
  layoutXml: string;
  entityLogicalName: string;
}

/**
 * Column definition parsed from layoutXml.
 */
export interface LayoutColumn {
  name: string;
  width: number;
  label: string;
  isLookup: boolean;
  formattedValueField?: string;
}

/**
 * Known column labels for Events entity fields.
 * Used when parsing layoutXml to provide display labels.
 */
const COLUMN_LABELS: Record<string, string> = {
  sprk_eventname: "Event Name",
  sprk_name: "Event Name",
  sprk_duedate: "Due Date",
  sprk_eventstatus: "Status",
  statecode: "Status",
  statuscode: "Status Reason",
  sprk_priority: "Priority",
  ownerid: "Owner",
  sprk_eventtype: "Event Type",
  sprk_eventtype_ref: "Event Type",
  sprk_regardingrecordname: "Regarding",
  sprk_regardingrecordtype: "Record Type",
  createdon: "Created On",
  modifiedon: "Modified On",
  createdby: "Created By",
  modifiedby: "Modified By",
};

/**
 * Fields that are lookup types (require formatted value access).
 */
const LOOKUP_FIELDS = new Set([
  "ownerid",
  "createdby",
  "modifiedby",
  "sprk_eventtype",
  "sprk_eventtype_ref",
  "sprk_regardingrecordtype",
]);

// ─────────────────────────────────────────────────────────────────────────────
// Xrm Access
// ─────────────────────────────────────────────────────────────────────────────

declare const Xrm: any;

/**
 * Get the Xrm object from the appropriate context.
 * Custom Pages run in an iframe, so Xrm may be on window.parent.
 */
function getXrm(): any | undefined {
  // Try window.Xrm first
  if (typeof Xrm !== "undefined" && Xrm?.WebApi) {
    return Xrm;
  }
  // Try parent.Xrm for Custom Pages running in iframe
  try {
    if (typeof window !== "undefined" && window.parent && (window.parent as any).Xrm?.WebApi) {
      return (window.parent as any).Xrm;
    }
  } catch (e) {
    console.debug("[FetchXmlService] Cannot access parent.Xrm:", e);
  }
  return undefined;
}

// ─────────────────────────────────────────────────────────────────────────────
// FetchXmlService
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Execute a FetchXML query against Dataverse.
 *
 * @param entityLogicalName - Entity to query
 * @param fetchXml - FetchXML query string
 * @returns Query results
 */
export async function executeFetchXml<T>(
  entityLogicalName: string,
  fetchXml: string
): Promise<FetchXmlResult<T>> {
  const xrm = getXrm();
  if (!xrm) {
    console.warn("[FetchXmlService] Xrm not available");
    return { entities: [], moreRecords: false };
  }

  try {
    // Encode FetchXML for URL
    const encodedFetchXml = encodeURIComponent(fetchXml);

    // Execute via Xrm.WebApi with fetchXml parameter
    const result = await xrm.WebApi.retrieveMultipleRecords(
      entityLogicalName,
      `?fetchXml=${encodedFetchXml}`
    );

    console.log(`[FetchXmlService] Query returned ${result.entities?.length || 0} records`);

    return {
      entities: result.entities || [],
      moreRecords: !!result["@Microsoft.Dynamics.CRM.morerecords"],
      pagingCookie: result["@Microsoft.Dynamics.CRM.fetchxmlpagingcookie"],
      totalRecordCount: result["@Microsoft.Dynamics.CRM.totalrecordcount"],
    };
  } catch (error) {
    console.error("[FetchXmlService] Query failed:", error);
    throw error;
  }
}

/**
 * Fetch a savedquery (view) by ID and return its definition.
 *
 * @param viewId - GUID of the savedquery
 * @returns View definition with FetchXML and LayoutXML
 */
export async function getViewById(viewId: string): Promise<ViewDefinition | null> {
  const xrm = getXrm();
  if (!xrm) {
    console.warn("[FetchXmlService] Xrm not available");
    return null;
  }

  try {
    // Clean the GUID (remove braces if present)
    const cleanId = viewId.replace(/[{}]/g, "");

    // Fetch the savedquery record
    const result = await xrm.WebApi.retrieveRecord(
      "savedquery",
      cleanId,
      "?$select=savedqueryid,name,fetchxml,layoutxml,returnedtypecode"
    );

    if (!result) {
      console.warn(`[FetchXmlService] View not found: ${viewId}`);
      return null;
    }

    console.log(`[FetchXmlService] Loaded view: ${result.name}`);

    return {
      id: result.savedqueryid,
      name: result.name,
      fetchXml: result.fetchxml,
      layoutXml: result.layoutxml,
      entityLogicalName: result.returnedtypecode,
    };
  } catch (error) {
    console.error(`[FetchXmlService] Failed to load view ${viewId}:`, error);
    return null;
  }
}

/**
 * Ensure the primary key attribute is present in the FetchXML.
 * This is the minimum required for record identification.
 *
 * Note: We intentionally do NOT inject other columns like sprk_duedate.
 * Different Event types (Tasks, Meetings, etc.) have different views
 * with different columns. The view defines what columns to fetch.
 *
 * @param fetchXml - Original FetchXML from saved view
 * @returns FetchXML with primary key attribute if missing
 */
export function ensureRequiredAttributes(fetchXml: string): string {
  // Only ensure the primary key is present for record identification
  const hasPrimaryKey = /<attribute\s+name="sprk_eventid"/i.test(fetchXml);

  if (!hasPrimaryKey) {
    // Find the entity element to inject the primary key
    const entityMatch = fetchXml.match(/<entity\s+name="[^"]+"\s*>/i);
    if (entityMatch) {
      const insertIndex = fetchXml.indexOf(entityMatch[0]) + entityMatch[0].length;
      return (
        fetchXml.slice(0, insertIndex) +
        '\n    <attribute name="sprk_eventid" />' +
        fetchXml.slice(insertIndex)
      );
    }
  }

  return fetchXml;
}

/**
 * Merge a date filter condition into existing FetchXML.
 *
 * Injects a date condition into the FetchXML's main filter.
 * Handles both single date and date range filters.
 *
 * @param fetchXml - Original FetchXML
 * @param dateFilter - Date filter to merge
 * @returns Modified FetchXML with date condition
 */
export function mergeDateFilterIntoFetchXml(
  fetchXml: string,
  dateFilter: {
    type: "single" | "range" | "clear";
    date?: string;
    start?: string;
    end?: string;
    dateFields?: string[];
  } | null
): string {
  if (!dateFilter || dateFilter.type === "clear") {
    return fetchXml;
  }

  // Get date fields to filter (default to sprk_duedate)
  const dateFields = dateFilter.dateFields?.length
    ? dateFilter.dateFields
    : ["sprk_duedate"];

  // Build the date condition(s)
  let dateConditions: string;

  if (dateFilter.type === "single" && dateFilter.date) {
    // Single date: filter for events on that date
    // Use 'on' operator for date fields
    if (dateFields.length === 1) {
      dateConditions = `<condition attribute="${dateFields[0].toLowerCase()}" operator="on" value="${dateFilter.date}" />`;
    } else {
      // Multiple fields - OR logic using filter type="or"
      const conditions = dateFields.map(
        (field) => `<condition attribute="${field.toLowerCase()}" operator="on" value="${dateFilter.date}" />`
      ).join("\n              ");
      dateConditions = `<filter type="or">\n              ${conditions}\n            </filter>`;
    }
  } else if (dateFilter.type === "range" && dateFilter.start && dateFilter.end) {
    // Date range: filter for events between start and end
    if (dateFields.length === 1) {
      const field = dateFields[0].toLowerCase();
      dateConditions = `<condition attribute="${field}" operator="on-or-after" value="${dateFilter.start}" />
            <condition attribute="${field}" operator="on-or-before" value="${dateFilter.end}" />`;
    } else {
      // Multiple fields - OR logic
      const conditions = dateFields.map((field) => {
        const f = field.toLowerCase();
        return `<filter type="and">
                <condition attribute="${f}" operator="on-or-after" value="${dateFilter.start}" />
                <condition attribute="${f}" operator="on-or-before" value="${dateFilter.end}" />
              </filter>`;
      }).join("\n              ");
      dateConditions = `<filter type="or">\n              ${conditions}\n            </filter>`;
    }
  } else {
    return fetchXml;
  }

  // Find the main <filter> element and inject date conditions
  // If no filter exists, add one after the attributes
  const hasFilter = /<filter\s+type=/i.test(fetchXml);

  if (hasFilter) {
    // Insert date conditions into existing filter
    // Find the first </filter> that closes the main filter and insert before it
    // We need to be careful with nested filters
    const filterMatch = fetchXml.match(/<filter\s+type="and"[^>]*>/i);
    if (filterMatch) {
      // Insert after the opening <filter type="and">
      const insertIndex = fetchXml.indexOf(filterMatch[0]) + filterMatch[0].length;
      return (
        fetchXml.slice(0, insertIndex) +
        "\n            " +
        dateConditions +
        fetchXml.slice(insertIndex)
      );
    }
  }

  // No existing filter - add one after attributes
  // Find </entity> and insert filter before it
  const entityCloseIndex = fetchXml.lastIndexOf("</entity>");
  if (entityCloseIndex !== -1) {
    const filterXml = `
          <filter type="and">
            ${dateConditions}
          </filter>`;
    return (
      fetchXml.slice(0, entityCloseIndex) +
      filterXml +
      "\n        " +
      fetchXml.slice(entityCloseIndex)
    );
  }

  // Fallback - return original
  console.warn("[FetchXmlService] Could not merge date filter into FetchXML");
  return fetchXml;
}

/**
 * Parse layoutXml from a saved view to extract column definitions.
 *
 * LayoutXml format example:
 * ```xml
 * <grid name="resultset" object="10066" jump="sprk_eventname" select="1">
 *   <row name="result" id="sprk_eventid">
 *     <cell name="sprk_eventname" width="200" />
 *     <cell name="sprk_duedate" width="150" />
 *     <cell name="ownerid" width="150" />
 *   </row>
 * </grid>
 * ```
 *
 * @param layoutXml - LayoutXml string from savedquery
 * @returns Array of column definitions
 */
export function parseLayoutXml(layoutXml: string): LayoutColumn[] {
  if (!layoutXml) {
    console.warn("[FetchXmlService] No layoutXml provided");
    return [];
  }

  const columns: LayoutColumn[] = [];

  try {
    // Parse cell elements using regex (no DOMParser in Dataverse context)
    // Match: <cell name="fieldname" width="200" ... />
    const cellRegex = /<cell\s+([^>]+)\/>/gi;
    let match;

    while ((match = cellRegex.exec(layoutXml)) !== null) {
      const attrs = match[1];

      // Extract name attribute
      const nameMatch = attrs.match(/name="([^"]+)"/i);
      if (!nameMatch) continue;

      const fieldName = nameMatch[1].toLowerCase();

      // Extract width attribute (default to 150)
      const widthMatch = attrs.match(/width="(\d+)"/i);
      const width = widthMatch ? parseInt(widthMatch[1], 10) : 150;

      // Determine if this is a lookup field
      const isLookup = LOOKUP_FIELDS.has(fieldName);

      // Get display label
      const label = COLUMN_LABELS[fieldName] || formatFieldNameAsLabel(fieldName);

      // Build formatted value field for lookups
      let formattedValueField: string | undefined;
      if (isLookup) {
        if (fieldName === "ownerid") {
          formattedValueField = "_ownerid_value@OData.Community.Display.V1.FormattedValue";
        } else if (fieldName === "sprk_eventtype" || fieldName === "sprk_eventtype_ref") {
          formattedValueField = "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue";
        } else if (fieldName === "createdby") {
          formattedValueField = "_createdby_value@OData.Community.Display.V1.FormattedValue";
        } else if (fieldName === "modifiedby") {
          formattedValueField = "_modifiedby_value@OData.Community.Display.V1.FormattedValue";
        } else if (fieldName === "sprk_regardingrecordtype") {
          formattedValueField = "_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue";
        }
      }

      columns.push({
        name: fieldName,
        width,
        label,
        isLookup,
        formattedValueField,
      });
    }

    console.log(`[FetchXmlService] Parsed ${columns.length} columns from layoutXml`);
    return columns;
  } catch (error) {
    console.error("[FetchXmlService] Failed to parse layoutXml:", error);
    return [];
  }
}

/**
 * Convert a field name to a readable label.
 * e.g., "sprk_myfield" -> "My Field"
 */
function formatFieldNameAsLabel(fieldName: string): string {
  // Remove prefix (sprk_, etc.)
  let name = fieldName.replace(/^[a-z]+_/, "");

  // Split on underscores and camelCase
  name = name.replace(/_/g, " ");
  name = name.replace(/([a-z])([A-Z])/g, "$1 $2");

  // Capitalize first letter of each word
  return name
    .split(" ")
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())
    .join(" ");
}

/* eslint-enable @typescript-eslint/no-explicit-any */
