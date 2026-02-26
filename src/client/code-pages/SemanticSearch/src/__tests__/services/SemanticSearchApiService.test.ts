/**
 * Unit tests for SemanticSearchApiService.ts — document search API client.
 *
 * Tests:
 * - search() sends POST to correct endpoint with auth headers and JSON body
 * - search() returns parsed DocumentSearchResponse on success
 * - search() throws ApiError on HTTP error responses
 * - search() propagates network failures and auth failures
 *
 * @see SemanticSearchApiService.ts
 */

import type {
    DocumentSearchRequest,
    DocumentSearchResponse,
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

import { search } from "../../services/SemanticSearchApiService";
import { BFF_API_BASE_URL } from "../../services/apiBase";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const EXPECTED_ENDPOINT = `${BFF_API_BASE_URL}/api/ai/search`;

const sampleRequest: DocumentSearchRequest = {
    query: "financial agreements",
    scope: "all",
    filters: {
        documentTypes: ["Contract"],
        fileTypes: ["pdf"],
    },
    options: {
        limit: 20,
        offset: 0,
        includeHighlights: true,
        hybridMode: "rrf",
    },
};

const sampleResponse: DocumentSearchResponse = {
    results: [
        {
            documentId: "doc-001",
            name: "Financial Agreement 2025.pdf",
            documentType: "Contract",
            fileType: "pdf",
            combinedScore: 0.92,
            similarity: 0.88,
            highlights: ["<em>financial</em> agreement between parties"],
            parentEntityType: "matter",
            parentEntityName: "Johnson v. Smith",
            createdAt: "2025-06-15T10:30:00Z",
        },
    ],
    metadata: {
        totalResults: 1,
        returnedResults: 1,
        searchDurationMs: 245,
        embeddingDurationMs: 38,
        executedMode: "rrf",
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

describe("SemanticSearchApiService", () => {
    beforeEach(() => {
        jest.clearAllMocks();
        mockGetAuthHeader.mockResolvedValue("Bearer test-token");
    });

    describe("search()", () => {
        // --- Request construction ---

        describe("request construction", () => {
            it("should POST to /api/ai/search endpoint", async () => {
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
                mockGetAuthHeader.mockResolvedValue("Bearer my-jwt-token");
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(sampleRequest);

                const [, init] = mockFetch.mock.calls[0];
                const headers = init?.headers as Record<string, string>;
                expect(headers["Authorization"]).toBe("Bearer my-jwt-token");
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

            it("should serialize minimal request with only required fields", async () => {
                const minimalRequest: DocumentSearchRequest = {
                    scope: "all",
                };
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(minimalRequest);

                const [, init] = mockFetch.mock.calls[0];
                expect(init?.body).toBe(JSON.stringify(minimalRequest));
            });

            it("should serialize request with entity scope", async () => {
                const entityRequest: DocumentSearchRequest = {
                    query: "contracts",
                    scope: "entity",
                    entityType: "matter",
                    entityId: "12345-abcde",
                };
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(entityRequest);

                const [, init] = mockFetch.mock.calls[0];
                const parsedBody = JSON.parse(init?.body as string);
                expect(parsedBody.scope).toBe("entity");
                expect(parsedBody.entityType).toBe("matter");
                expect(parsedBody.entityId).toBe("12345-abcde");
            });

            it("should serialize request with documentIds scope", async () => {
                const docIdsRequest: DocumentSearchRequest = {
                    scope: "documentIds",
                    documentIds: ["doc-1", "doc-2", "doc-3"],
                };
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                await search(docIdsRequest);

                const [, init] = mockFetch.mock.calls[0];
                const parsedBody = JSON.parse(init?.body as string);
                expect(parsedBody.scope).toBe("documentIds");
                expect(parsedBody.documentIds).toEqual(["doc-1", "doc-2", "doc-3"]);
            });
        });

        // --- Success responses ---

        describe("success responses", () => {
            it("should return parsed DocumentSearchResponse on 200", async () => {
                mockFetch.mockResolvedValue(createSuccessResponse(sampleResponse));

                const result = await search(sampleRequest);

                expect(result).toEqual(sampleResponse);
            });

            it("should return response with empty results array", async () => {
                const emptyResponse: DocumentSearchResponse = {
                    results: [],
                    metadata: {
                        totalResults: 0,
                        returnedResults: 0,
                        searchDurationMs: 12,
                        embeddingDurationMs: 5,
                    },
                };
                mockFetch.mockResolvedValue(createSuccessResponse(emptyResponse));

                const result = await search(sampleRequest);

                expect(result.results).toEqual([]);
                expect(result.metadata.totalResults).toBe(0);
            });

            it("should return response with multiple results", async () => {
                const multiResponse: DocumentSearchResponse = {
                    results: [
                        { documentId: "doc-1", name: "Doc A", combinedScore: 0.95 },
                        { documentId: "doc-2", name: "Doc B", combinedScore: 0.87 },
                        { documentId: "doc-3", name: "Doc C", combinedScore: 0.72 },
                    ],
                    metadata: {
                        totalResults: 50,
                        returnedResults: 3,
                        searchDurationMs: 320,
                        embeddingDurationMs: 40,
                    },
                };
                mockFetch.mockResolvedValue(createSuccessResponse(multiResponse));

                const result = await search(sampleRequest);

                expect(result.results).toHaveLength(3);
                expect(result.metadata.totalResults).toBe(50);
            });

            it("should return response with warnings in metadata", async () => {
                const responseWithWarnings: DocumentSearchResponse = {
                    results: [],
                    metadata: {
                        totalResults: 0,
                        returnedResults: 0,
                        searchDurationMs: 15,
                        embeddingDurationMs: 0,
                        warnings: [
                            { code: "EMBEDDING_FALLBACK", message: "Using cached embedding" },
                        ],
                    },
                };
                mockFetch.mockResolvedValue(createSuccessResponse(responseWithWarnings));

                const result = await search(sampleRequest);

                expect(result.metadata.warnings).toHaveLength(1);
                expect(result.metadata.warnings![0].code).toBe("EMBEDDING_FALLBACK");
            });
        });

        // --- Error responses ---

        describe("error responses", () => {
            it("should throw ApiError with status 400 on validation error", async () => {
                const problemDetails = {
                    title: "Validation Error",
                    detail: "Query exceeds maximum length.",
                    errors: { query: ["Max length is 1000 characters."] },
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
                expect(thrownError!.detail).toBe("Query exceeds maximum length.");
                expect(thrownError!.errors).toEqual({ query: ["Max length is 1000 characters."] });
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
                    createErrorResponse(403, { title: "Forbidden" }, "Forbidden"),
                );

                await expect(search(sampleRequest)).rejects.toMatchObject({
                    status: 403,
                    title: "Forbidden",
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
                        { title: "Internal Server Error", detail: "An unexpected error occurred." },
                        "Internal Server Error",
                    ),
                );

                await expect(search(sampleRequest)).rejects.toMatchObject({
                    status: 500,
                    title: "Internal Server Error",
                });
            });

            it("should throw ApiError with statusText when body is not JSON", async () => {
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

            it("should throw on DNS resolution failure", async () => {
                mockFetch.mockRejectedValue(new TypeError("getaddrinfo ENOTFOUND"));

                await expect(search(sampleRequest)).rejects.toThrow("getaddrinfo ENOTFOUND");
            });

            it("should throw if MSAL token acquisition fails", async () => {
                mockGetAuthHeader.mockRejectedValue(
                    new Error("MSAL not initialized. Call initialize() first."),
                );

                await expect(search(sampleRequest)).rejects.toThrow(
                    "MSAL not initialized. Call initialize() first.",
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
                const token = "Bearer end-to-end-token";
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
        });
    });
});
