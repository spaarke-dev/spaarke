/**
 * Authentication initialization for SemanticSearchControl PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * This replaces the local MsalAuthProvider with the shared authenticatedFetch() pattern.
 *
 * All configuration values are resolved at runtime from Dataverse environment variables
 * and PCF manifest parameters. No hardcoded CLIENT_ID, TENANT_ID, or BFF URLs.
 *
 * @spaarke/auth has NO React dependency -- safe for React 16 PCF controls.
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
 * @param tenantId Azure AD tenant ID (from Dataverse environment variable or manifest)
 * @param clientAppId PCF Client Application ID for MSAL authentication
 * @param bffAppId BFF Application ID (for scope construction)
 * @param bffApiUrl BFF API base URL (from Dataverse environment variable)
 * @param dataverseUrl Dataverse org URL for redirect URI (e.g. https://org.crm.dynamics.com)
 */
export async function initializeAuth(
  tenantId: string,
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string,
  dataverseUrl: string
): Promise<void> {
  console.info('[authInit] Initializing @spaarke/auth for SemanticSearchControl...');

  // tenantId parameter is intentionally unused now — @spaarke/auth resolves
  // tenant-specific authority via resolveTenantFromXrm() (reads
  // Xrm.Utility.getGlobalContext().organizationSettings.tenantId via frame-walk).
  // Passing an explicit authority bypasses that resolution and was the cause of
  // the popup regression discovered 2026-05-13. Kept in signature to avoid
  // touching every caller.
  void tenantId;

  const config: IAuthConfig = {
    clientId: clientAppId,
    // authority intentionally omitted — see comment above
    // CRITICAL: Static redirect URI matching Azure AD app registration
    // Resolved at runtime from Xrm.Utility.getGlobalContext().getClientUrl()
    redirectUri: dataverseUrl,
    bffApiScope: `api://${bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    proactiveRefresh: true,
  };

  await initAuth(config);

  console.info('[authInit] @spaarke/auth initialized successfully for SemanticSearchControl');
}
