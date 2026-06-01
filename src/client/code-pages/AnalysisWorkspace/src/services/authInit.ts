/**
 * authInit.ts — Spaarke Auth v2 bootstrap for AnalysisWorkspace Code Page.
 *
 * Initializes the shared @spaarke/auth provider with values resolved at runtime
 * from Dataverse Environment Variables (sprk_BffApiBaseUrl, sprk_BffApiAppId,
 * sprk_MsalClientId, sprk_TenantId). Re-exports the function-based auth surface
 * (`useAuth`, `authenticatedFetch`, `getAccessToken`) that consumers should use.
 *
 * Spaarke Auth v2 contract (per CLAUDE.md §D-AUTH-*):
 *   - No `accessToken: string` field. Tokens never cross a component boundary.
 *   - `authenticatedFetch` is the only code that materializes Bearer headers.
 *   - SSE paths use the narrow `getAccessToken()` escape hatch (token never
 *     snapshotted — `await`ed immediately before each fetch).
 *
 * @see .claude/AUDIT-FINDINGS-AUTH-SYSTEM.md — canonical auth design
 * @see projects/spaarke-auth-v2-and-hardening/CLAUDE.md — project rules
 */

import {
  initAuth,
  getAuthProvider,
  authenticatedFetch as sharedAuthFetch,
  resolveRuntimeConfig,
  AuthError,
  useAuth as sharedUseAuth,
} from '@spaarke/auth';
import type { IRuntimeConfig, UseAuthResult } from '@spaarke/auth';

// Re-export library surface (consumers import from here for namespacing)
export { initAuth, getAuthProvider, resolveRuntimeConfig, AuthError };
export type { IRuntimeConfig, UseAuthResult };

// Re-export the library's useAuth hook directly. Components in AnalysisWorkspace
// import `useAuth` from '../hooks/useAuth' (a thin re-export below) OR from
// '@spaarke/auth' — both resolve to the same provider singleton.
export { sharedUseAuth as useAuth };

/** Cached runtime config after first resolution. */
let _runtimeConfig: IRuntimeConfig | null = null;

/**
 * Get the resolved runtime config. Must call initializeAuth() first.
 * @throws Error if runtime config has not been resolved yet.
 */
export function getRuntimeConfig(): IRuntimeConfig {
  if (!_runtimeConfig) {
    throw new Error('[AnalysisWorkspace:Auth] Runtime config not resolved. Call initializeAuth() first.');
  }
  return _runtimeConfig;
}

/**
 * Initialize authentication for the AnalysisWorkspace.
 *
 * 1. Resolves runtime config (BFF URL, OAuth scope, MSAL client ID, tenant ID)
 *    from Dataverse Environment Variables.
 * 2. Passes resolved config to initAuth() from @spaarke/auth.
 *
 * The library handles MSAL setup, token caching, proactive refresh, and the
 * BroadcastChannel logout signal internally. We pass `proactiveRefresh: true`
 * so the provider refreshes tokens before expiry (4-minute cadence matches the
 * cadence the deprecated AuthContext.tsx used to do manually).
 *
 * tenantId MUST be passed so the library constructs a tenant-scoped authority
 * (`login.microsoftonline.com/{tenantId}`) instead of `/organizations` —
 * the latter causes ssoSilent to fail (popup-on-every-startup, the
 * regression this entire workstream eliminates).
 *
 * @throws AuthError if MSAL init or token acquisition fails.
 */
export async function initializeAuth(): Promise<void> {
  const config = await resolveRuntimeConfig();
  _runtimeConfig = config;

  await initAuth({
    clientId: config.msalClientId,
    tenantId: config.tenantId,
    bffApiScope: config.bffOAuthScope,
    bffBaseUrl: config.bffBaseUrl,
    proactiveRefresh: true,
  });
}

/**
 * Acquire a fresh BFF access token via the auth provider's cache + JWT exp
 * validation. Delegates to the singleton provider.
 *
 * Use this ONLY for SSE / WebSocket paths where authenticatedFetch cannot wrap
 * the request lifecycle. For one-shot BFF calls, prefer `authenticatedFetch`.
 */
export async function getAccessToken(): Promise<string> {
  return getAuthProvider().getAccessToken();
}

/**
 * authenticatedFetch — single canonical entry point for BFF API calls.
 *
 * Wraps `fetch` with Bearer header attachment, 401 retry, and proactive
 * refresh hand-off. Use this for ALL BFF API calls in AnalysisWorkspace.
 */
export const authenticatedFetch = sharedAuthFetch;
