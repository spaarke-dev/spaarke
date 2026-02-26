/**
 * Unit tests for RecordSearchApiService.ts — record search API client.
 *
 * Tests:
 * - search() sends POST to correct endpoint with auth headers and JSON body
 * - search() returns parsed RecordSearchResponse on success
 * - search() throws ApiError on HTTP error responses
 * - search() propagates network failures and auth failures
 *
 * @see RecordSearchApiService.ts
 */

import type {
    RecordSearchRequest,
    RecordSearchResponse,
    ApiError,
} from "../../types";

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockGetAuthHeader = jest.fn<Promise<string>, []>();

jest.mock("../../services/auth/MsalAuthProvider", () => ({
    msalAuthProvider: {
        getAuthHeader: mockGetAuthHeader,
    },
}));

// Mock global fetch
const mockFetch = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();
global.fetch = mockFetch as typeof global.fetch;

import { search } from "../../services/RecordSearchApiService";
import { BFF_API_BASE_URL } from "../../services/apiBase";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const EXPECTED_ENDPOINT = `${BFF_API_BASE_URL}/api/ai/search/records`;

const sampleRequest: RecordSearchRequest = {
    query: "Johnson merger acquisition",
    recordTypes: ["sprk_matter", "sprk_project"],
    filters: {
        organizations: ["Acme Corp"],
        people: ["John Smith"],
    },
    options: {
        limit: 20,
        offset: 0,
        hybridMode: "rrf",
    },
};

