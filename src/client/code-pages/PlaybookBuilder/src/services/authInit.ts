/**
 * Auth initialization bridge for PlaybookBuilder Code Page.
 *
 * Thin wrapper around @spaarke/auth that provides the same public API
 * previously exported by authService.ts. Uses proactive 4-minute token
 * refresh (handled by @spaarke/auth internally).
 *
 * Migration mapping:
 *   authService.initializeAuth()     -> initAuth({ proactiveRefresh: true })
 *   authService.getAccessToken()     -> getAuthProvider().getAccessToken()
 *   authService.clearTokenCache()    -> getAuthProvider().clearCache()
 *   authService.stopTokenRefresh()   -> getAuthProvider().dispose()
 *   authService.isXrmAvailable()     -> (internal to @spaarke/auth)
 *   authService.getClientUrl()       -> (unchanged — still local utility)
 *   authService.isSameOriginDataverse() -> (unchanged — still local utility)
 *
 * @see @spaarke/auth for the shared authentication library
 * @see ADR-008 - Endpoint filters for auth
 */

import {
  initAuth,
  getAuthProvider,
  authenticatedFetch,
  AuthError,
} from "@spaarke/auth";
import type { SpaarkeAuthProvider } from "@spaarke/auth";

// Re-export core symbols for consumers
export { initAuth, getAuthProvider, authenticatedFetch, AuthError };
export type { SpaarkeAuthProvider };

/**
 * Initialize authentication for PlaybookBuilder with proactive token refresh.
 *
 * Enables proactive 4-minute refresh interval via @spaarke/auth.
 * PlaybookBuilder uses Bearer tokens when cross-origin and session cookies
 * when same-origin with Dataverse.
 *
 * @returns The initial access token string
 */
export async function initializeAuth(): Promise<string> {
  const provider = await initAuth({ proactiveRefresh: true });
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

/**
 * Stop the proactive token refresh interval.
 * Call on component unmount to prevent leaks.
 */
export function stopTokenRefresh(): void {
  getAuthProvider().dispose();
}

/**
 * Get the Dataverse org URL from Xrm.Utility.getGlobalContext().
 *
 * @returns The org URL (e.g., "https://orgname.crm.dynamics.com"), or null if Xrm unavailable
 */
export function getClientUrl(): string | null {
  /* eslint-disable @typescript-eslint/no-explicit-any */
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window && window.top !== window.parent)
      frames.push(window.top!);
  } catch {
    /* cross-origin */
  }

  for (const frame of frames) {
    try {
      const xrm = (frame as any).Xrm;
      if (xrm?.Utility?.getGlobalContext) {
        const clientUrl = xrm.Utility.getGlobalContext().getClientUrl();
        if (clientUrl) return clientUrl;
      }
    } catch {
      /* cross-origin */
    }
  }
  /* eslint-enable @typescript-eslint/no-explicit-any */

  return null;
}

/**
 * Detect if we're running inside Dataverse (same-origin).
 * When true, session cookies authenticate API calls automatically.
 */
export function isSameOriginDataverse(): boolean {
  const clientUrl = getClientUrl();
  if (clientUrl) {
    try {
      const dvOrigin = new URL(clientUrl).origin.toLowerCase();
      return dvOrigin === window.location.origin.toLowerCase();
    } catch {
      // fall through
    }
  }
  const hostname = window.location.hostname.toLowerCase();
  return hostname.endsWith(".dynamics.com");
}
