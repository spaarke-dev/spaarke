/**
 * FilterPanel component
 *
 * Container for filter controls: Document Type, File Type, Date Range,
 * Threshold, and Search Mode — all using consistent FilterDropdown components.
 * Filters are applied when the user clicks Search or presses Enter.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see spec.md for scope-aware filter rules
 */

import * as React from "react";
import { useCallback } from "react";
import {
    makeStyles,
    tokens,
    Button,
    Divider,
} from "@fluentui/react-components";
import {
    Dismiss20Regular,
    ChevronLeft20Regular,
} from "@fluentui/react-icons";
import { IFilterPanelProps, SearchFilters, DateRange, SearchMode } from "../types";
import { FilterDropdown } from "./FilterDropdown";
import { DateRangeFilter } from "./DateRangeFilter";
import { useFilterOptions } from "../hooks/useFilterOptions";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        width: "100%",
        minWidth: 0,
        overflow: "hidden",
        boxSizing: "border-box",
    },
    header: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    clearButton: {
        minWidth: "auto",
    },
    collapseButton: {
        minWidth: "auto",
    },
    filterSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalS,
        minWidth: 0,
    },
});

// Default empty filters
const emptyFilters: SearchFilters = {
    documentTypes: [],
    matterTypes: [],
    dateRange: null,
    fileTypes: [],
    threshold: 0,
    searchMode: "hybrid",
};

// Threshold options (replaces Slider — 5 preset values)
const THRESHOLD_OPTIONS = [
    { key: "0", label: "Off" },
    { key: "25", label: "25%" },
    { key: "50", label: "50%" },
    { key: "75", label: "75%" },
    { key: "100", label: "100%" },
];

// Search mode options
const MODE_OPTIONS = [
    { key: "hybrid", label: "Hybrid" },
    { key: "vectorOnly", label: "Concept Only" },
    { key: "keywordOnly", label: "Keyword Only" },
];

/**
 * FilterPanel component for search filters.
 *
 * @param props.filters - Current filter values
 * @param props.searchScope - Current search scope (all, matter, custom)
 * @param props.scopeId - ID for scoped search
 * @param props.onFiltersChange - Callback when filters change
 * @param props.onApply - Callback to apply filters (triggers search)
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
        filters.fileTypes.length > 0 ||
        filters.threshold > 0 ||
        filters.searchMode !== "hybrid";

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

    // Handle threshold change (single-select dropdown, string[] → number)
    const handleThresholdChange = useCallback(
        (keys: string[]) => {
            const value = keys.length > 0 ? parseInt(keys[0], 10) : 0;
            onFiltersChange({
                ...filters,
                threshold: isNaN(value) ? 0 : value,
            });
        },
        [filters, onFiltersChange]
    );

    // Handle search mode change (single-select dropdown, string[] → SearchMode)
    const handleSearchModeChange = useCallback(
        (keys: string[]) => {
            if (keys.length > 0) {
                onFiltersChange({
                    ...filters,
                    searchMode: keys[0] as SearchMode,
                });
            }
        },
        [filters, onFiltersChange]
    );

    // Scope-aware visibility: hide Matter Type when scope is "matter"
    const showMatterTypeFilter = searchScope !== "matter";

    return (
        <div className={styles.container}>
            {/* Header — Clear (left) and Collapse (right) */}
            <div className={styles.header}>
                {hasActiveFilters ? (
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
                ) : (
                    <div />
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

            <Divider />

            {/* Threshold Filter */}
            <div className={styles.filterSection}>
                <FilterDropdown
                    label="Threshold"
                    options={THRESHOLD_OPTIONS}
                    selectedKeys={[String(filters.threshold)]}
                    onSelectionChange={handleThresholdChange}
                    disabled={disabled}
                    multiSelect={false}
                />
            </div>

            {/* Search Mode Filter */}
            <div className={styles.filterSection}>
                <FilterDropdown
                    label="Mode"
                    options={MODE_OPTIONS}
                    selectedKeys={[filters.searchMode]}
                    onSelectionChange={handleSearchModeChange}
                    disabled={disabled}
                    multiSelect={false}
                />
            </div>
        </div>
    );
};

export default FilterPanel;
