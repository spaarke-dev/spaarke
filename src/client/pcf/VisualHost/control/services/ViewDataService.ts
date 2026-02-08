/**
 * View Data Service
 * Fetches data using Dataverse saved views with context filtering.
 * Supports FetchXML retrieval from savedquery, context filter injection,
 * parameter substitution, and query priority resolution.
 *
 * Tasks 040-042 - Visualization Module R2
 */

import type { IChartDefinition } from "../types";
import type { IConfigWebApi } from "./ConfigurationLoader";
import { logger } from "../utils/logger";

/**
 * Context for view-driven data fetching
 */
export interface IViewDataContext {
  /** Saved view ID (savedquery GUID) */
  viewId: string;
  /** Optional context filter for related record filtering */
  contextFilter?: { fieldName: string; recordId: string };
  /** Maximum items to return */
  maxItems?: number;
  /** Runtime parameters for FetchXML substitution */
  substitutionParams?: ISubstitutionParams;
  /** Additional parameter mappings JSON */
  paramMappings?: string;
}

/**
 * View data service error
 */
export class ViewDataError extends Error {
  constructor(message: string, public readonly cause?: unknown) {
    super(message);
    this.name = "ViewDataError";
  }
}

/**
 * Event record mapped from Dataverse
 */
export interface IEventRecord {
  eventId: string;
  eventName: string;
  eventTypeName: string;
  dueDate: Date;
  daysUntilDue: number;
  isOverdue: boolean;
  eventTypeColor?: string;
  description?: string;
  assignedTo?: string;
}

/**
 * Cache for view FetchXML definitions
 */
const viewCache = new Map<string, { fetchXml: string; entityName: string; timestamp: number }>();
const VIEW_CACHE_TTL_MS = 10 * 60 * 1000; // 10 minutes (views change infrequently)

/**
 * Clear view cache
 */
export function clearViewCache(viewId?: string): void {
  if (viewId) {
    viewCache.delete(viewId);
  } else {
    viewCache.clear();
  }
}

/**
 * Retrieve FetchXML from a Dataverse saved view (savedquery entity)
 *
 * @param webApi - WebAPI interface
 * @param viewId - The savedquery GUID
 * @returns Object containing the fetchxml string and entity logical name
 */
export async function getViewFetchXml(
  webApi: IConfigWebApi,
  viewId: string
): Promise<{ fetchXml: string; entityName: string }> {
  const normalizedId = viewId.replace(/[{}]/g, "").toLowerCase();

  // Check cache
  const cached = viewCache.get(normalizedId);
  if (cached && Date.now() - cached.timestamp < VIEW_CACHE_TTL_MS) {
    logger.debug("ViewDataService", `View cache hit for ${normalizedId}`);
    return { fetchXml: cached.fetchXml, entityName: cached.entityName };
  }

  logger.info("ViewDataService", `Retrieving view definition: ${normalizedId}`);

  try {
    const record = await webApi.retrieveRecord(
      "savedquery",
      normalizedId,
      "?$select=fetchxml,returnedtypecode,name"
    );

    const fetchXml = record.fetchxml as string;
    const entityName = record.returnedtypecode as string;

    if (!fetchXml) {
      throw new ViewDataError(`View ${normalizedId} has no FetchXML defined`);
    }

    // Cache the result
    viewCache.set(normalizedId, {
      fetchXml,
      entityName: entityName || "",
      timestamp: Date.now(),
    });

    logger.info("ViewDataService", `Retrieved view: ${record.name}`, {
      entityName,
      fetchXmlLength: fetchXml.length,
    });

    return { fetchXml, entityName };
  } catch (error) {
    if (error instanceof ViewDataError) throw error;

    const msg = error instanceof Error ? error.message : String(error);

    if (msg.includes("does not exist") || msg.includes("not found") || msg.includes("0x80040217")) {
      throw new ViewDataError(`View not found: ${normalizedId}`);
    }

    throw new ViewDataError(`Failed to retrieve view: ${msg}`, error);
  }
}

