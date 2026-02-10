/**
 * Date Filter Utility for UniversalDatasetGrid
 *
 * Converts CalendarFilter to PCF FilterExpression for dataset filtering.
 * Applies filter to sprk_duedate field.
 *
 * @version 1.0.0
 * Task: 011 - Implement Date Filtering on Dataset
 */

import {
    CalendarFilter,
    isSingleDateFilter,
    isRangeFilter,
    isClearFilter
} from "../types";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Dataverse field name for due date filtering
 */
export const DATE_FILTER_FIELD = "sprk_duedate";

/**
 * PCF ConditionOperator values
 * @see https://docs.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.query.conditionoperator
 */
const ConditionOperator = {
    Equal: 0 as ComponentFramework.PropertyHelper.DataSetApi.Types.ConditionOperator,
    GreaterEqual: 4 as ComponentFramework.PropertyHelper.DataSetApi.Types.ConditionOperator,
    LessEqual: 5 as ComponentFramework.PropertyHelper.DataSetApi.Types.ConditionOperator,
    OnOrAfter: 25 as ComponentFramework.PropertyHelper.DataSetApi.Types.ConditionOperator,
    OnOrBefore: 26 as ComponentFramework.PropertyHelper.DataSetApi.Types.ConditionOperator,
    On: 27 as ComponentFramework.PropertyHelper.DataSetApi.Types.ConditionOperator,
};

/**
 * PCF FilterOperator values
 */
const FilterOperator = {
    And: 0 as ComponentFramework.PropertyHelper.DataSetApi.Types.FilterOperator,
    Or: 1 as ComponentFramework.PropertyHelper.DataSetApi.Types.FilterOperator,
};

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Result of buildDateFilter
 */
export interface DateFilterResult {
    /** Whether a filter should be applied (false for clear) */
    shouldFilter: boolean;
    /** The filter expression (null if clear) */
    filterExpression: ComponentFramework.PropertyHelper.DataSetApi.FilterExpression | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Main Function
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Build a PCF FilterExpression from a CalendarFilter
 *
 * Converts the calendar filter types to dataset filtering conditions:
 * - single: sprk_duedate eq {date}
 * - range: sprk_duedate ge {start} AND sprk_duedate le {end}
 * - clear: no filter (returns shouldFilter=false)
 *
 * @param calendarFilter - The calendar filter from PCF input
 * @returns DateFilterResult with filter expression or clear indicator
 *
 * @example
 * ```typescript
 * const filter = parseCalendarFilter('{"type":"single","date":"2026-02-04"}');
 * const result = buildDateFilter(filter);
 * if (result.shouldFilter && result.filterExpression) {
 *     dataset.filtering.setFilter(result.filterExpression);
 * } else {
 *     dataset.filtering.clearFilter();
 * }
 * dataset.refresh();
 * ```
 */
export function buildDateFilter(
    calendarFilter: CalendarFilter | null | undefined
): DateFilterResult {
    // No filter or null - clear
    if (!calendarFilter) {
        return {
            shouldFilter: false,
            filterExpression: null,
        };
    }

    // Clear filter type
    if (isClearFilter(calendarFilter)) {
        return {
            shouldFilter: false,
            filterExpression: null,
        };
    }

    // Single date filter: sprk_duedate eq {date}
    if (isSingleDateFilter(calendarFilter)) {
        return {
            shouldFilter: true,
            filterExpression: {
                filterOperator: FilterOperator.And,
                conditions: [
                    {
                        attributeName: DATE_FILTER_FIELD,
                        conditionOperator: ConditionOperator.On,
                        value: calendarFilter.date,
                    },
                ],
            },
        };
    }

    // Range filter: sprk_duedate ge {start} AND sprk_duedate le {end}
    if (isRangeFilter(calendarFilter)) {
        return {
            shouldFilter: true,
            filterExpression: {
                filterOperator: FilterOperator.And,
                conditions: [
                    {
                        attributeName: DATE_FILTER_FIELD,
                        conditionOperator: ConditionOperator.OnOrAfter,
                        value: calendarFilter.start,
                    },
                    {
                        attributeName: DATE_FILTER_FIELD,
                        conditionOperator: ConditionOperator.OnOrBefore,
                        value: calendarFilter.end,
                    },
                ],
            },
        };
    }

    // Unknown filter type - treat as clear
    return {
        shouldFilter: false,
        filterExpression: null,
    };
}

/**
 * Apply date filter to a PCF dataset
 *
 * Convenience function that applies or clears the filter and refreshes the dataset.
 *
 * @param dataset - PCF dataset from context
 * @param calendarFilter - The calendar filter to apply
 * @returns true if filter was applied, false if cleared
 */
export function applyDateFilter(
    dataset: ComponentFramework.PropertyTypes.DataSet,
    calendarFilter: CalendarFilter | null | undefined
): boolean {
    if (!dataset?.filtering) {
        console.warn("[dateFilter] Dataset filtering API not available");
        return false;
    }

    const result = buildDateFilter(calendarFilter);

    if (result.shouldFilter && result.filterExpression) {
        // Apply the filter
        dataset.filtering.setFilter(result.filterExpression);
        dataset.refresh();
        return true;
    } else {
        // Clear any existing filter
        dataset.filtering.clearFilter();
        dataset.refresh();
        return false;
    }
}
