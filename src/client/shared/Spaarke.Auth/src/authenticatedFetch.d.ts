/**
 * Performs a fetch request with BFF Bearer token authentication.
 *
 * Features:
 *   - Auto-attaches Bearer header from SpaarkeAuthProvider
 *   - 401 retry with exponential backoff (up to 3 attempts)
 *   - RFC 7807 ProblemDetails error parsing
 *   - Returns Response on success, throws ApiError or AuthError on failure
 *
 * @param url Full or relative URL to fetch
 * @param init Standard fetch RequestInit options
 * @returns Fetch Response (status 2xx-3xx)
 * @throws ApiError for non-2xx responses with ProblemDetails
 * @throws AuthError when token acquisition fails after retries
 */
export declare function authenticatedFetch(url: string, init?: RequestInit): Promise<Response>;
//# sourceMappingURL=authenticatedFetch.d.ts.map