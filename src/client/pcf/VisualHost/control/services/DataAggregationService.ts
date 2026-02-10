/**
 * Data Aggregation Service
 * Fetches data from Dataverse views and performs client-side aggregation
 * for chart visualization.
 *
 * Task 022 - Visualization Module
 */

import type {
  IChartData,
  IAggregatedDataPoint,
  IChartDefinition,
} from "../types";
import { AggregationType } from "../types";
import { logger } from "../utils/logger";
import { getViewFetchXml, injectContextFilter, injectRequiredAttributes } from "./ViewDataService";
import type { IConfigWebApi } from "./ConfigurationLoader";

/**
 * WebAPI interface for data aggregation
 * Abstraction layer to enable testing without ComponentFramework
 */
export interface IAggregationWebApi {
  retrieveMultipleRecords(
    entityType: string,
    options?: string,
    maxPageSize?: number
  ): Promise<{
    entities: Array<Record<string, unknown>>;
    nextLink?: string;
  }>;
}

/**
 * Context interface for data aggregation
 * Abstraction layer to enable testing without ComponentFramework
 */
export interface IAggregationContext {
  webAPI: IAggregationWebApi;
}

/**
 * Context filter for related record filtering (v1.1.0)
 */
export interface IContextFilter {
  /** Field name to filter on (e.g., _sprk_matterid_value) */
  fieldName: string;
  /** Record ID to filter by (current record's entity ID) */
  recordId: string;
}

/**
 * Options for data aggregation
 */
export interface IAggregationOptions {
  /** Maximum records to fetch (for pagination) */
  maxRecords?: number;
  /** Skip cache and fetch fresh data */
  skipCache?: boolean;
  /** Custom page size for fetching */
  pageSize?: number;
  /** Context filter for related records (v1.1.0) */
  contextFilter?: IContextFilter;
}

/**
 * Aggregation error types
 */
export class AggregationError extends Error {
  constructor(message: string, public readonly cause?: unknown) {
    super(message);
    this.name = "AggregationError";
  }
}

/**
 * Cache entry for aggregated data
 */
interface ICacheEntry {
  data: IChartData;
  timestamp: number;
}

/**
 * Cache TTL in milliseconds (2 minutes - shorter than config since data changes more often)
 */
const CACHE_TTL_MS = 2 * 60 * 1000;

/**
 * Maximum records to fetch by default.
 * Dataverse FetchXML 'top' attribute limit: 0–12000 inclusive.
 */
const DEFAULT_MAX_RECORDS = 5000;

/**
 * In-memory cache for aggregated data
 */
const cache = new Map<string, ICacheEntry>();

/**
 * Check if cache entry is still valid
 */
function isCacheValid(entry: ICacheEntry): boolean {
  return Date.now() - entry.timestamp < CACHE_TTL_MS;
}

/**
 * Generate cache key for aggregation request
 */
function getCacheKey(
  entityName: string,
  viewId: string | undefined,
  aggregationType: AggregationType,
  aggregationField: string | undefined,
  groupByField: string | undefined,
  contextFilter?: IContextFilter
): string {
  const contextPart = contextFilter
    ? `:${contextFilter.fieldName}=${contextFilter.recordId}`
    : "";
  return `${entityName}:${viewId || "all"}:${aggregationType}:${aggregationField || ""}:${groupByField || ""}${contextPart}`;
}

/**
 * Clear cache entries
 */
export function clearAggregationCache(cacheKey?: string): void {
  if (cacheKey) {
    cache.delete(cacheKey);
    logger.debug("DataAggregationService", `Cache cleared for ${cacheKey}`);
  } else {
    cache.clear();
    logger.debug("DataAggregationService", "Cache cleared entirely");
  }
}

/**
 * Extract a readable error message from Dataverse WebAPI error objects.
 * PCF WebAPI errors are plain objects with { errorCode, message }, not Error instances.
 */
function extractErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (error && typeof error === "object") {
    const obj = error as Record<string, unknown>;
    if (typeof obj.message === "string") return obj.message;
    try { return JSON.stringify(error); } catch { /* ignore */ }
  }
  return String(error);
}

/**
 * Fetch all records from a Dataverse entity/view using FetchXML.
 * When a viewId is provided, fetches the view's FetchXML and executes it
 * (with optional context filter injection). Falls back to basic FetchXML when no view.
 */
