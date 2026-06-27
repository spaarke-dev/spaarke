/**
 * useSemanticSearch hook
 *
 * Main React hook for managing search state and execution.
 * Handles search execution, pagination state, and result accumulation.
 *
 * @see spec.md for search behavior requirements
 */

import { useState, useCallback, useRef } from 'react';
import { SearchResult, SearchFilters, SearchScope, SearchState, SearchError, SearchOptions } from '../types';
import { SemanticSearchApiService } from '../services';

/**
 * Default search options.
 *
 * v1.1.49 — `limit` raised from 8 → 25 to power the lazy-load infinite scroll
 * (Item 9). The PCF requests 25 docs per page on initial load AND each
 * `loadMore` call; the sentinel-driven hook in the parent fires `loadMore`
 * as the user scrolls. The BFF already supports `offset`/`count` on the
 * /api/ai/search endpoint (SemanticSearchEndpoints → BuildSearchOptions
 * passes `Skip = offset`, `Size = limit`).
 */
const DEFAULT_OPTIONS: SearchOptions = {
  limit: 25,
  offset: 0,
  includeHighlights: true,
};

/**
 * Return type for useSemanticSearch hook
 */
interface UseSemanticSearchResult {
  /** Current search results */
  results: SearchResult[];

  /** Total count of results from API */
  totalCount: number;

  /** Current search state */
  state: SearchState;

  /** Whether currently loading initial results */
  isLoading: boolean;

  /** Whether currently loading more results */
  isLoadingMore: boolean;

  /** Current error, if any */
  error: SearchError | null;

  /** Whether more results are available */
  hasMore: boolean;

  /** Current query */
  query: string;

  /** Execute a new search (clears previous results) */
  search: (query: string, filters: SearchFilters) => Promise<void>;

  /** Load more results (pagination) */
  loadMore: () => Promise<void>;

  /** Clear all results and reset state */
  reset: () => void;
}

/**
 * Hook for managing semantic search state.
 *
 * @param apiService - SemanticSearchApiService instance
 * @param scope - Search scope (all, matter, custom)
 * @param scopeId - ID for scoped searches (matterId, etc.)
 *
 * @example
 * ```tsx
 * const {
 *   results,
 *   isLoading,
 *   hasMore,
 *   search,
 *   loadMore
 * } = useSemanticSearch(apiService, 'all', null);
 *
 * // Execute search
 * await search('contract terms', filters);
 *
 * // Load more when scrolling
 * if (hasMore) {
 *   await loadMore();
 * }
 * ```
 */
