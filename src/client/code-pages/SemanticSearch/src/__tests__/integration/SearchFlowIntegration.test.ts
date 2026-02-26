/**
 * Integration tests for the end-to-end search flow.
 *
 * Tests the integration between:
 *   - useSemanticSearch hook + SemanticSearchApiService (document search)
 *   - useRecordSearch hook + RecordSearchApiService (record search)
 *   - Shared apiBase (auth headers, response handling)
 *
 * All HTTP requests are mocked at the global fetch level so the full
 * service → apiBase → fetch pipeline is exercised. MSAL auth is mocked
 * to return a fake Bearer token immediately.
 *
 * Tests cover:
 *   - Document search: correct API call with parameters
 *   - Record search: correct API call with recordTypes and parameters
 *   - Filter passthrough: client-side filters mapped to API request body
 *   - Error handling: API failures surface as user-friendly error messages
 *   - State transitions: idle → loading → success (or error)
 *   - Pagination: loadMore appends results with correct offset
 *
 * @see useSemanticSearch.ts — document search hook
 * @see useRecordSearch.ts — record search hook
 * @see SemanticSearchApiService.ts — POST /api/ai/search
 * @see RecordSearchApiService.ts — POST /api/ai/search/records
 */

import { renderHook, act } from "@testing-library/react";
import type {
    DocumentSearchResponse,
    DocumentSearchResult,
    RecordSearchResponse,
    RecordSearchResult,
    SearchFilters,
} from "../../types";

// ---------------------------------------------------------------------------
// Mocks — MSAL auth provider returns a fake token immediately
// ---------------------------------------------------------------------------

jest.mock("../../services/auth/MsalAuthProvider", () => ({
    msalAuthProvider: {
        getAuthHeader: jest.fn().mockResolvedValue("Bearer fake-integration-token"),
        initialize: jest.fn().mockResolvedValue(undefined),
        isAuthenticated: jest.fn().mockReturnValue(true),
    },
}));

// Mock global fetch at the lowest level so the full service pipeline is tested
const mockFetch = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();
global.fetch = mockFetch as typeof global.fetch;

// Import hooks AFTER mocks are established
import { useSemanticSearch } from "../../hooks/useSemanticSearch";
import { useRecordSearch } from "../../hooks/useRecordSearch";
import { BFF_API_BASE_URL } from "../../services/apiBase";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const DEFAULT_FILTERS: SearchFilters = {
    documentTypes: [],
    fileTypes: [],
    matterTypes: [],
    dateRange: { from: null, to: null },
    threshold: 50,
    searchMode: "rrf",
};

const FILTERS_WITH_DOCUMENT_TYPE: SearchFilters = {
    documentTypes: ["Contract"],
    fileTypes: ["pdf"],
    matterTypes: [],
    dateRange: { from: "2025-01-01", to: "2025-12-31" },
    threshold: 50,
    searchMode: "rrf",
    entityTypes: ["matter"],
};

function makeDocResult(id: string, name: string, score: number): DocumentSearchResult {
    return {
        documentId: id,
        name,
        combinedScore: score,
        documentType: "Contract",
        fileType: "pdf",
        parentEntityType: "matter",
        parentEntityName: "Test Matter",
        createdAt: "2025-06-15T10:30:00Z",
    };
}

