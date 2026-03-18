/**
 * MSAL configuration for the Secure Project Workspace SPA.
 *
 * Authentication model: Entra B2B — external users are guest accounts in the
 * main Spaarke workforce tenant. They authenticate with their existing
 * Microsoft 365 credentials (SSO). No separate credentials required.
 *
 * Flow: Authorization code + PKCE (Microsoft's recommended pattern for SPAs
 * calling protected web APIs). Auth code is exchanged server-side by MSAL;
 * the SPA receives access tokens for BFF API calls.
 *
 * App registrations (main tenant a221a95e-6abc-4434-aecc-e48338a1b2f2):
 *   SPA client:  spaarke-external-access-SPA  (f306885a-8251-492c-8d3e-34d7b476ffd0)
 *   BFF API:     SDAP-BFF-SPE-API             (1e40baad-e065-4aea-a8d4-4b7ab273458c)
 *   BFF scope:   api://1e40baad-.../SDAP.Access
 *
 * See: docs/architecture/power-pages-spa-guide.md
 * See: notes/auth-migration-b2b-msal.md
 */

import { PublicClientApplication, type Configuration } from "@azure/msal-browser";
import { MSAL_CLIENT_ID, MSAL_TENANT_ID } from "../config";

const msalConfig: Configuration = {
  auth: {
    clientId: MSAL_CLIENT_ID,
    authority: `https://login.microsoftonline.com/${MSAL_TENANT_ID}`,
    /**
     * Redirect URI must match one of the registered SPA redirect URIs on the
     * spaarke-external-access-SPA app registration:
     *   https://sprk-external-workspace.powerappsportals.com  (production)
     *   http://localhost:3000                                  (local dev)
     */
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    /**
     * sessionStorage: tokens survive page refresh but not new tab/window.
     * Appropriate for a portal-hosted SPA where each tab is independent.
     * Do not use localStorage for B2B — avoids token leakage across tabs.
     */
    cacheLocation: "sessionStorage",
    storeAuthStateInCookie: false,
  },
};

/**
 * Singleton MSAL instance for the SPA.
 * Must be passed to MsalProvider in main.tsx.
 * Call msalInstance.initialize() before rendering (see main.tsx).
 */
export const msalInstance = new PublicClientApplication(msalConfig);
