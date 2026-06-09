/**
 * useSemanticSearch hook -- Document search state management.
 *
 * Manages search execution, pagination, cancellation, and result accumulation
 * for document semantic search via the BFF API.
 *
 * Adapted from the PCF SemanticSearchControl hook but simplified for the
 * code page context: no PCF apiService dependency, uses module-level search()
 * function directly, and adds AbortController-based cancellation.
 *
 * @see SemanticSearchApiService — API client (POST /api/ai/search)
 * @see types/index.ts — DocumentSearchRequest, DocumentSearchResponse, etc.
 */

import { useState, useCallback, useRef } from 'react';
import { search as searchDocuments } from '../services/SemanticSearchApiService';
import type {
  DocumentSearchRequest,
  DocumentSearchResult,
  DocumentSearchResponse,
  SearchFilters,
  SearchState,
  ApiError,
} from '../types';

/** Number of results fetched per page */
const PAGE_SIZE = 20;

/** Hard ceiling on total loaded results to prevent runaway memory usage */
const MAX_RESULTS = 1000;

/**
 * Return type for the useSemanticSearch hook.
 */
export interface UseSemanticSearchReturn {
  /** Current accumulated search results */
  results: DocumentSearchResult[];
  /** Total matching documents reported by the API */
  totalCount: number;
  /** Current search execution state */
  searchState: SearchState;
  /** Whether more results can be loaded via loadMore() */
  hasMore: boolean;
  /** User-friendly error message when searchState is "error", otherwise null */
  errorMessage: string | null;
  /** Search execution duration in milliseconds, or null if no search has completed */
  searchTime: number | null;
  /**
   * Execute a new search, clearing previous results.
   *
   * @param query - Search query text.
   * @param filters - Active search filters.
   * @param searchIndexName - Optional Azure AI Search index name to route the
   *   request to (FR-CP-04 / FR-PCF-03 — sourced from the
   *   `searchIndexName` URL envelope param parsed by `parseUrlParams`).
   *   When non-empty (after trimming), the value is forwarded in the BFF
   *   request body so the server resolves the request against that index.
   *   When null / undefined / empty / whitespace-only, the field is OMITTED
   *   entirely from the body and the BFF falls through to its existing
   *   tenant default index chain (FR-BFF-04). Empty string MUST NOT be
   *   sent — absence is the protocol signal for "use server default".
   */
  search: (
    query: string,
    filters: SearchFilters,
    searchIndexName?: string | null,
    scope?: string | null,
    entityId?: string | null
  ) => void;
  /** Load the next page of results (appends to existing) */
  loadMore: () => void;
  /** Reset all state to initial idle values */
  reset: () => void;
}

/**
 * Map an API SearchFilters (client-side) to the DocumentSearchFilters shape
 * expected by the API request body. Only includes non-empty filter values.
 */
function buildDocumentFilters(filters: SearchFilters) {
  const docFilters: Record<string, unknown> = {};

  if (filters.documentTypes.length > 0) {
    docFilters.documentTypes = filters.documentTypes;
  }
  if (filters.fileTypes.length > 0) {
    docFilters.fileTypes = filters.fileTypes;
  }
  if (filters.entityTypes && filters.entityTypes.length > 0) {
    docFilters.entityTypes = filters.entityTypes;
  }
  if (filters.dateRange.from || filters.dateRange.to) {
    docFilters.dateRange = {
      field: 'createdAt',
      from: filters.dateRange.from ?? undefined,
      to: filters.dateRange.to ?? undefined,
    };
  }

  return Object.keys(docFilters).length > 0 ? docFilters : undefined;
}

/**
 * FR-CP-04 — Build the conditional `searchIndexName` body fragment.
 *
 *   - Non-empty trimmed string → `{ searchIndexName: <trimmed> }` (the BFF
 *     validates against `appsettings.AiSearch.AllowedIndexes` and returns
 *     400 INDEX_NOT_ALLOWED on miss).
 *   - null / undefined / empty / whitespace-only → `{}` so the key is
 *     OMITTED entirely from the body and the BFF falls through to its
 *     existing tenant default index chain (FR-BFF-04). Sending `""` is
 *     incorrect — absence is the protocol signal for "use server default".
 *
 * Returned as a partial fragment so the caller can `...spread` it onto the
 * request body, preserving the property's absence when empty.
 */
