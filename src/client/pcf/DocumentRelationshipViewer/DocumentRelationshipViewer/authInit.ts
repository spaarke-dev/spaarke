/**
 * Authentication initialization for DocumentRelationshipViewer PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * All configuration values are resolved at runtime from Dataverse environment
 * variables via environmentVariables.ts -- no hardcoded dev URLs or IDs.
 *
 * @spaarke/auth has NO React dependency -- safe for React 16 PCF controls.
 *
 * Runtime config resolution:
 *   - BFF API URL: resolved from Dataverse env var (sprk_BffApiBaseUrl)
 *   - MSAL Client ID: resolved from Dataverse env var (sprk_MsalClientId)
 *   - Tenant ID: resolved from Dataverse env var (sprk_TenantId)
 *   - BFF App ID: resolved from Dataverse env var (sprk_BffApiAppId)
 *   - Redirect URI: derived from Xrm.Utility.getGlobalContext().getClientUrl()
 *
 * Usage:
 *   import { initializeAuth } from './authInit';
 *   await initializeAuth(tenantId, clientAppId, bffAppId, bffApiUrl, dataverseUrl);
 *
 * Then use authenticatedFetch() from '@spaarke/auth' for all BFF API calls.
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

/**
 * Initialize @spaarke/auth with PCF-specific configuration.
 *
 * PCF controls require:
 * - Static redirect URI (Dataverse org URL, not window.location.origin)
 * - Named scope format: api://<BFF_APP_ID>/user_impersonation
 * - Session storage for MSAL cache
 *
 * @param tenantId Azure AD tenant ID (from Dataverse env var sprk_TenantId)
 * @param clientAppId PCF Client Application ID (from Dataverse env var sprk_MsalClientId)
 * @param bffAppId BFF Application ID (from Dataverse env var sprk_BffApiAppId)
 * @param bffApiUrl BFF API base URL (from Dataverse env var sprk_BffApiBaseUrl)
 * @param dataverseUrl Dataverse org URL for redirect URI (from Xrm context)
 */
export async function initializeAuth(
  tenantId: string,
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string,
  dataverseUrl: string
): Promise<void> {
  console.info('[authInit] Initializing @spaarke/auth for DocumentRelationshipViewer...');

  // Derive redirect URI from Dataverse org URL (no hardcoded environment URLs)
  const redirectUri = dataverseUrl.replace(/\/+$/, '');

  const config: IAuthConfig = {
    clientId: clientAppId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    // Redirect URI derived from Dataverse org URL at runtime
    redirectUri,
    // Named scope: api://<BFF_APP_ID>/user_impersonation
    bffApiScope: `api://${bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    // PCF controls benefit from proactive refresh to avoid token expiry during long sessions
    proactiveRefresh: true,
  };

  await initAuth(config);

  console.info('[authInit] @spaarke/auth initialized successfully for DocumentRelationshipViewer');
}
