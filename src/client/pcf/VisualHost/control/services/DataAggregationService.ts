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
 * Options for data aggregation
 */
export interface IAggregationOptions {
  /** Maximum records to fetch (for pagination) */
  maxRecords?: number;
  /** Skip cache and fetch fresh data */
  skipCache?: boolean;
  /** Custom page size for fetching */
  pageSize?: number;
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
 * Default page size for fetching records
 */
const DEFAULT_PAGE_SIZE = 5000;

/**
 * Maximum records to fetch by default
 */
const DEFAULT_MAX_RECORDS = 50000;

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
  groupByField: string | undefined
): string {
  return `${entityName}:${viewId || "all"}:${aggregationType}:${aggregationField || ""}:${groupByField || ""}`;
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
 * Fetch all records from a Dataverse entity/view with pagination
 */
export async function fetchRecords(
  context: IAggregationContext,
  entityName: string,
  options?: {
    viewId?: string;
    selectColumns?: string[];
    filter?: string;
    maxRecords?: number;
    pageSize?: number;
  }
): Promise<Array<Record<string, unknown>>> {
  const maxRecords = options?.maxRecords ?? DEFAULT_MAX_RECORDS;
  const pageSize = options?.pageSize ?? DEFAULT_PAGE_SIZE;
  const allRecords: Array<Record<string, unknown>> = [];

  // Build query options
  let queryOptions = "";

  if (options?.selectColumns && options.selectColumns.length > 0) {
    queryOptions += `?$select=${options.selectColumns.join(",")}`;
  }

  if (options?.filter) {
    queryOptions += queryOptions ? "&" : "?";
    queryOptions += `$filter=${encodeURIComponent(options.filter)}`;
  }

  // Add page size
  queryOptions += queryOptions ? "&" : "?";
  queryOptions += `$top=${Math.min(pageSize, maxRecords)}`;

  logger.debug("DataAggregationService", `Fetching records from ${entityName}`, {
    queryOptions,
    maxRecords,
  });

  try {
    let result = await context.webAPI.retrieveMultipleRecords(
      entityName,
      queryOptions,
      pageSize
    );

    allRecords.push(...result.entities);

    // Fetch additional pages if needed
    while (result.nextLink && allRecords.length < maxRecords) {
      const remainingRecords = maxRecords - allRecords.length;
      const nextPageOptions = `${queryOptions}&$top=${Math.min(pageSize, remainingRecords)}`;

      result = await context.webAPI.retrieveMultipleRecords(
        entityName,
        nextPageOptions,
        pageSize
      );

      allRecords.push(...result.entities);
      logger.debug(
        "DataAggregationService",
        `Fetched page, total records: ${allRecords.length}`
      );
    }

    logger.info(
      "DataAggregationService",
      `Fetched ${allRecords.length} records from ${entityName}`
    );
    return allRecords;
  } catch (error: unknown) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    logger.error("DataAggregationService", `Failed to fetch records: ${errorMessage}`, error);
    throw new AggregationError(`Failed to fetch records: ${errorMessage}`, error);
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
  const groups = new Map<string, Array<Record<string, unknown>>>();

  for (const record of records) {
    const groupValue = record[groupByField];
    const groupKey = formatGroupKey(groupValue);

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

  // Sort by value descending (most common pattern for charts)
  dataPoints.sort((a, b) => b.value - a.value);

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
    groupByField
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
    aggregationType,
    aggregationField,
    groupByField,
  });

  // Determine which columns to fetch
  const selectColumns: string[] = [];
  if (groupByField) {
    selectColumns.push(groupByField);
  }
  if (aggregationField && aggregationType !== AggregationType.Count) {
    selectColumns.push(aggregationField);
  }

  // Fetch records
  const records = await fetchRecords(context, entityName, {
    viewId,
    selectColumns: selectColumns.length > 0 ? selectColumns : undefined,
    maxRecords: options?.maxRecords,
    pageSize: options?.pageSize,
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
