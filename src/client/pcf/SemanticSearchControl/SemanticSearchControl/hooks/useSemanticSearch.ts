/**
 * useSemanticSearch hook
 *
 * Main React hook for managing search state and execution.
 * Handles search execution, pagination state, and result accumulation.
 *
 * @see spec.md for search behavior requirements
 */

import { useState, useCallback, useRef } from "react";
import {
    SearchResult,
    SearchFilters,
    SearchScope,
    SearchState,
    SearchError,
    SearchOptions,
} from "../types";
import { SemanticSearchApiService } from "../services";

/**
 * Default search options
 */
const DEFAULT_OPTIONS: SearchOptions = {
    limit: 20,
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
    scopeId: string | null
): UseSemanticSearchResult {
    // Search state
    const [results, setResults] = useState<SearchResult[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [state, setState] = useState<SearchState>("idle");
    const [error, setError] = useState<SearchError | null>(null);
    const [query, setQuery] = useState("");

    // Track current filters to use in loadMore
    const filtersRef = useRef<SearchFilters>({
        documentTypes: [],
        matterTypes: [],
        dateRange: null,
        fileTypes: [],
    });

    // Calculate if more results available
    const hasMore = results.length < totalCount;

    // Derived loading states
    const isLoading = state === "loading";
    const isLoadingMore = state === "loadingMore";

    /**
     * Execute a new search (clears previous results).
     * Empty query is supported — returns all documents in scope ordered by date.
     */
    const search = useCallback(
        async (searchQuery: string, filters: SearchFilters): Promise<void> => {
            // Update state
            setQuery(searchQuery);
            filtersRef.current = filters;
            setState("loading");
            setError(null);

            // Clear previous results
            setResults([]);
            setTotalCount(0);

            try {
                const response = await apiService.search({
                    query: searchQuery,
                    scope,
                    scopeId,
                    filters,
                    options: {
                        ...DEFAULT_OPTIONS,
                        offset: 0,
                    },
                });

                setResults(response.results);
                setTotalCount(response.totalCount);
                setState("success");
            } catch (err) {
                const searchError = err as SearchError;
                setError(searchError);
                setState("error");
            }
        },
        [apiService, scope, scopeId]
    );

    /**
     * Load more results (pagination)
     */
    const loadMore = useCallback(async (): Promise<void> => {
        // Don't load more if already loading or no more results
        if (state === "loading" || state === "loadingMore" || !hasMore) {
            return;
        }

        setState("loadingMore");

        try {
            const response = await apiService.search({
                query,
                scope,
                scopeId,
                filters: filtersRef.current,
                options: {
                    ...DEFAULT_OPTIONS,
                    offset: results.length,
                },
            });

            // Append new results to existing
            setResults((prev) => [...prev, ...response.results]);
            setTotalCount(response.totalCount);
            setState("success");
        } catch (err) {
            const searchError = err as SearchError;
            setError(searchError);
            setState("error");
        }
    }, [apiService, scope, scopeId, query, results.length, state, hasMore]);

    /**
     * Clear all results and reset state
     */
    const reset = useCallback(() => {
        setResults([]);
        setTotalCount(0);
        setState("idle");
        setError(null);
        setQuery("");
        filtersRef.current = {
            documentTypes: [],
            matterTypes: [],
            dateRange: null,
            fileTypes: [],
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
