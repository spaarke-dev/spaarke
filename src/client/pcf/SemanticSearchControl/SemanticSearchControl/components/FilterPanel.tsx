/**
 * FilterPanel component
 *
 * Container for filter controls (Document Type, Matter Type, Date Range, File Type).
 * Supports scope-aware visibility to hide irrelevant filters based on search scope.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see spec.md for scope-aware filter rules
 */

import * as React from "react";
import { useCallback } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
    Divider,
} from "@fluentui/react-components";
import { Dismiss20Regular, ChevronLeft20Regular } from "@fluentui/react-icons";
import { IFilterPanelProps, SearchFilters, DateRange } from "../types";
import { FilterDropdown } from "./FilterDropdown";
import { DateRangeFilter } from "./DateRangeFilter";
import { useFilterOptions } from "../hooks/useFilterOptions";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
    },
    header: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    title: {
        fontWeight: tokens.fontWeightSemibold,
    },
    filterSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    headerActions: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXXS,
    },
    clearButton: {
        minWidth: "auto",
    },
    collapseButton: {
        minWidth: "auto",
    },
});

// Default empty filters
const emptyFilters: SearchFilters = {
    documentTypes: [],
    matterTypes: [],
    dateRange: null,
    fileTypes: [],
};

/**
 * FilterPanel component for search filters.
 *
 * @param props.filters - Current filter values
 * @param props.searchScope - Current search scope (all, matter, custom)
 * @param props.scopeId - ID for scoped search
 * @param props.onFiltersChange - Callback when filters change
 * @param props.disabled - Whether filters are disabled
 */
export const FilterPanel: React.FC<IFilterPanelProps> = ({
    filters,
    searchScope,
    onFiltersChange,
    disabled,
    onCollapse,
}) => {
    const styles = useStyles();

    // Fetch filter options from Dataverse
    const {
        documentTypeOptions,
        matterTypeOptions,
        fileTypeOptions,
        isLoading: optionsLoading,
    } = useFilterOptions();

    // Check if any filters are active
    const hasActiveFilters =
        filters.documentTypes.length > 0 ||
        filters.matterTypes.length > 0 ||
        filters.dateRange !== null ||
        filters.fileTypes.length > 0;

    // Handle clear all filters
    const handleClearFilters = useCallback(() => {
        onFiltersChange(emptyFilters);
    }, [onFiltersChange]);

    // Handle document type change
    const handleDocumentTypesChange = useCallback(
        (keys: string[]) => {
            onFiltersChange({
                ...filters,
                documentTypes: keys,
            });
        },
        [filters, onFiltersChange]
    );

    // Handle matter type change
    const handleMatterTypesChange = useCallback(
        (keys: string[]) => {
            onFiltersChange({
                ...filters,
                matterTypes: keys,
            });
        },
        [filters, onFiltersChange]
    );

    // Handle file type change
    const handleFileTypesChange = useCallback(
        (keys: string[]) => {
            onFiltersChange({
                ...filters,
                fileTypes: keys,
            });
        },
        [filters, onFiltersChange]
    );

    // Handle date range change
    const handleDateRangeChange = useCallback(
        (range: DateRange | null) => {
            onFiltersChange({
                ...filters,
                dateRange: range,
            });
        },
        [filters, onFiltersChange]
    );

    // Scope-aware visibility: hide Matter Type when scope is "matter"
    const showMatterTypeFilter = searchScope !== "matter";

    return (
        <div className={styles.container}>
            {/* Header with Clear and Collapse buttons */}
            <div className={styles.header}>
                <Text className={styles.title}>Filters</Text>
                <div className={styles.headerActions}>
                    {hasActiveFilters && (
                        <Button
                            className={styles.clearButton}
                            appearance="subtle"
                            size="small"
                            icon={<Dismiss20Regular />}
                            onClick={handleClearFilters}
                            disabled={disabled}
                        >
                            Clear
                        </Button>
                    )}
                    {onCollapse && (
                        <Button
                            className={styles.collapseButton}
                            appearance="subtle"
                            size="small"
                            icon={<ChevronLeft20Regular />}
                            onClick={onCollapse}
                            aria-label="Collapse filters"
                        />
                    )}
                </div>
            </div>

            <Divider />

            {/* Document Type Filter */}
            <div className={styles.filterSection}>
                <FilterDropdown
                    label="Document Type"
                    options={documentTypeOptions}
                    selectedKeys={filters.documentTypes}
                    onSelectionChange={handleDocumentTypesChange}
                    disabled={disabled || optionsLoading}
                    multiSelect
                />
            </div>

            {/* Matter Type Filter - hidden when scoped to matter */}
            {showMatterTypeFilter && (
                <div className={styles.filterSection}>
                    <FilterDropdown
                        label="Matter Type"
                        options={matterTypeOptions}
                        selectedKeys={filters.matterTypes}
                        onSelectionChange={handleMatterTypesChange}
                        disabled={disabled || optionsLoading}
                        multiSelect
                    />
                </div>
            )}

            {/* Date Range Filter */}
            <div className={styles.filterSection}>
                <DateRangeFilter
                    label="Date Range"
                    value={filters.dateRange}
                    onChange={handleDateRangeChange}
                    disabled={disabled}
                />
            </div>

            {/* File Type Filter */}
            <div className={styles.filterSection}>
                <FilterDropdown
                    label="File Type"
                    options={fileTypeOptions}
                    selectedKeys={filters.fileTypes}
                    onSelectionChange={handleFileTypesChange}
                    disabled={disabled || optionsLoading}
                    multiSelect
                />
            </div>
        </div>
    );
};

export default FilterPanel;
