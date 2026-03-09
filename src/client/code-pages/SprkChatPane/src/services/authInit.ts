/**
 * Auth initialization bridge for SprkChatPane Code Page.
 *
 * Thin wrapper around @spaarke/auth that provides the same public API
 * previously exported by authService.ts. Requires Xrm to be available
 * (throws AuthError if not).
 *
 * Migration mapping:
 *   authService.initializeAuth()  -> initAuth({ requireXrm: true })
 *   authService.getAccessToken()  -> getAuthProvider().getAccessToken()
 *   authService.clearTokenCache() -> getAuthProvider().clearCache()
 *   authService.isXrmAvailable()  -> isXrmAvailable() (local re-export)
 *   authService.getClientUrl()    -> (unchanged — still local utility)
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
 * Initialize authentication for SprkChatPane with Xrm requirement.
 *
 * Throws AuthError if Xrm is not available — this pane must be opened
 * from within Dataverse.
 *
 * @returns The initial access token string
 */
export async function initializeAuth(): Promise<string> {
    const provider = await initAuth({ requireXrm: true });
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
 * Check if Xrm SDK is available in the current context.
 * Performs frame-walk to check window, parent, and top frames.
 */
export function isXrmAvailable(): boolean {
    if (typeof window === "undefined") return false;
    try {
        /* eslint-disable @typescript-eslint/no-explicit-any */
        const xrm = (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
        /* eslint-enable @typescript-eslint/no-explicit-any */
        return !!xrm?.Utility?.getGlobalContext;
    } catch {
        return false;
    }
}

/**
 * Get the Dataverse org URL from Xrm.Utility.getGlobalContext().
 *
 * @returns The org URL (e.g., "https://orgname.crm.dynamics.com")
 * @throws AuthError if Xrm is not available or getClientUrl() returns empty
 */
export function getClientUrl(): string {
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const frames: Window[] = [window];
    try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm;
            if (xrm?.Utility?.getGlobalContext) {
                const clientUrl = xrm.Utility.getGlobalContext().getClientUrl();
                if (clientUrl) return clientUrl;
            }
        } catch { /* cross-origin */ }
    }
    /* eslint-enable @typescript-eslint/no-explicit-any */

    throw new AuthError(
        "Xrm SDK is not available. This page must be opened from within Dataverse.",
        "xrm_required",
    );
}
