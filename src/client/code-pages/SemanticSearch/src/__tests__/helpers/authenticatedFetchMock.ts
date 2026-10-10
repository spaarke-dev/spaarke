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
  const { isApiError, problemOf, isAuthFailure, ApiError } = jest.requireActual('@spaarke/auth');
  return {
    authenticatedFetch: async (url: string, init?: RequestInit) => {
      const authorization = await getAuthHeader();
      const response: Response = await (global.fetch as typeof fetch)(url, {
        ...init,
        headers: { ...(init?.headers as Record<string, string>), Authorization: authorization },
      });
      if (!response.ok) {
        let problem = null;
        try {
          problem = await response.json();
        } catch {
          /* not ProblemDetails */
        }
        throw new ApiError(
          problem?.detail ?? problem?.title ?? (response.statusText || `HTTP ${response.status}`),
          response.status,
          problem
        );
      }
      return response;
    },
    isApiError,
    problemOf,
    isAuthFailure,
  };
}
