/**
 * ColumnFilter Component
 *
 * Manages column filtering state and integrates with the PCF dataset filtering API.
 * Provides filter icons in column headers with popup filter controls.
 *
 * Task 016: Add Column/Field Filters
 */

import * as React from 'react';
import { FilterPopup, FilterValue, ChoiceOption, TextFilterValue, ChoiceFilterValue, DateFilterValue } from './FilterPopup';
import { logger } from '../utils/logger';

/**
 * Active filter state for a column
 */
export interface ColumnFilterState {
    columnName: string;
    filterValue: FilterValue;
}

/**
 * Props for ColumnFilter component
 */
export interface ColumnFilterProps {
    /** Column definition from dataset */
    column: ComponentFramework.PropertyHelper.DataSetApi.Column;

    /** Current filter value for this column */
    filterValue: FilterValue;

    /** Callback when filter changes */
    onFilterChange: (columnName: string, value: FilterValue) => void;

    /** Dataset for extracting choice options */
    dataset?: ComponentFramework.PropertyTypes.DataSet;
}

/**
 * Extract choice options from dataset column metadata
 *
 * For OptionSet/Choice columns, extracts the available options
 * to populate the filter dropdown.
 */
function extractChoiceOptions(
    column: ComponentFramework.PropertyHelper.DataSetApi.Column,
    dataset?: ComponentFramework.PropertyTypes.DataSet
): ChoiceOption[] {
    // Check if column has options metadata (OptionSet columns)
    // The PCF dataset API doesn't directly expose options, so we need to extract from records
    // or rely on the column metadata if available

    const options: ChoiceOption[] = [];
    const seenValues = new Set<string | number>();

    // If we have dataset records, extract unique values
    if (dataset?.sortedRecordIds && dataset?.records) {
        for (const recordId of dataset.sortedRecordIds) {
            const record = dataset.records[recordId];
            if (!record) continue;

            try {
                const rawValue = record.getValue(column.name);
                const formattedValue = record.getFormattedValue(column.name);

                if (rawValue !== null && rawValue !== undefined && !seenValues.has(String(rawValue))) {
                    seenValues.add(typeof rawValue === 'number' ? rawValue : String(rawValue));
                    options.push({
                        value: typeof rawValue === 'number' ? rawValue : String(rawValue),
                        label: formattedValue || String(rawValue),
                    });
                }
            } catch {
                // getValue may fail for some column types, skip
            }
        }
    }

    // Sort options by label
    options.sort((a, b) => a.label.localeCompare(b.label));

    return options;
}

/**
 * Check if a column data type should show filter controls
 */
function isFilterableColumn(dataType: string): boolean {
    const normalizedType = dataType.toLowerCase();

    // Support filtering for these types
    const filterableTypes = [
        'singleline.text',
        'multiple',
        'optionset',
        'picklist',
        'status',
        'state',
        'datetime',
        'dateonly',
        'lookup.simple',
        'lookup.customer',
        'lookup.owner',
        'wholenumber',
        'decimal',
        'currency',
        'boolean',
        'two.options',
    ];

    return filterableTypes.some(type => normalizedType.includes(type) || normalizedType === type);
}

/**
 * ColumnFilter Component
 *
 * Renders a filter icon button for a column that opens the FilterPopup.
 * Extracts choice options for OptionSet columns.
 */
