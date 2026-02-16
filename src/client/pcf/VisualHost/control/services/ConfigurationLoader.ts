/**
 * Configuration Loader Service
 * Loads sprk_chartdefinition records from Dataverse WebAPI
 * and returns strongly-typed ChartDefinition objects.
 *
 * Task 021 - Visualization Module
 */

import type { IChartDefinition } from "../types";
import { VisualType, AggregationType, OnClickAction, ValueFormat, ColorSource } from "../types";
import { logger } from "../utils/logger";

/**
 * WebAPI interface for retrieving Dataverse records
 * Abstraction layer to enable testing without ComponentFramework
 */
export interface IConfigWebApi {
  retrieveRecord(
    entityType: string,
    id: string,
    options?: string
  ): Promise<Record<string, unknown>>;
  retrieveMultipleRecords(
    entityType: string,
    options?: string
  ): Promise<{ entities: Array<Record<string, unknown>> }>;
}

/**
 * Context interface for configuration loading
 * Abstraction layer to enable testing without ComponentFramework
 */
export interface IConfigContext {
  webAPI: IConfigWebApi;
}

/**
 * Configuration loading error types
 */
export class ConfigurationNotFoundError extends Error {
  constructor(id: string) {
    super(`Chart definition not found: ${id}`);
    this.name = "ConfigurationNotFoundError";
  }
}

export class ConfigurationLoadError extends Error {
  constructor(message: string, public readonly cause?: unknown) {
    super(message);
    this.name = "ConfigurationLoadError";
  }
}

/**
 * Entity field names for sprk_chartdefinition
 */
const ENTITY_NAME = "sprk_chartdefinition";
const ENTITY_SET_NAME = "sprk_chartdefinitions";

const FIELDS = {
  id: "sprk_chartdefinitionid",
  name: "sprk_name",
  visualType: "sprk_visualtype",
  entityLogicalName: "sprk_entitylogicalname",
  baseViewId: "sprk_baseviewid",
  aggregationField: "sprk_aggregationfield",
  aggregationType: "sprk_aggregationtype",
  groupByField: "sprk_groupbyfield",
  optionsJson: "sprk_optionsjson",
  // Click action fields
  onClickAction: "sprk_onclickaction",
  onClickTarget: "sprk_onclicktarget",
  onClickRecordField: "sprk_onclickrecordfield",
  // Card list configuration fields
  contextFieldName: "sprk_contextfieldname",
  viewListTabName: "sprk_viewlisttabname",
  maxDisplayItems: "sprk_maxdisplayitems",
  // Drill-through configuration
  drillThroughTarget: "sprk_drillthroughtarget",
  // FetchXML fields
  fetchXmlQuery: "sprk_fetchxmlquery",
  fetchXmlParams: "sprk_fetchxmlparams",
  // MetricCard configuration fields (v1.2.33)
  valueFormat: "sprk_valueformat",
  colorSource: "sprk_colorsource",
  // Card shape (v1.2.44)
  metricCardShape: "sprk_metriccardshape",
} as const;

/**
 * Select columns for WebAPI query
 */
const SELECT_COLUMNS = [
  FIELDS.id,
  FIELDS.name,
  FIELDS.visualType,
  FIELDS.entityLogicalName,
  FIELDS.baseViewId,
  FIELDS.aggregationField,
  FIELDS.aggregationType,
  FIELDS.groupByField,
  FIELDS.optionsJson,
  FIELDS.onClickAction,
  FIELDS.onClickTarget,
  FIELDS.onClickRecordField,
  FIELDS.contextFieldName,
  FIELDS.viewListTabName,
  FIELDS.maxDisplayItems,
  FIELDS.drillThroughTarget,
  FIELDS.fetchXmlQuery,
  FIELDS.fetchXmlParams,
  FIELDS.valueFormat,
  FIELDS.colorSource,
  FIELDS.metricCardShape,
].join(",");

/**
 * Cache entry for chart definitions
 */
interface ICacheEntry {
  definition: IChartDefinition;
  timestamp: number;
}

/**
 * Cache TTL in milliseconds (5 minutes)
 */
const CACHE_TTL_MS = 5 * 60 * 1000;

