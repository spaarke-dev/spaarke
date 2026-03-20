/**
 * Auth initialization for SemanticSearch Code Page.
 *
 * Uses @spaarke/auth shared library with runtime configuration from
 * Dataverse Environment Variables. No build-time .env.production values
 * are used for BFF URL or MSAL Client ID.
 *
 * Bootstrap sequence:
 *   1. resolveRuntimeConfig() queries Dataverse for sprk_BffApiBaseUrl,
 *      sprk_BffApiAppId, and sprk_MsalClientId (uses session cookie, pre-auth)
 *   2. initializeRuntimeConfig() caches the BFF base URL for apiBase.ts consumers
 *   3. initAuth() initializes MSAL with the resolved clientId and bffApiScope
 *
 * @spaarke/auth defaults (still applied):
 *   - authority: https://login.microsoftonline.com/organizations (multi-tenant)
 *   - redirectUri: window.location.origin (auto-detected)
 */

import { initAuth, getAuthProvider, authenticatedFetch, resolveRuntimeConfig } from '@spaarke/auth';
import { initializeRuntimeConfig } from './apiBase';

/**
 * Initialize authentication. Call once at app startup before rendering.
 *
 * 1. Resolves BFF URL, MSAL client ID, and BFF OAuth scope from Dataverse
 *    environment variables at runtime (replaces build-time .env.production).
 * 2. Caches the BFF base URL for API service clients.
 * 3. Initializes MSAL with the resolved configuration.
 */
export async function initializeAuth(): Promise<void> {
  // 1. Resolve runtime config from Dataverse environment variables
  const runtimeConfig = await resolveRuntimeConfig();

  // 2. Cache BFF base URL for apiBase.ts consumers (getBffBaseUrl)
  await initializeRuntimeConfig();

  // 3. Initialize MSAL with resolved values
  await initAuth({
    clientId: runtimeConfig.msalClientId,
    bffApiScope: runtimeConfig.bffOAuthScope,
    bffBaseUrl: runtimeConfig.bffBaseUrl,
  });
}

/**
 * Get an Authorization header value ("Bearer {token}") for BFF API calls.
 * Drop-in replacement for msalAuthProvider.getAuthHeader().
 */
export async function getAuthHeader(): Promise<string> {
  const token = await getAuthProvider().getAccessToken();
  return `Bearer ${token}`;
}

/**
 * Get a raw access token for BFF API calls.
 * Drop-in replacement for msalAuthProvider.getToken().
 */
export async function getToken(): Promise<string> {
  return getAuthProvider().getAccessToken();
}

/**
 * Clear the auth token cache.
 * Drop-in replacement for msalAuthProvider.clearCache().
 */
export function clearAuthCache(): void {
  getAuthProvider().clearCache();
}

/**
 * Check whether the user is currently authenticated.
 * Drop-in replacement for msalAuthProvider.isAuthenticated().
 */
export function isAuthenticated(): boolean {
  return getAuthProvider().isAuthenticated();
}

// Re-export authenticatedFetch for convenience
export { authenticatedFetch, getAuthProvider };
