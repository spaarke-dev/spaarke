/**
 * Drill Interaction Types - Spaarke Visuals Framework
 * Defines the contract for chart click interactions that trigger drill-through filtering
 * Project: visualization-module
 */
/**
 * Supported filter operators for drill interactions
 */
export type DrillOperator = 'eq' | 'in' | 'between';
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
export type DrillValue = string | number | boolean | Date | null | DrillValueArray | DrillValueRange;
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
export declare function drillInteractionToFetchXml(interaction: DrillInteraction): string;
/**
 * Convert a DrillInteraction to OData filter string
 *
 * @param interaction - The drill interaction to convert
 * @returns OData $filter string
 */
export declare function drillInteractionToOData(interaction: DrillInteraction): string;
/**
 * Type guard to check if a value is a DrillInteraction
 */
export declare function isDrillInteraction(value: unknown): value is DrillInteraction;
//# sourceMappingURL=DrillInteractionTypes.d.ts.map