function buildSearchIndexNameFragment(searchIndexName: string | null | undefined): { searchIndexName?: string } {
  const trimmed = typeof searchIndexName === 'string' ? searchIndexName.trim() : '';
  return trimmed.length > 0 ? { searchIndexName: trimmed } : {};
}

/**
 * Extract a user-friendly error message from an API error or generic Error.
 */
function extractErrorMessage(err: unknown): string {
  // ApiError (thrown by handleApiResponse)
  if (typeof err === 'object' && err !== null && 'status' in err) {
    const apiError = err as ApiError;
    if (apiError.status === 429) {
      return 'Too many requests. Please wait a moment and try again.';
    }
    if (apiError.status === 401 || apiError.status === 403) {
      return 'You do not have permission to perform this search. Please sign in again.';
    }
    return apiError.detail || apiError.title || 'An unexpected error occurred.';
  }
  // AbortError (should not surface, but guard)
  if (err instanceof DOMException && err.name === 'AbortError') {
    return 'Search was cancelled.';
  }
  // Generic Error
  if (err instanceof Error) {
    return err.message;
  }
  return 'An unexpected error occurred.';
}

/**
 * React hook for document semantic search state management.
 *
 * Provides search(), loadMore(), and reset() actions along with reactive
 * state for results, loading indicators, errors, and pagination.
 *
 * @example
 * ```tsx
 * const {
 *     results, totalCount, searchState, hasMore,
 *     errorMessage, searchTime, search, loadMore, reset,
 * } = useSemanticSearch();
 *
 * // Execute a search
 * search("employment contracts 2024", filters);
 *
 * // Load next page when user scrolls
 * if (hasMore) loadMore();
 * ```
 */