export async function fetchRecords(
  context: IAggregationContext,
  entityName: string,
  options?: {
    viewId?: string;
    selectColumns?: string[];
    contextFilter?: { fieldName: string; recordId: string };
    maxRecords?: number;
  }
): Promise<Array<Record<string, unknown>>> {
  const maxRecords = options?.maxRecords ?? DEFAULT_MAX_RECORDS;

  // When a saved view is provided, use its FetchXML (applies view filters)
  if (options?.viewId) {
    logger.info("DataAggregationService", `Fetch path: VIEW (${options.viewId})`);
    return fetchRecordsFromView(context, entityName, options.viewId, {
      contextFilter: options.contextFilter,
      maxRecords,
      requiredColumns: options.selectColumns,
    });
  }

  // Fallback: basic FetchXML query (no view filters)
  logger.info("DataAggregationService", `Fetch path: BASIC (${entityName}, no view)`);
  return fetchRecordsBasic(context, entityName, {
    selectColumns: options?.selectColumns,
    contextFilter: options?.contextFilter,
    maxRecords,
  });
}

/**
 * Fetch records using a basic FetchXML query (no saved view).
 * Builds FetchXML from entity name, optional column selection, and context filter.
 */
async function fetchRecordsBasic(
  context: IAggregationContext,
  entityName: string,
  options?: {
    selectColumns?: string[];
    contextFilter?: { fieldName: string; recordId: string };
    maxRecords?: number;
  }
): Promise<Array<Record<string, unknown>>> {
  const maxRecords = options?.maxRecords ?? DEFAULT_MAX_RECORDS;

  // Build attribute elements
  let attributesXml: string;
  if (options?.selectColumns && options.selectColumns.length > 0) {
    attributesXml = options.selectColumns
      .map((col) => `<attribute name="${col}" />`)
      .join("");
  } else {
    attributesXml = "<all-attributes />";
  }

  // Build context filter (transform _field_value → field for FetchXML)
  let filterXml = "";
  if (options?.contextFilter) {
    const filterField = options.contextFilter.fieldName
      .replace(/^_/, "")
      .replace(/_value$/, "");
    const cleanId = options.contextFilter.recordId.replace(/[{}]/g, "");
    filterXml = `<filter type="and"><condition attribute="${filterField}" operator="eq" value="${cleanId}" /></filter>`;
    logger.info("DataAggregationService", `Context filter: ${options.contextFilter.fieldName} → ${filterField} = ${cleanId}`);
  }

  const fetchXml = `<fetch top="${maxRecords}"><entity name="${entityName}">${attributesXml}${filterXml}</entity></fetch>`;

  logger.debug("DataAggregationService", `FetchXML (basic): ${fetchXml.substring(0, 500)}`);

  try {
    const encodedFetchXml = encodeURIComponent(fetchXml);
    const result = await context.webAPI.retrieveMultipleRecords(
      entityName,
      `?fetchXml=${encodedFetchXml}`
    );

    logger.info(
      "DataAggregationService",
      `Fetched ${result.entities.length} records from ${entityName}`
    );
    return result.entities;
  } catch (error: unknown) {
    const errorMessage = extractErrorMessage(error);
    logger.error("DataAggregationService", `Failed to fetch records (basic): ${errorMessage}`, error);
    throw new AggregationError(`Failed to fetch records: ${errorMessage}`, error);
  }
}

/**
 * Fetch records using a saved view's FetchXML with optional context filter injection.
 * Retrieves the view definition, injects context filter if needed, and executes.
 */
async function fetchRecordsFromView(
  context: IAggregationContext,
  entityName: string,
  viewId: string,
  options?: {
    contextFilter?: { fieldName: string; recordId: string };
    maxRecords?: number;
    /** WebAPI property names required for chart aggregation (groupByField, aggregationField) */
    requiredColumns?: string[];
  }
): Promise<Array<Record<string, unknown>>> {
  const webApi = context.webAPI as IConfigWebApi;

  try {
    // Retrieve the saved view's FetchXML
    const { fetchXml: viewFetchXml, entityName: viewEntity } =
      await getViewFetchXml(webApi, viewId);

    const resolvedEntity = viewEntity || entityName;
    let fetchXml = viewFetchXml;

    // DIAGNOSTIC: Log the raw view FetchXML BEFORE any injection
    logger.info("DataAggregationService", `[DIAG] Raw view FetchXML (before injection):\n${viewFetchXml}`);

    // Inject required attributes for chart aggregation (groupByField, aggregationField)
    // This ensures the view returns these columns even if they're not in the view's column set
    if (options?.requiredColumns && options.requiredColumns.length > 0) {
      fetchXml = injectRequiredAttributes(fetchXml, options.requiredColumns);
    }

    // Inject context filter if provided
    if (options?.contextFilter) {
      const filterField = options.contextFilter.fieldName
        .replace(/^_/, "")
        .replace(/_value$/, "");
      logger.info("DataAggregationService", `[DIAG] Context filter: fieldName="${options.contextFilter.fieldName}" → filterField="${filterField}", recordId="${options.contextFilter.recordId}"`);
      fetchXml = injectContextFilter(fetchXml, filterField, options.contextFilter.recordId);
    } else {
      logger.info("DataAggregationService", `[DIAG] No context filter provided`);
    }

    // DIAGNOSTIC: Log the FINAL FetchXML that will be executed
    logger.info("DataAggregationService", `[DIAG] Final FetchXML (after injection):\n${fetchXml}`);

    const encodedFetchXml = encodeURIComponent(fetchXml);
    const result = await context.webAPI.retrieveMultipleRecords(
      resolvedEntity,
      `?fetchXml=${encodedFetchXml}`
    );

    logger.info(
      "DataAggregationService",
      `Fetched ${result.entities.length} records from view ${viewId}`
    );

    return result.entities;
  } catch (error: unknown) {
    const errorMessage = extractErrorMessage(error);
    logger.error("DataAggregationService", `Failed to fetch from view: ${errorMessage}`, error);
    throw new AggregationError(`Failed to fetch from view: ${errorMessage}`, error);
  }
}

