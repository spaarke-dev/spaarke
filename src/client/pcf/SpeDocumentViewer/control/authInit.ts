/**
 * Authentication initialization for SpeDocumentViewer PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * This replaces the local AuthService.ts with the shared authenticatedFetch() pattern.
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
 * - Redirect URI matching the current Dataverse org URL (read from window.location.origin so the
 *   same bundle works across dev / demo / customer tenants — each environment's URL must be
 *   registered on the MSAL client app's SPA redirect URIs).
 * - Named scope format: api://<BFF_APP_ID>/SDAP.Access
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
  console.info('[authInit] Initializing @spaarke/auth for SpeDocumentViewer...');

  // Derive redirect URI from current page origin so the same bundle works in any environment.
  // The MSAL client app's SPA redirect URIs must include every Dataverse origin you deploy to.
  const redirectUri =
    typeof window !== 'undefined' && window.location && window.location.origin
      ? window.location.origin
      : 'https://spaarkedev1.crm.dynamics.com';

  // tenantId parameter is intentionally unused now — @spaarke/auth resolves
  // tenant-specific authority via resolveTenantFromXrm() (reads
  // Xrm.Utility.getGlobalContext().organizationSettings.tenantId via frame-walk).
  // Passing an explicit authority bypasses that resolution and was the cause of
  // the popup regression on 2026-05-13 when the manifest property was empty.
  // Kept in signature for now to avoid touching every caller.
  void tenantId;

  const config: IAuthConfig = {
    clientId: clientAppId,
    // authority intentionally omitted — see comment above
    redirectUri,
    bffApiScope: `api://${bffAppId}/SDAP.Access`,
    bffBaseUrl: bffApiUrl,
    proactiveRefresh: true,
  };

  await initAuth(config);

  console.info(`[authInit] @spaarke/auth initialized successfully for SpeDocumentViewer (redirectUri=${redirectUri})`);
}
