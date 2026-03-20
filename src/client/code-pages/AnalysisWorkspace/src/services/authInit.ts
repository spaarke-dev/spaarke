/**
 * Auth initialization bridge for AnalysisWorkspace Code Page.
 *
 * Thin wrapper around @spaarke/auth that provides the same public API
 * previously exported by authService.ts. All token acquisition, caching,
 * retry, and MSAL logic is now handled by the shared library.
 *
 * Runtime config resolution:
 *   resolveRuntimeConfig() queries Dataverse Environment Variables for
 *   BFF URL, OAuth scope, and MSAL client ID at runtime -- replacing
 *   build-time .env.production values.
 *
 * Migration mapping:
 *   authService.initializeAuth()  -> initAuth() (from @spaarke/auth)
 *   authService.getAccessToken()  -> getAuthProvider().getAccessToken()
 *   authService.clearTokenCache() -> getAuthProvider().clearCache()
 *   authService.isXrmAvailable()  -> (removed -- Xrm check handled internally)
 *   authService.getClientUrl()    -> (unchanged -- still local utility)
 *
 * @see @spaarke/auth for the shared authentication library
 * @see ADR-008 - Endpoint filters for auth
 */

import { initAuth, getAuthProvider, authenticatedFetch, AuthError, resolveRuntimeConfig } from '@spaarke/auth';
import type { SpaarkeAuthProvider, IRuntimeConfig } from '@spaarke/auth';

// Re-export core symbols for consumers
export { initAuth, getAuthProvider, authenticatedFetch, AuthError, resolveRuntimeConfig };
export type { SpaarkeAuthProvider, IRuntimeConfig };

/** Cached runtime config after first resolution. */
let _runtimeConfig: IRuntimeConfig | null = null;

/**
 * Get the resolved runtime config. Must call initializeAuth() first.
 * @throws Error if runtime config has not been resolved yet.
 */
export function getRuntimeConfig(): IRuntimeConfig {
  if (!_runtimeConfig) {
    throw new Error(
      '[AnalysisWorkspace:Auth] Runtime config not resolved. Call initializeAuth() first.'
    );
  }
  return _runtimeConfig;
}

/**
 * Initialize authentication for the AnalysisWorkspace.
 *
 * 1. Resolves runtime config from Dataverse Environment Variables
 *    (BFF URL, OAuth scope, MSAL client ID).
 * 2. Passes resolved config to initAuth() from @spaarke/auth.
 *
 * AnalysisWorkspace does not require Xrm (supports MSAL fallback)
 * and does not use proactive refresh (AuthContext handles its own interval).
 *
 * @returns The initial access token string
 */
export async function initializeAuth(): Promise<string> {
  // Resolve BFF URL, OAuth scope, and MSAL client ID from Dataverse env vars
  const config = await resolveRuntimeConfig();
  _runtimeConfig = config;

  const provider = await initAuth({
    clientId: config.msalClientId,
    bffApiScope: config.bffOAuthScope,
    bffBaseUrl: config.bffBaseUrl,
  });
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