export const ColumnFilter: React.FC<ColumnFilterProps> = ({
    column,
    filterValue,
    onFilterChange,
    dataset,
}) => {
    // Check if column is filterable
    const isFilterable = isFilterableColumn(column.dataType);

    // Extract choice options for OptionSet columns
    const choiceOptions = React.useMemo(() => {
        if (column.dataType.toLowerCase().includes('optionset') ||
            column.dataType.toLowerCase().includes('picklist') ||
            column.dataType.toLowerCase().includes('status') ||
            column.dataType.toLowerCase().includes('state') ||
            column.dataType.toLowerCase().includes('boolean') ||
            column.dataType.toLowerCase().includes('two.options')) {
            return extractChoiceOptions(column, dataset);
        }
        return [];
    }, [column, dataset]);

    // Don't render filter for non-filterable columns
    if (!isFilterable) {
        return null;
    }

    return (
        <FilterPopup
            columnName={column.name}
            columnDisplayName={column.displayName}
            dataType={column.dataType}
            filterValue={filterValue}
            onFilterChange={onFilterChange}
            choiceOptions={choiceOptions}
        />
    );
};

/**
 * Hook: useColumnFilters
 *
 * Manages filter state for multiple columns and integrates with the PCF dataset filtering API.
 * Combines multiple column filters with AND logic.
 */
export function useColumnFilters(
    dataset: ComponentFramework.PropertyTypes.DataSet
): {
    filters: Map<string, FilterValue>;
    setFilter: (columnName: string, value: FilterValue) => void;
    clearAllFilters: () => void;
    hasActiveFilters: boolean;
    activeFilterCount: number;
} {
    const [filters, setFilters] = React.useState<Map<string, FilterValue>>(new Map());

    // Track previous filters to avoid unnecessary API calls
    const prevFiltersRef = React.useRef<string>('');

    /**
     * Apply filters to the dataset using the PCF filtering API
     */
    const applyFiltersToDataset = React.useCallback((currentFilters: Map<string, FilterValue>) => {
        try {
            // Serialize for comparison
            const filtersJson = JSON.stringify(Array.from(currentFilters.entries()));
            if (filtersJson === prevFiltersRef.current) {
                return; // No change, skip
            }
            prevFiltersRef.current = filtersJson;

            // Clear existing filters first
            if (dataset.filtering && typeof dataset.filtering.clearFilter === 'function') {
                dataset.filtering.clearFilter();
            }

            // Apply each active filter
            if (dataset.filtering && typeof dataset.filtering.setFilter === 'function') {
                currentFilters.forEach((filterValue, columnName) => {
                    if (filterValue === null) return;

                    const filterExpression = buildFilterExpression(columnName, filterValue);
                    if (filterExpression) {
                        logger.debug('ColumnFilter', `Applying filter to ${columnName}:`, filterExpression);

                        try {
                            // PCF dataset filtering API
                            dataset.filtering.setFilter(filterExpression);
                        } catch (filterError) {
                            logger.warn('ColumnFilter', `Failed to apply filter to ${columnName}:`, filterError);
                        }
                    }
                });
            } else {
                logger.warn('ColumnFilter', 'Dataset filtering API not available');
            }

            // Refresh the dataset to apply filters
            dataset.refresh();

        } catch (error) {
            logger.error('ColumnFilter', 'Failed to apply filters:', error);
        }
    }, [dataset]);

    /**
     * Set filter for a specific column
     */
    const setFilter = React.useCallback((columnName: string, value: FilterValue) => {
        setFilters(prev => {
            const newFilters = new Map(prev);
            if (value === null) {
                newFilters.delete(columnName);
            } else {
                newFilters.set(columnName, value);
            }

            // Apply filters to dataset
            applyFiltersToDataset(newFilters);

            return newFilters;
        });
    }, [applyFiltersToDataset]);

    /**
     * Clear all filters
     */
    const clearAllFilters = React.useCallback(() => {
        setFilters(new Map());

        try {
            if (dataset.filtering && typeof dataset.filtering.clearFilter === 'function') {
                dataset.filtering.clearFilter();
            }
            dataset.refresh();
        } catch (error) {
            logger.error('ColumnFilter', 'Failed to clear filters:', error);
        }

        prevFiltersRef.current = '';
    }, [dataset]);

    const hasActiveFilters = filters.size > 0;
    const activeFilterCount = filters.size;

    return {
        filters,
        setFilter,
        clearAllFilters,
        hasActiveFilters,
        activeFilterCount,
    };
}

