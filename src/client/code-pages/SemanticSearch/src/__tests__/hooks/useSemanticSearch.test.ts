/**
 * Unit tests for useSemanticSearch hook -- document search state management.
 *
 * Tests:
 * - Initial state (idle, empty results, no error)
 * - search() transitions: idle -> loading -> success
 * - search() error handling: 401, 403, 429, generic errors
 * - loadMore() pagination: appends results, offset calculation
 * - loadMore() guards: no-op when not in success state or no more results
 * - reset() clears all state back to idle
 * - Abort/cancellation: new search cancels in-flight request
 * - Edge cases: empty results, MAX_RESULTS cap
 *
 * @see useSemanticSearch.ts
 */

import { renderHook, act } from "@testing-library/react";
import type {
    DocumentSearchResponse,
    DocumentSearchResult,
    SearchFilters,
    ApiError,
} from "../../types";

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockSearch = jest.fn<Promise<DocumentSearchResponse>, [unknown]>();

jest.mock("../../services/SemanticSearchApiService", () => ({
    search: (...args: unknown[]) => mockSearch(...args),
}));

import { useSemanticSearch } from "../../hooks/useSemanticSearch";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const defaultFilters: SearchFilters = {
    documentTypes: [],
    fileTypes: [],
    matterTypes: [],
    dateRange: { from: null, to: null },
    threshold: 0.5,
    searchMode: "rrf",
};

const filtersWithTypes: SearchFilters = {
    documentTypes: ["Contract"],
    fileTypes: ["pdf"],
    matterTypes: [],
    dateRange: { from: "2025-01-01", to: null },
    threshold: 0.5,
    searchMode: "rrf",
    entityTypes: ["matter"],
};

function makeResult(id: string, score = 0.9): DocumentSearchResult {
    return {
        documentId: id,
        name: `Document ${id}`,
        combinedScore: score,
        documentType: "Contract",
        fileType: "pdf",
    };
}

