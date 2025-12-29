/**
 * Drill Interaction Types - Spaarke Visuals Framework
 * Defines the contract for chart click interactions that trigger drill-through filtering
 * Project: visualization-module
 */

/**
 * Supported filter operators for drill interactions
 */
export type DrillOperator = "eq" | "in" | "between";

/**
 * Drill interaction contract - emitted when user clicks a chart element
 * Used to filter the dataset grid in the drill-through workspace
 *
 * @example
 * // Single value filter (clicking a bar)
 * { field: "statuscode", operator: "eq", value: 1, label: "Active" }
 *
 * @example
 * // Multiple value filter (clicking a grouped element)
 * { field: "sprk_projecttype", operator: "in", value: [1, 2, 3], label: "Type A, B, C" }
 *
 * @example
 * // Range filter (clicking a time period)
 * { field: "createdon", operator: "between", value: ["2025-01-01", "2025-01-31"], label: "January 2025" }
 */
export interface DrillInteraction {
  /** Logical name of the field to filter */
  field: string;

  /** Filter operator */
  operator: DrillOperator;

  /** Filter value(s) - type depends on operator */
  value: DrillValue;

  /** Human-readable label for display */
  label?: string;
}

/**
 * Value types for drill interactions
 */
export type DrillValue =
  | string
  | number
  | boolean
  | Date
  | null
  | DrillValueArray
  | DrillValueRange;

/**
 * Array of values for "in" operator
 */
export type DrillValueArray = (string | number | boolean | null)[];

/**
 * Range of values for "between" operator (start, end)
 */
export type DrillValueRange = [string | number | Date, string | number | Date];

/**
 * Filter state context value
 */
export interface IFilterState {
  /** Currently active filter (null if no filter) */
  activeFilter: DrillInteraction | null;

  /** Set a new filter */
  setFilter: (filter: DrillInteraction) => void;

  /** Clear the active filter */
  clearFilter: () => void;

  /** Whether a filter is currently active */
  isFiltered: boolean;
}

/**
 * Convert a DrillInteraction to FetchXML filter condition
 *
 * @param interaction - The drill interaction to convert
 * @returns FetchXML condition element as string
 */
export function drillInteractionToFetchXml(interaction: DrillInteraction): string {
  const { field, operator, value } = interaction;

  switch (operator) {
    case "eq":
      return `<condition attribute="${field}" operator="eq" value="${value}" />`;

    case "in":
      if (Array.isArray(value)) {
        const values = value.map((v) => `<value>${v}</value>`).join("");
        return `<condition attribute="${field}" operator="in">${values}</condition>`;
      }
      return `<condition attribute="${field}" operator="eq" value="${value}" />`;

    case "between":
      if (Array.isArray(value) && value.length === 2) {
        return `<filter type="and">
          <condition attribute="${field}" operator="ge" value="${value[0]}" />
          <condition attribute="${field}" operator="le" value="${value[1]}" />
        </filter>`;
      }
      return `<condition attribute="${field}" operator="eq" value="${value}" />`;

    default:
      return `<condition attribute="${field}" operator="eq" value="${value}" />`;
  }
}

/**
 * Convert a DrillInteraction to OData filter string
 *
 * @param interaction - The drill interaction to convert
 * @returns OData $filter string
 */
export function drillInteractionToOData(interaction: DrillInteraction): string {
  const { field, operator, value } = interaction;

  switch (operator) {
    case "eq":
      if (typeof value === "string") {
        return `${field} eq '${value}'`;
      }
      return `${field} eq ${value}`;

    case "in":
      if (Array.isArray(value)) {
        const conditions = value.map((v) => {
          if (typeof v === "string") {
            return `${field} eq '${v}'`;
          }
          return `${field} eq ${v}`;
        });
        return `(${conditions.join(" or ")})`;
      }
      return `${field} eq ${value}`;

    case "between":
      if (Array.isArray(value) && value.length === 2) {
        return `(${field} ge ${value[0]} and ${field} le ${value[1]})`;
      }
      return `${field} eq ${value}`;

    default:
      return `${field} eq ${value}`;
  }
}

/**
 * Type guard to check if a value is a DrillInteraction
 */
export function isDrillInteraction(value: unknown): value is DrillInteraction {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const obj = value as Record<string, unknown>;
  const validOperators: DrillOperator[] = ["eq", "in", "between"];
  return (
    typeof obj.field === "string" &&
    typeof obj.operator === "string" &&
    validOperators.indexOf(obj.operator as DrillOperator) !== -1 &&
    obj.value !== undefined
  );
}
