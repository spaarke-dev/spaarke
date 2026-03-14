/**
 * Authentication initialization for AnalysisWorkspace PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * This replaces the local AuthService.ts and MsalAuthProvider.ts with the
 * shared authenticatedFetch() pattern.
 *
 * Scope Reconciliation:
 *   - Old: api://<BFF_APP_ID>/SDAP.Access (local AuthService)
 *   - Old: api://1e40baad-.../user_impersonation (local MsalAuthProvider/msalConfig)
 *   - New: Uses @spaarke/auth default scope (api://1e40baad-.../user_impersonation)
 *   SDAP.Access and user_impersonation are on the SAME App Registration.
 *   Going forward, user_impersonation is the standard scope.
 *
 * @spaarke/auth has NO React dependency — safe for React 16 PCF controls.
 *
 * Usage:
 *   import { initializeAuth } from './authInit';
 *   await initializeAuth(tenantId, clientAppId, bffAppId, bffApiUrl);
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
 * @param tenantId Azure AD tenant ID
 * @param clientAppId PCF Client Application ID (for MSAL authentication)
 * @param bffAppId BFF Application ID (for scope construction)
 * @param bffApiUrl BFF API base URL
 */
export async function initializeAuth(
  tenantId: string,
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string
): Promise<void> {
  console.info('[authInit] Initializing @spaarke/auth for AnalysisWorkspace...');

  const config: IAuthConfig = {
    clientId: clientAppId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    // CRITICAL: Static redirect URI matching Azure AD app registration
    // Must be the Dataverse org URL, NOT window.location.origin
    redirectUri: 'https://spaarkedev1.crm.dynamics.com',
    // Scope: api://<BFF_APP_ID>/user_impersonation (replaces SDAP.Access)
    // Both SDAP.Access and user_impersonation are on the same App Registration.
    // user_impersonation is the standard scope going forward.
    bffApiScope: `api://${bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    // PCF controls benefit from proactive refresh to avoid token expiry during long sessions
    proactiveRefresh: true,
  };

  await initAuth(config);

  console.info('[authInit] @spaarke/auth initialized successfully for AnalysisWorkspace');
}