const sampleResponse: RecordSearchResponse = {
    results: [
        {
            recordId: "rec-001",
            recordType: "sprk_matter",
            recordName: "Johnson Merger Agreement",
            recordDescription: "Corporate merger matter for Johnson Industries",
            confidenceScore: 0.94,
            matchReasons: ["Name match: Johnson", "Type: merger"],
            organizations: ["Acme Corp", "Johnson Industries"],
            people: ["John Smith", "Jane Doe"],
            keywords: ["merger", "acquisition", "corporate"],
            createdAt: "2025-03-10T14:00:00Z",
            modifiedAt: "2025-11-20T09:30:00Z",
        },
    ],
    metadata: {
        totalCount: 1,
        searchTime: 180,
        hybridMode: "rrf",
    },
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function createSuccessResponse(body: unknown): Response {
    return {
        ok: true,
        status: 200,
        statusText: "OK",
        json: jest.fn().mockResolvedValue(body),
    } as unknown as Response;
}

function createErrorResponse(
    status: number,
    body: unknown,
    statusText = "Error",
): Response {
    return {
        ok: false,
        status,
        statusText,
        json: jest.fn().mockResolvedValue(body),
    } as unknown as Response;
}

function createNetworkErrorResponse(
    status: number,
    statusText: string,
): Response {
    return {
        ok: false,
        status,
        statusText,
        json: jest.fn().mockRejectedValue(new SyntaxError("Unexpected token")),
    } as unknown as Response;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("RecordSearchApiService", () => {
    beforeEach(() => {
        jest.clearAllMocks();
        mockGetAuthHeader.mockResolvedValue("Bearer test-token");
    });

    describe("search()", () => {
        // --- Request construction ---

        describe("request construction", () => {
            it("should POST to /api/ai/search/records endpoint", async () => {
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(sampleRequest);

                expect(mockFetch).toHaveBeenCalledTimes(1);
                const [url] = mockFetch.mock.calls[0];
                expect(url).toBe(EXPECTED_ENDPOINT);
            });

            it("should use POST method", async () => {
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(sampleRequest);

                const [, init] = mockFetch.mock.calls[0];
                expect(init?.method).toBe("POST");
            });

            it("should include Authorization header from MSAL", async () => {
                mockGetAuthHeader.mockResolvedValue("Bearer record-search-token");
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(sampleRequest);

                const [, init] = mockFetch.mock.calls[0];
                const headers = init?.headers as Record<string, string>;
                expect(headers["Authorization"]).toBe("Bearer record-search-token");
            });

            it("should include Content-Type application/json header", async () => {
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(sampleRequest);

                const [, init] = mockFetch.mock.calls[0];
                const headers = init?.headers as Record<string, string>;
                expect(headers["Content-Type"]).toBe("application/json");
            });

            it("should JSON-stringify the request body", async () => {
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(sampleRequest);

                const [, init] = mockFetch.mock.calls[0];
                expect(init?.body).toBe(JSON.stringify(sampleRequest));
            });

            it("should serialize request with single record type", async () => {
                const singleTypeRequest: RecordSearchRequest = {
                    query: "invoice overdue",
                    recordTypes: ["sprk_invoice"],
                };
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(singleTypeRequest);

                const [, init] = mockFetch.mock.calls[0];
                const parsedBody = JSON.parse(init?.body as string);
                expect(parsedBody.recordTypes).toEqual(["sprk_invoice"]);
            });

            it("should serialize request with all three record types", async () => {
                const allTypesRequest: RecordSearchRequest = {
                    query: "quarterly report",
                    recordTypes: ["sprk_matter", "sprk_project", "sprk_invoice"],
                };
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(allTypesRequest);

                const [, init] = mockFetch.mock.calls[0];
                const parsedBody = JSON.parse(init?.body as string);
                expect(parsedBody.recordTypes).toEqual([
                    "sprk_matter",
                    "sprk_project",
                    "sprk_invoice",
                ]);
            });

            it("should serialize request without optional filters", async () => {
                const noFiltersRequest: RecordSearchRequest = {
                    query: "test query",
                    recordTypes: ["sprk_matter"],
                };
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(noFiltersRequest);

                const [, init] = mockFetch.mock.calls[0];
                const parsedBody = JSON.parse(init?.body as string);
                expect(parsedBody.filters).toBeUndefined();
                expect(parsedBody.options).toBeUndefined();
            });

            it("should serialize request with referenceNumbers filter", async () => {
                const refNumRequest: RecordSearchRequest = {
                    query: "matter 12345",
                    recordTypes: ["sprk_matter"],
                    filters: {
                        referenceNumbers: ["MAT-12345", "MAT-12346"],
                    },
                };
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(refNumRequest);

                const [, init] = mockFetch.mock.calls[0];
                const parsedBody = JSON.parse(init?.body as string);
                expect(parsedBody.filters.referenceNumbers).toEqual(["MAT-12345", "MAT-12346"]);
            });

            it("should serialize request with all filter types", async () => {
                const fullFilterRequest: RecordSearchRequest = {
                    query: "complex search",
                    recordTypes: ["sprk_matter", "sprk_project"],
                    filters: {
                        organizations: ["Org A", "Org B"],
                        people: ["Person 1", "Person 2"],
                        referenceNumbers: ["REF-001"],
                    },
                    options: {
                        limit: 10,
                        offset: 5,
                        hybridMode: "vectorOnly",
                    },
                };
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(fullFilterRequest);

                const [, init] = mockFetch.mock.calls[0];
                const parsedBody = JSON.parse(init?.body as string);
                expect(parsedBody.filters.organizations).toEqual(["Org A", "Org B"]);
                expect(parsedBody.filters.people).toEqual(["Person 1", "Person 2"]);
                expect(parsedBody.filters.referenceNumbers).toEqual(["REF-001"]);
                expect(parsedBody.options.limit).toBe(10);
                expect(parsedBody.options.offset).toBe(5);
                expect(parsedBody.options.hybridMode).toBe("vectorOnly");
            });
        });

        // --- Success responses ---

        describe("success responses", () => {
            it("should return parsed RecordSearchResponse on 200", async () => {
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                const result = await search(sampleRequest);

                expect(result).toEqual(sampleResponse);
            });

            it("should return response with empty results array", async () => {
                const emptyResponse: RecordSearchResponse = {
                    results: [],
                    metadata: {
                        totalCount: 0,
                        searchTime: 8,
                        hybridMode: "rrf",
                    },
                };
                mockFetch.mockResolvedValue(createSuccessResponse(emptyResponse));

                const result = await search(sampleRequest);

                expect(result.results).toEqual([]);
                expect(result.metadata.totalCount).toBe(0);
            });

            it("should return response with multiple results of different record types", async () => {
                const multiResponse: RecordSearchResponse = {
                    results: [
                        {
                            recordId: "rec-1",
                            recordType: "sprk_matter",
                            recordName: "Matter A",
                            confidenceScore: 0.95,
                        },
                        {
                            recordId: "rec-2",
                            recordType: "sprk_project",
                            recordName: "Project B",
                            confidenceScore: 0.88,
                        },
                        {
                            recordId: "rec-3",
                            recordType: "sprk_invoice",
                            recordName: "Invoice C",
                            confidenceScore: 0.71,
                        },
                    ],
                    metadata: {
                        totalCount: 100,
                        searchTime: 250,
                        hybridMode: "rrf",
                    },
                };
                mockFetch.mockResolvedValue(createSuccessResponse(multiResponse));

                const result = await search(sampleRequest);

                expect(result.results).toHaveLength(3);
                expect(result.results[0].recordType).toBe("sprk_matter");
                expect(result.results[1].recordType).toBe("sprk_project");
                expect(result.results[2].recordType).toBe("sprk_invoice");
                expect(result.metadata.totalCount).toBe(100);
            });

            it("should return response with all optional fields populated", async () => {
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                const result = await search(sampleRequest);

                const firstResult = result.results[0];
                expect(firstResult.recordDescription).toBeDefined();
                expect(firstResult.matchReasons).toBeDefined();
                expect(firstResult.matchReasons).toHaveLength(2);
                expect(firstResult.organizations).toContain("Acme Corp");
                expect(firstResult.people).toContain("John Smith");
                expect(firstResult.keywords).toContain("merger");
                expect(firstResult.createdAt).toBeDefined();
                expect(firstResult.modifiedAt).toBeDefined();
            });

            it("should return response with vectorOnly hybrid mode", async () => {
                const vectorResponse: RecordSearchResponse = {
                    results: [],
                    metadata: {
                        totalCount: 0,
                        searchTime: 15,
                        hybridMode: "vectorOnly",
                    },
                };
                mockFetch.mockResolvedValue(createSuccessResponse(vectorResponse));

                const result = await search(sampleRequest);

                expect(result.metadata.hybridMode).toBe("vectorOnly");
            });
        });

        // --- Error responses ---

        describe("error responses", () => {
            it("should throw ApiError with status 400 on validation error", async () => {
                const problemDetails = {
                    title: "Validation Error",
                    detail: "recordTypes must contain at least one value.",
                    errors: { recordTypes: ["At least one record type is required."] },
                };
                mockFetch.mockResolvedValue(createErrorResponse(400, problemDetails, "Bad Request"));

                let thrownError: ApiError | undefined;
                try {
                    await search(sampleRequest);
                } catch (e) {
                    thrownError = e as ApiError;
                }

                expect(thrownError).toBeDefined();
                expect(thrownError!.status).toBe(400);
                expect(thrownError!.title).toBe("Validation Error");
                expect(thrownError!.detail).toBe("recordTypes must contain at least one value.");
                expect(thrownError!.errors).toEqual({
                    recordTypes: ["At least one record type is required."],
                });
            });

            it("should throw ApiError with status 401 on unauthorized", async () => {
                mockFetch.mockResolvedValue(
                    createErrorResponse(401, { title: "Unauthorized" }, "Unauthorized"),
                );

                await expect(search(sampleRequest)).rejects.toMatchObject({
                    status: 401,
                    title: "Unauthorized",
                });
            });

            it("should throw ApiError with status 403 on forbidden", async () => {
                mockFetch.mockResolvedValue(
                    createErrorResponse(
                        403,
                        { title: "Forbidden", detail: "Insufficient permissions for record search." },
                        "Forbidden",
                    ),
                );

                await expect(search(sampleRequest)).rejects.toMatchObject({
                    status: 403,
                    title: "Forbidden",
                    detail: "Insufficient permissions for record search.",
                });
            });

            it("should throw ApiError with status 429 on rate limit", async () => {
                mockFetch.mockResolvedValue(
                    createErrorResponse(
                        429,
                        { title: "Too Many Requests", detail: "Rate limit exceeded." },
                        "Too Many Requests",
                    ),
                );

                await expect(search(sampleRequest)).rejects.toMatchObject({
                    status: 429,
                    title: "Too Many Requests",
                });
            });

            it("should throw ApiError with status 500 on server error", async () => {
                mockFetch.mockResolvedValue(
                    createErrorResponse(
                        500,
                        { title: "Internal Server Error", detail: "Database connection timeout." },
                        "Internal Server Error",
                    ),
                );

                await expect(search(sampleRequest)).rejects.toMatchObject({
                    status: 500,
                    title: "Internal Server Error",
                    detail: "Database connection timeout.",
                });
            });

            it("should throw ApiError with statusText when body is not JSON", async () => {
                mockFetch.mockResolvedValue(
                    createNetworkErrorResponse(502, "Bad Gateway"),
                );

                await expect(search(sampleRequest)).rejects.toMatchObject({
                    status: 502,
                    title: "Bad Gateway",
                });
            });

            it("should throw ApiError with status 503 on service unavailable", async () => {
                mockFetch.mockResolvedValue(
                    createNetworkErrorResponse(503, "Service Unavailable"),
                );

                await expect(search(sampleRequest)).rejects.toMatchObject({
                    status: 503,
                    title: "Service Unavailable",
                });
            });
        });

        // --- Network and auth failures ---

        describe("network and auth failures", () => {
            it("should throw on network failure (fetch rejects)", async () => {
                mockFetch.mockRejectedValue(new TypeError("Failed to fetch"));

                await expect(search(sampleRequest)).rejects.toThrow("Failed to fetch");
            });

            it("should throw on connection refused", async () => {
                mockFetch.mockRejectedValue(new TypeError("ERR_CONNECTION_REFUSED"));

                await expect(search(sampleRequest)).rejects.toThrow("ERR_CONNECTION_REFUSED");
            });

            it("should throw if MSAL token acquisition fails", async () => {
                mockGetAuthHeader.mockRejectedValue(
                    new Error("MSAL not initialized. Call initialize() first."),
                );

                await expect(search(sampleRequest)).rejects.toThrow(
                    "MSAL not initialized. Call initialize() first.",
                );
            });

            it("should throw if MSAL popup is cancelled", async () => {
                mockGetAuthHeader.mockRejectedValue(
                    new Error("Authentication cancelled by user"),
                );

                await expect(search(sampleRequest)).rejects.toThrow(
                    "Authentication cancelled by user",
                );
            });

            it("should not call fetch if auth header acquisition fails", async () => {
                mockGetAuthHeader.mockRejectedValue(new Error("Token error"));

                try {
                    await search(sampleRequest);
                } catch {
                    // expected
                }

                expect(mockFetch).not.toHaveBeenCalled();
            });

            it("should throw on request timeout (AbortError)", async () => {
                const abortError = new DOMException("The operation was aborted", "AbortError");
                mockFetch.mockRejectedValue(abortError);

                await expect(search(sampleRequest)).rejects.toThrow("The operation was aborted");
            });
        });

        // --- Integration-style: end-to-end request/response flow ---

        describe("end-to-end flow", () => {
            it("should acquire token, build headers, call fetch, and return parsed response", async () => {
                const token = "Bearer end-to-end-record-token";
                mockGetAuthHeader.mockResolvedValue(token);
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                const result = await search(sampleRequest);

                // Token was acquired
                expect(mockGetAuthHeader).toHaveBeenCalledTimes(1);

                // Fetch was called with correct arguments
                expect(mockFetch).toHaveBeenCalledTimes(1);
                const [url, init] = mockFetch.mock.calls[0];
                expect(url).toBe(EXPECTED_ENDPOINT);
                expect(init?.method).toBe("POST");
                expect((init?.headers as Record<string, string>)["Authorization"]).toBe(token);
                expect(init?.body).toBe(JSON.stringify(sampleRequest));

                // Response was returned
                expect(result).toEqual(sampleResponse);
            });

            it("should use different endpoints from SemanticSearchApiService", () => {
                // Verify the record search endpoint is distinct
                expect(EXPECTED_ENDPOINT).toBe(
                    "https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/search/records",
                );
                expect(EXPECTED_ENDPOINT).not.toBe(
                    "https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/search",
                );
            });
        });
    });
});
