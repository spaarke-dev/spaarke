/**
 * authInit.ts
 * Thin wrapper around @spaarke/auth for the Legal Operations Workspace.
 *
 * Initializes the shared auth provider with workspace-appropriate defaults
 * (BFF base URL from runtimeConfig, tenant ID from runtimeConfig, proactive
 * refresh enabled) and re-exports the two functions consumed across the
 * workspace: authenticatedFetch and getTenantId.
 *
 * Why pass tenantId: when omitted, @spaarke/auth falls back to the
 * `organizations` authority, which causes MSAL to show a "Pick an account"
 * popup on first acquisition because AAD cannot disambiguate the tenant
 * cookie. Passing tenantId from runtimeConfig builds a tenant-specific
 * authority and enables silent SSO from the host browser session.
 *
 * Migration note: This replaces direct usage of the legacy bffAuthProvider.ts
 * (deleted alongside this rewrite).
 */

import { initAuth, authenticatedFetch as sharedAuthFetch, getAuthProvider } from '@spaarke/auth';
import {
  getBffBaseUrl,
  getBffOAuthScope,
  getMsalClientId,
  getTenantId as getRuntimeTenantId,
} from '../config/runtimeConfig';

// ---------------------------------------------------------------------------
// Initialization
// ---------------------------------------------------------------------------

let _initPromise: Promise<void> | null = null;

/**
 * Initialize the @spaarke/auth provider for the workspace.
 * Safe to call multiple times — returns the same promise.
 *
 * Note: we DO NOT pass an explicit `authority` — when `tenantId` is supplied,
 * the library constructs `https://login.microsoftonline.com/{tenantId}` for us.
 */
export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      try {
        await initAuth({
          clientId: getMsalClientId(),
          tenantId: getRuntimeTenantId(),
          bffBaseUrl: getBffBaseUrl(),
          bffApiScope: getBffOAuthScope(),
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
