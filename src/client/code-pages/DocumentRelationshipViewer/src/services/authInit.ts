/**
 * Auth initialization for DocumentRelationshipViewer Code Page.
 *
 * Uses @spaarke/auth shared library instead of local MsalAuthProvider.
 * This eliminates hardcoded tenant ID and redirect URI, making the
 * code page portable across Dataverse environments.
 *
 * Runtime config (clientId, bffApiScope, bffBaseUrl) is resolved from
 * Dataverse Environment Variables via resolveRuntimeConfig() in index.tsx
 * and passed to initializeAuth() at bootstrap.
 *
 * @spaarke/auth defaults:
 *   - authority: https://login.microsoftonline.com/organizations (multi-tenant)
 *   - redirectUri: window.location.origin (auto-detected)
 */

import { initAuth, getAuthProvider, authenticatedFetch } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

/**
 * Initialize authentication with runtime-resolved config.
 * Call once at app startup before rendering.
 *
 * @param config Auth config with clientId, bffApiScope, and bffBaseUrl
 *               resolved from Dataverse Environment Variables.
 */
export async function initializeAuth(config?: IAuthConfig): Promise<void> {
  await initAuth(config);
}

/**
 * Get a raw access token for BFF API calls.
 * Drop-in replacement for MsalAuthProvider.getInstance().getToken().
 */
export async function getToken(): Promise<string> {
  return getAuthProvider().getAccessToken();
}

/**
 * Clear the auth token cache.
 * Drop-in replacement for MsalAuthProvider.getInstance().clearCache().
 */
export function clearAuthCache(): void {
  getAuthProvider().clearCache();
}

/**
 * Check whether the user is currently authenticated.
 * Drop-in replacement for MsalAuthProvider.getInstance().isAuthenticated().
 */
export function isAuthenticated(): boolean {
  return getAuthProvider().isAuthenticated();
}

// Re-export authenticatedFetch for convenience
export { authenticatedFetch, getAuthProvider };
