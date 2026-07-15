/**
 * Auth bootstrap for the Communication Code Page — @spaarke/auth v2 ONLY.
 *
 * ADR-028: function-based auth is the only public contract. This page NEVER
 * self-bootstraps MSAL (`new PublicClientApplication`), NEVER snapshots a token,
 * and NEVER passes an `accessToken` prop. Runtime config (BFF URL, OAuth scope,
 * MSAL client ID, tenant ID) is resolved from Dataverse Environment Variables
 * via `resolveRuntimeConfig()`; `data=` URL overrides win when present
 * (reference §7.3).
 */

import { resolveRuntimeConfig, initAuth, authenticatedFetch, type AuthenticatedFetchFn } from '@spaarke/auth';
import type { ICommunicationAuthParams } from '../types/communication';

export interface IResolvedAuth {
  /** The v2 function-based fetch — the only transport handed to renderers. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** BFF base URL (host only, NO `/api`) — passed through to renderers. */
  bffBaseUrl: string;
}

/**
 * Resolve runtime config (with optional `data=` overrides) and initialize
 * @spaarke/auth. Returns the pieces renderers need. Call ONCE at bootstrap.
 */
export async function initializeAuth(overrides: ICommunicationAuthParams): Promise<IResolvedAuth> {
  // If the caller supplied a client ID on the URL, seed the window global BEFORE
  // resolveRuntimeConfig() so its fallback chain can find it (mirrors the
  // DocumentRelationshipViewer exemplar).
  if (overrides.clientId) {
    window.__SPAARKE_MSAL_CLIENT_ID__ = overrides.clientId;
  }

  const runtime = await resolveRuntimeConfig();

  const clientId = overrides.clientId ?? runtime.msalClientId;
  const tenantId = overrides.tenantId ?? runtime.tenantId;
  const bffBaseUrl = overrides.bffBaseUrl ?? runtime.bffBaseUrl;
  const bffApiScope = overrides.scope ?? runtime.bffOAuthScope;

  await initAuth({
    clientId,
    tenantId,
    bffBaseUrl,
    bffApiScope,
    proactiveRefresh: true,
    // Authority is intentionally omitted — the library derives it from tenantId.
  });

  return { authenticatedFetch, bffBaseUrl };
}
