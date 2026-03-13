/**
 * Auth initialization for DocumentRelationshipViewer Code Page.
 *
 * Uses @spaarke/auth shared library instead of local MsalAuthProvider.
 * This eliminates hardcoded tenant ID and redirect URI, making the
 * code page portable across Dataverse environments.
 *
 * @spaarke/auth defaults:
 *   - authority: https://login.microsoftonline.com/organizations (multi-tenant)
 *   - redirectUri: window.location.origin (auto-detected)
 *   - clientId: 170c98e1-d486-4355-bcbe-170454e0207c (same app registration)
 *   - bffApiScope: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
 */

import { initAuth, getAuthProvider, authenticatedFetch } from "@spaarke/auth";

/**
 * Initialize authentication. Call once at app startup before rendering.
 * Replaces MsalAuthProvider.getInstance().initialize() from the old local auth.
 */
export async function initializeAuth(): Promise<void> {
  await initAuth();
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
