/**
 * Unit tests for useSemanticSearch hook
 *
 * @see useSemanticSearch.ts for implementation
 */
import { renderHook, act } from "@testing-library/react-hooks";
import { useSemanticSearch } from "../../hooks/useSemanticSearch";
import { SemanticSearchApiService } from "../../services";
import { SearchFilters, SearchResponse } from "../../types";

// Mock the API service
const mockSearch = jest.fn();
const mockApiService = {
    search: mockSearch,
} as unknown as SemanticSearchApiService;

// Helper to create mock response
const createMockResponse = (results: number, total: number): SearchResponse => ({
    results: Array.from({ length: results }, (_, i) => ({
        documentId: `doc-${i}`,
        name: `Document ${i}`,
        fileType: "pdf",
        documentType: "contract",
        matterName: null,
        matterId: null,
        createdAt: "2026-01-01",
        combinedScore: 0.95 - i * 0.01,
        highlights: ["test highlight"],
        fileUrl: `https://example.com/doc-${i}`,
        recordUrl: `https://crm.dynamics.com/doc-${i}`,
        createdBy: null,
        summary: null,
        tldr: null,
    })),
    totalCount: total,
    metadata: {
        searchTimeMs: 100,
        query: "test query",
    },
});

// Default empty filters
const emptyFilters: SearchFilters = {
    documentTypes: [],
    matterTypes: [],
    dateRange: null,
    fileTypes: [],
    threshold: 0,
    searchMode: "hybrid",
};

describe("useSemanticSearch", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    it("should initialize with idle state", () => {
        const { result } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        expect(result.current.results).toEqual([]);
        expect(result.current.totalCount).toBe(0);
        expect(result.current.state).toBe("idle");
        expect(result.current.isLoading).toBe(false);
        expect(result.current.isLoadingMore).toBe(false);
        expect(result.current.error).toBeNull();
        expect(result.current.hasMore).toBe(false);
        expect(result.current.query).toBe("");
    });

    it("should execute search successfully", async () => {
        mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

        const { result, waitForNextUpdate } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        act(() => {
            result.current.search("test query", emptyFilters);
        });

        expect(result.current.isLoading).toBe(true);
        expect(result.current.state).toBe("loading");

        await waitForNextUpdate();

        expect(result.current.results).toHaveLength(10);
        expect(result.current.totalCount).toBe(50);
        expect(result.current.state).toBe("success");
        expect(result.current.isLoading).toBe(false);
        expect(result.current.hasMore).toBe(true);
        expect(result.current.query).toBe("test query");
    });

    it("should not search empty query", async () => {
        const { result } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        await act(async () => {
            await result.current.search("", emptyFilters);
        });

        expect(mockSearch).not.toHaveBeenCalled();
        expect(result.current.state).toBe("idle");
    });

    it("should not search whitespace-only query", async () => {
        const { result } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        await act(async () => {
            await result.current.search("   ", emptyFilters);
        });

        expect(mockSearch).not.toHaveBeenCalled();
        expect(result.current.state).toBe("idle");
    });

    it("should handle search error", async () => {
        const mockError = { message: "Network error", retryable: true };
        mockSearch.mockRejectedValueOnce(mockError);

        const { result, waitForNextUpdate } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        act(() => {
            result.current.search("test query", emptyFilters);
        });

        await waitForNextUpdate();

        expect(result.current.state).toBe("error");
        expect(result.current.error).toEqual(mockError);
        expect(result.current.results).toEqual([]);
    });

    it("should load more results", async () => {
        // First search returns 10 of 50
        mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

        const { result, waitForNextUpdate } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        // Initial search
        act(() => {
            result.current.search("test query", emptyFilters);
        });
        await waitForNextUpdate();

        expect(result.current.results).toHaveLength(10);

        // Load more returns another 10
        mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

        act(() => {
            result.current.loadMore();
        });

        expect(result.current.isLoadingMore).toBe(true);
        expect(result.current.state).toBe("loadingMore");

        await waitForNextUpdate();

        expect(result.current.results).toHaveLength(20);
        expect(result.current.state).toBe("success");
    });

    it("should not load more when already loading", async () => {
        mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

        const { result, waitForNextUpdate } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        // Start initial search
        act(() => {
            result.current.search("test query", emptyFilters);
        });

        // Try to load more while loading
        await act(async () => {
            await result.current.loadMore();
        });

        // Should only have called search once
        expect(mockSearch).toHaveBeenCalledTimes(1);

        await waitForNextUpdate();
    });

    it("should not load more when no more results", async () => {
        // Search returns all 10 results
        mockSearch.mockResolvedValueOnce(createMockResponse(10, 10));

        const { result, waitForNextUpdate } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        act(() => {
            result.current.search("test query", emptyFilters);
        });
        await waitForNextUpdate();

        expect(result.current.hasMore).toBe(false);

        // Try to load more
        await act(async () => {
            await result.current.loadMore();
        });

        // Should only have called search once (initial)
        expect(mockSearch).toHaveBeenCalledTimes(1);
    });

    it("should reset state", async () => {
        mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

        const { result, waitForNextUpdate } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        // Execute search
        act(() => {
            result.current.search("test query", emptyFilters);
        });
        await waitForNextUpdate();

        expect(result.current.results).toHaveLength(10);

        // Reset
        act(() => {
            result.current.reset();
        });

        expect(result.current.results).toEqual([]);
        expect(result.current.totalCount).toBe(0);
        expect(result.current.state).toBe("idle");
        expect(result.current.query).toBe("");
    });

    it("should pass scope and scopeId to API", async () => {
        mockSearch.mockResolvedValueOnce(createMockResponse(5, 5));

        const { result, waitForNextUpdate } = renderHook(() =>
            useSemanticSearch(mockApiService, "matter", "matter-123")
        );

        act(() => {
            result.current.search("test", emptyFilters);
        });
        await waitForNextUpdate();

        expect(mockSearch).toHaveBeenCalledWith(
            expect.objectContaining({
                scope: "matter",
                scopeId: "matter-123",
            })
        );
    });

    it("should pass filters to API", async () => {
        mockSearch.mockResolvedValueOnce(createMockResponse(5, 5));

        const { result, waitForNextUpdate } = renderHook(() =>
            useSemanticSearch(mockApiService, "all", null)
        );

        const filters: SearchFilters = {
            documentTypes: ["contract"],
            matterTypes: [],
            dateRange: { from: "2026-01-01", to: "2026-01-31" },
            fileTypes: ["pdf"],
            threshold: 0,
            searchMode: "hybrid",
        };

        act(() => {
            result.current.search("test", filters);
        });
        await waitForNextUpdate();

        expect(mockSearch).toHaveBeenCalledWith(
            expect.objectContaining({
                filters,
            })
        );
    });
});
