import { Configuration, LogLevel } from "@azure/msal-browser";

/**
 * MSAL Configuration for SDAP Universal Dataset Grid
 *
 * This configuration enables SSO silent authentication in Dataverse PCF controls,
 * allowing the control to acquire user tokens and call Spe.Bff.Api OBO endpoints.
 *
 * ADR Compliance:
 * - ADR-002: Client-side authentication (no plugins)
 * - ADR-007: Single configuration file (no abstraction layers)
 *
 * Sprint 4 Integration:
 * - Acquires tokens for api://spe-bff-api/user_impersonation scope
 * - Tokens sent to Spe.Bff.Api OBO endpoints via Authorization header
 * - TokenHelper.ExtractBearerToken() validates header format
 *
 * @see https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-js-initializing-client-applications
 */

// ============================================================================
// Azure App Registration Configuration
// ============================================================================
/**
 * Azure AD Application (Client) ID
 *
 * From: Sparke DSM-SPE Dev 2 App Registration
 * Azure Portal → Azure Active Directory → App registrations → Sparke DSM-SPE Dev 2 → Overview
 *
 * IMPORTANT: This is the PCF CLIENT app (170c98e1...), NOT the BFF API app (1e40baad...)
 * The PCF client authenticates users and requests tokens for the BFF API scope
 */
const CLIENT_ID = "170c98e1-d486-4355-bcbe-170454e0207c";

/**
 * Azure AD Tenant (Directory) ID
 *
 * From: Sparke DSM-SPE Dev 2 App Registration
 * Azure Portal → Azure Active Directory → App registrations → Sparke DSM-SPE Dev 2 → Overview
 */
const TENANT_ID = "a221a95e-6abc-4434-aecc-e48338a1b2f2";

/**
 * Dataverse Environment Redirect URI
 *
 * Environment: SPAARKE DEV 1
 * Value: https://spaarkedev1.crm.dynamics.com
 *
 * Note: This MUST match a redirect URI configured in Azure App Registration → Authentication → Redirect URIs,
 *       otherwise authentication will fail with "redirect_uri_mismatch" error.
 *
 * Important: You may need to add this redirect URI to the Azure App Registration if not already present.
 */
const REDIRECT_URI = "https://spaarkedev1.crm.dynamics.com";

// ============================================================================
// MSAL Browser Configuration
// ============================================================================

/**
 * MSAL PublicClientApplication Configuration
 *
 * Configuration structure:
 * - auth: Azure AD authentication settings
 * - cache: Token cache settings (sessionStorage for PCF controls)
 * - system: Logging and telemetry settings
 *
 * @see https://azuread.github.io/microsoft-authentication-library-for-js/ref/msal-browser/interfaces/Configuration.html
 */
