/**
 * msalConfig.ts
 * MSAL Browser configuration for the Legal Operations Workspace custom page.
 *
 * IMPORTANT: Configuration is lazy (function, not module-level const) because
 * this module may be imported during Vite bundle evaluation before
 * setRuntimeConfig() runs in the async main.tsx bootstrap.
 *
 * The preferred auth path is @spaarke/auth via authInit.ts. This module is
 * only used by the legacy bffAuthProvider.ts (retained for fallback).
 *
 * Environment portability:
 *   - Redirect URI: auto-detected from window.location.origin
 *   - Authority: "organizations" endpoint (multi-tenant)
 *   - Client ID: resolved at runtime from Dataverse Environment Variables
 */

import type { Configuration } from '@azure/msal-browser';
import { LogLevel } from '@azure/msal-browser';
import { getMsalClientId } from './runtimeConfig';

const REDIRECT_URI = window.location.origin;

/**
 * Returns MSAL config lazily. Must be called AFTER setRuntimeConfig() in bootstrap.
 * Replaces the former module-level `const msalConfig` which threw during bundle
 * evaluation when imported before runtime config was initialized.
 */
export function getMsalConfig(): Configuration {
  return {
    auth: {
      clientId: getMsalClientId(),
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
              break;
            case LogLevel.Verbose:
              break;
          }
        },
        logLevel: LogLevel.Warning,
      },
    },
  };
}

/**
 * Legacy export — consumed by bffAuthProvider.ts which calls
 * `new PublicClientApplication(msalConfig)` lazily inside ensureMsalInitialized().
 * The Proxy defers getMsalClientId() until property access (after bootstrap).
 */
export const msalConfig: Configuration = new Proxy({} as Configuration, {
  get(_target, prop) {
    return (getMsalConfig() as Record<string | symbol, unknown>)[prop];
  },
});