/**
 * Perform aggregation on a set of records
 */
export function aggregateRecords(
  records: Array<Record<string, unknown>>,
  aggregationType: AggregationType,
  aggregationField?: string,
  groupByField?: string
): IAggregatedDataPoint[] {
  // If no group by field, return single aggregated value
  if (!groupByField) {
    const value = calculateAggregation(records, aggregationType, aggregationField);
    return [
      {
        label: "Total",
        value,
        fieldValue: null,
      },
    ];
  }

  // Group records by the groupByField
  // For lookup/optionset fields, prefer the @OData.Community.Display.V1.FormattedValue
  // annotation which provides human-readable labels (e.g., "Task" instead of a GUID)
  const formattedValueKey = `${groupByField}@OData.Community.Display.V1.FormattedValue`;
  const groups = new Map<string, Array<Record<string, unknown>>>();

  // DIAGNOSTIC: Log first record's groupByField values
  if (records.length > 0) {
    const first = records[0];
    const rawVal = first[groupByField];
    const fmtVal = first[formattedValueKey];
    logger.info("DataAggregationService", `[DIAG] groupByField="${groupByField}" → raw=${rawVal === undefined ? "(undefined)" : JSON.stringify(rawVal)}, formatted=${fmtVal === undefined ? "(undefined)" : JSON.stringify(fmtVal)}`);
  }

  for (const record of records) {
    const formattedValue = record[formattedValueKey] as string | undefined;
    const rawValue = record[groupByField];
    const groupKey = formattedValue || formatGroupKey(rawValue);

    if (!groups.has(groupKey)) {
      groups.set(groupKey, []);
    }
    groups.get(groupKey)!.push(record);
  }

  // Calculate aggregation for each group
  const dataPoints: IAggregatedDataPoint[] = [];

  for (const [groupKey, groupRecords] of groups) {
    const value = calculateAggregation(groupRecords, aggregationType, aggregationField);
    const firstRecord = groupRecords[0];
    const fieldValue = firstRecord?.[groupByField];

    dataPoints.push({
      label: groupKey,
      value,
      fieldValue,
    });
  }

  // Sort alphabetically by label (A→Z, left-to-right on bar charts)
  dataPoints.sort((a, b) => a.label.localeCompare(b.label));

  logger.debug(
    "DataAggregationService",
    `Aggregated ${records.length} records into ${dataPoints.length} groups`
  );

  return dataPoints;
}

/**
 * Format a group value into a display string
 */
function formatGroupKey(value: unknown): string {
  if (value === null || value === undefined) {
    return "(Blank)";
  }

  if (typeof value === "boolean") {
    return value ? "Yes" : "No";
  }

  if (typeof value === "object") {
    // Handle Dataverse lookup/optionset formatted values
    if ("name" in value && typeof (value as Record<string, unknown>).name === "string") {
      return (value as Record<string, unknown>).name as string;
    }
    return String(value);
  }

  return String(value);
}

/**
 * Calculate aggregation value for a set of records
 */
function calculateAggregation(
  records: Array<Record<string, unknown>>,
  aggregationType: AggregationType,
  aggregationField?: string
): number {
  if (records.length === 0) {
    return 0;
  }

  switch (aggregationType) {
    case AggregationType.Count:
      return records.length;

    case AggregationType.Sum:
      return calculateSum(records, aggregationField);

    case AggregationType.Average:
      return calculateAverage(records, aggregationField);

    case AggregationType.Min:
      return calculateMin(records, aggregationField);

    case AggregationType.Max:
      return calculateMax(records, aggregationField);

    default:
      logger.warn("DataAggregationService", `Unknown aggregation type: ${aggregationType}`);
      return records.length;
  }
}

/**
 * Extract numeric values from records
 */