export function useSemanticSearch(): UseSemanticSearchReturn {
  // --- State ---
  const [results, setResults] = useState<DocumentSearchResult[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [searchState, setSearchState] = useState<SearchState>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [searchTime, setSearchTime] = useState<number | null>(null);

  // --- Refs (stable across renders, used by loadMore) ---
  const currentQueryRef = useRef('');
  const currentFiltersRef = useRef<SearchFilters | null>(null);
  // FR-CP-04 — captured at search() time so loadMore() reuses the same
  // index routing across pages. null means "not provided / use BFF default".
  const currentSearchIndexNameRef = useRef<string | null>(null);
  // multi-container-multi-index-r1 UAT 2026-06-09 fix: scope + entityId from
  // the URL envelope MUST be sent to the BFF on each search/loadMore so the
  // code page mirrors the PCF's entity-scoped view (instead of the previously
  // hardcoded `scope: 'all'`). null means "tenant-wide / no entity scope".
  const currentScopeRef = useRef<string | null>(null);
  const currentEntityIdRef = useRef<string | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  // --- Derived ---
  const hasMore = totalCount > results.length && results.length < MAX_RESULTS;

  /**
   * Execute a new document search, clearing previous results.
   *
   * Cancels any in-flight request before starting. Stores query and filters
   * in refs so loadMore() can reuse them for subsequent pages.
   */
  const search = useCallback(
    (
      query: string,
      filters: SearchFilters,
      searchIndexName?: string | null,
      scope?: string | null,
      entityId?: string | null
    ): void => {
      // Cancel any in-flight request
      abortControllerRef.current?.abort();
      const controller = new AbortController();
      abortControllerRef.current = controller;

      // Store for loadMore (FR-CP-04: same index routing across pages)
      currentQueryRef.current = query;
      currentFiltersRef.current = filters;
      currentSearchIndexNameRef.current =
        typeof searchIndexName === 'string' && searchIndexName.trim().length > 0 ? searchIndexName.trim() : null;
      currentScopeRef.current = typeof scope === 'string' && scope.trim().length > 0 ? scope.trim() : null;
      currentEntityIdRef.current = typeof entityId === 'string' && entityId.trim().length > 0 ? entityId.trim() : null;

      // Reset state for new search
      setResults([]);
      setTotalCount(0);
      setErrorMessage(null);
      setSearchTime(null);
      setSearchState('loading');

      // multi-container-multi-index-r1 UAT 2026-06-09 fix: scope + entityId
      // were previously hardcoded to 'all', so opening the code page from a
      // PCF on a Matter form returned tenant-wide results regardless of which
      // matter the user came from. Both fields now come from URL envelope
      // (FR-CP-03) so the modal mirrors the PCF's entity-scoped view.
      // When scope is null/'all' or entityId is null, the BFF runs a tenant-
      // wide search (preserves the prior fallback behavior).
      const effectiveScope = currentScopeRef.current ?? 'all';
      const effectiveEntityId = currentEntityIdRef.current ?? undefined;

      // FR-CP-04 — Conditionally include `searchIndexName` in the body. The
      // spread + cast pattern keeps `DocumentSearchRequest` untouched while
      // still forwarding the field to the BFF. JSON.stringify in the API
      // service serializes the field when present; when absent the key is
      // omitted entirely (NOT sent as empty string).
      const requestBody = {
        query,
        scope: effectiveScope,
        ...(effectiveEntityId ? { scopeId: effectiveEntityId } : {}),
        filters: buildDocumentFilters(filters),
        options: {
          limit: PAGE_SIZE,
          offset: 0,
          includeHighlights: true,
          hybridMode: filters.searchMode,
        },
        ...buildSearchIndexNameFragment(searchIndexName),
      } as DocumentSearchRequest;

      searchDocuments(requestBody)
        .then((response: DocumentSearchResponse) => {
          // If this request was cancelled, discard the result
          if (controller.signal.aborted) return;

          setResults(response.results);
          setTotalCount(response.metadata.totalResults);
          setSearchTime(response.metadata.searchDurationMs);
          setSearchState('success');
        })
        .catch((err: unknown) => {
          // If this request was cancelled, do not surface the error
          if (controller.signal.aborted) return;

          setErrorMessage(extractErrorMessage(err));
          setSearchState('error');
        });
    },
    []
  );

  /**
   * Load the next page of results, appending to the existing set.
   *
   * No-op if the current state is not "success" or if there are no more
   * results to fetch.
   */
  const loadMore = useCallback((): void => {
    if (searchState !== 'success' || !hasMore) return;

    const filters = currentFiltersRef.current;
    if (!filters) return;

    // Cancel any prior in-flight loadMore
    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setSearchState('loadingMore');

    // Capture current length for offset (stable at call time)
    const currentLength = results.length;

    // FR-CP-04 — Reuse the same `searchIndexName` captured at search() time
    // so pagination stays bound to the same index. Cast + spread mirrors
    // the pattern in search() above.
    //
    // multi-container-multi-index-r1 UAT 2026-06-09: also reuse the same
    // scope/entityId captured at search() time so subsequent pages stay
    // entity-scoped (otherwise pagination silently falls back to tenant-wide
    // and merges unrelated docs into the result set).
    const effectiveScope = currentScopeRef.current ?? 'all';
    const effectiveEntityId = currentEntityIdRef.current ?? undefined;
    const requestBody = {
      query: currentQueryRef.current,
      scope: effectiveScope,
      ...(effectiveEntityId ? { scopeId: effectiveEntityId } : {}),
      filters: buildDocumentFilters(filters),
      options: {
        limit: PAGE_SIZE,
        offset: currentLength,
        includeHighlights: true,
        hybridMode: filters.searchMode,
      },
      ...buildSearchIndexNameFragment(currentSearchIndexNameRef.current),
    } as DocumentSearchRequest;

    searchDocuments(requestBody)
      .then((response: DocumentSearchResponse) => {
        if (controller.signal.aborted) return;

        setResults(prev => [...prev, ...response.results]);
        setTotalCount(response.metadata.totalResults);
        setSearchState('success');
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;

        setErrorMessage(extractErrorMessage(err));
        setSearchState('error');
      });
  }, [searchState, hasMore, results.length]);

  /**
   * Reset all state back to idle defaults and cancel any in-flight request.
   */
  const reset = useCallback((): void => {
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
    currentQueryRef.current = '';
    currentFiltersRef.current = null;
    currentSearchIndexNameRef.current = null;

    setResults([]);
    setTotalCount(0);
    setSearchState('idle');
    setErrorMessage(null);
    setSearchTime(null);
  }, []);

  return {
    results,
    totalCount,
    searchState,
    hasMore,
    errorMessage,
    searchTime,
    search,
    loadMore,
    reset,
  };
}