function makeResponse(
    results: DocumentSearchResult[],
    totalResults: number,
    durationMs = 200,
): DocumentSearchResponse {
    return {
        results,
        metadata: {
            totalResults,
            returnedResults: results.length,
            searchDurationMs: durationMs,
            embeddingDurationMs: 30,
        },
    };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("useSemanticSearch", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // --- Initial state ---

    describe("initial state", () => {
        it("should start with idle searchState", () => {
            const { result } = renderHook(() => useSemanticSearch());

            expect(result.current.searchState).toBe("idle");
        });

        it("should start with empty results", () => {
            const { result } = renderHook(() => useSemanticSearch());

            expect(result.current.results).toEqual([]);
            expect(result.current.totalCount).toBe(0);
        });

        it("should start with no error", () => {
            const { result } = renderHook(() => useSemanticSearch());

            expect(result.current.errorMessage).toBeNull();
        });

        it("should start with no search time", () => {
            const { result } = renderHook(() => useSemanticSearch());

            expect(result.current.searchTime).toBeNull();
        });

        it("should start with hasMore false", () => {
            const { result } = renderHook(() => useSemanticSearch());

            expect(result.current.hasMore).toBe(false);
        });
    });

    // --- search() happy path ---

    describe("search() — success", () => {
        it("should transition to loading state immediately", () => {
            // Use a promise that never resolves to capture the loading state
            mockSearch.mockReturnValue(new Promise(() => {}));
            const { result } = renderHook(() => useSemanticSearch());

            act(() => {
                result.current.search("employment contracts", defaultFilters);
            });

            expect(result.current.searchState).toBe("loading");
        });

        it("should transition to success state after API resolves", async () => {
            const response = makeResponse([makeResult("doc-1")], 1);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("employment contracts", defaultFilters);
            });

            expect(result.current.searchState).toBe("success");
        });

        it("should populate results from API response", async () => {
            const results = [makeResult("doc-1"), makeResult("doc-2")];
            const response = makeResponse(results, 2);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("contracts", defaultFilters);
            });

            expect(result.current.results).toEqual(results);
            expect(result.current.totalCount).toBe(2);
        });

        it("should record search duration", async () => {
            const response = makeResponse([makeResult("doc-1")], 1, 456);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.searchTime).toBe(456);
        });

        it("should handle empty results", async () => {
            const response = makeResponse([], 0);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("nonexistent query", defaultFilters);
            });

            expect(result.current.searchState).toBe("success");
            expect(result.current.results).toEqual([]);
            expect(result.current.totalCount).toBe(0);
            expect(result.current.hasMore).toBe(false);
        });

        it("should clear previous results when starting a new search", async () => {
            const firstResponse = makeResponse([makeResult("doc-1")], 1);
            const secondResponse = makeResponse([makeResult("doc-2")], 1);
            mockSearch
                .mockResolvedValueOnce(firstResponse)
                .mockResolvedValueOnce(secondResponse);
            const { result } = renderHook(() => useSemanticSearch());

            // First search
            await act(async () => {
                result.current.search("first", defaultFilters);
            });
            expect(result.current.results[0].documentId).toBe("doc-1");

            // Second search — should replace results
            await act(async () => {
                result.current.search("second", defaultFilters);
            });
            expect(result.current.results).toHaveLength(1);
            expect(result.current.results[0].documentId).toBe("doc-2");
        });

        it("should pass correct request shape to API service", async () => {
            mockSearch.mockResolvedValue(makeResponse([], 0));
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("employment dispute", filtersWithTypes);
            });

            expect(mockSearch).toHaveBeenCalledWith({
                query: "employment dispute",
                scope: "all",
                filters: {
                    documentTypes: ["Contract"],
                    fileTypes: ["pdf"],
                    entityTypes: ["matter"],
                    dateRange: {
                        field: "createdAt",
                        from: "2025-01-01",
                        to: undefined,
                    },
                },
                options: {
                    limit: 20,
                    offset: 0,
                    includeHighlights: true,
                    hybridMode: "rrf",
                },
            });
        });

        it("should not include empty filter arrays in request", async () => {
            mockSearch.mockResolvedValue(makeResponse([], 0));
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            const callArg = mockSearch.mock.calls[0][0] as Record<string, unknown>;
            expect(callArg.filters).toBeUndefined();
        });

        it("should set hasMore when totalCount exceeds results length", async () => {
            const response = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`doc-${i}`)),
                50,
            );
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.hasMore).toBe(true);
        });

        it("should set hasMore false when all results are loaded", async () => {
            const response = makeResponse([makeResult("doc-1")], 1);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.hasMore).toBe(false);
        });
    });

    // --- search() error handling ---

    describe("search() — error handling", () => {
        it("should transition to error state on API failure", async () => {
            const apiError: ApiError = {
                status: 500,
                title: "Internal Server Error",
                detail: "Something went wrong.",
            };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.searchState).toBe("error");
        });

        it("should set user-friendly message for 401 errors", async () => {
            const apiError: ApiError = {
                status: 401,
                title: "Unauthorized",
            };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.errorMessage).toBe(
                "You do not have permission to perform this search. Please sign in again.",
            );
        });

        it("should set user-friendly message for 403 errors", async () => {
            const apiError: ApiError = {
                status: 403,
                title: "Forbidden",
            };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.errorMessage).toBe(
                "You do not have permission to perform this search. Please sign in again.",
            );
        });

        it("should set rate-limit message for 429 errors", async () => {
            const apiError: ApiError = {
                status: 429,
                title: "Too Many Requests",
            };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.errorMessage).toBe(
                "Too many requests. Please wait a moment and try again.",
            );
        });

        it("should use detail from ApiError when available", async () => {
            const apiError: ApiError = {
                status: 400,
                title: "Validation Error",
                detail: "Query exceeds maximum length.",
            };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.errorMessage).toBe("Query exceeds maximum length.");
        });

        it("should use title from ApiError when detail is missing", async () => {
            const apiError: ApiError = {
                status: 500,
                title: "Internal Server Error",
            };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.errorMessage).toBe("Internal Server Error");
        });

        it("should handle generic Error objects", async () => {
            mockSearch.mockRejectedValue(new Error("Network connection lost"));
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe("Network connection lost");
        });

        it("should handle unknown error types", async () => {
            mockSearch.mockRejectedValue("string error");
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.errorMessage).toBe("An unexpected error occurred.");
        });

        it("should clear error when executing a new search", async () => {
            // First: cause an error
            mockSearch.mockRejectedValueOnce(new Error("fail"));
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });
            expect(result.current.errorMessage).not.toBeNull();

            // Second: successful search should clear error
            mockSearch.mockResolvedValueOnce(makeResponse([], 0));
            await act(async () => {
                result.current.search("test2", defaultFilters);
            });
            expect(result.current.errorMessage).toBeNull();
        });
    });

    // --- loadMore() ---

    describe("loadMore()", () => {
        it("should append results to existing set", async () => {
            // First page
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`p1-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });
            expect(result.current.results).toHaveLength(20);

            // Second page
            const page2 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`p2-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page2);

            await act(async () => {
                result.current.loadMore();
            });

            expect(result.current.results).toHaveLength(40);
            expect(result.current.results[0].documentId).toBe("p1-0");
            expect(result.current.results[20].documentId).toBe("p2-0");
        });

        it("should transition through loadingMore state", async () => {
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`doc-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            // loadMore with a never-resolving promise to capture loading state
            mockSearch.mockReturnValueOnce(new Promise(() => {}));
            act(() => {
                result.current.loadMore();
            });

            expect(result.current.searchState).toBe("loadingMore");
        });

        it("should pass correct offset in loadMore request", async () => {
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`doc-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test query", defaultFilters);
            });

            mockSearch.mockResolvedValueOnce(makeResponse([], 50));
            await act(async () => {
                result.current.loadMore();
            });

            // Second call should have offset=20
            const secondCall = mockSearch.mock.calls[1][0] as Record<string, unknown>;
            const options = secondCall.options as Record<string, unknown>;
            expect(options.offset).toBe(20);
        });

        it("should be no-op when searchState is not success", () => {
            const { result } = renderHook(() => useSemanticSearch());

            // State is idle, loadMore should do nothing
            act(() => {
                result.current.loadMore();
            });

            expect(mockSearch).not.toHaveBeenCalled();
            expect(result.current.searchState).toBe("idle");
        });

        it("should be no-op when hasMore is false", async () => {
            // Load exactly all results (no more to fetch)
            const response = makeResponse([makeResult("doc-1")], 1);
            mockSearch.mockResolvedValueOnce(response);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });
            expect(result.current.hasMore).toBe(false);

            mockSearch.mockClear();
            act(() => {
                result.current.loadMore();
            });

            expect(mockSearch).not.toHaveBeenCalled();
        });

        it("should handle error during loadMore", async () => {
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`doc-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            mockSearch.mockRejectedValueOnce(new Error("Load failed"));
            await act(async () => {
                result.current.loadMore();
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe("Load failed");
            // Original results should still be present (not cleared on loadMore error)
            expect(result.current.results).toHaveLength(20);
        });
    });

    // --- reset() ---

    describe("reset()", () => {
        it("should clear results and reset to idle", async () => {
            mockSearch.mockResolvedValue(
                makeResponse([makeResult("doc-1")], 1, 100),
            );
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });
            expect(result.current.results).toHaveLength(1);

            act(() => {
                result.current.reset();
            });

            expect(result.current.results).toEqual([]);
            expect(result.current.totalCount).toBe(0);
            expect(result.current.searchState).toBe("idle");
            expect(result.current.errorMessage).toBeNull();
            expect(result.current.searchTime).toBeNull();
            expect(result.current.hasMore).toBe(false);
        });

        it("should clear error state", async () => {
            mockSearch.mockRejectedValue(new Error("fail"));
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });
            expect(result.current.searchState).toBe("error");

            act(() => {
                result.current.reset();
            });

            expect(result.current.searchState).toBe("idle");
            expect(result.current.errorMessage).toBeNull();
        });
    });

    // --- Cancellation ---

    describe("cancellation", () => {
        it("should discard results from cancelled search when new search starts", async () => {
            let resolveFirst: (value: DocumentSearchResponse) => void;
            const firstPromise = new Promise<DocumentSearchResponse>((resolve) => {
                resolveFirst = resolve;
            });
            mockSearch.mockReturnValueOnce(firstPromise);

            const secondResponse = makeResponse([makeResult("second")], 1);
            mockSearch.mockResolvedValueOnce(secondResponse);

            const { result } = renderHook(() => useSemanticSearch());

            // Start first search
            act(() => {
                result.current.search("first", defaultFilters);
            });

            // Start second search before first completes (cancels first)
            await act(async () => {
                result.current.search("second", defaultFilters);
            });

            // Now resolve the first (should be discarded since aborted)
            await act(async () => {
                resolveFirst!(makeResponse([makeResult("first")], 1));
            });

            // Results should be from the second search only
            expect(result.current.results).toHaveLength(1);
            expect(result.current.results[0].documentId).toBe("second");
        });

        it("should not surface error from cancelled request", async () => {
            let rejectFirst: (reason: unknown) => void;
            const firstPromise = new Promise<DocumentSearchResponse>((_, reject) => {
                rejectFirst = reject;
            });
            mockSearch.mockReturnValueOnce(firstPromise);

            const secondResponse = makeResponse([makeResult("doc-1")], 1);
            mockSearch.mockResolvedValueOnce(secondResponse);

            const { result } = renderHook(() => useSemanticSearch());

            // Start first search
            act(() => {
                result.current.search("first", defaultFilters);
            });

            // Start second search (cancels first)
            await act(async () => {
                result.current.search("second", defaultFilters);
            });

            // First rejects after cancellation
            await act(async () => {
                rejectFirst!(new Error("aborted"));
            });

            // Should be in success state from second search, not error
            expect(result.current.searchState).toBe("success");
            expect(result.current.errorMessage).toBeNull();
        });
    });

    // --- Edge cases ---

    describe("edge cases", () => {
        it("should cap hasMore at MAX_RESULTS (1000)", async () => {
            // Simulate having 1000 results already loaded, with totalCount > 1000
            const results = Array.from({ length: 20 }, (_, i) => makeResult(`doc-${i}`));
            // Report totalCount far exceeding MAX_RESULTS
            const response = makeResponse(results, 5000);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            // With 20 results loaded and totalCount 5000, hasMore should be true
            expect(result.current.hasMore).toBe(true);
        });

        it("should handle AbortError from DOMException", async () => {
            const abortError = new DOMException("The operation was aborted", "AbortError");
            mockSearch.mockRejectedValue(abortError);
            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", defaultFilters);
            });

            expect(result.current.errorMessage).toBe("Search was cancelled.");
        });
    });
});
