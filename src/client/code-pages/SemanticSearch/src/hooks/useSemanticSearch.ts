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
import type { SearchRequestFragment } from '../services/targetEntityNormalize';

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
  /**
   * Execute a new search.
   *
   * Phase G (2026-06-10): when `requestFragment` is provided, its
   * `scope` / `entityType` / `searchIndexName` win over the legacy
   * `searchIndexName` / `scope` / `entityId` arguments below. The fragment
   * comes from `targetEntityNormalize.buildSearchRequestFragment` and
   * represents the dropdown-selected `sprk_aisearchindex` row.
   *
   * The legacy positional args remain to support the auto-search-on-mount
   * code path (URL envelope → PCF-style scope), which doesn't yet have a
   * fragment in hand.
   */
  search: (
    query: string,
    filters: SearchFilters,
    searchIndexName?: string | null,
    scope?: string | null,
    entityId?: string | null,
    requestFragment?: SearchRequestFragment | null
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
  // Phase G (2026-06-10) — when a dropdown-derived request fragment is
  // supplied, it takes precedence over the legacy scope/entityId mapping.
  // Captured here so loadMore() reuses the same routing across pages.
  const currentRequestFragmentRef = useRef<SearchRequestFragment | null>(null);
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
      entityId?: string | null,
      requestFragment?: SearchRequestFragment | null
    ): void => {
      // Cancel any in-flight request
      abortControllerRef.current?.abort();
      const controller = new AbortController();
      abortControllerRef.current = controller;

      // Store for loadMore (FR-CP-04: same index routing across pages)
      currentQueryRef.current = query;
      currentFiltersRef.current = filters;
      currentRequestFragmentRef.current = requestFragment ?? null;
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

      // Phase G (2026-06-10) — request-body construction precedence:
      //   1. If `requestFragment` is provided (from `buildSearchRequestFragment`
      //      driven by the Search Index dropdown), use its scope/entityType/
      //      searchIndexName verbatim.
      //   2. Otherwise fall back to the legacy URL-envelope-driven mapping
      //      (PCF-style `scope` + `entityId` → BFF `entity` + `entityType`).
      //      This branch preserves the auto-search-on-mount behavior for code
      //      pages opened from a PCF entity form.
      let bffScope: string;
      let bffEntityType: string | undefined;
      let bffEntityId: string | undefined;
      let effectiveSearchIndexName: string | null;

      if (requestFragment) {
        // Phase G path — fragment wins.
        bffScope = requestFragment.scope;
        bffEntityType = requestFragment.entityType;
        // The fragment does NOT carry entityId — the dropdown is the user
        // selecting an index/scope, not narrowing to a specific record.
        // Leave entityId undefined unless the legacy entityId is also set
        // and the fragment scope is 'entity' (allows combining "dropdown
        // picks Matter index" + "URL envelope brought a matter ID").
        bffEntityId =
          requestFragment.scope === 'entity' && currentEntityIdRef.current ? currentEntityIdRef.current : undefined;
        effectiveSearchIndexName = requestFragment.searchIndexName ?? null;
      } else {
        // Legacy path — multi-container-multi-index-r1 UAT 2026-06-09 fix.
        // PCF→BFF contract mapping (mirrors SemanticSearchApiService.transformRequest
        // in the PCF): URL envelope sends PCF-style `scope` ('matter', 'project',
        // 'invoice', 'account', 'contact', 'document') + `entityId`. BFF
        // SemanticSearchRequest expects `scope='entity' + entityType + entityId`
        // for entity scopes, or `scope='all'` for tenant-wide.
        const ENTITY_SCOPES = ['matter', 'project', 'invoice', 'account', 'contact', 'document'];
        const incomingScope = currentScopeRef.current;
        const incomingEntityId = currentEntityIdRef.current;

        if (incomingScope && ENTITY_SCOPES.includes(incomingScope) && incomingEntityId) {
          bffScope = 'entity';
          bffEntityType = incomingScope;
          bffEntityId = incomingEntityId;
        } else if (incomingScope === 'entity' && incomingEntityId) {
          // Already-translated form (paranoia branch — shouldn't happen from PCF
          // but defensive in case callers send the BFF shape directly).
          bffScope = 'entity';
          bffEntityType = undefined; // caller must include in filters if needed
          bffEntityId = incomingEntityId;
        } else {
          bffScope = 'all';
          bffEntityType = undefined;
          bffEntityId = undefined;
        }
        effectiveSearchIndexName = currentSearchIndexNameRef.current;
      }

      // FR-CP-04 — Conditionally include `searchIndexName` in the body. The
      // spread + cast pattern keeps `DocumentSearchRequest` untouched while
      // still forwarding the field to the BFF. JSON.stringify in the API
      // service serializes the field when present; when absent the key is
      // omitted entirely (NOT sent as empty string).
      const requestBody = {
        query,
        scope: bffScope,
        ...(bffEntityType ? { entityType: bffEntityType } : {}),
        ...(bffEntityId ? { entityId: bffEntityId } : {}),
        filters: buildDocumentFilters(filters),
        options: {
          limit: PAGE_SIZE,
          offset: 0,
          includeHighlights: true,
          hybridMode: filters.searchMode,
        },
        ...buildSearchIndexNameFragment(effectiveSearchIndexName),
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
    // so pagination stays bound to the same index. Phase G (2026-06-10) —
    // same precedence as search(): captured fragment wins over legacy
    // scope/entityId mapping.
    let lmBffScope: string;
    let lmBffEntityType: string | undefined;
    let lmBffEntityId: string | undefined;
    let lmEffectiveSearchIndexName: string | null;

    const lmFragment = currentRequestFragmentRef.current;
    if (lmFragment) {
      lmBffScope = lmFragment.scope;
      lmBffEntityType = lmFragment.entityType;
      lmBffEntityId =
        lmFragment.scope === 'entity' && currentEntityIdRef.current ? currentEntityIdRef.current : undefined;
      lmEffectiveSearchIndexName = lmFragment.searchIndexName ?? null;
    } else {
      const ENTITY_SCOPES = ['matter', 'project', 'invoice', 'account', 'contact', 'document'];
      const lmIncomingScope = currentScopeRef.current;
      const lmIncomingEntityId = currentEntityIdRef.current;
      if (lmIncomingScope && ENTITY_SCOPES.includes(lmIncomingScope) && lmIncomingEntityId) {
        lmBffScope = 'entity';
        lmBffEntityType = lmIncomingScope;
        lmBffEntityId = lmIncomingEntityId;
      } else {
        lmBffScope = 'all';
        lmBffEntityType = undefined;
        lmBffEntityId = undefined;
      }
      lmEffectiveSearchIndexName = currentSearchIndexNameRef.current;
    }

    const requestBody = {
      query: currentQueryRef.current,
      scope: lmBffScope,
      ...(lmBffEntityType ? { entityType: lmBffEntityType } : {}),
      ...(lmBffEntityId ? { entityId: lmBffEntityId } : {}),
      filters: buildDocumentFilters(filters),
      options: {
        limit: PAGE_SIZE,
        offset: currentLength,
        includeHighlights: true,
        hybridMode: filters.searchMode,
      },
      ...buildSearchIndexNameFragment(lmEffectiveSearchIndexName),
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
    currentScopeRef.current = null;
    currentEntityIdRef.current = null;
    currentRequestFragmentRef.current = null;

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
