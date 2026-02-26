/**
 * Unit tests for apiBase.ts — shared API base utilities.
 *
 * Tests:
 * - BFF_API_BASE_URL constant
 * - buildAuthHeaders() — constructs Authorization + Content-Type headers
 * - handleApiResponse<T>() — parses JSON on 2xx, throws ApiError on failure
 *
 * @see apiBase.ts
 */

import type { ApiError } from "../../types";

// ---------------------------------------------------------------------------
// Mocks — must be declared before imports that reference them
// ---------------------------------------------------------------------------

const mockGetAuthHeader = jest.fn<Promise<string>, []>();

jest.mock("../../services/auth/MsalAuthProvider", () => ({
    msalAuthProvider: {
        getAuthHeader: mockGetAuthHeader,
    },
}));

import { BFF_API_BASE_URL, buildAuthHeaders, handleApiResponse } from "../../services/apiBase";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Create a minimal mock Response object for testing handleApiResponse. */
function createMockResponse(
    status: number,
    body: unknown,
    ok?: boolean,
    statusText?: string,
): Response {
    const isOk = ok ?? (status >= 200 && status < 300);
    return {
        ok: isOk,
        status,
        statusText: statusText ?? (isOk ? "OK" : "Error"),
        json: jest.fn().mockResolvedValue(body),
    } as unknown as Response;
}

