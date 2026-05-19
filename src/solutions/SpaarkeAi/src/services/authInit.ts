/**
 * authInit.ts
 * Thin wrapper around @spaarke/auth for the SpaarkeAi Code Page.
 *
 * Initializes the shared auth provider with workspace-appropriate defaults
 * (BFF base URL from runtimeConfig, proactive refresh enabled) and re-exports
 * the two functions consumed across the workspace: authenticatedFetch and
 * getTenantId.
 *
 * @see src/solutions/LegalWorkspace/src/services/authInit.ts — canonical pattern
 * @see ADR-006 - Code Pages for standalone dialogs
 * @see ADR-022 - Code Pages bundle their own React and auth
 */

import { initAuth, authenticatedFetch as sharedAuthFetch, getAuthProvider } from "@spaarke/auth";
import { getBffBaseUrl, getBffOAuthScope, getMsalClientId, getTenantId } from "../config/runtimeConfig";

// ---------------------------------------------------------------------------
// Initialization
// ---------------------------------------------------------------------------

let _initPromise: Promise<void> | null = null;

/**
 * Initialize the @spaarke/auth provider for SpaarkeAi.
 * Safe to call multiple times — returns the same promise.
 */
export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      try {
        await initAuth({
          clientId: getMsalClientId(),
          // tenantId from sprk_TenantId env var → library constructs tenant-specific
          // authority `login.microsoftonline.com/{tenantId}`. Without this the
          // authority falls back to `/organizations` and ssoSilent can't
          // disambiguate the session cookie → popup-on-every-startup.
          tenantId: getTenantId(),
          bffBaseUrl: getBffBaseUrl(),
          bffApiScope: getBffOAuthScope(),
          proactiveRefresh: true,
        });
        console.info("[SpaarkeAi:authInit] @spaarke/auth initialized successfully");
      } catch (err) {
        console.warn("[SpaarkeAi:authInit] @spaarke/auth initialization failed", err);
        _initPromise = null; // Allow retry
        throw err;
      }
    })();
  }
  return _initPromise;
}

// ---------------------------------------------------------------------------
// Re-exports for workspace consumers
// ---------------------------------------------------------------------------

/**
 * Performs a fetch request with BFF Bearer token authentication.
 * Ensures auth is initialized before making the request.
 */
export async function authenticatedFetch(
  url: string,
  init?: RequestInit
): Promise<Response> {
  await ensureAuthInitialized();
  return sharedAuthFetch(url, init);
}

/**
 * Resolve the Azure AD tenant ID from MSAL account or Xrm context.
 */
export async function getTenantId(): Promise<string> {
  await ensureAuthInitialized();
  return getAuthProvider().getTenantId();
}
