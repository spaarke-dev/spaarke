/**
 * SearchFilterPane -- Collapsible left-side filter pane
 *
 * Layout (top to bottom):
 *   1. Collapse toggle button (header row, no "Filters" label)
 *   2. Saved Searches selector (prominent, above search)
 *   3. AI Search query textarea (multi-line, labeled)
 *   4. Domain-aware filter dropdowns
 *   5. Date range, threshold, mode
 *   6. Search button
 *
 * @see ADR-021 for Fluent UI v9 design system requirements
 */

import { useState, useCallback } from "react";
import {
    makeStyles,
    tokens,
    mergeClasses,
    Textarea,
    Button,
    Slider,
    Dropdown,
    Option,
    Label,
    Text,
} from "@fluentui/react-components";
import {
    ChevronDoubleLeft20Regular,
    ChevronDoubleRight20Regular,
    Search20Regular,
} from "@fluentui/react-icons";
import type { SearchDomain, SearchFilters, FilterOption, HybridMode, SavedSearch } from "../types";
import { FilterDropdown } from "./FilterDropdown";
import { DateRangeFilter } from "./DateRangeFilter";
import { SavedSearchSelector } from "./SavedSearchSelector";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SearchFilterPaneProps {
    /** Currently active search domain — controls which filter sections are visible */
    activeDomain: SearchDomain;
    /** Current filter state */
    filters: SearchFilters;
    /** Callback when any filter value changes */
    onFiltersChange: (filters: SearchFilters) => void;
    /** Callback to trigger a search with updated filters */
    onSearch: (query: string, filters: SearchFilters) => void;
    /** Available filter option lists for dropdowns */
    filterOptions: {
        documentTypes: FilterOption[];
        fileTypes: FilterOption[];
        matterTypes: FilterOption[];
    };
    /** Whether a search is currently in progress */
    isLoading: boolean;
    /** Current search query (controlled by parent App) */
    query: string;
    /** Callback when query text changes */
    onQueryChange: (query: string) => void;
    /** Saved searches for the selector */
    savedSearches: SavedSearch[];
    /** Currently active saved search name */
    currentSearchName: string | null;
    /** Called when a saved search is selected */
    onSelectSavedSearch: (search: SavedSearch) => void;
    /** Called when user saves current search */
    onSaveCurrentSearch: () => void;
    /** Whether saved searches are loading */
    isSavedSearchesLoading: boolean;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const EXPANDED_WIDTH = "280px";
const COLLAPSED_WIDTH = "40px";
const TRANSITION_DURATION = "200ms";

const SEARCH_MODE_OPTIONS: { value: HybridMode; label: string }[] = [
    { value: "rrf", label: "Hybrid (RRF)" },
    { value: "vectorOnly", label: "Vector Only" },
    { value: "keywordOnly", label: "Keyword Only" },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    pane: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
        overflowY: "auto",
        overflowX: "hidden",
        transitionProperty: "width, min-width, padding",
        transitionDuration: TRANSITION_DURATION,
        transitionTimingFunction: "ease-in-out",
    },
    expanded: {
        width: EXPANDED_WIDTH,
        minWidth: EXPANDED_WIDTH,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
    },
    collapsed: {
        width: COLLAPSED_WIDTH,
        minWidth: COLLAPSED_WIDTH,
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalXS,
        paddingRight: tokens.spacingHorizontalXS,
        alignItems: "center",
    },
    header: {
        display: "flex",
        justifyContent: "flex-end",
        alignItems: "center",
        marginBottom: tokens.spacingVerticalXS,
    },
    collapseButton: {
        minWidth: "auto",
    },
    savedSearchSection: {
        marginBottom: tokens.spacingVerticalS,
    },
    querySection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        marginBottom: tokens.spacingVerticalS,
    },
    queryLabel: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
    queryTextarea: {
        width: "100%",
    },
    filterSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        marginBottom: tokens.spacingVerticalS,
    },
    sectionLabel: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
    sliderContainer: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
    },
    sliderLabel: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    sliderValue: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    modeDropdown: {
        width: "100%",
    },
    searchButton: {
        marginTop: tokens.spacingVerticalS,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SearchFilterPane: React.FC<SearchFilterPaneProps> = ({
    activeDomain,
    filters,
    onFiltersChange,
    onSearch,
    filterOptions,
    isLoading,
    query,
    onQueryChange,
    savedSearches,
    currentSearchName,
    onSelectSavedSearch,
    onSaveCurrentSearch,
    isSavedSearchesLoading,
}) => {
    const styles = useStyles();
    const [isCollapsed, setIsCollapsed] = useState(false);

    // --- Domain-visibility logic ---
    const showDocumentTypeFilter = activeDomain === "documents";
    const showFileTypeFilter = activeDomain === "documents";
    const showMatterTypeFilter =
        activeDomain === "documents" || activeDomain === "matters";
    const showDateRange = true;
    const showThreshold = true;
    const showMode = true;

    // --- Handlers ---

    const handleToggleCollapse = useCallback(() => {
        setIsCollapsed((prev) => !prev);
    }, []);

    const handleThresholdChange = useCallback(
        (_ev: unknown, data: { value: number }) => {
            onFiltersChange({ ...filters, threshold: data.value });
        },
        [filters, onFiltersChange],
    );

    const handleModeChange = useCallback(
        (_ev: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                onFiltersChange({
                    ...filters,
                    searchMode: data.optionValue as HybridMode,
                });
            }
        },
        [filters, onFiltersChange],
    );

    const handleSearch = useCallback(() => {
        onSearch(query, filters);
    }, [onSearch, query, filters]);

    const handleQueryKeyDown = useCallback(
        (ev: React.KeyboardEvent) => {
            // Ctrl+Enter or Cmd+Enter triggers search (plain Enter adds newline)
            if (ev.key === "Enter" && (ev.ctrlKey || ev.metaKey)) {
                ev.preventDefault();
                onSearch(query, filters);
            }
        },
        [onSearch, query, filters],
    );

    const handleDocumentTypesChange = useCallback(
        (selected: string[]) => {
            onFiltersChange({ ...filters, documentTypes: selected });
        },
        [filters, onFiltersChange],
    );

    const handleFileTypesChange = useCallback(
        (selected: string[]) => {
            onFiltersChange({ ...filters, fileTypes: selected });
        },
        [filters, onFiltersChange],
    );

    const handleMatterTypesChange = useCallback(
        (selected: string[]) => {
            onFiltersChange({ ...filters, matterTypes: selected });
        },
        [filters, onFiltersChange],
    );

    const handleDateRangeChange = useCallback(
        (dateRange: { from: string | null; to: string | null }) => {
            onFiltersChange({ ...filters, dateRange });
        },
        [filters, onFiltersChange],
    );

    // --- Render ---

    const paneClassName = mergeClasses(
        styles.pane,
        isCollapsed ? styles.collapsed : styles.expanded,
    );

    // Collapsed state: only show expand button
    if (isCollapsed) {
        return (
            <div className={paneClassName} role="region" aria-label="Search filters">
                <Button
                    className={styles.collapseButton}
                    appearance="subtle"
                    size="small"
                    icon={<ChevronDoubleRight20Regular />}
                    onClick={handleToggleCollapse}
                    aria-label="Expand filters"
                />
            </div>
        );
    }

    return (
        <div className={paneClassName} role="region" aria-label="Search filters">
            {/* Header: collapse button only (no label) */}
            <div className={styles.header}>
                <Button
                    className={styles.collapseButton}
                    appearance="subtle"
                    size="small"
                    icon={<ChevronDoubleLeft20Regular />}
                    onClick={handleToggleCollapse}
                    aria-label="Collapse filters"
                />
            </div>

            {/* Saved Searches — prominent, above search */}
            <div className={styles.savedSearchSection}>
                <SavedSearchSelector
                    savedSearches={savedSearches}
                    currentSearchName={currentSearchName}
                    onSelectSavedSearch={onSelectSavedSearch}
                    onSaveCurrentSearch={onSaveCurrentSearch}
                    isLoading={isSavedSearchesLoading}
                />
            </div>

            {/* AI Search query textarea */}
            <div className={styles.querySection}>
                <Label className={styles.queryLabel}>AI Search</Label>
                <Textarea
                    className={styles.queryTextarea}
                    placeholder="Describe what you're looking for..."
                    value={query}
                    onChange={(_ev, data) => onQueryChange(data.value)}
                    onKeyDown={handleQueryKeyDown}
                    resize="vertical"
                    rows={6}
                    aria-label="AI search query"
                />
            </div>

            {/* Document Type Filter (Documents domain only) */}
            {showDocumentTypeFilter && (
                <div className={styles.filterSection}>
                    <FilterDropdown
                        label="Document Type"
                        options={filterOptions.documentTypes}
                        selectedValues={filters.documentTypes}
                        onChange={handleDocumentTypesChange}
                    />
                </div>
            )}

            {/* File Type Filter (Documents domain only) */}
            {showFileTypeFilter && (
                <div className={styles.filterSection}>
                    <FilterDropdown
                        label="File Type"
                        options={filterOptions.fileTypes}
                        selectedValues={filters.fileTypes}
                        onChange={handleFileTypesChange}
                    />
                </div>
            )}

            {/* Matter Type Filter (Documents + Matters domains) */}
            {showMatterTypeFilter && (
                <div className={styles.filterSection}>
                    <FilterDropdown
                        label="Matter Type"
                        options={filterOptions.matterTypes}
                        selectedValues={filters.matterTypes}
                        onChange={handleMatterTypesChange}
                    />
                </div>
            )}

            {/* Date Range Filter (all domains) */}
            {showDateRange && (
                <div className={styles.filterSection}>
                    <DateRangeFilter
                        value={filters.dateRange}
                        onChange={handleDateRangeChange}
                    />
                </div>
            )}

            {/* Threshold Slider (all domains) */}
            {showThreshold && (
                <div className={styles.filterSection}>
                    <div className={styles.sliderContainer}>
                        <div className={styles.sliderLabel}>
                            <Label className={styles.sectionLabel}>
                                Relevance Threshold
                            </Label>
                            <Text className={styles.sliderValue}>
                                {filters.threshold}%
                            </Text>
                        </div>
                        <Slider
                            min={0}
                            max={100}
                            value={filters.threshold}
                            onChange={handleThresholdChange}
                            disabled={isLoading}
                            aria-label="Relevance threshold"
                        />
                    </div>
                </div>
            )}

            {/* Search Mode Dropdown (all domains) */}
            {showMode && (
                <div className={styles.filterSection}>
                    <Label className={styles.sectionLabel}>Search Mode</Label>
                    <Dropdown
                        className={styles.modeDropdown}
                        value={
                            SEARCH_MODE_OPTIONS.find(
                                (opt) => opt.value === filters.searchMode,
                            )?.label ?? "Hybrid (RRF)"
                        }
                        selectedOptions={[filters.searchMode]}
                        onOptionSelect={handleModeChange}
                        disabled={isLoading}
                        aria-label="Search mode"
                    >
                        {SEARCH_MODE_OPTIONS.map((opt) => (
                            <Option key={opt.value} value={opt.value}>
                                {opt.label}
                            </Option>
                        ))}
                    </Dropdown>
                </div>
            )}

            {/* Search Button */}
            <Button
                className={styles.searchButton}
                appearance="primary"
                icon={<Search20Regular />}
                onClick={handleSearch}
                disabled={isLoading}
            >
                Search
            </Button>
        </div>
    );
};

export default SearchFilterPane;
