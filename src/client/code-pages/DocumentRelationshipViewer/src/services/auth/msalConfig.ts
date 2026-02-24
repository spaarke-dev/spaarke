import { Configuration, LogLevel } from "@azure/msal-browser";

/**
 * MSAL Configuration for DocumentRelationshipViewer Code Page
 *
 * Same app registration as the PCF version.
 * Redirect URI points to the Dataverse environment where the web resource is hosted.
 */

const CLIENT_ID = "170c98e1-d486-4355-bcbe-170454e0207c";
const TENANT_ID = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
const REDIRECT_URI = "https://spaarkedev1.crm.dynamics.com";

export const msalConfig: Configuration = {
    auth: {
        clientId: CLIENT_ID,
        authority: `https://login.microsoftonline.com/${TENANT_ID}`,
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
                    case LogLevel.Error: console.error(`[MSAL] ${message}`); break;
                    case LogLevel.Warning: console.warn(`[MSAL] ${message}`); break;
                    case LogLevel.Info: console.info(`[MSAL] ${message}`); break;
                    case LogLevel.Verbose: console.debug(`[MSAL] ${message}`); break;
                }
            },
            logLevel: LogLevel.Warning,
        },
    },
};

export const loginRequest = {
    scopes: ["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"],
    loginHint: undefined as string | undefined,
};

export function validateMsalConfig(): void {
    const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!CLIENT_ID || !guidRegex.test(CLIENT_ID)) {
        throw new Error(`[MSAL Config] Invalid CLIENT_ID: "${CLIENT_ID}"`);
    }
    if (!TENANT_ID || !guidRegex.test(TENANT_ID)) {
        throw new Error(`[MSAL Config] Invalid TENANT_ID: "${TENANT_ID}"`);
    }
    if (!REDIRECT_URI.startsWith("https://") || !REDIRECT_URI.includes(".dynamics.com")) {
        throw new Error(`[MSAL Config] Invalid REDIRECT_URI: "${REDIRECT_URI}"`);
    }
}
