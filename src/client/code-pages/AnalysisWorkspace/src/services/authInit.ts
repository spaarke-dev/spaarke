/**
 * Auth initialization bridge for AnalysisWorkspace Code Page.
 *
 * Thin wrapper around @spaarke/auth that provides the same public API
 * previously exported by authService.ts. All token acquisition, caching,
 * retry, and MSAL logic is now handled by the shared library.
 *
 * Migration mapping:
 *   authService.initializeAuth()  -> initAuth() (from @spaarke/auth)
 *   authService.getAccessToken()  -> getAuthProvider().getAccessToken()
 *   authService.clearTokenCache() -> getAuthProvider().clearCache()
 *   authService.isXrmAvailable()  -> (removed — Xrm check handled internally)
 *   authService.getClientUrl()    -> (unchanged — still local utility)
 *
 * @see @spaarke/auth for the shared authentication library
 * @see ADR-008 - Endpoint filters for auth
 */

import { initAuth, getAuthProvider, authenticatedFetch, AuthError } from '@spaarke/auth';
import type { SpaarkeAuthProvider } from '@spaarke/auth';

// Re-export core symbols for consumers
export { initAuth, getAuthProvider, authenticatedFetch, AuthError };
export type { SpaarkeAuthProvider };

/**
 * Initialize authentication for the AnalysisWorkspace.
 *
 * Calls initAuth() from @spaarke/auth with default options.
 * AnalysisWorkspace does not require Xrm (supports MSAL fallback)
 * and does not use proactive refresh (AuthContext handles its own interval).
 *
 * @returns The initial access token string
 */
export async function initializeAuth(): Promise<string> {
  const provider = await initAuth();
  return provider.getAccessToken();
}

/**
 * Get an access token for the BFF API.
 * Delegates to the shared auth provider.
 */
export async function getAccessToken(): Promise<string> {
  return getAuthProvider().getAccessToken();
}

/**
 * Clear the in-memory token cache.
 * Call on 401 responses to force re-acquisition.
 */
export function clearTokenCache(): void {
  getAuthProvider().clearCache();
}
