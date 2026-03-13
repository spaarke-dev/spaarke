/**
 * Authentication initialization for SemanticSearchControl PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * This replaces the local MsalAuthProvider with the shared authenticatedFetch() pattern.
 *
 * @spaarke/auth has NO React dependency — safe for React 16 PCF controls.
 *
 * Usage:
 *   import { initializeAuth } from './authInit';
 *   await initializeAuth(apiBaseUrl);
 *
 * Then use authenticatedFetch() from '@spaarke/auth' for all BFF API calls.
 */

import { initAuth } from "@spaarke/auth";
import type { IAuthConfig } from "@spaarke/auth";

/**
 * Azure AD Application (Client) ID
 * From: Sparke DSM-SPE Dev 2 App Registration
 * This is the PCF CLIENT app, NOT the BFF API app.
 */
const CLIENT_ID = "170c98e1-d486-4355-bcbe-170454e0207c";

/**
 * Azure AD Tenant (Directory) ID
 */
const TENANT_ID = "a221a95e-6abc-4434-aecc-e48338a1b2f2";

/**
 * BFF Application ID (for scope construction)
 */
const BFF_APP_ID = "1e40baad-e065-4aea-a8d4-4b7ab273458c";

/**
 * Initialize @spaarke/auth with PCF-specific configuration.
 *
 * PCF controls require:
 * - Static redirect URI (Dataverse org URL, not window.location.origin)
 * - Named scope format: api://<BFF_APP_ID>/user_impersonation
 * - Session storage for MSAL cache
 *
 * @param bffApiUrl BFF API base URL (from control parameters)
 */
export async function initializeAuth(bffApiUrl: string): Promise<void> {
  console.info(
    "[authInit] Initializing @spaarke/auth for SemanticSearchControl...",
  );

  const config: IAuthConfig = {
    clientId: CLIENT_ID,
    authority: `https://login.microsoftonline.com/${TENANT_ID}`,
    // CRITICAL: Static redirect URI matching Azure AD app registration
    // Must be the Dataverse org URL, NOT window.location.origin
    redirectUri: "https://spaarkedev1.crm.dynamics.com",
    // Named scope: api://<BFF_APP_ID>/user_impersonation
    bffApiScope: `api://${BFF_APP_ID}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    // PCF controls benefit from proactive refresh to avoid token expiry during long sessions
    proactiveRefresh: true,
  };

  await initAuth(config);

  console.info(
    "[authInit] @spaarke/auth initialized successfully for SemanticSearchControl",
  );
}