/**
 * In-memory cache for loaded configurations
 */
const cache = new Map<string, ICacheEntry>();

/**
 * Check if cache entry is still valid
 */
function isCacheValid(entry: ICacheEntry): boolean {
  return Date.now() - entry.timestamp < CACHE_TTL_MS;
}

/**
 * Clear a specific cache entry
 */
export function clearCache(id?: string): void {
  if (id) {
    cache.delete(id);
    logger.debug("ConfigurationLoader", `Cache cleared for ${id}`);
  } else {
    cache.clear();
    logger.debug("ConfigurationLoader", "Cache cleared entirely");
  }
}

/**
 * Parse optionsJson field safely
 * Returns empty object on parse failure
 */
function parseOptionsJson(jsonString?: string): Record<string, unknown> {
  if (!jsonString || jsonString.trim() === "") {
    return {};
  }

  try {
    const parsed = JSON.parse(jsonString);
    if (typeof parsed !== "object" || parsed === null) {
      logger.warn("ConfigurationLoader", "optionsJson is not an object", {
        jsonString,
      });
      return {};
    }
    return parsed as Record<string, unknown>;
  } catch (error) {
    logger.warn("ConfigurationLoader", "Failed to parse optionsJson", {
      jsonString,
      error,
    });
    return {};
  }
}

/**
 * Validate and convert visual type value
 */
function parseVisualType(value: unknown): VisualType {
  const numValue = typeof value === "number" ? value : parseInt(String(value), 10);

  if (isNaN(numValue)) {
    logger.warn("ConfigurationLoader", "Invalid visual type, defaulting to MetricCard", {
      value,
    });
    return VisualType.MetricCard;
  }

  // Validate it's a known VisualType
  if (Object.values(VisualType).includes(numValue)) {
    return numValue as VisualType;
  }

  logger.warn("ConfigurationLoader", "Unknown visual type, defaulting to MetricCard", {
    value: numValue,
  });
  return VisualType.MetricCard;
}

/**
 * Validate and convert aggregation type value
 */
function parseAggregationType(value: unknown): AggregationType | undefined {
  if (value === null || value === undefined) {
    return undefined;
  }

  const numValue = typeof value === "number" ? value : parseInt(String(value), 10);

  if (isNaN(numValue)) {
    return undefined;
  }

  // Validate it's a known AggregationType
  if (Object.values(AggregationType).includes(numValue)) {
    return numValue as AggregationType;
  }

  logger.warn("ConfigurationLoader", "Unknown aggregation type", { value: numValue });
  return undefined;
}

/**
 * Validate and convert click action value
 */
function parseOnClickAction(value: unknown): OnClickAction | undefined {
  if (value === null || value === undefined) {
    return undefined;
  }

  const numValue = typeof value === "number" ? value : parseInt(String(value), 10);

  if (isNaN(numValue)) {
    return undefined;
  }

  if (Object.values(OnClickAction).includes(numValue)) {
    return numValue as OnClickAction;
  }

  logger.warn("ConfigurationLoader", "Unknown click action type", { value: numValue });
  return undefined;
}

/**
 * Validate and convert value format option set value
 */
function parseValueFormat(value: unknown): ValueFormat | undefined {
  if (value === null || value === undefined) {
    return undefined;
  }
  const numValue = typeof value === "number" ? value : parseInt(String(value), 10);
  if (isNaN(numValue)) {
    return undefined;
  }
  if (Object.values(ValueFormat).includes(numValue)) {
    return numValue as ValueFormat;
  }
  logger.warn("ConfigurationLoader", "Unknown value format", { value: numValue });
  return undefined;
}

/**
 * Validate and convert color source option set value
 */
function parseColorSource(value: unknown): ColorSource | undefined {
  if (value === null || value === undefined) {
    return undefined;
  }
  const numValue = typeof value === "number" ? value : parseInt(String(value), 10);
  if (isNaN(numValue)) {
    return undefined;
  }
  if (Object.values(ColorSource).includes(numValue)) {
    return numValue as ColorSource;
  }
  logger.warn("ConfigurationLoader", "Unknown color source", { value: numValue });
  return undefined;
}

/**
 * Map Dataverse record to IChartDefinition
 */