/**
 * PCF ConditionOperator enum values.
 * These map to the Dataverse ConditionOperator enum.
 */
const ConditionOperator = {
    Equal: 0,
    NotEqual: 1,
    GreaterThan: 2,
    LessThan: 3,
    GreaterEqual: 4,
    LessEqual: 5,
    BeginsWith: 6,
    // EndsWith is not directly supported in PCF - using Like instead
    Like: 8, // Used for Contains
    // In is not directly supported - need to use Equal with multiple conditions
} as const;

/**
 * Build PCF filter expression from FilterValue
 *
 * Converts our FilterValue to the format expected by the PCF dataset.filtering API.
 * The PCF filtering API expects a ConditionExpression-like object.
 *
 * Note: The PCF dataset filtering API has limited operator support compared to
 * the full Dataverse FetchXML conditions. We use the closest available operators.
 */
function buildFilterExpression(
    columnName: string,
    filterValue: FilterValue
): ComponentFramework.PropertyHelper.DataSetApi.FilterExpression | null {
    if (!filterValue) return null;

    // Build condition based on filter type
    // Using 'as any' cast because the PCF types for conditionOperator are inconsistent
    // between different versions of the PCF framework
    let conditionOperator: number = ConditionOperator.Equal;
    let conditionValue: string = '';

    if (filterValue.type === 'text') {
        const textFilter = filterValue as TextFilterValue;
        conditionValue = textFilter.value;

        switch (textFilter.operator) {
            case 'contains':
                conditionOperator = ConditionOperator.Like;
                conditionValue = `%${textFilter.value}%`;
                break;
            case 'equals':
                conditionOperator = ConditionOperator.Equal;
                break;
            case 'startswith':
                conditionOperator = ConditionOperator.BeginsWith;
                break;
            case 'endswith':
                // EndsWith is approximated with Like
                conditionOperator = ConditionOperator.Like;
                conditionValue = `%${textFilter.value}`;
                break;
            default:
                conditionOperator = ConditionOperator.Like;
                conditionValue = `%${textFilter.value}%`;
        }
    } else if (filterValue.type === 'choice') {
        const choiceFilter = filterValue as ChoiceFilterValue;
        // For choice filters, use Equal (In operator not directly available)
        conditionOperator = ConditionOperator.Equal;
        conditionValue = String(choiceFilter.values[0] || '');
    } else if (filterValue.type === 'date') {
        const dateFilter = filterValue as DateFilterValue;

        if (!dateFilter.value) return null;

        // Format date as ISO string for the API
        const dateStr = dateFilter.value.toISOString().split('T')[0];

        switch (dateFilter.operator) {
            case 'equals':
                conditionOperator = ConditionOperator.Equal;
                conditionValue = dateStr;
                break;
            case 'before':
                conditionOperator = ConditionOperator.LessThan;
                conditionValue = dateStr;
                break;
            case 'after':
                conditionOperator = ConditionOperator.GreaterThan;
                conditionValue = dateStr;
                break;
            case 'between':
                conditionOperator = ConditionOperator.GreaterEqual;
                conditionValue = dateStr;
                break;
            default:
                conditionOperator = ConditionOperator.Equal;
                conditionValue = dateStr;
        }
    }

    // Build condition with type assertion for PCF compatibility
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const condition: ComponentFramework.PropertyHelper.DataSetApi.ConditionExpression = {
        attributeName: columnName,
        conditionOperator: conditionOperator as unknown as ComponentFramework.PropertyHelper.DataSetApi.ConditionExpression['conditionOperator'],
        value: conditionValue,
    };

    // Build filter expression
    const filterExpression: ComponentFramework.PropertyHelper.DataSetApi.FilterExpression = {
        conditions: [condition],
        filterOperator: 0, // And
    };

    return filterExpression;
}