function makeDocResponse(
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

function makeRecordResult(id: string, name: string, type: string, score: number): RecordSearchResult {
    return {
        recordId: id,
        recordType: type,
        recordName: name,
        confidenceScore: score,
        matchReasons: ["Name match"],
        organizations: ["Acme Corp"],
        createdAt: "2025-03-10T08:00:00Z",
    };
}

function makeRecordResponse(
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

function createFetchResponse(body: unknown, ok = true, status = 200, statusText = "OK"): Response {
    return {
        ok,
        status,
        statusText,
        json: jest.fn().mockResolvedValue(body),
        headers: new Headers(),
        redirected: false,
        type: "basic",
        url: "",
        clone: jest.fn(),
        body: null,
        bodyUsed: false,
        arrayBuffer: jest.fn(),
        blob: jest.fn(),
        formData: jest.fn(),
        text: jest.fn(),
    } as unknown as Response;
}

function createErrorFetchResponse(
    status: number,
    body: unknown,
    statusText = "Error",
): Response {
    return createFetchResponse(body, false, status, statusText);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("SearchFlowIntegration", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // =======================================================================
    // Document Search Flow
    // =======================================================================

    describe("Document search flow (useSemanticSearch → SemanticSearchApiService → fetch)", () => {
        it("should call POST /api/ai/search with correct query and default options", async () => {
            const response = makeDocResponse(
                [makeDocResult("doc-1", "Lease Agreement", 0.95)],
                1,
            );
            mockFetch.mockResolvedValue(createFetchResponse(response));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("commercial lease", DEFAULT_FILTERS);
            });

            expect(mockFetch).toHaveBeenCalledTimes(1);
            const [url, init] = mockFetch.mock.calls[0];
            expect(url).toBe(`${BFF_API_BASE_URL}/api/ai/search`);
            expect(init?.method).toBe("POST");

            const body = JSON.parse(init?.body as string);
            expect(body.query).toBe("commercial lease");
            expect(body.scope).toBe("all");
            expect(body.options.limit).toBe(20);
            expect(body.options.offset).toBe(0);
            expect(body.options.includeHighlights).toBe(true);
            expect(body.options.hybridMode).toBe("rrf");
        });

        it("should include Bearer token in Authorization header", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(makeDocResponse([], 0)));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            const [, init] = mockFetch.mock.calls[0];
            const headers = init?.headers as Record<string, string>;
            expect(headers["Authorization"]).toBe("Bearer fake-integration-token");
            expect(headers["Content-Type"]).toBe("application/json");
        });

        it("should populate results and metadata on successful response", async () => {
            const docs = [
                makeDocResult("doc-1", "Lease Agreement", 0.95),
                makeDocResult("doc-2", "Service Contract", 0.88),
                makeDocResult("doc-3", "NDA", 0.72),
            ];
            mockFetch.mockResolvedValue(createFetchResponse(makeDocResponse(docs, 42, 350)));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("commercial lease", DEFAULT_FILTERS);
            });

            expect(result.current.searchState).toBe("success");
            expect(result.current.results).toHaveLength(3);
            expect(result.current.results[0].documentId).toBe("doc-1");
            expect(result.current.results[0].name).toBe("Lease Agreement");
            expect(result.current.totalCount).toBe(42);
            expect(result.current.searchTime).toBe(350);
        });

        it("should transition through idle → loading → success states", async () => {
            let resolveResponse: (value: Response) => void;
            const pendingFetch = new Promise<Response>((resolve) => {
                resolveResponse = resolve;
            });
            mockFetch.mockReturnValue(pendingFetch);

            const { result } = renderHook(() => useSemanticSearch());

            // Initially idle
            expect(result.current.searchState).toBe("idle");

            // Start search — synchronously transitions to loading
            act(() => {
                result.current.search("test query", DEFAULT_FILTERS);
            });
            expect(result.current.searchState).toBe("loading");

            // Resolve — transitions to success
            await act(async () => {
                resolveResponse!(createFetchResponse(makeDocResponse([], 0)));
            });
            expect(result.current.searchState).toBe("success");
        });

        it("should handle empty results gracefully", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(makeDocResponse([], 0, 15)));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("nonexistent query xyz", DEFAULT_FILTERS);
            });

            expect(result.current.searchState).toBe("success");
            expect(result.current.results).toEqual([]);
            expect(result.current.totalCount).toBe(0);
            expect(result.current.hasMore).toBe(false);
        });
    });

    // =======================================================================
    // Record Search Flow
    // =======================================================================

    describe("Record search flow (useRecordSearch → RecordSearchApiService → fetch)", () => {
        it("should call POST /api/ai/search/records with correct recordTypes", async () => {
            const response = makeRecordResponse(
                [makeRecordResult("matter-1", "Johnson v. Smith", "sprk_matter", 0.91)],
                1,
            );
            mockFetch.mockResolvedValue(createFetchResponse(response));

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("Johnson", ["sprk_matter"], DEFAULT_FILTERS);
            });

            expect(mockFetch).toHaveBeenCalledTimes(1);
            const [url, init] = mockFetch.mock.calls[0];
            expect(url).toBe(`${BFF_API_BASE_URL}/api/ai/search/records`);
            expect(init?.method).toBe("POST");

            const body = JSON.parse(init?.body as string);
            expect(body.query).toBe("Johnson");
            expect(body.recordTypes).toEqual(["sprk_matter"]);
            expect(body.options.limit).toBe(20);
            expect(body.options.offset).toBe(0);
            expect(body.options.hybridMode).toBe("rrf");
        });

        it("should populate record results and metadata on success", async () => {
            const records = [
                makeRecordResult("matter-1", "Johnson v. Smith", "sprk_matter", 0.91),
                makeRecordResult("matter-2", "Acme Corp Dispute", "sprk_matter", 0.85),
                makeRecordResult("matter-3", "IP Licensing Case", "sprk_matter", 0.78),
            ];
            mockFetch.mockResolvedValue(createFetchResponse(makeRecordResponse(records, 25, 180)));

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("dispute", ["sprk_matter"], DEFAULT_FILTERS);
            });

            expect(result.current.searchState).toBe("success");
            expect(result.current.results).toHaveLength(3);
            expect(result.current.results[0].recordId).toBe("matter-1");
            expect(result.current.results[0].recordName).toBe("Johnson v. Smith");
            expect(result.current.totalCount).toBe(25);
            expect(result.current.searchTime).toBe(180);
        });

        it("should transition through idle → loading → success states", async () => {
            let resolveResponse: (value: Response) => void;
            const pendingFetch = new Promise<Response>((resolve) => {
                resolveResponse = resolve;
            });
            mockFetch.mockReturnValue(pendingFetch);

            const { result } = renderHook(() => useRecordSearch());

            expect(result.current.searchState).toBe("idle");

            act(() => {
                result.current.search("test", ["sprk_matter"], DEFAULT_FILTERS);
            });
            expect(result.current.searchState).toBe("loading");

            await act(async () => {
                resolveResponse!(createFetchResponse(makeRecordResponse([], 0)));
            });
            expect(result.current.searchState).toBe("success");
        });
    });

    // =======================================================================
    // Filter Passthrough
    // =======================================================================

    describe("Filter passthrough to API requests", () => {
        it("should map documentTypes and fileTypes filters into document search request", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(makeDocResponse([], 0)));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", FILTERS_WITH_DOCUMENT_TYPE);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body.filters.documentTypes).toEqual(["Contract"]);
            expect(body.filters.fileTypes).toEqual(["pdf"]);
            expect(body.filters.entityTypes).toEqual(["matter"]);
        });

        it("should map date range filter into document search request", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(makeDocResponse([], 0)));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", FILTERS_WITH_DOCUMENT_TYPE);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body.filters.dateRange).toEqual({
                field: "createdAt",
                from: "2025-01-01",
                to: "2025-12-31",
            });
        });

        it("should omit filters when all filter arrays are empty and no date range", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(makeDocResponse([], 0)));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body.filters).toBeUndefined();
        });

        it("should pass hybridMode from searchMode filter to options", async () => {
            const filtersVectorOnly: SearchFilters = {
                ...DEFAULT_FILTERS,
                searchMode: "vectorOnly",
            };
            mockFetch.mockResolvedValue(createFetchResponse(makeDocResponse([], 0)));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", filtersVectorOnly);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body.options.hybridMode).toBe("vectorOnly");
        });

        it("should pass hybridMode to record search options", async () => {
            const filtersKeyword: SearchFilters = {
                ...DEFAULT_FILTERS,
                searchMode: "keywordOnly",
            };
            mockFetch.mockResolvedValue(createFetchResponse(makeRecordResponse([], 0)));

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", ["sprk_matter"], filtersKeyword);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body.options.hybridMode).toBe("keywordOnly");
        });
    });

    // =======================================================================
    // Error Handling
    // =======================================================================

    describe("Error handling when API fails", () => {
        it("should transition to error state on HTTP 500 (document search)", async () => {
            mockFetch.mockResolvedValue(
                createErrorFetchResponse(500, {
                    title: "Internal Server Error",
                    detail: "Something went wrong on the server.",
                }),
            );

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe("Something went wrong on the server.");
        });

        it("should transition to error state on HTTP 500 (record search)", async () => {
            mockFetch.mockResolvedValue(
                createErrorFetchResponse(500, {
                    title: "Internal Server Error",
                    detail: "Record search failed.",
                }),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", ["sprk_matter"], DEFAULT_FILTERS);
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe("Record search failed.");
        });

        it("should show permission message for 401 errors", async () => {
            mockFetch.mockResolvedValue(
                createErrorFetchResponse(401, { title: "Unauthorized" }, "Unauthorized"),
            );

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe(
                "You do not have permission to perform this search. Please sign in again.",
            );
        });

        it("should show permission message for 403 errors", async () => {
            mockFetch.mockResolvedValue(
                createErrorFetchResponse(403, { title: "Forbidden" }, "Forbidden"),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", ["sprk_project"], DEFAULT_FILTERS);
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe(
                "You do not have permission to perform this search. Please sign in again.",
            );
        });

        it("should show rate-limit message for 429 errors", async () => {
            mockFetch.mockResolvedValue(
                createErrorFetchResponse(429, { title: "Too Many Requests" }, "Too Many Requests"),
            );

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            expect(result.current.errorMessage).toBe(
                "Too many requests. Please wait a moment and try again.",
            );
        });

        it("should handle network failure (fetch rejects)", async () => {
            mockFetch.mockRejectedValue(new TypeError("Failed to fetch"));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).toBe("Failed to fetch");
        });

        it("should clear error when a new successful search is executed", async () => {
            // First: trigger an error
            mockFetch.mockResolvedValueOnce(
                createErrorFetchResponse(500, { title: "Server Error" }),
            );

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("first", DEFAULT_FILTERS);
            });
            expect(result.current.searchState).toBe("error");
            expect(result.current.errorMessage).not.toBeNull();

            // Second: successful search clears the error
            mockFetch.mockResolvedValueOnce(createFetchResponse(makeDocResponse([], 0)));

            await act(async () => {
                result.current.search("second", DEFAULT_FILTERS);
            });
            expect(result.current.searchState).toBe("success");
            expect(result.current.errorMessage).toBeNull();
        });
    });

    // =======================================================================
    // Pagination (loadMore)
    // =======================================================================

    describe("Pagination via loadMore", () => {
        it("should append results from second page with correct offset (document search)", async () => {
            // First page: 20 results out of 50
            const page1Results = Array.from({ length: 20 }, (_, i) =>
                makeDocResult(`doc-${i}`, `Document ${i}`, 0.9 - i * 0.01),
            );
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(makeDocResponse(page1Results, 50)),
            );

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });
            expect(result.current.results).toHaveLength(20);
            expect(result.current.hasMore).toBe(true);

            // Second page: 20 more results
            const page2Results = Array.from({ length: 20 }, (_, i) =>
                makeDocResult(`doc-${20 + i}`, `Document ${20 + i}`, 0.7 - i * 0.01),
            );
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(makeDocResponse(page2Results, 50)),
            );

            await act(async () => {
                result.current.loadMore();
            });

            expect(result.current.results).toHaveLength(40);
            expect(result.current.results[0].documentId).toBe("doc-0");
            expect(result.current.results[20].documentId).toBe("doc-20");

            // Verify offset was 20 in second call
            const secondCallBody = JSON.parse(mockFetch.mock.calls[1][1]?.body as string);
            expect(secondCallBody.options.offset).toBe(20);
        });

        it("should append results from second page with correct offset (record search)", async () => {
            const page1Results = Array.from({ length: 20 }, (_, i) =>
                makeRecordResult(`matter-${i}`, `Matter ${i}`, "sprk_matter", 0.9 - i * 0.01),
            );
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(makeRecordResponse(page1Results, 50)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", ["sprk_matter"], DEFAULT_FILTERS);
            });
            expect(result.current.results).toHaveLength(20);
            expect(result.current.hasMore).toBe(true);

            const page2Results = Array.from({ length: 20 }, (_, i) =>
                makeRecordResult(`matter-${20 + i}`, `Matter ${20 + i}`, "sprk_matter", 0.7 - i * 0.01),
            );
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(makeRecordResponse(page2Results, 50)),
            );

            await act(async () => {
                result.current.loadMore();
            });

            expect(result.current.results).toHaveLength(40);

            const secondCallBody = JSON.parse(mockFetch.mock.calls[1][1]?.body as string);
            expect(secondCallBody.options.offset).toBe(20);
        });

        it("should not call API when hasMore is false", async () => {
            // All results fit in a single page
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(makeDocResponse([makeDocResult("doc-1", "Doc 1", 0.9)], 1)),
            );

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });
            expect(result.current.hasMore).toBe(false);

            mockFetch.mockClear();

            act(() => {
                result.current.loadMore();
            });

            expect(mockFetch).not.toHaveBeenCalled();
        });

        it("should transition through loadingMore state", async () => {
            const page1 = Array.from({ length: 20 }, (_, i) =>
                makeDocResult(`doc-${i}`, `Doc ${i}`, 0.9),
            );
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(makeDocResponse(page1, 50)),
            );

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            // Use a never-resolving promise to capture loadingMore state
            mockFetch.mockReturnValueOnce(new Promise(() => {}));

            act(() => {
                result.current.loadMore();
            });
            expect(result.current.searchState).toBe("loadingMore");
        });
    });

    // =======================================================================
    // New search replaces previous results
    // =======================================================================

    describe("New search replaces previous results", () => {
        it("should clear document results when starting a new search", async () => {
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(
                    makeDocResponse([makeDocResult("doc-old", "Old Result", 0.9)], 1),
                ),
            );

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("first query", DEFAULT_FILTERS);
            });
            expect(result.current.results[0].documentId).toBe("doc-old");

            mockFetch.mockResolvedValueOnce(
                createFetchResponse(
                    makeDocResponse([makeDocResult("doc-new", "New Result", 0.95)], 1),
                ),
            );

            await act(async () => {
                result.current.search("second query", DEFAULT_FILTERS);
            });
            expect(result.current.results).toHaveLength(1);
            expect(result.current.results[0].documentId).toBe("doc-new");
        });

        it("should clear record results when starting a new search", async () => {
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(
                    makeRecordResponse(
                        [makeRecordResult("old-1", "Old Matter", "sprk_matter", 0.8)],
                        1,
                    ),
                ),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("first", ["sprk_matter"], DEFAULT_FILTERS);
            });
            expect(result.current.results[0].recordId).toBe("old-1");

            mockFetch.mockResolvedValueOnce(
                createFetchResponse(
                    makeRecordResponse(
                        [makeRecordResult("new-1", "New Matter", "sprk_matter", 0.95)],
                        1,
                    ),
                ),
            );

            await act(async () => {
                result.current.search("second", ["sprk_matter"], DEFAULT_FILTERS);
            });
            expect(result.current.results).toHaveLength(1);
            expect(result.current.results[0].recordId).toBe("new-1");
        });
    });
});
