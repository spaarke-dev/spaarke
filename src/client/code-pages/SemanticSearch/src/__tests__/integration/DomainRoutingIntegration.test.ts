/**
 * Integration tests for domain-to-API routing.
 *
 * Verifies that the correct API endpoint and request shape is used
 * for each search domain:
 *
 *   - Documents domain → POST /api/ai/search (SemanticSearchApiService)
 *   - Matters domain   → POST /api/ai/search/records with recordTypes=["sprk_matter"]
 *   - Projects domain  → POST /api/ai/search/records with recordTypes=["sprk_project"]
 *   - Invoices domain  → POST /api/ai/search/records with recordTypes=["sprk_invoice"]
 *
 * Tests mock at the global fetch level so the full pipeline is exercised:
 *   hook → service → apiBase → fetch
 *
 * @see App.tsx — DOMAIN_RECORD_TYPES mapping
 * @see useSemanticSearch.ts — document search hook
 * @see useRecordSearch.ts — record search hook
 * @see types/index.ts — RecordEntityTypes constants
 */

import { renderHook, act } from "@testing-library/react";
import type {
    DocumentSearchResponse,
    RecordSearchResponse,
    SearchFilters,
} from "../../types";
import { RecordEntityTypes } from "../../types";

// ---------------------------------------------------------------------------
// Mocks — MSAL auth provider returns a fake token immediately
// ---------------------------------------------------------------------------

jest.mock("../../services/auth/MsalAuthProvider", () => ({
    msalAuthProvider: {
        getAuthHeader: jest.fn().mockResolvedValue("Bearer fake-routing-token"),
        initialize: jest.fn().mockResolvedValue(undefined),
        isAuthenticated: jest.fn().mockReturnValue(true),
    },
}));

// Mock global fetch
const mockFetch = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();
global.fetch = mockFetch as typeof global.fetch;

// Import hooks AFTER mocks
import { useSemanticSearch } from "../../hooks/useSemanticSearch";
import { useRecordSearch } from "../../hooks/useRecordSearch";
import { BFF_API_BASE_URL } from "../../services/apiBase";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DOC_SEARCH_ENDPOINT = `${BFF_API_BASE_URL}/api/ai/search`;
const RECORD_SEARCH_ENDPOINT = `${BFF_API_BASE_URL}/api/ai/search/records`;

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

const docResponse: DocumentSearchResponse = {
    results: [
        {
            documentId: "doc-001",
            name: "Sample Contract.pdf",
            combinedScore: 0.92,
            documentType: "Contract",
            fileType: "pdf",
        },
    ],
    metadata: {
        totalResults: 1,
        returnedResults: 1,
        searchDurationMs: 200,
        embeddingDurationMs: 30,
    },
};

function makeRecordResponse(recordType: string): RecordSearchResponse {
    return {
        results: [
            {
                recordId: `${recordType}-001`,
                recordType,
                recordName: `Test ${recordType} Record`,
                confidenceScore: 0.88,
                matchReasons: ["Name match"],
            },
        ],
        metadata: {
            totalCount: 1,
            searchTime: 150,
            hybridMode: "rrf",
        },
    };
}

