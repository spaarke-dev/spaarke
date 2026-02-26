/**
 * SearchFilterPane -- Collapsible left-side filter pane
 *
 * Layout (top to bottom):
 *   1. "Search Criteria" header with collapse chevron
 *   2. Domain tabs (Documents / Matters / Projects / Invoices)
 *   3. Saved Searches selector
 *   4. AI Search query textarea
 *   5. Dashed separator
 *   6. Domain-aware filter dropdowns (Document Type, File Type, Matter Type)
 *   7. Date range
 *   8. Search button
 *
 * Relevance Threshold and Search Mode live in VisualizationSettings (overlay).
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
    Label,
    Text,
} from "@fluentui/react-components";
import {
    ChevronDoubleLeft20Regular,
    ChevronDoubleRight20Regular,
    Search20Regular,
} from "@fluentui/react-icons";
import type { SearchDomain, SearchFilters, FilterOption, SavedSearch } from "../types";
import { SearchDomainTabs } from "./SearchDomainTabs";
import { FilterDropdown } from "./FilterDropdown";
import { DateRangeFilter } from "./DateRangeFilter";
import { SavedSearchSelector } from "./SavedSearchSelector";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SearchFilterPaneProps {
    /** Currently active search domain — controls which filter sections are visible */
    activeDomain: SearchDomain;
    /** Callback when the user switches domain tabs */
    onDomainChange: (domain: SearchDomain) => void;
    /** Callback to trigger a domain-change search (query + domain) */
    onDomainSearch: (query: string, domain: SearchDomain) => void;
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
    paneTitle: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginBottom: "20px",
    },
    collapseButton: {
        minWidth: "auto",
    },
    domainTabsSection: {
        marginBottom: tokens.spacingVerticalM,
    },
    savedSearchSection: {
        marginBottom: tokens.spacingVerticalM,
    },
    querySection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        marginBottom: tokens.spacingVerticalM,
    },
    queryLabel: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
    queryTextarea: {
        width: "100%",
    },
    separator: {
        borderBottom: `1px dashed ${tokens.colorNeutralStroke2}`,
        marginTop: tokens.spacingVerticalM,
        marginBottom: tokens.spacingVerticalM,
    },
    filterSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        marginBottom: tokens.spacingVerticalM,
    },
    searchButton: {
        marginTop: tokens.spacingVerticalM,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SearchFilterPane: React.FC<SearchFilterPaneProps> = ({
    activeDomain,
    onDomainChange,
    onDomainSearch,
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

    // --- Handlers ---

    const handleToggleCollapse = useCallback(() => {
        setIsCollapsed((prev) => !prev);
    }, []);

    const handleSearch = useCallback(() => {
        onSearch(query, filters);
    }, [onSearch, query, filters]);

    const handleQueryKeyDown = useCallback(
        (ev: React.KeyboardEvent) => {
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
            {/* Header: "Search Criteria" title + collapse chevron */}
            <div className={styles.paneTitle}>
                <Text weight="semibold" size={400}>Search Criteria</Text>
                <Button
                    className={styles.collapseButton}
                    appearance="subtle"
                    size="small"
                    icon={<ChevronDoubleLeft20Regular />}
                    onClick={handleToggleCollapse}
                    aria-label="Collapse filters"
                />
            </div>

            {/* Domain tabs (Documents / Matters / Projects / Invoices) */}
            <div className={styles.domainTabsSection}>
                <SearchDomainTabs
                    activeDomain={activeDomain}
                    onDomainChange={onDomainChange}
                    query={query}
                    onSearch={onDomainSearch}
                />
            </div>

            {/* Dotted divider between domain tabs and saved searches */}
            <div className={styles.separator} />

            {/* Saved Searches */}
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

            {/* Dashed separator between query and filters */}
            <div className={styles.separator} />

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

            {/* Dotted divider between type filters and date range */}
            <div className={styles.separator} />

            {/* Date Range Filter (all domains) */}
            <div className={styles.filterSection}>
                <DateRangeFilter
                    value={filters.dateRange}
                    onChange={handleDateRangeChange}
                />
            </div>

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