export function useSemanticSearch(
  apiService: SemanticSearchApiService,
  scope: SearchScope,
  scopeId: string | null,
  /**
   * FR-PCF-02 (Wave 9 wiring) — Azure AI Search index name forwarded into
   * every search/searchUnion/loadMore call. Sourced from the PCF manifest's
   * `searchIndexName` bound property (task 030) which itself binds to the
   * scope record's `sprk_searchindexname` Dataverse field.
   *
   * - Non-empty trimmed string → forwarded to the BFF in the request body
   *   so the request routes to that index.
   * - `null` / `undefined` / empty / whitespace → service omits the field
   *   entirely (per `SemanticSearchApiService.transformRequest`) and the
   *   BFF falls through to its tenant default index chain (FR-BFF-04).
   */
  searchIndexName?: string | null
): UseSemanticSearchResult {
  // Search state
  const [results, setResults] = useState<SearchResult[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [state, setState] = useState<SearchState>('idle');
  const [error, setError] = useState<SearchError | null>(null);
  const [query, setQuery] = useState('');

  // Track current filters to use in loadMore
  const filtersRef = useRef<SearchFilters>({
    documentTypes: [],
    matterTypes: [],
    dateRange: null,
    fileTypes: [],
    threshold: 50,
    searchMode: 'hybrid',
    associatedOnly: true,
  });

  // Calculate if more results available
  const hasMore = results.length < totalCount;

  // Derived loading states
  const isLoading = state === 'loading';
  const isLoadingMore = state === 'loadingMore';

  /**
   * Execute a new search (clears previous results).
   * Empty query is supported — returns all documents in scope ordered by date.
   */
  const search = useCallback(
    async (searchQuery: string, filters: SearchFilters): Promise<void> => {
      // Update state
      setQuery(searchQuery);
      filtersRef.current = filters;
      setState('loading');
      setError(null);

      // Clear previous results
      setResults([]);
      setTotalCount(0);

      try {
        // multi-container-multi-index-r1 (post-Phase D UAT): single source-of-truth
        // is now the AI Search index. The wizard pipeline + "Send to Index" ribbon
        // reliably populate `spaarke-files-index` with parentEntityType/parentEntityId
        // on every chunk, so the semantic search (filtered by parent) returns the
        // complete set. The previous `searchUnion` (semantic + Dataverse-associated
        // merged client-side) was an indexing-reliability workaround and is retired
        // — it caused the PCF and Code Page surfaces to drift since the Code Page
        // never had the union. Both surfaces now query the same path identically.
        const response = await apiService.search(
          {
            query: searchQuery,
            scope,
            scopeId,
            filters,
            options: {
              ...DEFAULT_OPTIONS,
              offset: 0,
            },
          },
          // FR-PCF-02 (Wave 9 wiring) — forward manifest-bound index name.
          searchIndexName ?? undefined
        );

        setResults(response.results);
        setTotalCount(response.totalCount);
        setState('success');
      } catch (err) {
        const searchError = err as SearchError;
        console.error('[useSemanticSearch] Search error:', searchError);
        setError(searchError);
        setState('error');
      }
    },
    [apiService, scope, scopeId, searchIndexName]
  );

  /**
   * Load more results (pagination)
   */
  const loadMore = useCallback(async (): Promise<void> => {
    // Don't load more if already loading or no more results
    if (state === 'loading' || state === 'loadingMore' || !hasMore) {
      return;
    }

    setState('loadingMore');

    try {
      // v1.1.49 — loadMore uses plain `search()` (NOT searchUnion). The
      // initial union returns up to N (semantic) + M (associated) docs at
      // offset=0; subsequent pages continue paginating the SEMANTIC path
      // only — the Dataverse-associated path returns its full small N up
      // front and has no meaningful "next page". Newly appended semantic
      // docs are deduped by id below so the union remains coherent.
      const response = await apiService.search(
        {
          query,
          scope,
          scopeId,
          filters: filtersRef.current,
          options: {
            ...DEFAULT_OPTIONS,
            offset: results.length,
          },
        },
        // FR-PCF-02 (Wave 9 wiring) — forward manifest-bound index name on
        // the paginated semantic path too, so subsequent pages route to the
        // same index as the initial union.
        searchIndexName ?? undefined
      );

      // Append new results to existing, defensive-deduping by documentId so
      // any overlap with the initial associated-only page is collapsed.
      setResults(prev => {
        const seen = new Set(prev.map(r => r.documentId));
        const fresh = response.results.filter(r => !seen.has(r.documentId));
        return [...prev, ...fresh];
      });
      setTotalCount(response.totalCount);
      setState('success');
    } catch (err) {
      const searchError = err as SearchError;
      setError(searchError);
      setState('error');
    }
  }, [apiService, scope, scopeId, query, results.length, state, hasMore, searchIndexName]);

  /**
   * Clear all results and reset state
   */
  const reset = useCallback(() => {
    setResults([]);
    setTotalCount(0);
    setState('idle');
    setError(null);
    setQuery('');
    filtersRef.current = {
      documentTypes: [],
      matterTypes: [],
      dateRange: null,
      fileTypes: [],
      threshold: 0,
      searchMode: 'hybrid',
    };
  }, []);

  return {
    results,
    totalCount,
    state,
    isLoading,
    isLoadingMore,
    error,
    hasMore,
    query,
    search,
    loadMore,
    reset,
  };
}

export default useSemanticSearch;