function createFetchResponse(body: unknown): Response {
    return {
        ok: true,
        status: 200,
        statusText: "OK",
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

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("DomainRoutingIntegration", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // =======================================================================
    // Documents domain → SemanticSearchApiService
    // =======================================================================

    describe("Documents domain", () => {
        it("should route to POST /api/ai/search", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(docResponse));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("employment contracts", DEFAULT_FILTERS);
            });

            expect(mockFetch).toHaveBeenCalledTimes(1);
            const [url] = mockFetch.mock.calls[0];
            expect(url).toBe(DOC_SEARCH_ENDPOINT);
        });

        it("should send DocumentSearchRequest shape (query, scope, options)", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(docResponse));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("lease agreement", DEFAULT_FILTERS);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body).toHaveProperty("query", "lease agreement");
            expect(body).toHaveProperty("scope", "all");
            expect(body).toHaveProperty("options");
            expect(body.options).toHaveProperty("limit", 20);
            expect(body.options).toHaveProperty("offset", 0);
            expect(body.options).toHaveProperty("includeHighlights", true);
            expect(body.options).toHaveProperty("hybridMode", "rrf");
        });

        it("should NOT send recordTypes field in document search", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(docResponse));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body).not.toHaveProperty("recordTypes");
        });

        it("should return DocumentSearchResult objects with documentId", async () => {
            mockFetch.mockResolvedValue(createFetchResponse(docResponse));

            const { result } = renderHook(() => useSemanticSearch());

            await act(async () => {
                result.current.search("test", DEFAULT_FILTERS);
            });

            expect(result.current.results).toHaveLength(1);
            expect(result.current.results[0]).toHaveProperty("documentId", "doc-001");
            expect(result.current.results[0]).toHaveProperty("name", "Sample Contract.pdf");
            expect(result.current.results[0]).toHaveProperty("combinedScore", 0.92);
        });
    });

    // =======================================================================
    // Matters domain → RecordSearchApiService with ["sprk_matter"]
    // =======================================================================

    describe("Matters domain", () => {
        it("should route to POST /api/ai/search/records", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Matter)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("employment dispute", [RecordEntityTypes.Matter], DEFAULT_FILTERS);
            });

            expect(mockFetch).toHaveBeenCalledTimes(1);
            const [url] = mockFetch.mock.calls[0];
            expect(url).toBe(RECORD_SEARCH_ENDPOINT);
        });

        it("should send recordTypes=[\"sprk_matter\"]", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Matter)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", [RecordEntityTypes.Matter], DEFAULT_FILTERS);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body.recordTypes).toEqual(["sprk_matter"]);
        });

        it("should send RecordSearchRequest shape (query, recordTypes, options)", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Matter)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("Johnson", [RecordEntityTypes.Matter], DEFAULT_FILTERS);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body).toHaveProperty("query", "Johnson");
            expect(body).toHaveProperty("recordTypes", ["sprk_matter"]);
            expect(body).toHaveProperty("options");
            expect(body.options).toHaveProperty("limit", 20);
            expect(body.options).toHaveProperty("offset", 0);
            expect(body.options).toHaveProperty("hybridMode", "rrf");
        });

        it("should NOT send scope or includeHighlights fields in record search", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Matter)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", [RecordEntityTypes.Matter], DEFAULT_FILTERS);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body).not.toHaveProperty("scope");
            expect(body.options).not.toHaveProperty("includeHighlights");
        });

        it("should return RecordSearchResult objects with recordId and recordType", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Matter)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", [RecordEntityTypes.Matter], DEFAULT_FILTERS);
            });

            expect(result.current.results).toHaveLength(1);
            expect(result.current.results[0]).toHaveProperty("recordId", "sprk_matter-001");
            expect(result.current.results[0]).toHaveProperty("recordType", "sprk_matter");
        });
    });

    // =======================================================================
    // Projects domain → RecordSearchApiService with ["sprk_project"]
    // =======================================================================

    describe("Projects domain", () => {
        it("should route to POST /api/ai/search/records", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Project)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("construction project", [RecordEntityTypes.Project], DEFAULT_FILTERS);
            });

            expect(mockFetch).toHaveBeenCalledTimes(1);
            const [url] = mockFetch.mock.calls[0];
            expect(url).toBe(RECORD_SEARCH_ENDPOINT);
        });

        it("should send recordTypes=[\"sprk_project\"]", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Project)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", [RecordEntityTypes.Project], DEFAULT_FILTERS);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body.recordTypes).toEqual(["sprk_project"]);
        });

        it("should return results with recordType sprk_project", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Project)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", [RecordEntityTypes.Project], DEFAULT_FILTERS);
            });

            expect(result.current.results[0].recordType).toBe("sprk_project");
        });
    });

    // =======================================================================
    // Invoices domain → RecordSearchApiService with ["sprk_invoice"]
    // =======================================================================

    describe("Invoices domain", () => {
        it("should route to POST /api/ai/search/records", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Invoice)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("overdue invoice", [RecordEntityTypes.Invoice], DEFAULT_FILTERS);
            });

            expect(mockFetch).toHaveBeenCalledTimes(1);
            const [url] = mockFetch.mock.calls[0];
            expect(url).toBe(RECORD_SEARCH_ENDPOINT);
        });

        it("should send recordTypes=[\"sprk_invoice\"]", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Invoice)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", [RecordEntityTypes.Invoice], DEFAULT_FILTERS);
            });

            const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string);
            expect(body.recordTypes).toEqual(["sprk_invoice"]);
        });

        it("should return results with recordType sprk_invoice", async () => {
            mockFetch.mockResolvedValue(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Invoice)),
            );

            const { result } = renderHook(() => useRecordSearch());

            await act(async () => {
                result.current.search("test", [RecordEntityTypes.Invoice], DEFAULT_FILTERS);
            });

            expect(result.current.results[0].recordType).toBe("sprk_invoice");
        });
    });

    // =======================================================================
    // RecordEntityTypes constant validation
    // =======================================================================

    describe("RecordEntityTypes constants", () => {
        it("should map Matter to 'sprk_matter'", () => {
            expect(RecordEntityTypes.Matter).toBe("sprk_matter");
        });

        it("should map Project to 'sprk_project'", () => {
            expect(RecordEntityTypes.Project).toBe("sprk_project");
        });

        it("should map Invoice to 'sprk_invoice'", () => {
            expect(RecordEntityTypes.Invoice).toBe("sprk_invoice");
        });
    });

    // =======================================================================
    // Cross-domain endpoint isolation
    // =======================================================================

    describe("Cross-domain endpoint isolation", () => {
        it("should use different endpoints for document vs. record search", async () => {
            // Document search
            mockFetch.mockResolvedValueOnce(createFetchResponse(docResponse));
            const { result: docResult } = renderHook(() => useSemanticSearch());

            await act(async () => {
                docResult.current.search("test", DEFAULT_FILTERS);
            });
            const docUrl = mockFetch.mock.calls[0][0];

            // Record search
            mockFetch.mockResolvedValueOnce(
                createFetchResponse(makeRecordResponse(RecordEntityTypes.Matter)),
            );
            const { result: recResult } = renderHook(() => useRecordSearch());

            await act(async () => {
                recResult.current.search("test", [RecordEntityTypes.Matter], DEFAULT_FILTERS);
            });
            const recUrl = mockFetch.mock.calls[1][0];

            expect(docUrl).toBe(DOC_SEARCH_ENDPOINT);
            expect(recUrl).toBe(RECORD_SEARCH_ENDPOINT);
            expect(docUrl).not.toBe(recUrl);
        });

        it("should use same /api/ai/search/records endpoint for all record domains", async () => {
            const domains = [
                RecordEntityTypes.Matter,
                RecordEntityTypes.Project,
                RecordEntityTypes.Invoice,
            ];

            for (const entityType of domains) {
                mockFetch.mockResolvedValueOnce(
                    createFetchResponse(makeRecordResponse(entityType)),
                );
            }

            const { result } = renderHook(() => useRecordSearch());

            for (const entityType of domains) {
                await act(async () => {
                    result.current.search("test", [entityType], DEFAULT_FILTERS);
                });
            }

            // All three calls should go to the same records endpoint
            for (let i = 0; i < domains.length; i++) {
                expect(mockFetch.mock.calls[i][0]).toBe(RECORD_SEARCH_ENDPOINT);
            }

            // But each call should have a different recordTypes value
            const recordTypesSent = mockFetch.mock.calls.map((call) => {
                const body = JSON.parse(call[1]?.body as string);
                return body.recordTypes;
            });
            expect(recordTypesSent[0]).toEqual(["sprk_matter"]);
            expect(recordTypesSent[1]).toEqual(["sprk_project"]);
            expect(recordTypesSent[2]).toEqual(["sprk_invoice"]);
        });
    });
});
