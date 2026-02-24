/**
 * FilterPanel component
 *
 * Container for filter controls: Document Type, File Type, Date Range,
 * Threshold slider, and Search Mode selector.
 * Filters are applied when the user clicks Search or presses Enter.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see spec.md for scope-aware filter rules
 */

import * as React from "react";
import { useCallback, useState } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
    Divider,
    Slider,
    Label,
    Dropdown,
    Option,
    Popover,
    PopoverTrigger,
    PopoverSurface,
    Tooltip,
} from "@fluentui/react-components";
import {
    Dismiss20Regular,
    ChevronLeft20Regular,
    Info20Regular,
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
    },
    header: {
        display: "flex",
        justifyContent: "flex-end",
        alignItems: "center",
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
    filterSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalS,
    },
    sliderSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        paddingBottom: tokens.spacingVerticalS,
    },
    sliderHeader: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    sliderLabel: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
    },
    sliderValue: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        minWidth: "32px",
        textAlign: "right" as const,
    },
    modeSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        paddingBottom: tokens.spacingVerticalS,
    },
    modeLabel: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
    },
    infoButton: {
        minWidth: "auto",
        padding: "0px",
    },
    infoPopover: {
        maxWidth: "320px",
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
    },
    infoHeading: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase300,
    },
    infoText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: tokens.lineHeightBase200,
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

// Search mode options for dropdown
const SEARCH_MODE_OPTIONS: { value: SearchMode; label: string }[] = [
    { value: "hybrid", label: "Hybrid" },
    { value: "vectorOnly", label: "Concept Only" },
    { value: "keywordOnly", label: "Keyword Only" },
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

    // Info popover open state
    const [infoOpen, setInfoOpen] = useState(false);

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

    // Handle threshold change
    const handleThresholdChange = useCallback(
        (_ev: unknown, data: { value: number }) => {
            onFiltersChange({
                ...filters,
                threshold: data.value,
            });
        },
        [filters, onFiltersChange]
    );

    // Handle search mode change
    const handleSearchModeChange = useCallback(
        (_ev: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                onFiltersChange({
                    ...filters,
                    searchMode: data.optionValue as SearchMode,
                });
            }
        },
        [filters, onFiltersChange]
    );

    // Scope-aware visibility: hide Matter Type when scope is "matter"
    const showMatterTypeFilter = searchScope !== "matter";

    // Get display label for current search mode
    const currentModeLabel = SEARCH_MODE_OPTIONS.find(
        (o) => o.value === filters.searchMode
    )?.label ?? "Hybrid";

    return (
        <div className={styles.container}>
            {/* Header — Clear and Collapse buttons only (no "Filters" label) */}
            <div className={styles.header}>
                <div className={styles.headerActions}>
                    {/* Info button */}
                    <Popover
                        open={infoOpen}
                        onOpenChange={(_ev, data) => setInfoOpen(data.open)}
                        positioning="below-end"
                        withArrow
                    >
                        <PopoverTrigger disableButtonEnhancement>
                            <Tooltip content="How semantic search works" relationship="label">
                                <Button
                                    className={styles.infoButton}
                                    appearance="subtle"
                                    size="small"
                                    icon={<Info20Regular />}
                                    aria-label="Search info"
                                />
                            </Tooltip>
                        </PopoverTrigger>
                        <PopoverSurface className={styles.infoPopover}>
                            <Text className={styles.infoHeading}>How Semantic Search Works</Text>
                            <Text className={styles.infoText}>
                                Semantic search finds documents by <strong>meaning</strong>, not just keywords.
                                Your query is converted to a mathematical representation of its concept,
                                then matched against document content.
                            </Text>
                            <Text className={styles.infoHeading}>Highlighted Text</Text>
                            <Text className={styles.infoText}>
                                The yellow highlighted passages show the most <strong>semantically relevant</strong> section
                                of each document. These may not contain your exact search words — they represent
                                passages the AI identified as most related to your query{"'"}s meaning.
                            </Text>
                            <Text className={styles.infoHeading}>Similarity Score</Text>
                            <Text className={styles.infoText}>
                                The percentage badge (e.g., 45%) indicates how closely a document{"'"}s content
                                matches your query{"'"}s meaning. Higher = more relevant.
                                Use the <strong>Threshold</strong> slider to hide low-scoring results.
                            </Text>
                            <Text className={styles.infoHeading}>Search Modes</Text>
                            <Text className={styles.infoText}>
                                <strong>Hybrid</strong> (default): Combines meaning-based and keyword search for best overall results.{" "}
                                <strong>Concept Only</strong>: Pure meaning-based search — good for abstract queries.{" "}
                                <strong>Keyword Only</strong>: Traditional exact-word matching — good for specific terms or clause numbers.
                            </Text>
                        </PopoverSurface>
                    </Popover>

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

            {/* Threshold Slider */}
            <div className={styles.sliderSection}>
                <div className={styles.sliderHeader}>
                    <Label className={styles.sliderLabel} size="small">
                        Threshold
                    </Label>
                    <Text className={styles.sliderValue}>
                        {filters.threshold}%
                    </Text>
                </div>
                <Slider
                    min={0}
                    max={100}
                    step={5}
                    value={filters.threshold}
                    onChange={handleThresholdChange}
                    disabled={disabled}
                    size="small"
                />
            </div>

            {/* Search Mode Dropdown */}
            <div className={styles.modeSection}>
                <Label className={styles.modeLabel} size="small">
                    Mode
                </Label>
                <Dropdown
                    size="small"
                    value={currentModeLabel}
                    selectedOptions={[filters.searchMode]}
                    onOptionSelect={handleSearchModeChange}
                    disabled={disabled}
                >
                    {SEARCH_MODE_OPTIONS.map((option) => (
                        <Option key={option.value} value={option.value}>
                            {option.label}
                        </Option>
                    ))}
                </Dropdown>
            </div>
        </div>
    );
};

export default FilterPanel;
