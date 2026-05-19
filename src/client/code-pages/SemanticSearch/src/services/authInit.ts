/**
 * Auth initialization for SemanticSearch Code Page.
 *
 * Uses @spaarke/auth shared library with runtime configuration from
 * Dataverse Environment Variables. No build-time .env.production values
 * are used for BFF URL or MSAL Client ID.
 *
 * Bootstrap sequence:
 *   1. resolveRuntimeConfig() queries Dataverse for sprk_BffApiBaseUrl,
 *      sprk_BffApiAppId, sprk_MsalClientId, and sprk_TenantId
 *      (uses session cookie, pre-auth)
 *   2. initializeRuntimeConfig() caches the BFF base URL for apiBase.ts consumers
 *   3. initAuth() initializes MSAL with the resolved clientId, tenantId, and bffApiScope
 *
 * Passing `tenantId` (rather than letting the library fall back to
 * /organizations) is the canonical SSO fix established by Phase 0/A: it lets
 * MSAL.ssoSilent pick the correct AAD session cookie and avoids the
 * "Pick an account" popup on first load (per memory
 * `project_auth_v2_baseline_msal_bug`).
 *
 * @spaarke/auth defaults (still applied when not overridden here):
 *   - redirectUri: window.location.origin (auto-detected)
 *   - cacheLocation: localStorage (cross-tab survival)
 */

import { initAuth, getAuthProvider, authenticatedFetch, resolveRuntimeConfig } from '@spaarke/auth';
import { initializeRuntimeConfig } from './apiBase';

/**
 * Initialize authentication. Call once at app startup before rendering.
 *
 * 1. Resolves BFF URL, MSAL client ID, BFF OAuth scope, and tenant ID from
 *    Dataverse environment variables at runtime.
 * 2. Caches the BFF base URL for API service clients.
 * 3. Initializes MSAL with the resolved configuration including tenantId so
 *    the authority is tenant-specific (avoids /organizations popup regression).
 */
export async function initializeAuth(): Promise<void> {
  // 1. Resolve runtime config from Dataverse environment variables
  const runtimeConfig = await resolveRuntimeConfig();

  // 2. Cache BFF base URL for apiBase.ts consumers (getBffBaseUrl)
  await initializeRuntimeConfig();

  // 3. Initialize MSAL with resolved values.
  // `tenantId` is passed so @spaarke/auth builds the tenant-specific authority
  // (https://login.microsoftonline.com/{tenantId}) instead of falling back to
  // /organizations. This is the canonical SSO fix — see memory
  // `project_auth_v2_baseline_msal_bug` and `feedback_name_collision_in_consumer_authinit`.
  //
  // No local sync `getTenantId` is exported from this file, so the direct
  // property access on `runtimeConfig.tenantId` does not trigger the name-collision
  // hazard documented for other consumers.
  await initAuth({
    clientId: runtimeConfig.msalClientId,
    tenantId: runtimeConfig.tenantId,
    bffApiScope: runtimeConfig.bffOAuthScope,
    bffBaseUrl: runtimeConfig.bffBaseUrl,
    proactiveRefresh: true,
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
