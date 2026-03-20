/**
 * msalConfig.ts
 * MSAL Browser configuration for the Legal Operations Workspace custom page.
 *
 * Uses the same Azure AD app registration as the PCF controls
 * (DocumentRelationshipViewer, AnalysisWorkspace, etc.).
 *
 * Environment portability:
 *   - Redirect URI: auto-detected from window.location.origin (works in any
 *     Dataverse environment as long as the origin is registered as a SPA
 *     redirect in the app registration).
 *   - Authority: uses "organizations" endpoint (multi-tenant) so the same
 *     client ID works across Spaarke environments and customer tenants
 *     (requires the app registration to be set to "Accounts in any
 *     organizational directory" in Azure AD).
 *   - Client ID: defaults to the Spaarke "DSM-SPE Dev 2" app registration.
 *     Override via window.__SPAARKE_MSAL_CLIENT_ID__ for customer deployments.
 */

import type { Configuration } from '@azure/msal-browser';
import { LogLevel } from '@azure/msal-browser';

// ---------------------------------------------------------------------------
// Azure AD App Registration (environment-portable)
// ---------------------------------------------------------------------------

/**
 * Client App ID — resolved at runtime from Dataverse Environment Variables.
 * The bootstrap in main.tsx calls resolveRuntimeConfig() which sets
 * window.__SPAARKE_MSAL_CLIENT_ID__ before MSAL is initialized.
 */
import { getMsalClientId } from './runtimeConfig';

const CLIENT_ID: string = getMsalClientId();

/**
 * Redirect URI — auto-detected from current page origin.
 * Works for any Dataverse environment (spaarkedev1, staging, prod, customer).
 * The origin must be registered as a SPA redirect in the app registration.
 */
const REDIRECT_URI = window.location.origin;

// ---------------------------------------------------------------------------
// MSAL Configuration
// ---------------------------------------------------------------------------

export const msalConfig: Configuration = {
  auth: {
    clientId: CLIENT_ID,
    authority: 'https://login.microsoftonline.com/organizations',
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
