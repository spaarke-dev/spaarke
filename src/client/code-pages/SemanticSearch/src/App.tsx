/**
 * SemanticSearch -- Main Application Shell
 *
 * Full-height flex column layout:
 *   - SearchCommandBar (top, fixed ~48px)
 *   - SearchDomainTabs (between toolbar and content)
 *   - Toolbar (saved search selector + view toggle, ~36px)
 *   - Content area (flex:1): SearchFilterPane (~280px) + main results area (flex:1)
 *   - StatusBar (bottom, fixed ~28px)
 *
 * Search flow:
 *   Documents domain  → useSemanticSearch (POST /api/ai/search)
 *   Matters/Projects/Invoices → useRecordSearch (POST /api/ai/search/records)
 *   Active results derived from activeDomain → routed to Grid or Graph view.
 *
 * All colors use Fluent v9 design tokens per ADR-021 -- no hard-coded colors.
 */

import { useState, useCallback, useEffect, useRef, useMemo } from "react";
import {
    makeStyles,
    tokens,
    Text,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";
import type {
    SearchDomain,
    SearchFilters,
    ViewMode,
    GraphClusterBy,
    SavedSearch,
    DocumentSearchResult,
    RecordSearchResult,
} from "./types";
import { RecordEntityTypes } from "./types";
import { SearchDomainTabs } from "./components/SearchDomainTabs";
import { SearchFilterPane } from "./components/SearchFilterPane";
import { ViewToggleToolbar } from "./components/ViewToggleToolbar";
import { SearchCommandBar } from "./components/SearchCommandBar";
import { StatusBar } from "./components/StatusBar";
import { SearchResultsGrid } from "./components/SearchResultsGrid";
import { SearchResultsGraph } from "./components/SearchResultsGraph";
import { useSavedSearches } from "./hooks/useSavedSearches";
import { useDocumentActions } from "./hooks/useDocumentActions";
import { useSemanticSearch } from "./hooks/useSemanticSearch";
import { useRecordSearch } from "./hooks/useRecordSearch";
import { useFilterOptions } from "./hooks/useFilterOptions";
import { useClusterLayout } from "./hooks/useClusterLayout";
import { getColumnsForDomain } from "./config/domainColumns";
import { openEntityRecord } from "./components/EntityRecordDialog";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AppProps {
    /** Pre-filled search query from URL */
    initialQuery: string;
    /** Active search domain tab */
    initialDomain: SearchDomain;
    /** Search scope filter */
    initialScope: string;
    /** Entity record ID for contextual search */
    initialEntityId: string;
    /** Saved search to load on startup */
    initialSavedSearchId: string;
    /** Whether the current theme is dark */
    isDark: boolean;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DEFAULT_FILTERS: SearchFilters = {
    documentTypes: [],
    fileTypes: [],
    matterTypes: [],
    dateRange: { from: null, to: null },
    threshold: 50,
    searchMode: "rrf",
};

/** Maps non-document domains to their Dataverse entity logical names */
const DOMAIN_RECORD_TYPES: Record<SearchDomain, string[]> = {
    documents: [],
    matters: [RecordEntityTypes.Matter],
    projects: [RecordEntityTypes.Project],
    invoices: [RecordEntityTypes.Invoice],
};

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        height: "100vh",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        overflow: "hidden",
    },
    commandBar: {
        display: "flex",
        alignItems: "center",
        height: "48px",
        minHeight: "48px",
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground1,
    },
    domainTabsRow: {
        display: "flex",
        alignItems: "center",
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground1,
    },
    toolbar: {
        display: "flex",
        alignItems: "center",
        height: "36px",
        minHeight: "36px",
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        gap: tokens.spacingHorizontalS,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
    },
    contentRow: {
        display: "flex",
        flexDirection: "row",
        flex: 1,
        overflow: "hidden",
    },
    mainArea: {
        display: "flex",
        flexDirection: "column",
        flex: 1,
        overflow: "hidden",
        backgroundColor: tokens.colorNeutralBackground1,
    },
    errorBar: {
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
    },
    idleState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        flex: 1,
        gap: tokens.spacingVerticalS,
        color: tokens.colorNeutralForeground3,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const App: React.FC<AppProps> = ({
    initialQuery,
    initialDomain,
    initialScope,
    initialEntityId,
    initialSavedSearchId,
    isDark,
}) => {
    const styles = useStyles();

    // Suppress unused-var warnings for props reserved for future use
    void initialScope;
    void initialEntityId;
    void isDark;

    // --- UI State ---
    const [activeDomain, setActiveDomain] = useState<SearchDomain>(initialDomain);
    const [filters, setFilters] = useState<SearchFilters>(DEFAULT_FILTERS);
    const [query, setQuery] = useState<string>(initialQuery);
    const [viewMode, setViewMode] = useState<ViewMode>("grid");
    const [clusterBy, setClusterBy] = useState<GraphClusterBy>("MatterType");
    const [selectedIds, setSelectedIds] = useState<string[]>([]);
    const [currentSearchName, setCurrentSearchName] = useState<string | null>(null);
    const [expandedClusterId, setExpandedClusterId] = useState<string | null>(null);

    // --- Search Hooks ---
    const {
        results: docResults,
        totalCount: docTotalCount,
        searchState: docSearchState,
        hasMore: docHasMore,
        errorMessage: docErrorMessage,
        searchTime: docSearchTime,
        search: searchDocuments,
        loadMore: loadMoreDocuments,
    } = useSemanticSearch();

    const {
        results: recordResults,
        totalCount: recordTotalCount,
        searchState: recordSearchState,
        hasMore: recordHasMore,
        errorMessage: recordErrorMessage,
        searchTime: recordSearchTime,
        search: searchRecords,
        loadMore: loadMoreRecords,
    } = useRecordSearch();

    // --- Filter Options (Dataverse WebAPI) ---
    const {
        documentTypes: filterDocTypes,
        fileTypes: filterFileTypes,
        matterTypes: filterMatterTypes,
    } = useFilterOptions();

    // --- Saved Searches ---
    const {
        savedSearches,
        isLoading: isSavedSearchesLoading,
        saveSearch,
    } = useSavedSearches();

    // --- Document Actions ---
    const {
        openInWeb,
        openInDesktop,
        download,
        deleteDocuments,
        emailLink,
        sendToIndex,
    } = useDocumentActions();

    // --- Active Domain Derivation ---
    const isDocDomain = activeDomain === "documents";
    const activeResults = isDocDomain ? docResults : recordResults;
    const activeTotalCount = isDocDomain ? docTotalCount : recordTotalCount;
    const activeSearchState = isDocDomain ? docSearchState : recordSearchState;
    const activeHasMore = isDocDomain ? docHasMore : recordHasMore;
    const activeErrorMessage = isDocDomain ? docErrorMessage : recordErrorMessage;
    const activeSearchTime = isDocDomain ? docSearchTime : recordSearchTime;
    const isSearching = activeSearchState === "loading";
    const isLoadingMore = activeSearchState === "loadingMore";

    // --- Grid Columns (domain-specific) ---
    const gridColumns = useMemo(
        () => getColumnsForDomain(activeDomain),
        [activeDomain],
    );

    // --- Cluster Layout (graph view) ---
    const { nodes, edges, isSimulating } = useClusterLayout(
        activeResults as (DocumentSearchResult | RecordSearchResult)[],
        clusterBy,
        expandedClusterId,
    );

    // =============================================
    // Search dispatch — routes to correct hook by domain
    // =============================================

    const executeSearch = useCallback(
        (searchQuery: string, searchFilters: SearchFilters, domain: SearchDomain) => {
            setSelectedIds([]);
            setExpandedClusterId(null);
            if (domain === "documents") {
                searchDocuments(searchQuery, searchFilters);
            } else {
                searchRecords(searchQuery, DOMAIN_RECORD_TYPES[domain], searchFilters);
            }
        },
        [searchDocuments, searchRecords],
    );

    // =============================================
    // Handlers
    // =============================================

    /** Called by SearchDomainTabs when user clicks a domain tab */
    const handleSearch = useCallback(
        (searchQuery: string, domain: SearchDomain) => {
            setQuery(searchQuery);
            executeSearch(searchQuery, filters, domain);
        },
        [filters, executeSearch],
    );

    const handleDomainChange = useCallback((domain: SearchDomain) => {
        setActiveDomain(domain);
        setSelectedIds([]);
        setExpandedClusterId(null);
    }, []);

    const handleFiltersChange = useCallback((newFilters: SearchFilters) => {
        setFilters(newFilters);
    }, []);

    /** Called by SearchFilterPane Search button — uses App-level query */
    const handleFilterSearch = useCallback(
        (_filterQuery: string, searchFilters: SearchFilters) => {
            setFilters(searchFilters);
            executeSearch(query, searchFilters, activeDomain);
        },
        [activeDomain, query, executeSearch],
    );

    const handleRefresh = useCallback(() => {
        executeSearch(query, filters, activeDomain);
    }, [activeDomain, query, filters, executeSearch]);

    const handleLoadMore = useCallback(() => {
        if (isDocDomain) {
            loadMoreDocuments();
        } else {
            loadMoreRecords();
        }
    }, [isDocDomain, loadMoreDocuments, loadMoreRecords]);

    const handleSelectionChange = useCallback((ids: string[]) => {
        setSelectedIds(ids);
    }, []);

    const handleSort = useCallback(
        (_columnKey: string, _direction: "asc" | "desc") => {
            // Client-side sort handled by Fluent DataGrid internally
        },
        [],
    );

    const handleDelete = useCallback(
        (ids: string[]) => {
            deleteDocuments(ids, () => {
                setSelectedIds([]);
                // Re-fetch after deletion (documents only)
                if (isDocDomain) {
                    searchDocuments(query, filters);
                }
            });
        },
        [deleteDocuments, isDocDomain, query, filters, searchDocuments],
    );

    /** Graph node click — toggle cluster expansion or open entity record */
    const handleGraphNodeClick = useCallback(
        (nodeId: string, nodeType: "cluster" | "record") => {
            if (nodeType === "cluster") {
                setExpandedClusterId((prev) => (prev === nodeId ? null : nodeId));
            } else {
                const recordId = nodeId.replace("record-", "");
                openEntityRecord(recordId, activeDomain);
            }
        },
        [activeDomain],
    );

    const handleSelectSavedSearch = useCallback(
        (search: SavedSearch) => {
            setCurrentSearchName(search.name);
            setActiveDomain(search.searchDomain);
            setFilters(search.filters);
            setViewMode(search.viewMode);
            setQuery(search.query);
            if (search.graphClusterBy) setClusterBy(search.graphClusterBy);
            executeSearch(search.query, search.filters, search.searchDomain);
        },
        [executeSearch],
    );

    const handleSaveCurrentSearch = useCallback(() => {
        const name = window.prompt("Enter a name for this saved search:");
        if (!name) return;
        saveSearch({
            name,
            searchDomain: activeDomain,
            query,
            filters,
            viewMode,
            columns: [],
            sortColumn: "similarity",
            sortDirection: "desc",
            graphClusterBy: clusterBy,
        });
        setCurrentSearchName(name);
    }, [saveSearch, activeDomain, query, filters, viewMode, clusterBy]);

    // =============================================
    // Auto-search on load (FR-13)
    // =============================================

    const autoSearchFired = useRef(false);

    useEffect(() => {
        if (autoSearchFired.current) return;

        // Auto-load saved search by ID from URL params
        if (initialSavedSearchId && !isSavedSearchesLoading) {
            const match = savedSearches.find((s) => s.id === initialSavedSearchId);
            if (match) {
                autoSearchFired.current = true;
                handleSelectSavedSearch(match);
                return;
            }
            autoSearchFired.current = true;
        }

        // Auto-execute search if query param is present
        if (initialQuery && !autoSearchFired.current) {
            autoSearchFired.current = true;
            executeSearch(initialQuery, filters, activeDomain);
        }
    }, [
        initialQuery,
        initialSavedSearchId,
        savedSearches,
        isSavedSearchesLoading,
        handleSelectSavedSearch,
        executeSearch,
        filters,
        activeDomain,
    ]);

    // =============================================
    // Render
    // =============================================

    return (
        <div className={styles.root} role="main" aria-label="Semantic Search">
            {/* Top row: SearchCommandBar */}
            <div className={styles.commandBar}>
                <SearchCommandBar
                    selectedIds={selectedIds}
                    activeDomain={activeDomain}
                    onDelete={handleDelete}
                    onRefresh={handleRefresh}
                    onEmailLink={(id) => emailLink(id)}
                    onOpenInWeb={(id) => openInWeb(id)}
                    onOpenInDesktop={(id) => openInDesktop(id)}
                    onDownload={(id) => download(id)}
                    onSendToIndex={(ids) => sendToIndex(ids)}
                    onSaveSearch={handleSaveCurrentSearch}
                />
            </div>

            {/* Domain tabs row */}
            <div className={styles.domainTabsRow}>
                <SearchDomainTabs
                    activeDomain={activeDomain}
                    onDomainChange={handleDomainChange}
                    query={query}
                    onSearch={handleSearch}
                />
            </div>

            {/* Third row: Toolbar (saved search selector + view toggle) */}
            <div className={styles.toolbar}>
                <ViewToggleToolbar
                    viewMode={viewMode}
                    onViewModeChange={setViewMode}
                    clusterBy={clusterBy}
                    onClusterByChange={setClusterBy}
                />
            </div>

            {/* Content row: filter pane + main results area */}
            <div className={styles.contentRow}>
                {/* Left pane: SearchFilterPane */}
                <SearchFilterPane
                    activeDomain={activeDomain}
                    filters={filters}
                    onFiltersChange={handleFiltersChange}
                    onSearch={handleFilterSearch}
                    filterOptions={{
                        documentTypes: filterDocTypes,
                        fileTypes: filterFileTypes,
                        matterTypes: filterMatterTypes,
                    }}
                    isLoading={isSearching}
                    query={query}
                    onQueryChange={setQuery}
                    savedSearches={savedSearches}
                    currentSearchName={currentSearchName}
                    onSelectSavedSearch={handleSelectSavedSearch}
                    onSaveCurrentSearch={handleSaveCurrentSearch}
                    isSavedSearchesLoading={isSavedSearchesLoading}
                />

                {/* Main area: grid or graph results */}
                <div className={styles.mainArea}>
                    {/* Error message bar */}
                    {activeErrorMessage && (
                        <div className={styles.errorBar}>
                            <MessageBar intent="error">
                                <MessageBarBody>{activeErrorMessage}</MessageBarBody>
                            </MessageBar>
                        </div>
                    )}

                    {/* Idle state — no search executed yet */}
                    {activeSearchState === "idle" && !activeErrorMessage && (
                        <div className={styles.idleState}>
                            <Text size={400} weight="semibold">
                                Search across {activeDomain}
                            </Text>
                            <Text size={200}>
                                Select a saved search or switch domain tabs to begin
                            </Text>
                        </div>
                    )}

                    {/* Active results — Grid or Graph view */}
                    {activeSearchState !== "idle" && !activeErrorMessage && (
                        viewMode === "grid" ? (
                            <SearchResultsGrid
                                results={activeResults as (DocumentSearchResult | RecordSearchResult)[]}
                                totalCount={activeTotalCount}
                                isLoading={isSearching}
                                isLoadingMore={isLoadingMore}
                                hasMore={activeHasMore}
                                activeDomain={activeDomain}
                                columns={gridColumns}
                                onLoadMore={handleLoadMore}
                                onSelectionChange={handleSelectionChange}
                                onSort={handleSort}
                            />
                        ) : (
                            <SearchResultsGraph
                                nodes={nodes}
                                edges={edges}
                                onNodeClick={handleGraphNodeClick}
                                clusterBy={clusterBy}
                                isLoading={isSearching || isSimulating}
                                resultCount={activeTotalCount}
                                expandedClusterId={expandedClusterId}
                            />
                        )
                    )}
                </div>
            </div>

            {/* Bottom row: StatusBar */}
            <StatusBar
                totalCount={activeTotalCount > 0 ? activeTotalCount : null}
                searchTime={activeSearchTime}
                version="1.0.0"
            />
        </div>
    );
};

export default App;
