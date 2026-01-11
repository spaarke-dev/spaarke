import { Configuration, LogLevel } from "@azure/msal-browser";

/**
 * MSAL Configuration for DocumentRelationshipViewer PCF
 *
 * This configuration enables SSO silent authentication in Dataverse PCF controls,
 * allowing the control to acquire user tokens and call Spe.Bff.Api OBO endpoints.
 *
 * ADR Compliance:
 * - ADR-002: Client-side authentication (no plugins)
 * - ADR-007: Single configuration file (no abstraction layers)
 */

// ============================================================================
// Azure App Registration Configuration
// ============================================================================

/**
 * Azure AD Application (Client) ID
 *
 * From: Sparke DSM-SPE Dev 2 App Registration
 * This is the PCF CLIENT app, NOT the BFF API app
 */
const CLIENT_ID = "170c98e1-d486-4355-bcbe-170454e0207c";

/**
 * Azure AD Tenant (Directory) ID
 */
const TENANT_ID = "a221a95e-6abc-4434-aecc-e48338a1b2f2";

/**
 * Dataverse Environment Redirect URI
 */
const REDIRECT_URI = "https://spaarkedev1.crm.dynamics.com";

// ============================================================================
// MSAL Browser Configuration
// ============================================================================

/**
 * MSAL PublicClientApplication Configuration
 */
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
        if (containsPii) {
          return;
        }

        switch (level) {
          case LogLevel.Error:
            console.error(`[MSAL] ${message}`);
            break;
          case LogLevel.Warning:
            console.warn(`[MSAL] ${message}`);
            break;
          case LogLevel.Info:
            console.info(`[MSAL] ${message}`);
            break;
          case LogLevel.Verbose:
            console.debug(`[MSAL] ${message}`);
            break;
        }
      },
      logLevel: LogLevel.Warning,
    },
  },
};

// ============================================================================
// OAuth Scopes Configuration
// ============================================================================

/**
 * OAuth scopes for SSO Silent Token Acquisition
 *
 * SPE BFF API App Registration:
 * - Client ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
 * - Scope: user_impersonation
 */
export const loginRequest = {
  scopes: [
    "api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"
  ],
  loginHint: undefined as string | undefined,
};

// ============================================================================
// Configuration Validation
// ============================================================================

/**
 * Validate MSAL configuration before initialization
 */
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

  console.info("[MSAL Config] Configuration validation passed");
}
