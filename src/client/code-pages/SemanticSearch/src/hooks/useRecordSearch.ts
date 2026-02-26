/**
 * useRecordSearch hook -- Entity record search state management.
 *
 * Manages search execution, pagination, cancellation, and result accumulation
 * for record-level semantic search (Matters, Projects, Invoices) via the BFF API.
 *
 * Mirrors useSemanticSearch but operates on RecordSearchResult[] and requires
 * a recordTypes parameter to specify which Dataverse entity types to search.
 *
 * Domain-to-recordTypes mapping is done by the caller:
 *   - Matters  -> ["sprk_matter"]
 *   - Projects -> ["sprk_project"]
 *   - Invoices -> ["sprk_invoice"]
 *
 * @see RecordSearchApiService — API client (POST /api/ai/search/records)
 * @see types/index.ts — RecordSearchRequest, RecordSearchResponse, etc.
 */

import { useState, useCallback, useRef } from "react";
import { search as searchRecords } from "../services/RecordSearchApiService";
import type {
    RecordSearchResult,
    RecordSearchResponse,
    SearchFilters,
    SearchState,
    ApiError,
} from "../types";

/** Number of results fetched per page */
const PAGE_SIZE = 20;

/** Hard ceiling on total loaded results to prevent runaway memory usage */
const MAX_RESULTS = 1000;

/**
 * Return type for the useRecordSearch hook.
 */
export interface UseRecordSearchReturn {
    /** Current accumulated record search results */
    results: RecordSearchResult[];
    /** Total matching records reported by the API */
    totalCount: number;
    /** Current search execution state */
    searchState: SearchState;
    /** Whether more results can be loaded via loadMore() */
    hasMore: boolean;
    /** User-friendly error message when searchState is "error", otherwise null */
    errorMessage: string | null;
    /** Search execution duration in milliseconds, or null if no search has completed */
    searchTime: number | null;
    /** Execute a new record search, clearing previous results */
    search: (query: string, recordTypes: string[], filters: SearchFilters) => void;
    /** Load the next page of results (appends to existing) */
    loadMore: () => void;
    /** Reset all state to initial idle values */
    reset: () => void;
}

/**
 * Extract a user-friendly error message from an API error or generic Error.
 */
function extractErrorMessage(err: unknown): string {
    // ApiError (thrown by handleApiResponse)
    if (typeof err === "object" && err !== null && "status" in err) {
        const apiError = err as ApiError;
        if (apiError.status === 429) {
            return "Too many requests. Please wait a moment and try again.";
        }
        if (apiError.status === 401 || apiError.status === 403) {
            return "You do not have permission to perform this search. Please sign in again.";
        }
        return apiError.detail || apiError.title || "An unexpected error occurred.";
    }
    // AbortError
    if (err instanceof DOMException && err.name === "AbortError") {
        return "Search was cancelled.";
    }
    // Generic Error
    if (err instanceof Error) {
        return err.message;
    }
    return "An unexpected error occurred.";
}

/**
 * React hook for record semantic search state management.
 *
 * Provides search(), loadMore(), and reset() actions along with reactive
 * state for results, loading indicators, errors, and pagination.
 *
 * @example
 * ```tsx
 * const {
 *     results, totalCount, searchState, hasMore,
 *     errorMessage, searchTime, search, loadMore, reset,
 * } = useRecordSearch();
 *
 * // Search for matters
 * search("employment dispute", ["sprk_matter"], filters);
 *
 * // Load next page when user scrolls
 * if (hasMore) loadMore();
 * ```
 */
export function useRecordSearch(): UseRecordSearchReturn {
    // --- State ---
    const [results, setResults] = useState<RecordSearchResult[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [searchState, setSearchState] = useState<SearchState>("idle");
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [searchTime, setSearchTime] = useState<number | null>(null);

    // --- Refs (stable across renders, used by loadMore) ---
    const currentQueryRef = useRef("");
    const currentRecordTypesRef = useRef<string[]>([]);
    const currentFiltersRef = useRef<SearchFilters | null>(null);
    const abortControllerRef = useRef<AbortController | null>(null);

    // --- Derived ---
    const hasMore = totalCount > results.length && results.length < MAX_RESULTS;

    /**
     * Execute a new record search, clearing previous results.
     *
     * Cancels any in-flight request before starting. Stores query, recordTypes,
     * and filters in refs so loadMore() can reuse them for subsequent pages.
     */
    const search = useCallback(
        (query: string, recordTypes: string[], filters: SearchFilters): void => {
            // Cancel any in-flight request
            abortControllerRef.current?.abort();
            const controller = new AbortController();
            abortControllerRef.current = controller;

            // Store for loadMore
            currentQueryRef.current = query;
            currentRecordTypesRef.current = recordTypes;
            currentFiltersRef.current = filters;

            // Reset state for new search
            setResults([]);
            setTotalCount(0);
            setErrorMessage(null);
            setSearchTime(null);
            setSearchState("loading");

            searchRecords({
                query,
                recordTypes,
                options: {
                    limit: PAGE_SIZE,
                    offset: 0,
                    hybridMode: filters.searchMode,
                },
            })
                .then((response: RecordSearchResponse) => {
                    // If this request was cancelled, discard the result
                    if (controller.signal.aborted) return;

                    setResults(response.results);
                    setTotalCount(response.metadata.totalCount);
                    setSearchTime(response.metadata.searchTime);
                    setSearchState("success");
                })
                .catch((err: unknown) => {
                    // If this request was cancelled, do not surface the error
                    if (controller.signal.aborted) return;

                    setErrorMessage(extractErrorMessage(err));
                    setSearchState("error");
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
        if (searchState !== "success" || !hasMore) return;

        const filters = currentFiltersRef.current;
        if (!filters) return;

        // Cancel any prior in-flight loadMore
        abortControllerRef.current?.abort();
        const controller = new AbortController();
        abortControllerRef.current = controller;

        setSearchState("loadingMore");

        // Capture current length for offset (stable at call time)
        const currentLength = results.length;

        searchRecords({
            query: currentQueryRef.current,
            recordTypes: currentRecordTypesRef.current,
            options: {
                limit: PAGE_SIZE,
                offset: currentLength,
                hybridMode: filters.searchMode,
            },
        })
            .then((response: RecordSearchResponse) => {
                if (controller.signal.aborted) return;

                setResults((prev) => [...prev, ...response.results]);
                setTotalCount(response.metadata.totalCount);
                setSearchState("success");
            })
            .catch((err: unknown) => {
                if (controller.signal.aborted) return;

                setErrorMessage(extractErrorMessage(err));
                setSearchState("error");
            });
    }, [searchState, hasMore, results.length]);

    /**
     * Reset all state back to idle defaults and cancel any in-flight request.
     */
    const reset = useCallback((): void => {
        abortControllerRef.current?.abort();
        abortControllerRef.current = null;
        currentQueryRef.current = "";
        currentRecordTypesRef.current = [];
        currentFiltersRef.current = null;

        setResults([]);
        setTotalCount(0);
        setSearchState("idle");
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