function mapToChartDefinition(
  record: Record<string, unknown>
): IChartDefinition {
  const definition: IChartDefinition = {
    sprk_chartdefinitionid: record[FIELDS.id] as string,
    sprk_name: (record[FIELDS.name] as string) || "Untitled Chart",
    sprk_visualtype: parseVisualType(record[FIELDS.visualType]),
    sprk_entitylogicalname: record[FIELDS.entityLogicalName] as string | undefined,
    sprk_baseviewid: (record["_sprk_baseviewid_value"] as string) || (record[FIELDS.baseViewId] as string) || undefined,
    sprk_aggregationfield: record[FIELDS.aggregationField] as string | undefined,
    sprk_aggregationtype: parseAggregationType(record[FIELDS.aggregationType]),
    sprk_groupbyfield: record[FIELDS.groupByField] as string | undefined,
    sprk_optionsjson: record[FIELDS.optionsJson] as string | undefined,
    // Map optionsJson to configurationjson as well — single Dataverse column
    // serves both chart rendering options and advanced configuration (field pivot, card config)
    sprk_configurationjson: record[FIELDS.optionsJson] as string | undefined,
    // Click action fields
    sprk_onclickaction: parseOnClickAction(record[FIELDS.onClickAction]),
    sprk_onclicktarget: record[FIELDS.onClickTarget] as string | undefined,
    sprk_onclickrecordfield: record[FIELDS.onClickRecordField] as string | undefined,
    // Card list configuration fields
    sprk_contextfieldname: record[FIELDS.contextFieldName] as string | undefined,
    sprk_viewlisttabname: record[FIELDS.viewListTabName] as string | undefined,
    sprk_maxdisplayitems: record[FIELDS.maxDisplayItems] as number | undefined,
    // Drill-through configuration
    sprk_drillthroughtarget: record[FIELDS.drillThroughTarget] as string | undefined,
    // FetchXML fields
    sprk_fetchxmlquery: record[FIELDS.fetchXmlQuery] as string | undefined,
    sprk_fetchxmlparams: record[FIELDS.fetchXmlParams] as string | undefined,
    // MetricCard configuration fields (v1.2.33)
    sprk_valueformat: parseValueFormat(record[FIELDS.valueFormat]),
    sprk_colorsource: parseColorSource(record[FIELDS.colorSource]),
    // Card shape (v1.2.44)
    sprk_metriccardshape: record[FIELDS.metricCardShape] as number | undefined,
  };

  // Validate optionsJson is valid JSON (for early warning)
  if (definition.sprk_optionsjson) {
    parseOptionsJson(definition.sprk_optionsjson);
  }

  // Diagnostic: log view ID resolution
  logger.info("ConfigurationLoader", `View ID resolution: _sprk_baseviewid_value=${record["_sprk_baseviewid_value"] || "(empty)"}, sprk_baseviewid=${record[FIELDS.baseViewId] || "(empty)"} → resolved=${definition.sprk_baseviewid || "(none)"}`);

  return definition;
}

/**
 * Load a chart definition by ID from Dataverse
 *
 * @param context - PCF context with webAPI access (or IConfigContext for testing)
 * @param id - Chart definition ID (GUID)
 * @param skipCache - Force reload from Dataverse (default: false)
 * @returns Promise resolving to IChartDefinition
 * @throws ConfigurationNotFoundError if record doesn't exist
 * @throws ConfigurationLoadError for other API errors
 */
