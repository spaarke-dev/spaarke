/**
 * Authentication initialization for the CommunicationAttachments PCF control.
 *
 * Uses the shared `@spaarke/auth` library for centralized MSAL token
 * management (ADR-028). NEVER instantiates `PublicClientApplication` directly
 * (INV-7). All configuration values are resolved at runtime from PCF manifest
 * input props (with a Dataverse environment-variable fallback resolved by the
 * caller) — no hardcoded CLIENT_ID, TENANT_ID, or BFF URL.
 *
 * `@spaarke/auth` has NO React dependency — safe for React 16 PCF controls.
 *
 * Usage:
 *   import { initializeAuth } from './authInit';
 *   await initializeAuth(tenantId, clientAppId, bffAppId, bffApiUrl, dataverseUrl);
 *
 * Then use `authenticatedFetch()` from `@spaarke/auth` for all BFF API calls.
 *
 * Mirrors `SemanticSearchControl/authInit.ts` (the canonical preview-wiring PCF).
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

/**
 * Initialize `@spaarke/auth` with PCF-specific configuration.
 *
 * @param tenantId Azure AD tenant ID (kept in signature; @spaarke/auth resolves
 *        tenant-specific authority via resolveTenantFromXrm(), so the explicit
 *        authority is intentionally omitted from the config).
 * @param clientAppId PCF Client Application ID for MSAL authentication.
 * @param bffAppId BFF Application ID (for scope construction).
 * @param bffApiUrl BFF API base URL (host only, no /api suffix).
 * @param dataverseUrl Dataverse org URL for the static redirect URI.
 */
export async function initializeAuth(
  tenantId: string,
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string,
  dataverseUrl: string
): Promise<void> {
  // tenantId intentionally unused — @spaarke/auth resolves tenant-specific
  // authority via resolveTenantFromXrm(). Passing an explicit authority bypasses
  // that resolution (the cause of the 2026-05-13 popup regression). Kept in the
  // signature to mirror the SemanticSearchControl contract.
  void tenantId;

  const config: IAuthConfig = {
    clientId: clientAppId,
    // authority intentionally omitted — see comment above.
    // Static redirect URI matching the Azure AD app registration (Dataverse org URL).
    redirectUri: dataverseUrl,
    bffApiScope: `api://${bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    proactiveRefresh: true,
  };

  await initAuth(config);
}
