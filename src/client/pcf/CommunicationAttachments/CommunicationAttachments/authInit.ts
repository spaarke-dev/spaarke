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

import { initAuth, isValidTenant } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

/**
 * Initialize `@spaarke/auth` with PCF-specific configuration.
 *
 * @param tenantId Azure AD tenant ID (manifest `tenantId` or `sprk_TenantId`). Validated with
 *        `isValidTenant`; when absent or invalid it is not passed and @spaarke/auth's own
 *        tenant discovery decides (it fails closed in a Dataverse host).
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
  // Pass the environment's tenant so the sign-in authority is tenant-specific (a B2B guest signed
  // in against /organizations lands in their home tenant — #1453). The 2026-05-13 popup regression
  // was a malformed `/undefined` authority, which @spaarke/auth now rejects.
  const config: IAuthConfig = {
    clientId: clientAppId,
    ...(isValidTenant(tenantId) ? { tenantId: tenantId.trim() } : {}),
    // Static redirect URI matching the Azure AD app registration (Dataverse org URL).
    redirectUri: dataverseUrl,
    bffApiScope: `api://${bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    proactiveRefresh: true,
  };

  await initAuth(config);
}