export async function loadChartDefinition(
  context: IConfigContext,
  id: string,
  skipCache = false
): Promise<IChartDefinition> {
  // Validate input
  if (!id || id.trim() === "") {
    throw new ConfigurationLoadError("Chart definition ID is required");
  }

  // Normalize GUID format (remove braces if present)
  const normalizedId = id.replace(/[{}]/g, "").toLowerCase();

  logger.debug("ConfigurationLoader", `Loading chart definition: ${normalizedId}`);

  // Check cache first (unless skipCache is true)
  if (!skipCache) {
    const cached = cache.get(normalizedId);
    if (cached && isCacheValid(cached)) {
      logger.debug("ConfigurationLoader", `Cache hit for ${normalizedId}`);
      return cached.definition;
    }
  }

  try {
    // Use WebAPI to retrieve the record
    const record = await context.webAPI.retrieveRecord(
      ENTITY_NAME,
      normalizedId,
      `?$select=${SELECT_COLUMNS}`
    );

    // Map to typed interface
    const definition = mapToChartDefinition(record);

    // Store in cache
    cache.set(normalizedId, {
      definition,
      timestamp: Date.now(),
    });

    logger.info("ConfigurationLoader", `Loaded chart definition: ${definition.sprk_name}`, {
      id: normalizedId,
      visualType: definition.sprk_visualtype,
      entity: definition.sprk_entitylogicalname,
    });

    return definition;
  } catch (error: unknown) {
    // Handle specific error types
    const errorMessage =
      error instanceof Error ? error.message : String(error);

    // Check for "not found" type errors
    if (
      errorMessage.includes("does not exist") ||
      errorMessage.includes("not found") ||
      errorMessage.includes("0x80040217") // Dataverse object not found
    ) {
      logger.warn("ConfigurationLoader", `Chart definition not found: ${normalizedId}`);
      throw new ConfigurationNotFoundError(normalizedId);
    }

    // Other errors
    logger.error("ConfigurationLoader", `Failed to load chart definition: ${normalizedId}`, error);
    throw new ConfigurationLoadError(
      `Failed to load chart definition: ${errorMessage}`,
      error
    );
  }
}

/**
 * Load multiple chart definitions by IDs
 *
 * @param context - PCF context with webAPI access (or IConfigContext for testing)
 * @param ids - Array of chart definition IDs
 * @param skipCache - Force reload from Dataverse (default: false)
 * @returns Promise resolving to array of IChartDefinition (or errors for failed loads)
 */
export async function loadChartDefinitions(
  context: IConfigContext,
  ids: string[],
  skipCache = false
): Promise<Array<IChartDefinition | Error>> {
  logger.debug("ConfigurationLoader", `Loading ${ids.length} chart definitions`);

  const results = await Promise.all(
    ids.map(async (id) => {
      try {
        return await loadChartDefinition(context, id, skipCache);
      } catch (error) {
        return error instanceof Error ? error : new Error(String(error));
      }
    })
  );

  const successCount = results.filter((r) => !(r instanceof Error)).length;
  logger.info("ConfigurationLoader", `Loaded ${successCount}/${ids.length} chart definitions`);

  return results;
}

/**
 * Query chart definitions with filter
 *
 * @param context - PCF context with webAPI access (or IConfigContext for testing)
 * @param filter - OData filter expression (e.g., "sprk_visualtype eq 100000001")
 * @returns Promise resolving to array of IChartDefinition
 */
export async function queryChartDefinitions(
  context: IConfigContext,
  filter?: string
): Promise<IChartDefinition[]> {
  logger.debug("ConfigurationLoader", "Querying chart definitions", { filter });

  try {
    let queryOptions = `?$select=${SELECT_COLUMNS}`;
    if (filter) {
      queryOptions += `&$filter=${encodeURIComponent(filter)}`;
    }

    const result = await context.webAPI.retrieveMultipleRecords(
      ENTITY_NAME,
      queryOptions
    );

    const definitions = result.entities.map(mapToChartDefinition);

    // Update cache for all loaded definitions
    definitions.forEach((def) => {
      const normalizedId = def.sprk_chartdefinitionid.toLowerCase();
      cache.set(normalizedId, {
        definition: def,
        timestamp: Date.now(),
      });
    });

    logger.info("ConfigurationLoader", `Query returned ${definitions.length} chart definitions`);
    return definitions;
  } catch (error: unknown) {
    const errorMessage =
      error instanceof Error ? error.message : String(error);
    logger.error("ConfigurationLoader", "Failed to query chart definitions", error);
    throw new ConfigurationLoadError(
      `Failed to query chart definitions: ${errorMessage}`,
      error
    );
  }
}

/**
 * Get parsed options from a chart definition
 * Convenience method to parse optionsJson field
 */
export function getChartOptions(
  definition: IChartDefinition
): Record<string, unknown> {
  return parseOptionsJson(definition.sprk_optionsjson);
}