function extractNumericValues(
  records: Array<Record<string, unknown>>,
  field?: string
): number[] {
  if (!field) {
    logger.warn("DataAggregationService", "No aggregation field specified for numeric aggregation");
    return [];
  }

  const values: number[] = [];

  for (const record of records) {
    const rawValue = record[field];

    if (rawValue === null || rawValue === undefined) {
      continue;
    }

    const numValue = typeof rawValue === "number" ? rawValue : parseFloat(String(rawValue));

    if (!isNaN(numValue)) {
      values.push(numValue);
    }
  }

  return values;
}

/**
 * Calculate sum of field values
 */
function calculateSum(
  records: Array<Record<string, unknown>>,
  field?: string
): number {
  const values = extractNumericValues(records, field);
  return values.reduce((sum, val) => sum + val, 0);
}

/**
 * Calculate average of field values
 */
function calculateAverage(
  records: Array<Record<string, unknown>>,
  field?: string
): number {
  const values = extractNumericValues(records, field);
  if (values.length === 0) return 0;
  return values.reduce((sum, val) => sum + val, 0) / values.length;
}

/**
 * Calculate minimum of field values
 */
function calculateMin(
  records: Array<Record<string, unknown>>,
  field?: string
): number {
  const values = extractNumericValues(records, field);
  if (values.length === 0) return 0;
  return Math.min(...values);
}

/**
 * Calculate maximum of field values
 */
function calculateMax(
  records: Array<Record<string, unknown>>,
  field?: string
): number {
  const values = extractNumericValues(records, field);
  if (values.length === 0) return 0;
  return Math.max(...values);
}

/**
 * Fetch and aggregate data based on chart definition
 *
 * @param context - PCF context with webAPI access
 * @param definition - Chart definition with aggregation settings
 * @param options - Optional aggregation options
 * @returns Promise resolving to IChartData
 */
export async function fetchAndAggregate(
  context: IAggregationContext,
  definition: IChartDefinition,
  options?: IAggregationOptions
): Promise<IChartData> {
  const entityName = definition.sprk_entitylogicalname;
  const viewId = definition.sprk_baseviewid;
  const aggregationType = definition.sprk_aggregationtype ?? AggregationType.Count;
  const aggregationField = definition.sprk_aggregationfield;
  const groupByField = definition.sprk_groupbyfield;

  if (!entityName) {
    throw new AggregationError("Entity name is required for data aggregation");
  }

  // Check cache
  const cacheKey = getCacheKey(
    entityName,
    viewId,
    aggregationType,
    aggregationField,
    groupByField,
    options?.contextFilter
  );

  if (!options?.skipCache) {
    const cached = cache.get(cacheKey);
    if (cached && isCacheValid(cached)) {
      logger.debug("DataAggregationService", `Cache hit for ${cacheKey}`);
      return cached.data;
    }
  }

  logger.info("DataAggregationService", `Aggregating data for ${definition.sprk_name}`, {
    entityName,
    viewId: viewId || "(none)",
    aggregationType,
    aggregationField,
    groupByField,
    contextFilter: options?.contextFilter,
  });

  // Determine which columns to fetch
  const selectColumns: string[] = [];
  if (groupByField) {
    selectColumns.push(groupByField);
  }
  if (aggregationField && aggregationType !== AggregationType.Count) {
    selectColumns.push(aggregationField);
  }

  // Fetch records: use view FetchXML when viewId available, basic FetchXML otherwise
  const records = await fetchRecords(context, entityName, {
    viewId,
    selectColumns: selectColumns.length > 0 ? selectColumns : undefined,
    contextFilter: options?.contextFilter,
    maxRecords: options?.maxRecords,
  });

  // Perform aggregation
  const dataPoints = aggregateRecords(
    records,
    aggregationType,
    aggregationField,
    groupByField
  );

  const chartData: IChartData = {
    dataPoints,
    totalRecords: records.length,
    aggregationType,
    aggregationField,
    groupByField,
  };

  // Store in cache
  cache.set(cacheKey, {
    data: chartData,
    timestamp: Date.now(),
  });

  logger.info(
    "DataAggregationService",
    `Aggregation complete: ${dataPoints.length} data points from ${records.length} records`
  );

  return chartData;
}

/**
 * Aggregate raw data without fetching (for use with pre-loaded data)
 */
export function aggregateData(
  records: Array<Record<string, unknown>>,
  aggregationType: AggregationType,
  aggregationField?: string,
  groupByField?: string
): IChartData {
  const dataPoints = aggregateRecords(
    records,
    aggregationType,
    aggregationField,
    groupByField
  );

  return {
    dataPoints,
    totalRecords: records.length,
    aggregationType,
    aggregationField,
    groupByField,
  };
}