/** Create a mock Response whose json() rejects (e.g., non-JSON body). */
function createMockResponseWithJsonError(
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

describe("apiBase", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // -----------------------------------------------------------------------
    // BFF_API_BASE_URL
    // -----------------------------------------------------------------------

    describe("BFF_API_BASE_URL", () => {
        it("should be the dev BFF API base URL", () => {
            expect(BFF_API_BASE_URL).toBe("https://spe-api-dev-67e2xz.azurewebsites.net");
        });

        it("should be a string", () => {
            expect(typeof BFF_API_BASE_URL).toBe("string");
        });

        it("should start with https://", () => {
            expect(BFF_API_BASE_URL).toMatch(/^https:\/\//);
        });

        it("should not have a trailing slash", () => {
            expect(BFF_API_BASE_URL).not.toMatch(/\/$/);
        });
    });

    // -----------------------------------------------------------------------
    // buildAuthHeaders()
    // -----------------------------------------------------------------------

    describe("buildAuthHeaders()", () => {
        it("should return Authorization and Content-Type headers", async () => {
            mockGetAuthHeader.mockResolvedValue("Bearer test-token-abc123");

            const headers = await buildAuthHeaders();

            expect(headers).toEqual({
                Authorization: "Bearer test-token-abc123",
                "Content-Type": "application/json",
            });
        });

        it("should call msalAuthProvider.getAuthHeader() exactly once", async () => {
            mockGetAuthHeader.mockResolvedValue("Bearer xyz");

            await buildAuthHeaders();

            expect(mockGetAuthHeader).toHaveBeenCalledTimes(1);
        });

        it("should propagate the exact token string from getAuthHeader()", async () => {
            const token = "Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.test-payload.signature";
            mockGetAuthHeader.mockResolvedValue(token);

            const headers = await buildAuthHeaders();

            expect(headers["Authorization"]).toBe(token);
        });

        it("should always set Content-Type to application/json", async () => {
            mockGetAuthHeader.mockResolvedValue("Bearer any-token");

            const headers = await buildAuthHeaders();

            expect(headers["Content-Type"]).toBe("application/json");
        });

        it("should throw if getAuthHeader() throws (MSAL not initialized)", async () => {
            mockGetAuthHeader.mockRejectedValue(
                new Error("MSAL not initialized. Call initialize() first."),
            );

            await expect(buildAuthHeaders()).rejects.toThrow(
                "MSAL not initialized. Call initialize() first.",
            );
        });

        it("should throw if getAuthHeader() throws (token acquisition failure)", async () => {
            mockGetAuthHeader.mockRejectedValue(
                new Error("Failed to acquire token: interaction_required"),
            );

            await expect(buildAuthHeaders()).rejects.toThrow(
                "Failed to acquire token: interaction_required",
            );
        });
    });

    // -----------------------------------------------------------------------
    // handleApiResponse()
    // -----------------------------------------------------------------------

    describe("handleApiResponse()", () => {
        // --- Success cases (2xx) ---

        describe("success responses (2xx)", () => {
            it("should parse and return JSON body on 200 OK", async () => {
                const body = { results: [{ id: "1" }], metadata: { totalResults: 1 } };
                const response = createMockResponse(200, body);

                const result = await handleApiResponse<typeof body>(response);

                expect(result).toEqual(body);
            });

            it("should parse and return JSON body on 201 Created", async () => {
                const body = { id: "new-resource-id" };
                const response = createMockResponse(201, body);

                const result = await handleApiResponse<typeof body>(response);

                expect(result).toEqual(body);
            });

            it("should parse and return empty object on 200", async () => {
                const response = createMockResponse(200, {});

                const result = await handleApiResponse<Record<string, unknown>>(response);

                expect(result).toEqual({});
            });

            it("should parse and return array body on 200", async () => {
                const body = [{ name: "a" }, { name: "b" }];
                const response = createMockResponse(200, body);

                const result = await handleApiResponse<typeof body>(response);

                expect(result).toEqual(body);
            });

            it("should call response.json() for success responses", async () => {
                const response = createMockResponse(200, { data: true });

                await handleApiResponse(response);

                expect(response.json).toHaveBeenCalledTimes(1);
            });
        });

        // --- Error cases with ProblemDetails body ---

        describe("error responses with ProblemDetails body", () => {
            it("should throw ApiError with all ProblemDetails fields on 400", async () => {
                const problemDetails = {
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    title: "Validation Error",
                    detail: "The query field is required.",
                    errors: { query: ["The query field is required."] },
                };
                const response = createMockResponse(400, problemDetails);

                let thrownError: ApiError | undefined;
                try {
                    await handleApiResponse(response);
                } catch (e) {
                    thrownError = e as ApiError;
                }

                expect(thrownError).toBeDefined();
                expect(thrownError!.status).toBe(400);
                expect(thrownError!.title).toBe("Validation Error");
                expect(thrownError!.detail).toBe("The query field is required.");
                expect(thrownError!.type).toBe("https://tools.ietf.org/html/rfc7231#section-6.5.1");
                expect(thrownError!.errors).toEqual({ query: ["The query field is required."] });
            });

            it("should throw ApiError with status 401 on unauthorized", async () => {
                const problemDetails = {
                    title: "Unauthorized",
                    detail: "Bearer token is missing or invalid.",
                };
                const response = createMockResponse(401, problemDetails, false, "Unauthorized");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 401,
                    title: "Unauthorized",
                    detail: "Bearer token is missing or invalid.",
                });
            });

            it("should throw ApiError with status 403 on forbidden", async () => {
                const problemDetails = {
                    title: "Forbidden",
                    detail: "User does not have access to this resource.",
                };
                const response = createMockResponse(403, problemDetails, false, "Forbidden");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 403,
                    title: "Forbidden",
                    detail: "User does not have access to this resource.",
                });
            });

            it("should throw ApiError with status 429 on rate limit", async () => {
                const problemDetails = {
                    title: "Too Many Requests",
                    detail: "Rate limit exceeded. Try again in 30 seconds.",
                };
                const response = createMockResponse(429, problemDetails, false, "Too Many Requests");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 429,
                    title: "Too Many Requests",
                });
            });

            it("should throw ApiError with status 500 on server error", async () => {
                const problemDetails = {
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                };
                const response = createMockResponse(500, problemDetails, false, "Internal Server Error");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                });
            });

            it("should fall back to statusText when title is missing in body", async () => {
                const problemDetails = {
                    detail: "Something went wrong.",
                    // title is missing
                };
                const response = createMockResponse(502, problemDetails, false, "Bad Gateway");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 502,
                    title: "Bad Gateway",
                    detail: "Something went wrong.",
                });
            });

            it("should include validation errors map when present", async () => {
                const problemDetails = {
                    title: "Validation Error",
                    errors: {
                        query: ["Max length is 1000 characters."],
                        scope: ["Invalid scope value."],
                    },
                };
                const response = createMockResponse(400, problemDetails);

                let thrownError: ApiError | undefined;
                try {
                    await handleApiResponse(response);
                } catch (e) {
                    thrownError = e as ApiError;
                }

                expect(thrownError!.errors).toEqual({
                    query: ["Max length is 1000 characters."],
                    scope: ["Invalid scope value."],
                });
            });

            it("should handle ProblemDetails with no detail field", async () => {
                const problemDetails = {
                    title: "Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                };
                const response = createMockResponse(404, problemDetails, false, "Not Found");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 404,
                    title: "Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    detail: undefined,
                });
            });
        });

        // --- Error cases without JSON body (or unparseable body) ---

        describe("error responses without parseable JSON body", () => {
            it("should throw ApiError with statusText when body is not JSON", async () => {
                const response = createMockResponseWithJsonError(503, "Service Unavailable");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 503,
                    title: "Service Unavailable",
                });
            });

            it("should not include detail or type when body parsing fails", async () => {
                const response = createMockResponseWithJsonError(502, "Bad Gateway");

                let thrownError: ApiError | undefined;
                try {
                    await handleApiResponse(response);
                } catch (e) {
                    thrownError = e as ApiError;
                }

                expect(thrownError).toBeDefined();
                expect(thrownError!.status).toBe(502);
                expect(thrownError!.title).toBe("Bad Gateway");
                expect(thrownError!.detail).toBeUndefined();
                expect(thrownError!.type).toBeUndefined();
                expect(thrownError!.errors).toBeUndefined();
            });

            it("should throw ApiError with status 500 when body JSON parse fails", async () => {
                const response = createMockResponseWithJsonError(500, "Internal Server Error");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 500,
                    title: "Internal Server Error",
                });
            });
        });

        // --- Edge cases ---

        describe("edge cases", () => {
            it("should use response.ok to determine success (not just status range)", async () => {
                // Response with status 200 but ok=false (unusual but possible)
                const response = createMockResponse(200, { data: "nope" }, false, "Weird");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 200,
                    title: "Weird",
                });
            });

            it("should throw (not return) on error — error is not a return value", async () => {
                const response = createMockResponse(400, { title: "Bad Request" });

                const promise = handleApiResponse(response);

                await expect(promise).rejects.toBeDefined();
            });

            it("should handle empty ProblemDetails body gracefully", async () => {
                const response = createMockResponse(422, {}, false, "Unprocessable Entity");

                await expect(handleApiResponse(response)).rejects.toMatchObject({
                    status: 422,
                    title: "Unprocessable Entity", // falls back to statusText since body.title is falsy
                });
            });
        });
    });
});
