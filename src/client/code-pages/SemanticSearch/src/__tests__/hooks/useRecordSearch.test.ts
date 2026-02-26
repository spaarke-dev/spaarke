/**
 * Unit tests for useRecordSearch hook -- record search state management.
 *
 * Tests:
 * - Initial state (idle, empty results, no error)
 * - search() transitions: idle -> loading -> success
 * - search() requires recordTypes parameter
 * - search() error handling: 401, 403, 429, generic errors
 * - loadMore() pagination: appends results, offset calculation
 * - loadMore() guards: no-op when not in success state or no more results
 * - reset() clears all state back to idle
 * - Abort/cancellation: new search cancels in-flight request
 * - Edge cases: empty results, MAX_RESULTS cap
 *
 * @see useRecordSearch.ts
 */

import { renderHook, act } from "@testing-library/react";
import type {
    RecordSearchResponse,
    RecordSearchResult,
    SearchFilters,
    ApiError,
} from "../../types";

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockSearch = jest.fn<Promise<RecordSearchResponse>, [unknown]>();

jest.mock("../../services/RecordSearchApiService", () => ({
    search: (...args: unknown[]) => mockSearch(...args),
}));

import { useRecordSearch } from "../../hooks/useRecordSearch";

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

const matterTypes = ["sprk_matter"];
const projectTypes = ["sprk_project"];
const multipleTypes = ["sprk_matter", "sprk_project", "sprk_invoice"];

function makeResult(id: string, type = "sprk_matter", score = 0.85): RecordSearchResult {
    return {
        recordId: id,
        recordType: type,
        recordName: `Record ${id}`,
        confidenceScore: score,
        organizations: ["Acme Corp"],
        people: ["John Doe"],
    };
}

