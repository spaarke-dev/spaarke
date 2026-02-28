/**
 * MSAL Browser configuration for the AnalysisWorkspace Code Page.
 *
 * Reuses the same Azure AD app registration as the LegalWorkspace and
 * PCF controls (CLIENT_ID = "DSM-SPE Dev 2").
 *
 * Environment portability:
 *   - Redirect URI: auto-detected from window.location.origin (works in any
 *     Dataverse environment as long as the origin is registered as a SPA
 *     redirect in the app registration).
 *   - Authority: uses "organizations" endpoint (multi-tenant) so the same
 *     client ID works across Spaarke environments and customer tenants.
 *   - Client ID: defaults to the Spaarke "DSM-SPE Dev 2" app registration.
 *     Override via window.__SPAARKE_MSAL_CLIENT_ID__ for customer deployments.
 *
 * Pattern copied from: src/solutions/LegalWorkspace/src/config/msalConfig.ts
 *
 * @see ADR-008 - Endpoint filters for auth
 * @see docs/architecture/sdap-auth-patterns.md - Pattern 7: Code Page Embedded Auth
 */

import type { Configuration } from "@azure/msal-browser";
import { LogLevel } from "@azure/msal-browser";

// ---------------------------------------------------------------------------
// Azure AD App Registration (environment-portable)
// ---------------------------------------------------------------------------

/**
 * Client App ID — defaults to "Sparke DSM-SPE Dev 2" app registration.
 * Override via window global for customer tenant deployments.
 */
/* eslint-disable @typescript-eslint/no-explicit-any */
const CLIENT_ID: string =
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ ||
    "170c98e1-d486-4355-bcbe-170454e0207c";
/* eslint-enable @typescript-eslint/no-explicit-any */

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
        authority: "https://login.microsoftonline.com/organizations",
        redirectUri: REDIRECT_URI,
        navigateToLoginRequestUrl: false,
    },
    cache: {
        cacheLocation: "sessionStorage",
        storeAuthStateInCookie: false,
    },
    system: {
        loggerOptions: {
            loggerCallback: (level, message, containsPii) => {
                if (containsPii) return;
                switch (level) {
                    case LogLevel.Error:
                        console.error(`[AnalysisWorkspace:MSAL] ${message}`);
                        break;
                    case LogLevel.Warning:
                        console.warn(`[AnalysisWorkspace:MSAL] ${message}`);
                        break;
                    case LogLevel.Info:
                        // Suppress info-level MSAL logs; set to console.info for debugging
                        break;
                    case LogLevel.Verbose:
                        break;
                }
            },
            logLevel: LogLevel.Warning,
        },
    },
};

// ---------------------------------------------------------------------------
// BFF API OAuth Scope
// ---------------------------------------------------------------------------

/**
 * OAuth scope for the BFF API. Used when acquiring tokens via MSAL.
 * This is the BFF API app registration's user_impersonation scope.
 */
export const BFF_API_SCOPE =
    "api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation";
