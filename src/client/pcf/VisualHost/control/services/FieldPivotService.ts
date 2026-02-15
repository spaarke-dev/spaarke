/**
 * Field Pivot Service
 *
 * Reads multiple fields from a single Dataverse record and transforms each
 * into an IAggregatedDataPoint for MetricCardMatrix rendering.
 *
 * Generic — not KPI-specific. Works for any entity with multiple numeric
 * fields that should display as a card row.
 *
 * v1.2.41 - Field pivot data source mode
 */

import type {
  IChartData,
  IAggregatedDataPoint,
  IFieldPivotConfig,
  IFieldPivotEntry,
} from "../types";
import { AggregationType } from "../types";
import { logger } from "../utils/logger";

const TAG = "FieldPivotService";

/**
 * WebAPI interface for single-record retrieval.
 * Uses the same retrieveRecord pattern available in PCF context.webAPI.
 */
export interface IFieldPivotWebApi {
  retrieveRecord(
    entityType: string,
    id: string,
    options?: string
  ): Promise<Record<string, unknown>>;
}

/**
 * Parse and validate fieldPivot configuration from configurationJson.
 * Returns null if fieldPivot is not configured.
 */
export function parseFieldPivotConfig(
  configurationJson: string | undefined
): IFieldPivotConfig | null {
  if (!configurationJson) return null;

  try {
    const json = JSON.parse(configurationJson);
    const pivot = json.fieldPivot;

    if (!pivot?.fields || !Array.isArray(pivot.fields) || pivot.fields.length === 0) {
      return null;
    }

    // Validate each entry has required properties
    const validFields = pivot.fields.filter(
      (f: IFieldPivotEntry) => f.field && f.label
    );

    if (validFields.length === 0) {
      logger.warn(TAG, "fieldPivot.fields defined but no valid entries (need field + label)");
      return null;
    }

    return { fields: validFields };
  } catch {
    // configurationJson exists but is not valid JSON or has no fieldPivot — not an error
    return null;
  }
}

/**
 * Fetch a single record from Dataverse and pivot configured fields
 * into IAggregatedDataPoint[] for card rendering.
 *
 * @param webApi - PCF WebAPI context
 * @param entityLogicalName - Entity to query (from chart definition sprk_entitylogicalname)
 * @param recordId - The record to retrieve (from PCF form context)
 * @param pivotConfig - Field pivot configuration from configurationJson
 * @returns IChartData with one data point per configured field
 */
export async function fetchAndPivot(
  webApi: IFieldPivotWebApi,
  entityLogicalName: string,
  recordId: string,
  pivotConfig: IFieldPivotConfig
): Promise<IChartData> {
  const fieldNames = pivotConfig.fields.map((f) => f.field);

  logger.info(TAG, `Fetching ${entityLogicalName} record ${recordId}`, {
    fields: fieldNames,
  });

  // Build $select to fetch only the fields we need
  const selectOption = `?$select=${fieldNames.join(",")}`;

  // Clean the record ID (remove braces if present)
  const cleanId = recordId.replace(/[{}]/g, "");

  const record = await webApi.retrieveRecord(entityLogicalName, cleanId, selectOption);

  logger.info(TAG, `Record retrieved, pivoting ${pivotConfig.fields.length} fields`);

  // Map each configured field to a data point
  const dataPoints: IAggregatedDataPoint[] = pivotConfig.fields.map(
    (entry, index) => {
      const rawValue = record[entry.field];
      const numericValue = typeof rawValue === "number" ? rawValue : 0;

      if (rawValue === null || rawValue === undefined) {
        logger.warn(TAG, `Field "${entry.field}" is null/undefined on record`);
      }

      return {
        label: entry.label,
        value: numericValue,
        fieldValue: entry.fieldValue ?? entry.label,
        sortOrder: entry.sortOrder ?? index,
      };
    }
  );

  logger.info(TAG, `Pivot complete: ${dataPoints.length} data points`, {
    points: dataPoints.map((dp) => `${dp.label}=${dp.value}`),
  });

  return {
    dataPoints,
    totalRecords: 1,
    aggregationType: AggregationType.Count,
    groupByField: undefined,
    aggregationField: undefined,
  };
}
