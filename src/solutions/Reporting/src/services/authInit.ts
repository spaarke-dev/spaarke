/**
 * authInit.ts
 * Thin wrapper around @spaarke/auth for the Reporting Code Page.
 *
 * Initializes the shared auth provider with BFF base URL from runtimeConfig
 * and re-exports authenticatedFetch for use across the module.
 *
 * @see .claude/patterns/auth/spaarke-auth-initialization.md
 */

import { initAuth, authenticatedFetch as sharedAuthFetch } from "@spaarke/auth";
import { getBffBaseUrl, getBffOAuthScope, getMsalClientId } from "../config/runtimeConfig";

// ---------------------------------------------------------------------------
// Initialization
// ---------------------------------------------------------------------------

let _initPromise: Promise<void> | null = null;

/**
 * Initialize the @spaarke/auth provider for the Reporting module.
 * Safe to call multiple times — returns the same promise.
 */
export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      try {
        await initAuth({
          clientId: getMsalClientId(),
          bffBaseUrl: getBffBaseUrl(),
          bffApiScope: getBffOAuthScope(),
          proactiveRefresh: true,
        });
        console.info("[authInit] @spaarke/auth initialized successfully");
      } catch (err) {
        console.warn("[authInit] @spaarke/auth initialization failed", err);
        _initPromise = null; // Allow retry
        throw err;
      }
    })();
  }
  return _initPromise;
}

// ---------------------------------------------------------------------------
// Re-exports for module consumers
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