/**
 * Inject a context filter condition into FetchXML.
 * Adds a filter condition for the specified field = recordId
 * without modifying the original saved view.
 *
 * @param fetchXml - Original FetchXML string
 * @param fieldName - The lookup field to filter on (e.g., "_sprk_matterid_value")
 * @param recordId - The record ID to filter by
 * @returns Modified FetchXML string with injected filter
 */
export function injectContextFilter(
  fetchXml: string,
  fieldName: string,
  recordId: string
): string {
  const cleanId = recordId.replace(/[{}]/g, "");

  // Build the condition to inject
  const condition = `<condition attribute="${fieldName}" operator="eq" value="${cleanId}" />`;

  // Strategy: Find the <entity> element and inject a filter inside it.
  // If there's already a <filter> at the entity level, wrap both in an AND filter.
  // We use string manipulation instead of DOMParser to avoid browser compatibility issues
  // in PCF environments where DOMParser may not be available.

  // Check if there's already a top-level <filter in the entity
  const entityMatch = fetchXml.match(/<entity\s[^>]*>/i);
  if (!entityMatch) {
    logger.warn("ViewDataService", "Could not find <entity> in FetchXML, appending filter");
    // Fallback: try to inject before </fetch>
    return fetchXml.replace(
      /<\/fetch>/i,
      `<filter type="and">${condition}</filter></fetch>`
    );
  }

  const entityTagEnd = fetchXml.indexOf(">", fetchXml.indexOf(entityMatch[0])) + 1;

  // Look for an existing top-level <filter> element (first one after <entity>)
  const afterEntity = fetchXml.substring(entityTagEnd);
  const existingFilterMatch = afterEntity.match(/(<filter[^>]*>)/i);

  if (existingFilterMatch) {
    // There's already a filter - wrap existing filter content and our condition in an AND
    // Find the position of the existing <filter> tag
    const filterPos = entityTagEnd + afterEntity.indexOf(existingFilterMatch[0]);
    const filterTag = existingFilterMatch[0];

    // Check if existing filter already has type="and"
    if (filterTag.includes('type="and"') || filterTag.includes("type='and'")) {
      // Just inject our condition right after the opening <filter> tag
      const insertPos = filterPos + filterTag.length;
      return (
        fetchXml.substring(0, insertPos) +
        condition +
        fetchXml.substring(insertPos)
      );
    }

    // Existing filter has a different type (e.g., "or") - wrap both in an AND
    // Find the matching </filter> for this filter element
    const closingFilterPos = findMatchingClose(fetchXml, filterPos, "filter");
    if (closingFilterPos >= 0) {
      const existingFilter = fetchXml.substring(filterPos, closingFilterPos + "</filter>".length);
      const wrappedFilter =
        `<filter type="and">${condition}${existingFilter}</filter>`;
      return (
        fetchXml.substring(0, filterPos) +
        wrappedFilter +
        fetchXml.substring(closingFilterPos + "</filter>".length)
      );
    }
  }

  // No existing filter - inject a new one right after <entity ...>
  return (
    fetchXml.substring(0, entityTagEnd) +
    `<filter type="and">${condition}</filter>` +
    fetchXml.substring(entityTagEnd)
  );
}

/**
 * Find the matching closing tag position for a given opening tag position.
 * Handles nested elements of the same type.
 */
function findMatchingClose(xml: string, openPos: number, tagName: string): number {
  const openTag = new RegExp(`<${tagName}[\\s>]`, "gi");
  const closeTag = `</${tagName}>`;
  let depth = 0;
  let pos = openPos;

  while (pos < xml.length) {
    const nextClose = xml.indexOf(closeTag, pos);
    if (nextClose < 0) return -1;

    // Count opening tags between current position and next close
    openTag.lastIndex = pos + 1;
    let match;
    while ((match = openTag.exec(xml)) !== null && match.index < nextClose) {
      depth++;
    }

    if (depth === 0) {
      return nextClose;
    }

    depth--;
    pos = nextClose + closeTag.length;
  }

  return -1;
}

/**
 * Apply max items (top count) to FetchXML
 */
