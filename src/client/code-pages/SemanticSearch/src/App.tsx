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
import { makeStyles, tokens, Text, MessageBar, MessageBarBody } from '@fluentui/react-components';
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
import { listActiveSearchIndexes, type AiSearchIndexRow } from './services/aiSearchIndexService';
import { buildSearchRequestFragment, type SearchRequestFragment } from './services/targetEntityNormalize';

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
 * Phase G (2026-06-10) — derive the existing `SearchDomain` (used for grid
 * column defaults + result adapter dispatch) from the request fragment's
 * `scope`/`entityType`. The Search Index dropdown is the new source of
 * truth; `activeDomain` is now a *projection* of the selected row.
 *
 * Mapping:
 *   - scope='all' → 'documents' (uses useSemanticSearch — searches docs)
 *   - entityType='matter' → 'matters' (uses useRecordSearch — searches records)
 *   - entityType='project' → 'projects'
 *   - entityType='invoice' → 'invoices'
 *   - entityType='document' → 'documents' (docs in this index)
 *   - entityType='event' / 'workassignment' / others → 'documents'
 *     (no dedicated SearchDomain — the fragment still narrows the search
 *     correctly via useSemanticSearch's entity-scope branch.)
 */
function deriveSearchDomain(fragment: SearchRequestFragment | null | undefined): SearchDomain {
  if (!fragment) return 'documents';
  if (fragment.scope === 'all') return 'documents';
  switch (fragment.entityType) {
    case 'matter':
      return 'matters';
    case 'project':
      return 'projects';
    case 'invoice':
      return 'invoices';
    default:
      // 'document' / 'event' / 'workassignment' / future entity types
      // all flow through useSemanticSearch.
      return 'documents';
  }
}

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
  const [filters, setFilters] = useState<SearchFilters>(() => buildInitialFilters(urlParams));
  const [selectedTags, setSelectedTags] = useState<string[]>(() => urlParams?.tags ?? []);
  const [associatedOnly, setAssociatedOnly] = useState<boolean>(() => urlParams?.associatedOnly ?? false);
  const [query, setQuery] = useState<string>(initialQuery);
  const [viewMode, setViewMode] = useState<ViewMode>('grid');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [currentSearchName, setCurrentSearchName] = useState<string | null>(null);

  // Phase G — Search Index catalog state (sourced from sprk_aisearchindex via
  // direct Dataverse Web API in `aiSearchIndexService.listActiveSearchIndexes`).
  // The selected row becomes the single source of truth for: searchIndexName,
  // entity scope (via `buildSearchRequestFragment`), and (transitively) the
  // `activeDomain` derivation used for grid/view defaults.
  const [searchIndexes, setSearchIndexes] = useState<AiSearchIndexRow[]>([]);
  const [isLoadingSearchIndexes, setIsLoadingSearchIndexes] = useState<boolean>(true);
  const [selectedSearchIndexId, setSelectedSearchIndexId] = useState<string>('');

  // `activeDomain` is now DERIVED from the selected row's fragment. We still
  // keep it as state so existing code that branches on the domain (grid
  // columns, gridRecords adapter, visualization defaults) works unchanged.
  // The legacy `initialDomain` URL envelope is honored only as a fallback
  // when no rows are loaded yet (first render before the catalog fetch).
  const [activeDomain, setActiveDomain] = useState<SearchDomain>(initialDomain);

  // Column-picker state lifted out of SearchResultsGrid so the unified
  // SearchCommandBar can host the picker UI alongside the other actions.
  // Reset when domain changes via the useEffect below — different domains
  // expose different column sets.
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

  // Pass-through: trust the BFF results.
  //
  // multi-container-multi-index-r1 UAT 2026-06-09: the previous client-side
  // threshold filter rejected all results when the BFF returned the
  // associated-only path (entity-scope + empty-query case) because those
  // docs come from Dataverse direct (score=0, not AI-ranked). That made the
  // viewer modal show "No results found" even though the BFF returned 25 docs.
  //
  // The BFF already applies the threshold to its semantic-search path
  // (SemanticSearchService.BuildFilter), and associated-only docs are
  // already entity-scoped (guaranteed relevant). Double-filtering client-side
  // was redundant for the semantic path and incorrect for the associated path.
  //
  // Future enhancement: if a user expects threshold to also filter the
  // associated path's results, that's a server-side concern — the BFF should
  // be the single arbiter of relevance, not the client.
  const activeResults = useMemo(() => {
    return rawResults;
    // Original (buggy for associatedOnly) — preserved as a comment for context:
    // const t = filters.threshold;
    // if (t <= 0) return rawResults;
    // return rawResults.filter(r => {
    //   const score = 'combinedScore' in r ? r.combinedScore : r.confidenceScore;
    //   return score * 100 >= t;
    // });
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

  // multi-container-multi-index-r1 UAT 2026-06-09 fix: scope/entityId from
  // the URL envelope are used ONLY for the initial auto-fire — they mirror
  // the PCF's entity-scoped view. Once the user types a query (or otherwise
  // initiates a search themselves), the scope drops to tenant-wide so the
  // code page behaves like its direct-opened counterpart. Without this the
  // matter scope persisted into every subsequent search → user could only
  // search within the matter they came from, which felt like the search
  // was "broken / cached" (vs working fine when the code page is opened
  // standalone).
  const [hasUserInitiatedSearch, setHasUserInitiatedSearch] = useState(false);

  /**
   * Build the SearchRequestFragment from the currently-selected index row.
   * Returns null when no row is selected yet (catalog still loading or empty),
   * in which case the hooks fall back to legacy URL-envelope behavior.
   */
  const currentFragment: SearchRequestFragment | null = useMemo(() => {
    const row = searchIndexes.find(r => r.sprk_aisearchindexid === selectedSearchIndexId);
    return row ? buildSearchRequestFragment(row) : null;
  }, [searchIndexes, selectedSearchIndexId]);

  const executeSearch = useCallback(
    (searchQuery: string, searchFilters: SearchFilters, domain: SearchDomain) => {
      setSelectedIds([]);

      // Phase G — dropdown-derived fragment is the single source of truth for
      // scope/entityType/searchIndexName. When unavailable (catalog still
      // loading or empty), the hooks fall back to legacy URL-envelope behavior.
      const fragment = currentFragment;
      const envelopeSearchIndexName = urlParams?.searchIndexName ?? null;
      const scopeArg = hasUserInitiatedSearch ? null : initialScope || null;
      const entityIdArg = hasUserInitiatedSearch ? null : initialEntityId || null;

      if (domain === 'documents') {
        searchDocuments(searchQuery, searchFilters, envelopeSearchIndexName, scopeArg, entityIdArg, fragment);
      } else {
        // Record-level search (matters/projects/invoices). The fragment's
        // searchIndexName flows through, but `recordTypes` is still derived
        // from `domain` (which is itself derived from `fragment.entityType`
        // via `deriveSearchDomain`).
        searchRecords(searchQuery, DOMAIN_RECORD_TYPES[domain], searchFilters, envelopeSearchIndexName, fragment);
      }
    },
    [searchDocuments, searchRecords, hasUserInitiatedSearch, initialScope, initialEntityId, urlParams, currentFragment]
  );

  // =============================================
  // Handlers
  // =============================================

  /**
   * Called by `SearchIndexSelector` when the user picks a different row.
   *
   * Phase G (2026-06-10):
   *   1. Update `selectedSearchIndexId` (drives `currentFragment` memo).
   *   2. Derive new `activeDomain` from the row's fragment (so grid columns
   *      + result adapter pick up correctly).
   *   3. Mark this as a user-initiated search (turns OFF the URL-envelope
   *      auto-fire entity scope — the dropdown intent supersedes it).
   *   4. Execute the search with the new fragment + current query/filters.
   */
  const handleSelectSearchIndex = useCallback(
    (row: AiSearchIndexRow) => {
      const fragment = buildSearchRequestFragment(row);
      const newDomain = deriveSearchDomain(fragment);
      setSelectedSearchIndexId(row.sprk_aisearchindexid);
      setActiveDomain(newDomain);
      setSelectedIds([]);
      setHasUserInitiatedSearch(true);

      // Execute the search directly using the new fragment (don't rely on
      // the `currentFragment` memo — it won't update until React flushes
      // the `selectedSearchIndexId` state above).
      const envelopeSearchIndexName = urlParams?.searchIndexName ?? null;
      if (newDomain === 'documents') {
        searchDocuments(query, filters, envelopeSearchIndexName, null, null, fragment);
      } else {
        searchRecords(query, DOMAIN_RECORD_TYPES[newDomain], filters, envelopeSearchIndexName, fragment);
      }
    },
    [query, filters, urlParams, searchDocuments, searchRecords]
  );

  const handleFiltersChange = useCallback((newFilters: SearchFilters) => {
    setFilters(newFilters);
  }, []);

  /** Called by SearchFilterPane Search button — uses App-level query */
  const handleFilterSearch = useCallback(
    (_filterQuery: string, searchFilters: SearchFilters) => {
      setFilters(searchFilters);
      setHasUserInitiatedSearch(true);
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
  // Phase G — Load `sprk_aisearchindex` catalog on mount (spec §6)
  // =============================================
  //
  // Default-selection precedence (spec §6):
  //   1. URL envelope `searchIndexName` matches a row → select that row
  //   2. Row with `sprk_isdefault === true` → select it
  //   3. First row in `sprk_displayorder` order → select it
  //   4. Empty list → leave `selectedSearchIndexId === ''` (UI disables)
  //
  // The catalog load is fire-and-forget (errors logged in the service); the
  // effect runs ONCE on mount because all deps are stable initial values.

  const catalogFetchedRef = useRef(false);
  useEffect(() => {
    if (catalogFetchedRef.current) return;
    catalogFetchedRef.current = true;

    let cancelled = false;
    (async () => {
      const rows = await listActiveSearchIndexes();
      if (cancelled) return;

      setSearchIndexes(rows);
      setIsLoadingSearchIndexes(false);

      if (rows.length === 0) {
        // No indexes available — leave the selection empty; UI shows the
        // "no indexes available" disabled state.
        setSelectedSearchIndexId('');
        return;
      }

      // 1. URL envelope match
      const envelopeName = urlParams?.searchIndexName?.trim() ?? '';
      if (envelopeName.length > 0) {
        const envelopeRow = rows.find(r => r.sprk_searchindexname === envelopeName);
        if (envelopeRow) {
          setSelectedSearchIndexId(envelopeRow.sprk_aisearchindexid);
          // Update activeDomain to match the row so the first auto-fire
          // search (below) routes to the correct hook.
          setActiveDomain(deriveSearchDomain(buildSearchRequestFragment(envelopeRow)));
          return;
        }
      }

      // 2. `sprk_isdefault === true`
      const defaultRow = rows.find(r => r.sprk_isdefault === true);
      if (defaultRow) {
        setSelectedSearchIndexId(defaultRow.sprk_aisearchindexid);
        setActiveDomain(deriveSearchDomain(buildSearchRequestFragment(defaultRow)));
        return;
      }

      // 3. First row in displayorder/displayname order (already sorted by
      // the OData query).
      const firstRow = rows[0];
      setSelectedSearchIndexId(firstRow.sprk_aisearchindexid);
      setActiveDomain(deriveSearchDomain(buildSearchRequestFragment(firstRow)));
    })();

    return () => {
      cancelled = true;
    };
    // urlParams identity is stable (memoized at App boundary by parent); the
    // effect runs once on mount regardless. Including it in deps so React's
    // exhaustive-deps lint is happy.
  }, [urlParams]);

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

    // Auto-execute search when launched from the PCF "Open Viewer":
    //   - With a query string OR
    //   - With an entity scope (entityId present) so the modal mirrors the
    //     PCF's "all docs for this matter/project/etc." view even when the
    //     user hadn't typed a query before clicking the launcher.
    //
    // Without the `initialEntityId` branch, opening the viewer on an entity
    // form without first typing a query left the modal in the empty default
    // state — diverged from PCF behavior (which always shows the entity's
    // docs regardless of query). See multi-container-multi-index-r1 UAT
    // 2026-06-09: "Open Viewer shows blank" repro.
    if ((initialQuery || initialEntityId) && !autoSearchFired.current) {
      autoSearchFired.current = true;
      executeSearch(initialQuery, filters, activeDomain);
    }
  }, [
    initialQuery,
    initialEntityId,
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
        {/* Left pane: SearchFilterPane (self-collapsing) — Phase G: hosts
            the new Search Index dropdown (replaces 4-domain tabs) + the
            relocated Relevance Threshold + Search Mode controls. */}
        <SearchFilterPane
          activeDomain={activeDomain}
          searchIndexes={searchIndexes}
          selectedSearchIndexId={selectedSearchIndexId}
          isLoadingSearchIndexes={isLoadingSearchIndexes}
          onSelectSearchIndex={handleSelectSearchIndex}
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
