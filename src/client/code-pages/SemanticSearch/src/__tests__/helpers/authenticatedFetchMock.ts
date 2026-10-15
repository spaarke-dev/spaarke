/**
 * Test double for `@spaarke/auth`'s `authenticatedFetch`.
 *
 * The real helper attaches the Bearer token, delegates to `fetch`, and THROWS
 * `ApiError(message, status, problemDetails)` for every non-2xx response (and `AuthError` once 401 retries are
 * spent). It never returns a non-OK Response, so a suite that fakes the network at `global.fetch` level
 * must reproduce that throw rather than let the service see `{ ok: false }`.
 *
 * Usage (inside a `jest.mock('@spaarke/auth', ...)` factory):
 *   const { createAuthenticatedFetchMock } = jest.requireActual('../helpers/authenticatedFetchMock');
 *   return { ...createAuthenticatedFetchMock(() => mockGetAuthHeader()), resolveRuntimeConfig: ... };
 */
export function createAuthenticatedFetchMock(getAuthHeader: () => Promise<string>) {
  const { isApiError, problemOf, isAuthFailure, ApiError, AuthError } = jest.requireActual('@spaarke/auth');
  return {
    authenticatedFetch: async (url: string, init?: RequestInit) => {
      const authorization = await getAuthHeader();
      const response: Response = await (global.fetch as typeof fetch)(url, {
        ...init,
        headers: { ...(init?.headers as Record<string, string>), Authorization: authorization },
      });
      if (!response.ok) {
        // Mirrors authenticatedFetch.ts: exhausted 401 retries throw AuthError (no status); anything else
        // throws ApiError(detail ?? title ?? 'HTTP n', status, problemDetails) where problemDetails is set
        // only when the JSON body carries `title` or `status`.
        if (response.status === 401) {
          throw new AuthError('Authentication failed after all retry attempts', 'auth_exhausted');
        }
        let problem = null;
        try {
          const body = await response.json();
          if (body && typeof body === 'object' && ('title' in body || 'status' in body)) {
            problem = body;
          }
        } catch {
          /* not ProblemDetails */
        }
        throw new ApiError(problem?.detail ?? problem?.title ?? `HTTP ${response.status}`, response.status, problem);
      }
      return response;
    },
    isApiError,
    problemOf,
    isAuthFailure,
  };
}
