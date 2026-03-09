/**
 * authInit.ts
 * Thin wrapper around @spaarke/auth for the Legal Operations Workspace.
 *
 * Initializes the shared auth provider with workspace-appropriate defaults
 * (BFF base URL from bffConfig, proactive refresh enabled) and re-exports
 * the two functions consumed across the workspace: authenticatedFetch and
 * getTenantId.
 *
 * Migration note: This replaces direct usage of bffAuthProvider.ts.
 * The old module is kept as reference but no longer imported by workspace code.
 */

import { initAuth, authenticatedFetch as sharedAuthFetch, getAuthProvider } from '@spaarke/auth';
import { getBffBaseUrl } from '../config/bffConfig';

// ---------------------------------------------------------------------------
// Initialization
// ---------------------------------------------------------------------------

let _initPromise: Promise<void> | null = null;

/**
 * Initialize the @spaarke/auth provider for the workspace.
 * Safe to call multiple times — returns the same promise.
 */
export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      try {
        await initAuth({
          bffBaseUrl: getBffBaseUrl(),
          proactiveRefresh: true,
        });
        console.info('[authInit] @spaarke/auth initialized successfully');
      } catch (err) {
        console.warn('[authInit] @spaarke/auth initialization failed', err);
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
  init?: RequestInit,
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
