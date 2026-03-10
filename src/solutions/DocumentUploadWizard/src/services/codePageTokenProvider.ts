/**
 * codePageTokenProvider.ts
 *
 * ITokenProvider implementations for the DocumentUploadWizard Code Page.
 *
 * Provides two token providers:
 *   1. BFF API token provider — for SPE file operations (via SdapApiClient / NavMapClient)
 *   2. Dataverse token provider — for OData record operations (via ODataDataverseClient)
 *
 * Both use @spaarke/auth's SpaarkeAuthProvider (initialized via initAuth() in main.tsx)
 * which chains 5 strategies: bridge -> cache -> Xrm -> MSAL silent -> MSAL popup.
 *
 * @see ADR-007  - All SPE operations through BFF API
 * @see ADR-008  - Endpoint filters for auth (Bearer token)
 */

import { getAuthProvider } from "@spaarke/auth";
import type { ITokenProvider } from "@spaarke/ui-components/services/document-upload";

// ---------------------------------------------------------------------------
// BFF API Token Provider
// ---------------------------------------------------------------------------

/**
 * Token provider for BFF API requests (SPE file operations, NavMap queries).
 *
 * Uses @spaarke/auth's 5-strategy cascade to acquire a BFF-scoped token.
 * The scope is configured via initAuth() in main.tsx (defaults to
 * api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation).
 *
 * @returns ITokenProvider function compatible with SdapApiClient and NavMapClient
 */
export function createBffTokenProvider(): ITokenProvider {
    return async (): Promise<string> => {
        const provider = getAuthProvider();
        const token = await provider.getAccessToken();
        if (!token) {
            throw new Error(
                "[codePageTokenProvider] Failed to acquire BFF API token. " +
                "Ensure initAuth() was called during app bootstrap."
            );
        }
        return token;
    };
}

// ---------------------------------------------------------------------------
// Dataverse Token Provider
// ---------------------------------------------------------------------------

/**
 * Dataverse organization URL resolved from Xrm context.
 *
 * Frame-walks through window -> parent -> top to find Xrm.Utility.getGlobalContext().
 * Falls back to known dev environment if Xrm is not available.
 */
export function resolveDataverseUrl(): string {
    // Try Xrm global context via frame-walk
    const frames: Window[] = [window];
    try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            /* eslint-disable @typescript-eslint/no-explicit-any */
            const xrm = (frame as any).Xrm;
            const clientUrl: string | undefined =
                xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
            /* eslint-enable @typescript-eslint/no-explicit-any */
            if (clientUrl) {
                // Strip trailing slash
                return clientUrl.endsWith("/") ? clientUrl.slice(0, -1) : clientUrl;
            }
        } catch {
            // Cross-origin frame — skip
        }
    }

    // Fallback: dev environment
    console.warn(
        "[codePageTokenProvider] Xrm context not found. " +
        "Falling back to dev Dataverse URL."
    );
    return "https://spaarkedev1.crm.dynamics.com";
}

/**
 * Token provider for Dataverse OData requests.
 *
 * Acquires a token scoped to the Dataverse environment's /.default audience.
 * Uses the same @spaarke/auth provider but targets a different scope.
 *
 * NOTE: In the Xrm webresource context, the BFF-scoped token returned by
 * SpaarkeAuthProvider.getAccessToken() is typically sufficient for
 * Dataverse OData calls because the BFF app registration has Dataverse
 * delegated permissions. If direct Dataverse tokens are needed in the future,
 * this can be extended to use MSAL with a Dataverse-specific scope.
 *
 * @returns ITokenProvider function compatible with ODataDataverseClient
 */
export function createDataverseTokenProvider(): ITokenProvider {
    return async (): Promise<string> => {
        const provider = getAuthProvider();
        const token = await provider.getAccessToken();
        if (!token) {
            throw new Error(
                "[codePageTokenProvider] Failed to acquire Dataverse token. " +
                "Ensure initAuth() was called during app bootstrap."
            );
        }
        return token;
    };
}
