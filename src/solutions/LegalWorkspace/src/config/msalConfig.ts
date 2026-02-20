/**
 * msalConfig.ts
 * MSAL Browser configuration for the Legal Operations Workspace custom page.
 *
 * Uses the same Azure AD app registration as the PCF controls
 * (DocumentRelationshipViewer, AnalysisWorkspace, etc.) — the custom page
 * runs in the same Dataverse origin so the redirect URI matches.
 *
 * Token acquisition order in bffAuthProvider:
 *   1. PCF bridge token (window global — fastest, no network)
 *   2. MSAL ssoSilent (uses existing Azure AD session — iframe, no UI)
 *   3. Empty string (anonymous / dev fallback)
 */

import type { Configuration } from '@azure/msal-browser';
import { LogLevel } from '@azure/msal-browser';

// ---------------------------------------------------------------------------
// Azure AD App Registration
// ---------------------------------------------------------------------------

/** PCF Client App ID — "Sparke DSM-SPE Dev 2" app registration. */
const CLIENT_ID = '170c98e1-d486-4355-bcbe-170454e0207c';

/** Azure AD Tenant (Directory) ID. */
const TENANT_ID = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';

/** Redirect URI — must match a registered SPA redirect in the app registration. */
const REDIRECT_URI = 'https://spaarkedev1.crm.dynamics.com';

// ---------------------------------------------------------------------------
// MSAL Configuration
// ---------------------------------------------------------------------------

export const msalConfig: Configuration = {
  auth: {
    clientId: CLIENT_ID,
    authority: `https://login.microsoftonline.com/${TENANT_ID}`,
    redirectUri: REDIRECT_URI,
    navigateToLoginRequestUrl: false,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      loggerCallback: (level, message, containsPii) => {
        if (containsPii) return;
        switch (level) {
          case LogLevel.Error:
            console.error(`[MSAL] ${message}`);
            break;
          case LogLevel.Warning:
            console.warn(`[MSAL] ${message}`);
            break;
          case LogLevel.Info:
            // Suppress info-level MSAL logs in production; set to console.info for debugging
            break;
          case LogLevel.Verbose:
            break;
        }
      },
      logLevel: LogLevel.Warning,
    },
  },
};
