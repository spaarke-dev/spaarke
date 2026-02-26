/**
 * Unit tests for useSavedSearches hook -- CRUD for saved search configurations.
 *
 * Tests:
 * - Load on mount: fetches from Dataverse WebAPI
 * - saveSearch: POST to sprk_gridconfigurations then refreshes
 * - updateSearch: PATCH to sprk_gridconfigurations(id) then refreshes
 * - deleteSearch: soft-delete via PATCH statecode=1 then refreshes
 * - Error handling for all CRUD operations
 * - Filters by user ID from Xrm context
 * - Parsing configjson: semantic-search type only, version checking
 * - Edge cases: unmount during async, non-semantic configs ignored
 *
 * @see useSavedSearches.ts
 */

import { renderHook, act, waitFor } from "@testing-library/react";
import type { SavedSearch, SearchFilters } from "../../types";

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockGetOrgUrl = jest.fn<string, []>();

jest.mock("../../services/DataverseWebApiService", () => ({
    getOrgUrl: () => mockGetOrgUrl(),
}));

// Mock global fetch
const mockFetch = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();
global.fetch = mockFetch as typeof global.fetch;

// Mock Xrm global for user ID
const mockGetUserId = jest.fn<string, []>();
const mockGetClientUrl = jest.fn<string, []>();

beforeAll(() => {
    (window as Record<string, unknown>).Xrm = {
        Utility: {
            getGlobalContext: () => ({
                getUserId: mockGetUserId,
                getClientUrl: mockGetClientUrl,
            }),
        },
    };
});

afterAll(() => {
    delete (window as Record<string, unknown>).Xrm;
});

import { useSavedSearches } from "../../hooks/useSavedSearches";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const ORG_URL = "https://spaarkedev1.crm.dynamics.com";
const USER_ID = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

const defaultFilters: SearchFilters = {
    documentTypes: [],
    fileTypes: [],
    matterTypes: [],
    dateRange: { from: null, to: null },
    threshold: 0.5,
    searchMode: "rrf",
};

const sampleSavedSearch: SavedSearch = {
    name: "My Employment Search",
    searchDomain: "documents",
    query: "employment contracts",
    filters: defaultFilters,
    viewMode: "grid",
    columns: ["name", "documentType", "similarity"],
    sortColumn: "similarity",
    sortDirection: "desc",
};

function makeConfigJson(search: Partial<SavedSearch> = {}): string {
    return JSON.stringify({
        _type: "semantic-search",
        _version: 1,
        searchDomain: search.searchDomain ?? "documents",
        query: search.query ?? "test",
        filters: search.filters ?? defaultFilters,
        viewMode: search.viewMode ?? "grid",
        columns: search.columns ?? ["name"],
        sortColumn: search.sortColumn ?? "similarity",
        sortDirection: search.sortDirection ?? "desc",
        graphClusterBy: search.graphClusterBy,
    });
}

function makeGridConfigRecord(
    id: string,
    name: string,
    configJson?: string,
) {
    return {
        sprk_gridconfigurationid: id,
        sprk_name: name,
        sprk_configjson: configJson ?? makeConfigJson({ query: name }),
        _createdby_value: USER_ID,
    };
}

function createODataResponse(records: unknown[]): Response {
    return {
        ok: true,
        status: 200,
        json: jest.fn().mockResolvedValue({ value: records }),
    } as unknown as Response;
}

function createSuccessResponse(): Response {
    return {
        ok: true,
        status: 204,
        json: jest.fn().mockResolvedValue({}),
    } as unknown as Response;
}