export function applyMaxItems(fetchXml: string, maxItems: number): string {
  // Check if <fetch> already has a "top" attribute
  const fetchTagMatch = fetchXml.match(/<fetch([^>]*)>/i);
  if (!fetchTagMatch) return fetchXml;

  const fetchAttrs = fetchTagMatch[1];

  if (/\btop\s*=/i.test(fetchAttrs)) {
    // Replace existing top value
    return fetchXml.replace(
      /(<fetch[^>]*)\btop\s*=\s*["']\d+["']([^>]*>)/i,
      `$1top="${maxItems}"$2`
    );
  }

  // Add top attribute
  return fetchXml.replace(
    /(<fetch)([^>]*>)/i,
    `$1 top="${maxItems}"$2`
  );
}

// ──────────────────────────────────────────────────
// Parameter Substitution (Task 041)
// ──────────────────────────────────────────────────

/**
 * Runtime parameters available for FetchXML substitution
 */
export interface ISubstitutionParams {
  /** Current record ID from form context */
  contextRecordId?: string;
  /** Current user's system user ID */
  currentUserId?: string;
}

/**
 * Substitute parameter placeholders in a FetchXML string.
 *
 * Supported placeholders:
 * - {contextRecordId} → current record ID from form context
 * - {currentUserId}   → current Dataverse system user ID
 * - {currentDate}     → today's date in ISO format (YYYY-MM-DD)
 * - {currentDateTime} → current date/time in ISO format
 *
 * Parameters from sprk_fetchxmlparams JSON are also substituted.
 *
 * @param fetchXml - FetchXML string with placeholders
 * @param params - Runtime parameter values
 * @param paramMappings - Optional JSON string of additional param mappings from sprk_fetchxmlparams
 * @returns FetchXML with placeholders replaced
 */
export function substituteParameters(
  fetchXml: string,
  params: ISubstitutionParams,
  paramMappings?: string
): string {
  let result = fetchXml;

  // Built-in parameters
  const now = new Date();
  const builtInParams: Record<string, string> = {
    contextRecordId: params.contextRecordId?.replace(/[{}]/g, "") || "",
    currentUserId: params.currentUserId?.replace(/[{}]/g, "") || "",
    currentDate: now.toISOString().split("T")[0], // YYYY-MM-DD
    currentDateTime: now.toISOString(),
  };

  // Replace built-in placeholders
  for (const [key, value] of Object.entries(builtInParams)) {
    if (value) {
      result = result.replace(new RegExp(`\\{${key}\\}`, "g"), value);
    }
  }

  // Parse and apply custom parameter mappings from sprk_fetchxmlparams
  if (paramMappings) {
    try {
      const mappings = JSON.parse(paramMappings) as Record<string, string>;
      for (const [key, value] of Object.entries(mappings)) {
        if (typeof value === "string") {
          result = result.replace(new RegExp(`\\{${key}\\}`, "g"), value);
        }
      }
    } catch {
      logger.warn("ViewDataService", "Failed to parse fetchXmlParams JSON", { paramMappings });
    }
  }

  // Log any remaining unresolved placeholders
  const remaining = result.match(/\{[a-zA-Z_]+\}/g);
  if (remaining) {
    logger.warn("ViewDataService", "Unresolved placeholders in FetchXML", { remaining });
  }

  return result;
}

// ──────────────────────────────────────────────────
// Query Priority Resolution (Task 042)
// ──────────────────────────────────────────────────

/**
 * Query source resolved by priority.
 * Priority order: pcfOverride → customFetchXml → view → directEntity
 */
export type QuerySource = "pcfOverride" | "customFetchXml" | "view" | "directEntity";

/**
 * Resolved query result from priority resolution
 */
export interface IResolvedQuery {
  /** Which source provided the query */
  source: QuerySource;
  /** The FetchXML to execute (if applicable) */
  fetchXml?: string;
  /** Entity logical name for the query */
  entityName: string;
}

/**
 * Inputs for query priority resolution
 */
export interface IQueryResolutionInputs {
  /** Chart definition with query configuration */
  chartDefinition: IChartDefinition;
  /** Optional PCF-level FetchXML override (from fetchXmlOverride property) */
  fetchXmlOverride?: string;
  /** Runtime substitution parameters */
  substitutionParams?: ISubstitutionParams;
  /** WebAPI for view retrieval */
  webApi: IConfigWebApi;
}

/**
 * Resolve which query source to use based on priority.
 *
 * Priority order (highest to lowest):
 * 1. PCF fetchXmlOverride property → per-deployment override
 * 2. Chart Definition sprk_fetchxmlquery → custom FetchXML on the record
 * 3. Chart Definition sprk_baseviewid → saved view reference
 * 4. Direct entity query → fallback using sprk_entitylogicalname
 *
 * @param inputs - All available query sources and parameters
 * @returns Resolved query with source, fetchXml, and entityName
 */
export async function resolveQuery(
  inputs: IQueryResolutionInputs
): Promise<IResolvedQuery> {
  const { chartDefinition, fetchXmlOverride, substitutionParams, webApi } = inputs;
  const entityName = chartDefinition.sprk_entitylogicalname || "sprk_event";

  // Priority 1: PCF override
  if (fetchXmlOverride?.trim()) {
    logger.info("ViewDataService", "Using PCF fetchXmlOverride", { entityName });
    let fetchXml = fetchXmlOverride;

    if (substitutionParams) {
      fetchXml = substituteParameters(fetchXml, substitutionParams, chartDefinition.sprk_fetchxmlparams);
    }

    return { source: "pcfOverride", fetchXml, entityName };
  }

  // Priority 2: Custom FetchXML on chart definition
  if (chartDefinition.sprk_fetchxmlquery?.trim()) {
    logger.info("ViewDataService", "Using custom FetchXML from chart definition", { entityName });
    let fetchXml = chartDefinition.sprk_fetchxmlquery;

    if (substitutionParams) {
      fetchXml = substituteParameters(fetchXml, substitutionParams, chartDefinition.sprk_fetchxmlparams);
    }

    return { source: "customFetchXml", fetchXml, entityName };
  }

  // Priority 3: Saved view
  if (chartDefinition.sprk_baseviewid?.trim()) {
    logger.info("ViewDataService", "Using saved view", {
      viewId: chartDefinition.sprk_baseviewid,
      entityName,
    });

    const { fetchXml: viewFetchXml, entityName: viewEntity } = await getViewFetchXml(
      webApi,
      chartDefinition.sprk_baseviewid
    );

    let fetchXml = viewFetchXml;
    if (substitutionParams) {
      fetchXml = substituteParameters(fetchXml, substitutionParams, chartDefinition.sprk_fetchxmlparams);
    }

    return {
      source: "view",
      fetchXml,
      entityName: viewEntity || entityName,
    };
  }

  // Priority 4: Direct entity query (no FetchXML, use OData)
  logger.info("ViewDataService", "Using direct entity query (no FetchXML source)", { entityName });
  return { source: "directEntity", entityName };
}

/**
 * Calculate days until a due date from today
 */
function calculateDaysUntilDue(dueDate: Date): { daysUntilDue: number; isOverdue: boolean } {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const due = new Date(dueDate);
  due.setHours(0, 0, 0, 0);
  const diffMs = due.getTime() - today.getTime();
  const daysUntilDue = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
  return { daysUntilDue, isOverdue: daysUntilDue < 0 };
}

/**
 * Map a Dataverse entity record to IEventRecord
 */
function mapRecordToEvent(record: Record<string, unknown>): IEventRecord {
  const dueDate = record.sprk_duedate
    ? new Date(record.sprk_duedate as string)
    : new Date();
  const { daysUntilDue, isOverdue } = calculateDaysUntilDue(dueDate);

  // Event type from expanded navigation property or formatted value
  const eventTypeName =
    (record["_sprk_eventtypeid_value@OData.Community.Display.V1.FormattedValue"] as string) ||
    "Event";

  // Event type color from expanded lookup (if available)
  const eventTypeColor = record["sprk_eventtype_ref.sprk_eventtypecolor"] as string | undefined;

  return {
    eventId: (record.sprk_eventid as string) || (record[`${getEntityPrimaryKey(record)}` ] as string) || "",
    eventName: (record.sprk_eventname as string) || (record.sprk_name as string) || "Untitled Event",
    eventTypeName,
    dueDate,
    daysUntilDue,
    isOverdue,
    eventTypeColor: eventTypeColor || undefined,
    description: record.sprk_description as string | undefined,
    assignedTo:
      (record["_sprk_assignedtoid_value@OData.Community.Display.V1.FormattedValue"] as string) ||
      undefined,
  };
}

/**
 * Attempt to find the primary key field from a record
 */
function getEntityPrimaryKey(record: Record<string, unknown>): string {
  // Look for common patterns: sprk_eventid, contactid, accountid, etc.
  for (const key of Object.keys(record)) {
    if (key.endsWith("id") && !key.startsWith("_") && !key.includes("@")) {
      return key;
    }
  }
  return "id";
}

/**
 * Execute a view-based query and return mapped event records.
 *
 * Flow:
 * 1. Retrieve the saved view's FetchXML from Dataverse
 * 2. Inject context filter if provided
 * 3. Apply max items limit
 * 4. Execute the FetchXML query via WebAPI
 * 5. Map results to IEventRecord array
 *
 * @param webApi - WebAPI interface for Dataverse access
 * @param viewContext - View query parameters
 * @returns Array of mapped event records
 */
export async function fetchEventsFromView(
  webApi: IConfigWebApi,
  viewContext: IViewDataContext
): Promise<IEventRecord[]> {
  const { viewId, contextFilter, maxItems } = viewContext;

  logger.info("ViewDataService", "Fetching events from view", {
    viewId,
    contextFilter,
    maxItems,
  });

  // Step 1: Get the view's FetchXML
  const { fetchXml: rawFetchXml, entityName } = await getViewFetchXml(webApi, viewId);

  // Step 1.5: Apply parameter substitution if params provided
  let fetchXml = viewContext.substitutionParams
    ? substituteParameters(rawFetchXml, viewContext.substitutionParams, viewContext.paramMappings)
    : rawFetchXml;

  // Step 2: Inject context filter if provided
  if (contextFilter?.fieldName && contextFilter?.recordId) {
    fetchXml = injectContextFilter(fetchXml, contextFilter.fieldName, contextFilter.recordId);
    logger.debug("ViewDataService", "Injected context filter", {
      fieldName: contextFilter.fieldName,
    });
  }

  // Step 3: Apply max items limit
  if (maxItems && maxItems > 0) {
    fetchXml = applyMaxItems(fetchXml, maxItems);
  }

  // Step 4: Execute FetchXML query via WebAPI
  try {
    const encodedFetchXml = encodeURIComponent(fetchXml);
    const queryOptions = `?fetchXml=${encodedFetchXml}`;

    const result = await webApi.retrieveMultipleRecords(entityName, queryOptions);

    logger.info("ViewDataService", `View query returned ${result.entities.length} records`, {
      entityName,
      viewId,
    });

    // Step 5: Map to event records
    return result.entities.map(mapRecordToEvent);
  } catch (error) {
    const msg = error instanceof Error ? error.message : String(error);
    logger.error("ViewDataService", "Failed to execute view query", error);
    throw new ViewDataError(`Failed to execute view query: ${msg}`, error);
  }
}

/**
 * Fetch events using a chart definition's view configuration.
 * Convenience wrapper that extracts view parameters from chart definition.
 *
 * @param webApi - WebAPI interface
 * @param chartDefinition - Chart definition containing view configuration
 * @param contextRecordId - Optional current record ID for context filtering
 * @returns Array of mapped event records
 */
export async function fetchEventsFromChartDefinition(
  webApi: IConfigWebApi,
  chartDefinition: IChartDefinition,
  contextRecordId?: string
): Promise<IEventRecord[]> {
  const viewId = chartDefinition.sprk_baseviewid;

  if (!viewId) {
    logger.warn("ViewDataService", "No view ID configured, falling back to direct query");
    return [];
  }

  const viewContext: IViewDataContext = {
    viewId,
    maxItems: chartDefinition.sprk_maxdisplayitems || 10,
  };

  // Add context filter if configured
  if (chartDefinition.sprk_contextfieldname && contextRecordId) {
    viewContext.contextFilter = {
      fieldName: chartDefinition.sprk_contextfieldname,
      recordId: contextRecordId,
    };
  }

  return fetchEventsFromView(webApi, viewContext);
}