function makeResponse(
    results: RecordSearchResult[],
    totalCount: number,
    searchTime = 150,
): RecordSearchResponse {
    return {
        results,
        metadata: {
            totalCount,
            searchTime,
            hybridMode: "rrf",
        },
    };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("useRecordSearch", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // --- Initial state ---

    describe("initial state", () => {
        it("should start with idle searchState", () => {
            const { result } = renderHook(() => useRecordSearch());

            expect(result.current.searchState).toBe("idle");
        });

        it("should start with empty results", () => {
            const { result } = renderHook(() => useRecordSearch());

            expect(result.current.results).toEqual([]);
            expect(result.current.totalCount).toBe(0);
        });

        it("should start with no error", () => {
            const { result } = renderHook(() => useRecordSearch());

            expect(result.current.errorMessage).toBeNull();
        });

        it("should start with no search time", () => {
            const { result } = renderHook(() => useRecordSearch());

            expect(result.current.searchTime).toBeNull();
        });

        it("should start with hasMore false", () => {
            const { result } = renderHook(() => useRecordSearch());

            expect(result.current.hasMore).toBe(false);
        });
    });

    // --- search() happy path ---

    describe("search() — success", () => {
        it("should transition to loading state immediately", () => {
            mockSearch.mockReturnValue(new Promise(() => {}));
            const { result } = renderHook(() => useRecordSearch());

            act(() => {
                result.current.search("employment dispute", matterTypes, defaultFilters);
            });

            expect(result.current.searchState).toBe("loading");
        });

        it("should transition to success state after API resolves", async () => {
            const response = makeResponse([makeResult("rec-1")], 1);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("employment dispute", matterTypes, defaultFilters);
            });

            expect(result.current.searchState).toBe("success");
        });

        it("should populate results from API response", async () => {
            const results = [makeResult("rec-1"), makeResult("rec-2")];
            const response = makeResponse(results, 2);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("dispute", matterTypes, defaultFilters);
            });

            expect(result.current.results).toEqual(results);
            expect(result.current.totalCount).toBe(2);
        });

        it("should record search duration", async () => {
            const response = makeResponse([makeResult("rec-1")], 1, 320);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.searchTime).toBe(320);
        });

        it("should handle empty results", async () => {
            const response = makeResponse([], 0);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("nonexistent", matterTypes, defaultFilters);
            });

            expect(result.current.searchState).toBe("success");
            expect(result.current.results).toEqual([]);
            expect(result.current.totalCount).toBe(0);
            expect(result.current.hasMore).toBe(false);
        });

        it("should pass recordTypes to the API service", async () => {
            mockSearch.mockResolvedValue(makeResponse([], 0));
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", multipleTypes, defaultFilters);
            });

            const callArg = mockSearch.mock.calls[0][0] as Record<string, unknown>;
            expect(callArg.recordTypes).toEqual(multipleTypes);
        });

        it("should pass correct request shape to API service", async () => {
            mockSearch.mockResolvedValue(makeResponse([], 0));
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("contract dispute", matterTypes, defaultFilters);
            });

            expect(mockSearch).toHaveBeenCalledWith({
                query: "contract dispute",
                recordTypes: matterTypes,
                options: {
                    limit: 20,
                    offset: 0,
                    hybridMode: "rrf",
                },
            });
        });

        it("should clear previous results when starting a new search", async () => {
            const firstResponse = makeResponse(
                [makeResult("rec-1", "sprk_matter")],
                1,
            );
            const secondResponse = makeResponse(
                [makeResult("rec-2", "sprk_project")],
                1,
            );
            mockSearch
                .mockResolvedValueOnce(firstResponse)
                .mockResolvedValueOnce(secondResponse);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("first", matterTypes, defaultFilters);
            });
            expect(result.current.results[0].recordId).toBe("rec-1");

            await act(async () => {
                result.current.search("second", projectTypes, defaultFilters);
            });
            expect(result.current.results).toHaveLength(1);
            expect(result.current.results[0].recordId).toBe("rec-2");
        });

        it("should set hasMore when totalCount exceeds results length", async () => {
            const response = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`rec-${i}`)),
                50,
            );
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.hasMore).toBe(true);
        });

        it("should set hasMore false when all results are loaded", async () => {
            const response = makeResponse([makeResult("rec-1")], 1);
            mockSearch.mockResolvedValue(response);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.hasMore).toBe(false);
        });

        it("should use hybridMode from filters", async () => {
            const vectorFilters: SearchFilters = {
                ...defaultFilters,
                searchMode: "vectorOnly",
            };
            mockSearch.mockResolvedValue(makeResponse([], 0));
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, vectorFilters);
            });

            const callArg = mockSearch.mock.calls[0][0] as Record<string, unknown>;
            const options = callArg.options as Record<string, unknown>;
            expect(options.hybridMode).toBe("vectorOnly");
        });
    });

    // --- search() error handling ---

    describe("search() — error handling", () => {
        it("should transition to error state on API failure", async () => {
            const apiError: ApiError = {
                status: 500,
                title: "Internal Server Error",
            };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.searchState).toBe("error");
        });

        it("should set user-friendly message for 401 errors", async () => {
            const apiError: ApiError = { status: 401, title: "Unauthorized" };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.errorMessage).toBe(
                "You do not have permission to perform this search. Please sign in again.",
            );
        });

        it("should set user-friendly message for 403 errors", async () => {
            const apiError: ApiError = { status: 403, title: "Forbidden" };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.errorMessage).toBe(
                "You do not have permission to perform this search. Please sign in again.",
            );
        });

        it("should set rate-limit message for 429 errors", async () => {
            const apiError: ApiError = { status: 429, title: "Too Many Requests" };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.errorMessage).toBe(
                "Too many requests. Please wait a moment and try again.",
            );
        });

        it("should use detail from ApiError when available", async () => {
            const apiError: ApiError = {
                status: 400,
                title: "Validation Error",
                detail: "recordTypes must contain at least one value.",
            };
            mockSearch.mockRejectedValue(apiError);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.errorMessage).toBe(
                "recordTypes must contain at least one value.",
            );
        });

        it("should handle generic Error objects", async () => {
            mockSearch.mockRejectedValue(new Error("Network timeout"));
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe("Network timeout");
        });

        it("should handle unknown error types", async () => {
            mockSearch.mockRejectedValue(42);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.errorMessage).toBe("An unexpected error occurred.");
        });

        it("should clear error when executing a new search", async () => {
            mockSearch.mockRejectedValueOnce(new Error("fail"));
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });
            expect(result.current.errorMessage).not.toBeNull();

            mockSearch.mockResolvedValueOnce(makeResponse([], 0));
            await act(async () => {
                result.current.search("test2", matterTypes, defaultFilters);
            });
            expect(result.current.errorMessage).toBeNull();
        });
    });

    // --- loadMore() ---

    describe("loadMore()", () => {
        it("should append results to existing set", async () => {
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`p1-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });
            expect(result.current.results).toHaveLength(20);

            const page2 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`p2-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page2);

            await act(async () => {
                result.current.loadMore();
            });

            expect(result.current.results).toHaveLength(40);
            expect(result.current.results[0].recordId).toBe("p1-0");
            expect(result.current.results[20].recordId).toBe("p2-0");
        });

        it("should transition through loadingMore state", async () => {
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`rec-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            mockSearch.mockReturnValueOnce(new Promise(() => {}));
            act(() => {
                result.current.loadMore();
            });

            expect(result.current.searchState).toBe("loadingMore");
        });

        it("should pass correct offset in loadMore request", async () => {
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`rec-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test query", matterTypes, defaultFilters);
            });

            mockSearch.mockResolvedValueOnce(makeResponse([], 50));
            await act(async () => {
                result.current.loadMore();
            });

            const secondCall = mockSearch.mock.calls[1][0] as Record<string, unknown>;
            const options = secondCall.options as Record<string, unknown>;
            expect(options.offset).toBe(20);
        });

        it("should reuse recordTypes from original search in loadMore", async () => {
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`rec-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", multipleTypes, defaultFilters);
            });

            mockSearch.mockResolvedValueOnce(makeResponse([], 50));
            await act(async () => {
                result.current.loadMore();
            });

            const secondCall = mockSearch.mock.calls[1][0] as Record<string, unknown>;
            expect(secondCall.recordTypes).toEqual(multipleTypes);
        });

        it("should be no-op when searchState is idle", () => {
            const { result } = renderHook(() => useRecordSearch());

            act(() => {
                result.current.loadMore();
            });

            expect(mockSearch).not.toHaveBeenCalled();
        });

        it("should be no-op when hasMore is false", async () => {
            const response = makeResponse([makeResult("rec-1")], 1);
            mockSearch.mockResolvedValueOnce(response);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            mockSearch.mockClear();
            act(() => {
                result.current.loadMore();
            });

            expect(mockSearch).not.toHaveBeenCalled();
        });

        it("should handle error during loadMore", async () => {
            const page1 = makeResponse(
                Array.from({ length: 20 }, (_, i) => makeResult(`rec-${i}`)),
                50,
            );
            mockSearch.mockResolvedValueOnce(page1);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            mockSearch.mockRejectedValueOnce(new Error("Pagination failed"));
            await act(async () => {
                result.current.loadMore();
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe("Pagination failed");
            expect(result.current.results).toHaveLength(20);
        });
    });

    // --- reset() ---

    describe("reset()", () => {
        it("should clear results and reset to idle", async () => {
            mockSearch.mockResolvedValue(makeResponse([makeResult("rec-1")], 1, 200));
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

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
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            act(() => {
                result.current.reset();
            });

            expect(result.current.searchState).toBe("idle");
            expect(result.current.errorMessage).toBeNull();
        });
    });

    // --- Cancellation ---

    describe("cancellation", () => {
        it("should discard results from cancelled search", async () => {
            let resolveFirst: (value: RecordSearchResponse) => void;
            const firstPromise = new Promise<RecordSearchResponse>((resolve) => {
                resolveFirst = resolve;
            });
            mockSearch.mockReturnValueOnce(firstPromise);

            const secondResponse = makeResponse([makeResult("second")], 1);
            mockSearch.mockResolvedValueOnce(secondResponse);

            const { result } = renderHook(() => useRecordSearch());

            act(() => {
                result.current.search("first", matterTypes, defaultFilters);
            });

            await act(async () => {
                result.current.search("second", matterTypes, defaultFilters);
            });

            await act(async () => {
                resolveFirst!(makeResponse([makeResult("first")], 1));
            });

            expect(result.current.results).toHaveLength(1);
            expect(result.current.results[0].recordId).toBe("second");
        });

        it("should not surface error from cancelled request", async () => {
            let rejectFirst: (reason: unknown) => void;
            const firstPromise = new Promise<RecordSearchResponse>((_, reject) => {
                rejectFirst = reject;
            });
            mockSearch.mockReturnValueOnce(firstPromise);

            mockSearch.mockResolvedValueOnce(makeResponse([makeResult("rec-1")], 1));

            const { result } = renderHook(() => useRecordSearch());

            act(() => {
                result.current.search("first", matterTypes, defaultFilters);
            });

            await act(async () => {
                result.current.search("second", matterTypes, defaultFilters);
            });

            await act(async () => {
                rejectFirst!(new Error("aborted"));
            });

            expect(result.current.searchState).toBe("success");
            expect(result.current.errorMessage).toBeNull();
        });
    });

    // --- Edge cases ---

    describe("edge cases", () => {
        it("should handle AbortError from DOMException", async () => {
            const abortError = new DOMException("Aborted", "AbortError");
            mockSearch.mockRejectedValue(abortError);
            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", matterTypes, defaultFilters);
            });

            expect(result.current.errorMessage).toBe("Search was cancelled.");
        });

        it("should handle search with different record types", async () => {
            mockSearch.mockResolvedValue(makeResponse([], 0));
            const { result } = renderHook(() => useRecordSearch());

            // Search with matter type
            await act(async () => {
                result.current.search("test", ["sprk_matter"], defaultFilters);
            });

            // Then search with project type
            await act(async () => {
                result.current.search("test", ["sprk_project"], defaultFilters);
            });

            const secondCall = mockSearch.mock.calls[1][0] as Record<string, unknown>;
            expect(secondCall.recordTypes).toEqual(["sprk_project"]);
        });
    });
});
