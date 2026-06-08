/**
 * SemanticSearch -- Main Application Shell
 *
 * Full-height flex column layout:
 *   - SearchCommandBar (top, fixed ~48px)
 *   - Toolbar (Grid | Map | Treemap | Timeline toggle, ~36px)
 *   - Content area (flex:1): SearchFilterPane (~280px, self-collapsing) + main results area
 *     - Filter pane includes: domain tabs, saved searches, query, filters, search button
 *     - VisualizationSettings overlay (upper-right) — view-conditional settings
 *   - StatusBar (bottom, fixed ~28px)
 *
 * Search flow:
 *   Documents domain  → useSemanticSearch (POST /api/ai/search)
 *   Matters/Projects/Invoices → useRecordSearch (POST /api/ai/search/records)
 *   Active results derived from activeDomain → routed to Grid, Map, Treemap, or Timeline.
 *
 * All colors use Fluent v9 design tokens per ADR-021 -- no hard-coded colors.
 */

import { useState, useCallback, useEffect, useRef, useMemo } from 'react';
import { makeStyles, tokens, Text, MessageBar, MessageBarBody, Divider } from '@fluentui/react-components';
import type {
  SearchDomain,
  SearchFilters,
  HybridMode,
  ViewMode,
  VisualizationColorBy,
  TimelineDateField,
  SavedSearch,
  DocumentSearchResult,
  RecordSearchResult,
  AppUrlParams,
} from './types';
import { RecordEntityTypes } from './types';
import { SearchFilterPane } from './components/SearchFilterPane';
import { ViewToggleToolbar } from './components/ViewToggleToolbar';
import { SearchCommandBar } from './components/SearchCommandBar';
import { StatusBar } from './components/StatusBar';
import { SearchResultsMap } from './components/SearchResultsMap';
import { SearchResultsTreemap } from './components/SearchResultsTreemap';
import { SearchResultsTimeline } from './components/SearchResultsTimeline';
import { VisualizationSettings } from './components/VisualizationSettings';
import { useSavedSearches } from './hooks/useSavedSearches';
import { useDocumentActions } from './hooks/useDocumentActions';
import { useSemanticSearch } from './hooks/useSemanticSearch';
import { useRecordSearch } from './hooks/useRecordSearch';
import { useFilterOptions } from './hooks/useFilterOptions';
import { useSearchViewDefinitions } from './hooks/useSearchViewDefinitions';
import { mapSearchResults } from './adapters/searchResultAdapter';
import { openEntityRecord } from './components/EntityRecordDialog';
import { DocumentPreviewDialog } from './components/DocumentPreviewDialog';
import { SearchResultsGrid } from './components/SearchResultsGrid';

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
  /**
   * Full parsed URL parameter envelope (FR-CP-01 / FR-CP-03).
   * Used to seed `filters` + `selectedTags` state on first render so the
   * auto-search effect (FR-13) fires with URL-derived filters on the first
   * network POST. Optional for backwards-compat with callers that haven't
   * threaded it yet.
   */
  urlParams?: AppUrlParams;
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
  searchMode: 'rrf',
};

/** Maps non-document domains to their Dataverse entity logical names */
const DOMAIN_RECORD_TYPES: Record<SearchDomain, string[]> = {
  documents: [],
  matters: [RecordEntityTypes.Matter],
  projects: [RecordEntityTypes.Project],
  invoices: [RecordEntityTypes.Invoice],
};

/**
 * Map the envelope `searchMode` literal (PCF/URL) to the API `HybridMode`
 * literal used by request bodies. See `EnvelopeSearchMode` doc-comment in
 * `types/index.ts` — `hybrid` is the envelope alias for the API's `rrf`.
 */
function envelopeSearchModeToHybridMode(mode: AppUrlParams['searchMode']): HybridMode | undefined {
  if (mode === 'hybrid') return 'rrf';
  if (mode === 'vectorOnly') return 'vectorOnly';
  if (mode === 'keywordOnly') return 'keywordOnly';
  return undefined;
}

/**
 * Seed `SearchFilters` state from URL-derived envelope params (FR-CP-03).
 * Used as the lazy `useState` initializer so values are present BEFORE the
 * first render commits and the auto-search effect fires — guaranteeing the
 * very first network POST uses URL-derived filters.
 *
 * Missing/undefined URL values fall back to `DEFAULT_FILTERS` so the page
 * still renders correctly when opened without any envelope.
 */
