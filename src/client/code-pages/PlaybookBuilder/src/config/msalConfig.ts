/**
 * @deprecated Use @spaarke/auth (via authInit.ts) instead. This file is scheduled for removal.
 * MSAL configuration is now handled internally by @spaarke/auth.
 */

/**
 * MSAL Browser configuration for the PlaybookBuilder Code Page.
 *
 * Reuses the same Azure AD app registration as AnalysisWorkspace
 * (CLIENT_ID = "DSM-SPE Dev 2").
 *
 * @see ADR-008 - Endpoint filters for auth
 */

import type { Configuration } from '@azure/msal-browser';
import { LogLevel } from '@azure/msal-browser';

const CLIENT_ID: string = window.__SPAARKE_MSAL_CLIENT_ID__
  || (() => { throw new Error('[Spaarke] MSAL client ID not configured. Window global __SPAARKE_MSAL_CLIENT_ID__ must be set by resolveRuntimeConfig() before this module loads.'); })();

const REDIRECT_URI = window.location.origin;

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
            console.error(`[PlaybookBuilder:MSAL] ${message}`);
            break;
          case LogLevel.Warning:
            console.warn(`[PlaybookBuilder:MSAL] ${message}`);
            break;
        }
      },
      logLevel: LogLevel.Warning,
    },
  },
};

export const BFF_API_SCOPE = 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation';
