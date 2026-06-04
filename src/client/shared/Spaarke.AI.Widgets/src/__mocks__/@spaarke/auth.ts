/**
 * @spaarke/auth stub for @spaarke/ai-widgets Jest tests.
 *
 * AiSessionProvider imports buildBffApiUrl, useAuth, and AuthenticatedFetchFn
 * from @spaarke/auth. The real implementation requires a browser MSAL context
 * that is unavailable in jsdom. This stub provides the minimal API surface
 * needed for module resolution and a default useAuth() that satisfies the
 * function-based contract introduced in Spaarke Auth v2 (§H-4).
 *
 * Tests that exercise specific auth behaviour override these with
 * jest.mock('@spaarke/auth', () => ({ ... })) — but the defaults below are
 * enough for the AiSessionProvider provider/context tests because the BFF
 * fetch is short-circuited by passing entityContext=null.
 */

export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

export function buildBffApiUrl(base: string, path: string): string {
  return `${base}${path}`;
}

export const authenticatedFetch: jest.MockedFunction<AuthenticatedFetchFn> = jest
  .fn<Promise<Response>, Parameters<AuthenticatedFetchFn>>()
  .mockResolvedValue({
    ok: false,
    status: 503,
    json: async () => ({}),
  } as Response);

/**
 * Default useAuth() for tests — provides a stable, authenticated session.
 * Individual tests may override via jest.mock('@spaarke/auth', () => ({ ... }))
 * to simulate logged-out / token-expired / etc states.
 */
export const useAuth = jest.fn(() => ({
  isAuthenticated: true,
  getAccessToken: jest.fn<Promise<string>, []>().mockResolvedValue('test-token'),
  authenticatedFetch,
  tenantId: 'test-tenant-guid',
  logout: jest.fn<Promise<void>, []>().mockResolvedValue(undefined),
}));