function buildInitialFilters(urlParams: AppUrlParams | undefined): SearchFilters {
  if (!urlParams) return DEFAULT_FILTERS;
  const mappedSearchMode = envelopeSearchModeToHybridMode(urlParams.searchMode);
  return {
    ...DEFAULT_FILTERS,
    fileTypes: urlParams.fileTypes ?? DEFAULT_FILTERS.fileTypes,
    dateRange: {
      from: urlParams.dateFrom ?? null,
      to: urlParams.dateTo ?? null,
    },
    threshold: urlParams.threshold ?? DEFAULT_FILTERS.threshold,
    searchMode: mappedSearchMode ?? DEFAULT_FILTERS.searchMode,
  };
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
  },
  // Unified command bar row (task 035 UI alignment v4 — operator UAT
  // 2026-06-04). Holds the SearchCommandBar (Refresh + Columns + Delete +
  // overflow), then the view tabs (icon-only), then visualization settings.
  // Everything right-aligned via `justifyContent: flex-end`.
  commandBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    height: '48px',
    minHeight: '48px',
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    columnGap: tokens.spacingHorizontalS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Vertical separator — replaces Fluent v9 `<Divider vertical>` which has
  // `flex-grow: 1` baked into its default style (Divider is designed to
  // *push apart* the items around it, the opposite of what we want here).
  // Fixed 1px width + flexShrink: 0 so it stays a thin static line.
  // Task 035 UI alignment v4 (2026-06-04).
  commandBarSeparator: {
    width: '1px',
    height: '20px',
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  contentRow: {
    display: 'flex',
    flexDirection: 'row',
    flex: 1,
    overflow: 'hidden',
  },
  mainArea: {
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  viewContainer: {
    position: 'relative',
    flex: 1,
    overflow: 'hidden',
  },
  errorBar: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  idleState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
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
  urlParams,
}) => {
  const styles = useStyles();

  // `isDark` is reserved for future per-component dark-mode branching.
  // (`initialScope` / `initialEntityId` are NO LONGER discarded — they are
  // wired into executeSearch's hook calls below per FR-CP-02. The previous
  // `void initialScope; void initialEntityId;` lines have been removed.)
  // TODO(task-042): once `useSemanticSearch` / `useRecordSearch` accept a
  // constructor-time `{ scope, entityId, searchIndexName }` arg, pass these
  // values there instead of plumbing them through executeSearch.
  void isDark;

  // --- UI State ---
  // `filters` + `selectedTags` are seeded from URL envelope params via lazy
  // `useState` initializers (FR-CP-03). Because lazy initialization runs on
  // the very first render — BEFORE the auto-search effect's first run — the
  // first POST issued by the auto-search effect (FR-13) carries the
  // URL-derived filter shape. Subsequent user edits go through setFilters /
  // setSelectedTags as normal.
  const [activeDomain, setActiveDomain] = useState<SearchDomain>(initialDomain);
  const [filters, setFilters] = useState<SearchFilters>(() => buildInitialFilters(urlParams));
  const [selectedTags, setSelectedTags] = useState<string[]>(() => urlParams?.tags ?? []);
  const [associatedOnly, setAssociatedOnly] = useState<boolean>(() => urlParams?.associatedOnly ?? false);
  const [query, setQuery] = useState<string>(initialQuery);
  const [viewMode, setViewMode] = useState<ViewMode>('grid');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [currentSearchName, setCurrentSearchName] = useState<string | null>(null);
  // Column-picker state lifted out of SearchResultsGrid so the unified
  // SearchCommandBar can host the picker UI alongside the other actions.
  // Task 035 UI alignment (2026-06-04). Reset when domain changes via the
  // useEffect below — different domains expose different column sets.
  const [hiddenColumns, setHiddenColumns] = useState<Set<string>>(new Set());
  useEffect(() => {
    setHiddenColumns(new Set());
  }, [activeDomain]);

  // --- Map view settings ---
  const [mapColorBy, setMapColorBy] = useState<VisualizationColorBy>('DocumentType');
  const [mapMinSimilarity, setMapMinSimilarity] = useState(60);

  // --- Treemap view settings ---
  const [treemapGroupBy, setTreemapGroupBy] = useState<VisualizationColorBy>('MatterType');
  const [treemapShowLabels, setTreemapShowLabels] = useState(true);

  // --- Timeline view settings ---
  const [timelineDateField, setTimelineDateField] = useState<TimelineDateField>('createdAt');
  const [timelineColorBy, setTimelineColorBy] = useState<VisualizationColorBy>('DocumentType');

  // --- Document Preview Dialog ---
  const [previewResult, setPreviewResult] = useState<DocumentSearchResult | null>(null);
  const [previewPosition, setPreviewPosition] = useState<{
    x: number;
    y: number;
  } | null>(null);

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
  const { savedSearches, isLoading: isSavedSearchesLoading, saveSearch } = useSavedSearches();

  // --- Document Actions ---
  const { openInWeb, openInDesktop, download, deleteDocuments, emailLink, sendToIndex } = useDocumentActions();

  // --- Active Domain Derivation ---
  const isDocDomain = activeDomain === 'documents';
  const rawResults = isDocDomain ? docResults : recordResults;

  // Client-side relevance threshold filter — hides results below the slider value
  const activeResults = useMemo(() => {
    const t = filters.threshold;
    if (t <= 0) return rawResults;
    return rawResults.filter(r => {
      const score = 'combinedScore' in r ? r.combinedScore : r.confidenceScore;
      return score * 100 >= t;
    });
  }, [rawResults, filters.threshold]);
  const activeTotalCount = isDocDomain ? docTotalCount : recordTotalCount;
  const activeSearchState = isDocDomain ? docSearchState : recordSearchState;
  const activeHasMore = isDocDomain ? docHasMore : recordHasMore;
  const activeErrorMessage = isDocDomain ? docErrorMessage : recordErrorMessage;
  const activeSearchTime = isDocDomain ? docSearchTime : recordSearchTime;
  const isSearching = activeSearchState === 'loading';
  const isLoadingMore = activeSearchState === 'loadingMore';

  // Detect validation-type errors (empty query) vs real errors
  const isValidationHint = activeErrorMessage ? /query.*(required|empty|missing)/i.test(activeErrorMessage) : false;

  // --- Grid View Definitions (from sprk_gridconfiguration, fallback to domainColumns.ts) ---
  const { columns: viewColumns } = useSearchViewDefinitions(activeDomain);

  // --- Map search results to IDatasetRecord for UniversalDatasetGrid ---
  const gridRecords = useMemo(() => mapSearchResults(activeResults, activeDomain), [activeResults, activeDomain]);

  // =============================================
  // Search dispatch — routes to correct hook by domain
  // =============================================

  const executeSearch = useCallback(
    (searchQuery: string, searchFilters: SearchFilters, domain: SearchDomain) => {
      setSelectedIds([]);
      // initialScope + initialEntityId + selectedTags + associatedOnly are
      // captured in the closure (no longer discarded per FR-CP-02). The
      // current hook surface (`search(query, filters)` / `search(query,
      // recordTypes, filters)`) does not yet thread these through to the
      // request body — task 042 extends the hook signatures to accept
      // `{ scope, entityId, searchIndexName, tags, associatedOnly }` at
      // construction time, after which the values seeded here will flow into
      // the first POST automatically. Until then, log them for traceability
      // so UAT can verify they reached App.tsx even before task 042 lands.
      // TODO(task-042): pass initialScope / initialEntityId / selectedTags /
      // associatedOnly / urlParams.searchIndexName as hook constructor args.
      if (initialScope || initialEntityId || selectedTags.length > 0 || associatedOnly) {
        console.debug('[SemanticSearch] URL-seeded scope/entity/tags/associatedOnly carried into executeSearch:', {
          initialScope,
          initialEntityId,
          selectedTags,
          associatedOnly,
        });
      }
      if (domain === 'documents') {
        searchDocuments(searchQuery, searchFilters);
      } else {
        searchRecords(searchQuery, DOMAIN_RECORD_TYPES[domain], searchFilters);
      }
    },
    [searchDocuments, searchRecords, initialScope, initialEntityId, selectedTags, associatedOnly]
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
    [filters, executeSearch]
  );

  const handleDomainChange = useCallback((domain: SearchDomain) => {
    setActiveDomain(domain);
    setSelectedIds([]);
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
    [activeDomain, query, executeSearch]
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

  const handleSort = useCallback((_columnKey: string, _direction: 'asc' | 'desc') => {
    // Client-side sort handled by Fluent DataGrid internally
  }, []);

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
    [deleteDocuments, isDocDomain, query, filters, searchDocuments]
  );

  /** Handle click on a result in any visualization view */
  const handleResultClick = useCallback(
    (resultId: string, domain: SearchDomain, clickPosition?: { x: number; y: number }) => {
      if (domain === 'documents') {
        // For documents: open preview dialog
        const docResult = docResults.find(r => r.documentId === resultId);
        if (docResult) {
          setPreviewResult(docResult);
          setPreviewPosition(clickPosition ?? null);
          return;
        }
      }
      // For non-documents (or if doc not found): open entity record directly
      openEntityRecord(resultId, domain);
    },
    [docResults]
  );

  const handlePreviewClose = useCallback(() => {
    setPreviewResult(null);
    setPreviewPosition(null);
  }, []);

  const handleSelectSavedSearch = useCallback(
    (search: SavedSearch) => {
      setCurrentSearchName(search.name);
      setActiveDomain(search.searchDomain);
      setFilters(search.filters);
      setViewMode(search.viewMode);
      setQuery(search.query);
      // Restore per-view settings if saved
      if (search.graphClusterBy) {
        setMapColorBy(search.graphClusterBy);
        setTreemapGroupBy(search.graphClusterBy);
        setTimelineColorBy(search.graphClusterBy);
      }
      executeSearch(search.query, search.filters, search.searchDomain);
    },
    [executeSearch]
  );

  // Cancel handler — clear AI Search query + filters + saved-search selection.
  // Wired to SearchFilterPane.onCancel (task 035 UI alignment, 2026-06-04).
  // Matches the EventsPage Calendar widget "Clear" semantics: returns the
  // panel to its initial state without executing a search. Also clears the
  // URL-seeded `selectedTags` + `associatedOnly` state added in FR-CP-03
  // (task 041) so "Clear" returns the page to true defaults, not to the
  // initial-URL-derived shape.
  const handleCancelSearch = useCallback(() => {
    setQuery('');
    setFilters(DEFAULT_FILTERS);
    setSelectedTags([]);
    setAssociatedOnly(false);
    setCurrentSearchName(null);
  }, []);

  const handleSaveCurrentSearch = useCallback(() => {
    const name = window.prompt('Enter a name for this saved search:');
    if (!name) return;
    saveSearch({
      name,
      searchDomain: activeDomain,
      query,
      filters,
      viewMode,
      columns: [],
      sortColumn: 'similarity',
      sortDirection: 'desc',
      graphClusterBy: mapColorBy,
    });
    setCurrentSearchName(name);
  }, [saveSearch, activeDomain, query, filters, viewMode, mapColorBy]);

  // =============================================
  // Auto-search on load (FR-13)
  // =============================================

  const autoSearchFired = useRef(false);

  useEffect(() => {
    if (autoSearchFired.current) return;

    // Auto-load saved search by ID from URL params
    if (initialSavedSearchId && !isSavedSearchesLoading) {
      const match = savedSearches.find(s => s.id === initialSavedSearchId);
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
      {/* Unified command bar — single Power Apps OOB-style row:
            [Refresh] [Delete] [...] | [Columns] | [Grid/Network/Treemap/Timeline] | [Settings]
          Task 035 UI alignment (2026-06-04). Replaces the prior two-row layout
          (SearchCommandBar + separate ViewToggleToolbar). */}
      <div className={styles.commandBar}>
        <SearchCommandBar
          selectedIds={selectedIds}
          activeDomain={activeDomain}
          onDelete={handleDelete}
          onRefresh={handleRefresh}
          onEmailLink={id => emailLink(id)}
          onOpenInWeb={id => openInWeb(id)}
          onOpenInDesktop={id => openInDesktop(id)}
          onDownload={id => download(id)}
          onSendToIndex={ids => sendToIndex(ids)}
          onSaveSearch={handleSaveCurrentSearch}
          columns={viewColumns}
          hiddenColumns={hiddenColumns}
          onHiddenColumnsChange={setHiddenColumns}
        />
        <span className={styles.commandBarSeparator} aria-hidden="true" />
        <ViewToggleToolbar viewMode={viewMode} onViewModeChange={setViewMode} />
        <span className={styles.commandBarSeparator} aria-hidden="true" />
        <VisualizationSettings
          viewMode={viewMode}
          threshold={filters.threshold}
          onThresholdChange={value => setFilters(prev => ({ ...prev, threshold: value }))}
          searchMode={filters.searchMode}
          onSearchModeChange={mode => setFilters(prev => ({ ...prev, searchMode: mode }))}
          mapColorBy={mapColorBy}
          onMapColorByChange={setMapColorBy}
          mapMinSimilarity={mapMinSimilarity}
          onMapMinSimilarityChange={setMapMinSimilarity}
          treemapGroupBy={treemapGroupBy}
          onTreemapGroupByChange={setTreemapGroupBy}
          treemapShowLabels={treemapShowLabels}
          onTreemapShowLabelsChange={setTreemapShowLabels}
          timelineDateField={timelineDateField}
          onTimelineDateFieldChange={setTimelineDateField}
          timelineColorBy={timelineColorBy}
          onTimelineColorByChange={setTimelineColorBy}
        />
      </div>

      {/* Content row: filter pane + main results area */}
      <div className={styles.contentRow}>
        {/* Left pane: SearchFilterPane (self-collapsing) */}
        <SearchFilterPane
          activeDomain={activeDomain}
          onDomainChange={handleDomainChange}
          onDomainSearch={handleSearch}
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
          onCancel={handleCancelSearch}
        />

        {/* Main area: grid or graph results */}
        <div className={styles.mainArea}>
          {/* Error / info message bar */}
          {activeErrorMessage && (
            <div className={styles.errorBar}>
              <MessageBar intent={isValidationHint ? 'info' : 'error'}>
                <MessageBarBody>
                  {isValidationHint ? 'Please enter search criteria to get results.' : activeErrorMessage}
                </MessageBarBody>
              </MessageBar>
            </div>
          )}

          {/* Idle state — no search executed yet */}
          {activeSearchState === 'idle' && !activeErrorMessage && (
            <div className={styles.idleState}>
              <Text size={400} weight="semibold">
                Search across {activeDomain}
              </Text>
              <Text size={200}>Select a saved search or switch domain tabs to begin</Text>
            </div>
          )}

          {/* Active results — 4-way view routing */}
          {activeSearchState !== 'idle' && !activeErrorMessage && (
            <>
              {viewMode === 'grid' && (
                <SearchResultsGrid
                  records={gridRecords}
                  totalCount={activeTotalCount}
                  isLoading={isSearching}
                  isLoadingMore={isLoadingMore}
                  hasMore={activeHasMore}
                  activeDomain={activeDomain}
                  columns={viewColumns}
                  hiddenColumns={hiddenColumns}
                  onLoadMore={handleLoadMore}
                  onSelectionChange={handleSelectionChange}
                  onSort={handleSort}
                />
              )}

              {viewMode === 'map' && (
                <div className={styles.viewContainer}>
                  <SearchResultsMap
                    results={activeResults as (DocumentSearchResult | RecordSearchResult)[]}
                    colorBy={mapColorBy}
                    minSimilarity={mapMinSimilarity}
                    isLoading={isSearching}
                    activeDomain={activeDomain}
                    onResultClick={handleResultClick}
                  />
                </div>
              )}

              {viewMode === 'treemap' && (
                <div className={styles.viewContainer}>
                  <SearchResultsTreemap
                    results={activeResults as (DocumentSearchResult | RecordSearchResult)[]}
                    groupBy={treemapGroupBy}
                    showLabels={treemapShowLabels}
                    isLoading={isSearching}
                    activeDomain={activeDomain}
                    onResultClick={handleResultClick}
                  />
                </div>
              )}

              {viewMode === 'timeline' && (
                <div className={styles.viewContainer}>
                  <SearchResultsTimeline
                    results={activeResults as (DocumentSearchResult | RecordSearchResult)[]}
                    dateField={timelineDateField}
                    colorBy={timelineColorBy}
                    isLoading={isSearching}
                    activeDomain={activeDomain}
                    onResultClick={handleResultClick}
                  />
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* Bottom row: StatusBar */}
      <StatusBar
        totalCount={activeTotalCount > 0 ? activeTotalCount : null}
        searchTime={activeSearchTime}
        version="1.0.0"
      />

      {/* Document preview dialog — opened when clicking a document data point */}
      <DocumentPreviewDialog
        open={previewResult !== null}
        result={previewResult}
        anchorPosition={previewPosition}
        onClose={handlePreviewClose}
      />
    </div>
  );
};

export default App;
