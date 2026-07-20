/**
 * MSAL configuration for the Secure Project Workspace SPA.
 *
 * Authentication model: Entra External ID (CIAM) — external users are LOCAL accounts
 * in a separate CIAM external tenant (spaarkeextid), NOT B2B guests in the workforce
 * tenant (ADR-028 Amendment A1). The SPA authenticates against the CIAM authority
 * (*.ciamlogin.com); the BFF validates the resulting tokens via its "Ciam" scheme.
 *
 * Flow: Authorization code + PKCE (Microsoft's recommended pattern for SPAs
 * calling protected web APIs). The SPA receives access tokens for BFF API calls.
 *
 * Authority + client id + BFF scope are config-driven (see config.ts) and sourced
 * from the CIAM tenant app registrations (task 028 ops prerequisite).
 *
 * See: docs/architecture/external-access-spa-architecture.md
 */

import { PublicClientApplication, type Configuration } from '@azure/msal-browser';
import { MSAL_CLIENT_ID, MSAL_AUTHORITY } from '../config';

/**
 * CIAM (Entra External ID) authorities use a *.ciamlogin.com host, which MSAL treats
 * as a non-default (B2C-style) authority — its host MUST be declared as a known
 * authority so MSAL trusts the OIDC metadata endpoint. Derived from the config-driven
 * authority so it stays correct across environments.
 */
const ciamAuthorityHost = new URL(MSAL_AUTHORITY).host;

const msalConfig: Configuration = {
  auth: {
    clientId: MSAL_CLIENT_ID,
    authority: MSAL_AUTHORITY,
    knownAuthorities: [ciamAuthorityHost],
    /**
     * Redirect URI must match a registered SPA redirect URI on the CIAM SPA app
     * registration (the SWA origin in production, http://localhost:3000 in local dev).
     */
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    /**
     * sessionStorage: tokens survive page refresh but not new tab/window — per-tab
     * isolation. This is the documented ADR-028 exception for the external SPA;
     * do NOT switch to localStorage or migrate to @spaarke/auth.
     *
     * Note: msal-browser v5 removed `storeAuthStateInCookie` from CacheOptions (it is now a
     * per-request option and defaults to off), so it is no longer set here.
     */
    cacheLocation: 'sessionStorage',
  },
};

/**
 * Singleton MSAL instance for the SPA.
 * Must be passed to MsalProvider in main.tsx.
 * Call msalInstance.initialize() before rendering (see main.tsx).
 */
export const msalInstance = new PublicClientApplication(msalConfig);
