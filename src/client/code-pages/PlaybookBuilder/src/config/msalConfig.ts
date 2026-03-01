/**
 * MSAL Browser configuration for the PlaybookBuilder Code Page.
 *
 * Reuses the same Azure AD app registration as AnalysisWorkspace
 * (CLIENT_ID = "DSM-SPE Dev 2").
 *
 * @see ADR-008 - Endpoint filters for auth
 */

import type { Configuration } from "@azure/msal-browser";
import { LogLevel } from "@azure/msal-browser";

/* eslint-disable @typescript-eslint/no-explicit-any */
const CLIENT_ID: string =
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ ||
    "170c98e1-d486-4355-bcbe-170454e0207c";
/* eslint-enable @typescript-eslint/no-explicit-any */

const REDIRECT_URI = window.location.origin;

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

export const BFF_API_SCOPE =
    "api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation";