function createErrorResponse(status: number): Response {
    return {
        ok: false,
        status,
        statusText: "Error",
        json: jest.fn().mockResolvedValue({}),
    } as unknown as Response;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("useSavedSearches", () => {
    beforeEach(() => {
        jest.clearAllMocks();
        mockGetOrgUrl.mockReturnValue(ORG_URL);
        mockGetUserId.mockReturnValue(`{${USER_ID}}`);
        mockGetClientUrl.mockReturnValue(ORG_URL);
    });

    // --- Load on mount ---

    describe("load on mount", () => {
        it("should start with isLoading true", () => {
            mockFetch.mockReturnValue(new Promise(() => {}));
            const { result } = renderHook(() => useSavedSearches());

            expect(result.current.isLoading).toBe(true);
        });

        it("should fetch saved searches on mount", async () => {
            const records = [
                makeGridConfigRecord("id-1", "Search A"),
                makeGridConfigRecord("id-2", "Search B"),
            ];
            mockFetch.mockResolvedValue(createODataResponse(records));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.savedSearches).toHaveLength(2);
            expect(result.current.savedSearches[0].name).toBe("Search A");
            expect(result.current.savedSearches[1].name).toBe("Search B");
        });

        it("should filter to semantic_search entity and active records", async () => {
            mockFetch.mockResolvedValue(createODataResponse([]));
            renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(mockFetch).toHaveBeenCalledTimes(1);
            });

            const [url] = mockFetch.mock.calls[0];
            const urlStr = url as string;
            expect(urlStr).toContain("sprk_gridconfigurations");
            expect(urlStr).toContain("semantic_search");
            expect(urlStr).toContain("statecode%20eq%200");
        });

        it("should filter by current user ID", async () => {
            mockFetch.mockResolvedValue(createODataResponse([]));
            renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(mockFetch).toHaveBeenCalledTimes(1);
            });

            const [url] = mockFetch.mock.calls[0];
            const urlStr = url as string;
            expect(urlStr).toContain(USER_ID);
        });

        it("should set error on fetch failure", async () => {
            mockFetch.mockResolvedValue(createErrorResponse(500));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.error).toContain("Failed to load saved searches");
        });

        it("should set error on network failure", async () => {
            mockFetch.mockRejectedValue(new TypeError("Failed to fetch"));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.error).toBe("Failed to fetch");
        });

        it("should skip records with non-semantic-search configjson", async () => {
            const records = [
                makeGridConfigRecord("id-1", "Semantic Search", makeConfigJson()),
                makeGridConfigRecord("id-2", "Not Semantic", JSON.stringify({
                    _type: "other-type",
                    _version: 1,
                })),
            ];
            mockFetch.mockResolvedValue(createODataResponse(records));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.savedSearches).toHaveLength(1);
        });

        it("should skip records with missing configjson", async () => {
            const records = [
                {
                    sprk_gridconfigurationid: "id-1",
                    sprk_name: "Empty Config",
                    // No sprk_configjson field
                },
            ];
            mockFetch.mockResolvedValue(createODataResponse(records));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.savedSearches).toHaveLength(0);
        });

        it("should skip records with invalid JSON in configjson", async () => {
            const records = [
                {
                    sprk_gridconfigurationid: "id-1",
                    sprk_name: "Bad JSON",
                    sprk_configjson: "not-json{{",
                },
            ];
            mockFetch.mockResolvedValue(createODataResponse(records));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.savedSearches).toHaveLength(0);
        });
    });

    // --- saveSearch ---

    describe("saveSearch()", () => {
        it("should POST new record to Dataverse", async () => {
            // Mount: load existing (empty)
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            // Save: POST
            mockFetch.mockResolvedValueOnce(createSuccessResponse());
            // Refresh: GET
            mockFetch.mockResolvedValueOnce(
                createODataResponse([makeGridConfigRecord("new-id", sampleSavedSearch.name)]),
            );

            await act(async () => {
                await result.current.saveSearch(sampleSavedSearch);
            });

            // Second call should be POST
            const [url, init] = mockFetch.mock.calls[1];
            expect(init?.method).toBe("POST");
            expect(url).toContain("sprk_gridconfigurations");

            const body = JSON.parse(init?.body as string);
            expect(body.sprk_name).toBe("My Employment Search");
            expect(body.sprk_entitylogicalname).toBe("semantic_search");
            expect(body.sprk_viewtype).toBe(2);
        });

        it("should set isSaving during save operation", async () => {
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            let resolveSave: () => void;
            mockFetch.mockReturnValueOnce(
                new Promise<Response>((resolve) => {
                    resolveSave = () => resolve(createSuccessResponse());
                }),
            );

            const savePromise = act(async () => {
                const p = result.current.saveSearch(sampleSavedSearch);
                return p;
            });

            // Should be saving while awaiting
            expect(result.current.isSaving).toBe(true);

            // Resolve save + refresh
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            await act(async () => {
                resolveSave!();
            });
            await savePromise;

            expect(result.current.isSaving).toBe(false);
        });

        it("should refresh list after successful save", async () => {
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetch.mockResolvedValueOnce(createSuccessResponse());
            mockFetch.mockResolvedValueOnce(
                createODataResponse([makeGridConfigRecord("new-id", "New Search")]),
            );

            await act(async () => {
                await result.current.saveSearch(sampleSavedSearch);
            });

            expect(result.current.savedSearches).toHaveLength(1);
        });

        it("should set error on save failure", async () => {
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetch.mockResolvedValueOnce(createErrorResponse(403));

            await act(async () => {
                await result.current.saveSearch(sampleSavedSearch);
            });

            expect(result.current.error).toContain("Failed to save search");
        });
    });

    // --- updateSearch ---

    describe("updateSearch()", () => {
        it("should PATCH existing record in Dataverse", async () => {
            const records = [makeGridConfigRecord("id-1", "Existing Search")];
            mockFetch.mockResolvedValueOnce(createODataResponse(records));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetch.mockResolvedValueOnce(createSuccessResponse());
            mockFetch.mockResolvedValueOnce(createODataResponse(records));

            const updated = { ...sampleSavedSearch, name: "Updated Name" };
            await act(async () => {
                await result.current.updateSearch("id-1", updated);
            });

            const [url, init] = mockFetch.mock.calls[1];
            expect(init?.method).toBe("PATCH");
            expect(url).toContain("sprk_gridconfigurations(id-1)");

            const body = JSON.parse(init?.body as string);
            expect(body.sprk_name).toBe("Updated Name");
        });

        it("should set error on update failure", async () => {
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetch.mockResolvedValueOnce(createErrorResponse(404));

            await act(async () => {
                await result.current.updateSearch("bad-id", sampleSavedSearch);
            });

            expect(result.current.error).toContain("Failed to update search");
        });

        it("should refresh list after successful update", async () => {
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetch.mockResolvedValueOnce(createSuccessResponse());
            const updatedRecords = [makeGridConfigRecord("id-1", "Updated")];
            mockFetch.mockResolvedValueOnce(createODataResponse(updatedRecords));

            await act(async () => {
                await result.current.updateSearch("id-1", sampleSavedSearch);
            });

            // Third fetch call should be the refresh GET
            expect(mockFetch).toHaveBeenCalledTimes(3);
        });
    });

    // --- deleteSearch ---

    describe("deleteSearch()", () => {
        it("should soft-delete by PATCHing statecode to 1", async () => {
            const records = [makeGridConfigRecord("id-1", "To Delete")];
            mockFetch.mockResolvedValueOnce(createODataResponse(records));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetch.mockResolvedValueOnce(createSuccessResponse());
            mockFetch.mockResolvedValueOnce(createODataResponse([]));

            await act(async () => {
                await result.current.deleteSearch("id-1");
            });

            const [url, init] = mockFetch.mock.calls[1];
            expect(init?.method).toBe("PATCH");
            expect(url).toContain("sprk_gridconfigurations(id-1)");

            const body = JSON.parse(init?.body as string);
            expect(body.statecode).toBe(1);
        });

        it("should refresh list after successful delete", async () => {
            const records = [makeGridConfigRecord("id-1", "To Delete")];
            mockFetch.mockResolvedValueOnce(createODataResponse(records));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetch.mockResolvedValueOnce(createSuccessResponse());
            mockFetch.mockResolvedValueOnce(createODataResponse([]));

            await act(async () => {
                await result.current.deleteSearch("id-1");
            });

            expect(result.current.savedSearches).toHaveLength(0);
        });

        it("should set error on delete failure", async () => {
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetch.mockResolvedValueOnce(createErrorResponse(500));

            await act(async () => {
                await result.current.deleteSearch("id-1");
            });

            expect(result.current.error).toContain("Failed to delete search");
        });
    });

    // --- refresh ---

    describe("refresh()", () => {
        it("should re-fetch saved searches from Dataverse", async () => {
            mockFetch.mockResolvedValueOnce(createODataResponse([]));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const newRecords = [makeGridConfigRecord("id-new", "New Result")];
            mockFetch.mockResolvedValueOnce(createODataResponse(newRecords));

            await act(async () => {
                await result.current.refresh();
            });

            expect(result.current.savedSearches).toHaveLength(1);
            expect(result.current.savedSearches[0].name).toBe("New Result");
        });
    });

    // --- Parsed SavedSearch shape ---

    describe("parsed SavedSearch shape", () => {
        it("should correctly parse all SavedSearch fields from configjson", async () => {
            const configJson = makeConfigJson({
                searchDomain: "matters",
                query: "employment dispute",
                filters: defaultFilters,
                viewMode: "graph",
                columns: ["name", "score"],
                sortColumn: "name",
                sortDirection: "asc",
                graphClusterBy: "Organization",
            });
            const records = [makeGridConfigRecord("id-1", "Full Search", configJson)];
            mockFetch.mockResolvedValue(createODataResponse(records));
            const { result } = renderHook(() => useSavedSearches());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const search = result.current.savedSearches[0];
            expect(search.id).toBe("id-1");
            expect(search.name).toBe("Full Search");
            expect(search.searchDomain).toBe("matters");
            expect(search.query).toBe("employment dispute");
            expect(search.viewMode).toBe("graph");
            expect(search.columns).toEqual(["name", "score"]);
            expect(search.sortColumn).toBe("name");
            expect(search.sortDirection).toBe("asc");
            expect(search.graphClusterBy).toBe("Organization");
        });
    });
});
