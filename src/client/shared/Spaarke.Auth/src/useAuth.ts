import type { AuthenticatedFetchFn } from './types';
import { getAuthProvider } from './initAuth';
import { authenticatedFetch } from './authenticatedFetch';

/**
 * Return shape of `useAuth()`.
 *
 * Function-based contract per AUDIT-FINDINGS-AUTH-SYSTEM §4.1: NO field is a
 * token string. `getAccessToken` is the narrow escape hatch for cases where
 * `authenticatedFetch` cannot wrap the network call (notably SSE `ReadableStream`
 * lifecycle).
 */
export interface UseAuthResult {
  /** Whether a fresh cached token is currently available (sync check). */
  isAuthenticated: boolean;
  /** Acquire a fresh token. Always goes through the provider's cache + JWT exp validation. */
  getAccessToken: () => Promise<string>;
  /** Authenticated fetch — automatically attaches Bearer header, retries 401 with backoff. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Azure AD tenant ID from the cached JWT `tid` claim. Empty string if no token cached. */
  tenantId: string;
  /** Log the user out. Full implementation lands in task 014 (server invalidation + BroadcastChannel). */
  logout: () => Promise<void>;
}

/**
 * React hook returning the primary public auth surface.
 *
 * Intentionally NOT reactive: no `useState` snapshot of the token (that snapshot
 * pattern is the root cause of the 401-after-token-refresh class of bugs this
 * project eliminates). Consumers re-call `getAccessToken()` / `authenticatedFetch()`
 * on every request and the provider's in-memory cache returns the fresh token.
 *
 * `isAuthenticated` is a sync getter; consumers re-read on each render. Task 015
 * adds a `BroadcastChannel` listener that will trigger a re-render on logout
 * broadcast, but the underlying value is still read fresh from the provider.
 *
 * Throws AuthError if `initAuth()` has not been called.
 */
export function useAuth(): UseAuthResult {
  const provider = getAuthProvider();
  return {
    isAuthenticated: provider.isAuthenticated(),
    getAccessToken: () => provider.getAccessToken(),
    authenticatedFetch,
    tenantId: provider.getCachedTenantId(),
    logout: async () => {
      console.warn(
        '[useAuth] logout() is a stub pending task 014. Clearing local caches; ' +
          'server-side OBO invalidation + BroadcastChannel notification not yet implemented.'
      );
      provider.clearAllCaches();
    },
  };
}