export const msalConfig: Configuration = {
  auth: {
    /**
     * Client ID from Azure App Registration
     * Identifies this application to Azure AD
     */
    clientId: CLIENT_ID,

    /**
     * Tenant-specific authority URL
     *
     * Format: https://login.microsoftonline.com/{tenantId}
     *
     * Why tenant-specific vs /common?
     * - Tenant-specific: Only allows users from specified tenant (more secure)
     * - /common: Allows any Azure AD user (not recommended for enterprise apps)
     *
     * SDAP uses tenant-specific for security.
     */
    authority: `https://login.microsoftonline.com/${TENANT_ID}`,

    /**
     * Redirect URI after authentication
     *
     * After user authenticates (popup or redirect), Azure AD redirects to this URI.
     * For PCF controls in Dataverse, this is the Dataverse environment URL.
     *
     * Important: Must match Azure App Registration → Authentication → Redirect URIs
     */
    redirectUri: REDIRECT_URI,

    /**
     * Whether to navigate to original request URL after authentication
     *
     * false: Stay on current page after login (recommended for PCF controls)
     * true: Navigate to loginRequestUrl (not needed for PCF)
     */
    navigateToLoginRequestUrl: false,
  },

  cache: {
    /**
     * Token cache location
     *
     * Options:
     * - "sessionStorage": Tokens cleared when browser tab closed (recommended for PCF)
     * - "localStorage": Tokens persist across browser sessions (less secure)
     * - "memoryStorage": Tokens only in memory (lost on page refresh)
     *
     * SDAP uses sessionStorage for security (tokens cleared on tab close).
     */
    cacheLocation: "sessionStorage",

    /**
     * Whether to store auth state in cookie
     *
     * false: Store in sessionStorage only (recommended for modern browsers)
     * true: Also store in cookie (needed for IE11, not supported in PCF)
     */
    storeAuthStateInCookie: false,
  },

  system: {
    /**
     * MSAL logging configuration
     *
     * Logs internal MSAL operations to browser console for debugging.
     */
    loggerOptions: {
      /**
       * Logger callback function
       *
       * @param level - Log level (Error, Warning, Info, Verbose)
       * @param message - Log message
       * @param containsPii - Whether message contains PII (personally identifiable information)
       */
      loggerCallback: (level, message, containsPii) => {
        // Don't log messages containing PII (email, name, etc.)
        if (containsPii) {
          return;
        }

        // Route to appropriate console method based on log level
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

      /**
       * Minimum log level to output
       *
       * Options:
       * - LogLevel.Error: Only errors
       * - LogLevel.Warning: Warnings and errors (recommended for production)
       * - LogLevel.Info: Info, warnings, and errors
       * - LogLevel.Verbose: All logs (use for debugging only)
       *
       * SDAP uses Warning for production (change to Verbose for debugging).
       */
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
 * Scopes define what permissions the token grants.
 *
 * Required scope for SDAP:
 * - api://spe-bff-api/user_impersonation: Access Spe.Bff.Api on behalf of user
 *
 * Why not User.Read?
 * - Spe.Bff.Api performs OBO flow to get Graph token for user
 * - PCF doesn't need direct Graph access (BFF handles it)
 * - Requesting only needed scope follows principle of least privilege
 *
 * Scope format:
 * - api://<application-id-or-name>/<permission-name>
 * - Example: api://12345678-1234-1234-1234-123456789abc/user_impersonation
 *   OR: api://spe-bff-api/user_impersonation (if friendly name configured)
 */
export const loginRequest = {
  /**
   * Scopes to request when acquiring token
   *
   * Array of scope strings. Multiple scopes can be requested:
   * scopes: ["api://app1/scope1", "api://app2/scope2"]
   *
   * SDAP requests only BFF API scope:
   * 1. Spe.Bff.Api access (user_impersonation) - ONLY scope needed by PCF control
   *
   * SPE BFF API App Registration:
   * - Client ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
   * - Application ID URI: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
   * - Scope: user_impersonation
   *
   * Why only one scope?
   * - PCF control authenticates to BFF API only
   * - BFF API handles Graph API access via On-Behalf-Of (OBO) flow
   * - BFF API uses .default scope to request all admin-consented permissions
   * - No need for PCF to request Graph API scopes directly
   *
   * OBO Flow (handled by BFF API):
   * 1. PCF sends user token (Token A) with user_impersonation scope
   * 2. BFF exchanges Token A for Graph token (Token B) using .default scope
   * 3. Token B has FileStorageContainer.Selected and Files.Read.All permissions
   * 4. BFF uses Token B to access SharePoint Embedded containers on behalf of user
   */
  scopes: [
    "api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"
  ],

  /**
   * Login hint (user email)
   *
   * When calling ssoSilent(), this can be set to user's email to skip account picker.
   * Set dynamically in MsalAuthProvider based on current Dataverse user.
   *
   * Example: "alice@contoso.com"
   */
  loginHint: undefined as string | undefined,
};

// ============================================================================
// Configuration Validation
// ============================================================================

/**
 * Validate MSAL configuration before initialization
 *
 * Checks that all required configuration values are set (not placeholder values).
 * Throws descriptive error if configuration invalid.
 *
 * Call this in MsalAuthProvider.initialize() to fail fast on misconfiguration.
 *
 * @throws Error if CLIENT_ID, TENANT_ID, or REDIRECT_URI contain placeholder values
 */
export function validateMsalConfig(): void {
  // Check CLIENT_ID
  if (!CLIENT_ID || CLIENT_ID.includes("YOUR_CLIENT_ID")) {
    throw new Error(
      "[MSAL Config] CLIENT_ID not set. " +
        "Update msalConfig.ts with actual Azure App Registration Client ID. " +
        "Find at: Azure Portal → App registrations → SDAP → Overview → Application (client) ID"
    );
  }

  // Check TENANT_ID
  if (!TENANT_ID || TENANT_ID.includes("YOUR_TENANT_ID")) {
    throw new Error(
      "[MSAL Config] TENANT_ID not set. " +
        "Update msalConfig.ts with actual Azure AD Tenant ID. " +
        "Find at: Azure Portal → App registrations → SDAP → Overview → Directory (tenant) ID"
    );
  }

  // Check REDIRECT_URI
  if (!REDIRECT_URI || REDIRECT_URI.includes("your-org")) {
    throw new Error(
      "[MSAL Config] REDIRECT_URI not set. " +
        "Update msalConfig.ts with actual Dataverse environment URL. " +
        "Format: https://<your-org>.crm.dynamics.com " +
        "Must match: Azure Portal → App registrations → SDAP → Authentication → Redirect URIs"
    );
  }

  // Validate GUID format for CLIENT_ID and TENANT_ID
  const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

  if (!guidRegex.test(CLIENT_ID)) {
    throw new Error(
      `[MSAL Config] CLIENT_ID has invalid GUID format: "${CLIENT_ID}". ` +
        "Expected format: 12345678-1234-1234-1234-123456789abc"
    );
  }

  if (!guidRegex.test(TENANT_ID)) {
    throw new Error(
      `[MSAL Config] TENANT_ID has invalid GUID format: "${TENANT_ID}". ` +
        "Expected format: 12345678-1234-1234-1234-123456789abc"
    );
  }

  // Validate REDIRECT_URI format
  if (!REDIRECT_URI.startsWith("https://") || !REDIRECT_URI.includes(".dynamics.com")) {
    throw new Error(
      `[MSAL Config] REDIRECT_URI has invalid format: "${REDIRECT_URI}". ` +
        "Expected format: https://<org>.crm.dynamics.com (or .crm2, .crm3, etc.)"
    );
  }

  console.info("[MSAL Config] Configuration validation passed ✅");
}

/**
 * Get current MSAL configuration (for debugging)
 *
 * Returns sanitized configuration with sensitive values masked.
 * Safe to log or display in UI.
 *
 * @returns Sanitized configuration object
 */
export function getMsalConfigDebugInfo(): Record<string, unknown> {
  return {
    clientId: CLIENT_ID.replace(/./g, "*").slice(0, 8) + "...", // Mask client ID
    tenantId: TENANT_ID.replace(/./g, "*").slice(0, 8) + "...", // Mask tenant ID
    redirectUri: REDIRECT_URI,
    authority: msalConfig.auth.authority,
    cacheLocation: msalConfig.cache?.cacheLocation,
    scopes: loginRequest.scopes,
  };
}
