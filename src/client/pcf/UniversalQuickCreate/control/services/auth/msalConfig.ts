import { Configuration, LogLevel } from '@azure/msal-browser';

/**
 * MSAL Configuration for Universal Document Upload PCF Control
 *
 * Runtime-resolved configuration -- no hardcoded CLIENT_ID, TENANT_ID, or BFF URL.
 * Values are resolved from Dataverse environment variables at runtime via
 * the shared environmentVariables.ts utility.
 *
 * ADR Compliance:
 * - ADR-002: Client-side authentication (no plugins)
 * - ADR-010: Configuration Over Code -- no hardcoded env-specific values
 *
 * @see https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-js-initializing-client-applications
 */

// ============================================================================
// Runtime Configuration Interface
// ============================================================================

/**
 * Runtime MSAL configuration resolved from Dataverse environment variables.
 *
 * These values are loaded at control initialization via environmentVariables.ts
 * and passed to createMsalConfig() / createLoginRequest().
 */
export interface RuntimeMsalConfig {
  /** Azure AD Application (Client) ID -- from Dataverse env var sprk_MsalClientId */
  clientId: string;

  /** Dataverse environment URL used as redirect URI (e.g., https://org.crm.dynamics.com) */
  redirectUri: string;

  /** BFF API Application ID URI for OAuth scope (e.g., api://<app-id>) */
  bffApiAppIdUri: string;
}

// ============================================================================
// MSAL Configuration Factory
// ============================================================================

/**
 * Create MSAL PublicClientApplication configuration from runtime values.
 *
 * Uses 'organizations' authority (multi-tenant) so no tenant ID is hardcoded.
 * The redirect URI is derived from the Dataverse environment URL at runtime.
 *
 * @param config - Runtime configuration resolved from Dataverse environment variables
 * @returns MSAL Configuration object
 */
export function createMsalConfig(config: RuntimeMsalConfig): Configuration {
  return {
    auth: {
      clientId: config.clientId,
      authority: 'https://login.microsoftonline.com/organizations',
      redirectUri: config.redirectUri,
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
}

// ============================================================================
// Login Request Factory
// ============================================================================

/**
 * Create OAuth login request with BFF API scope resolved at runtime.
 *
 * @param config - Runtime configuration with BFF API app ID URI
 * @returns Login request object with scopes array
 */
export function createLoginRequest(config: RuntimeMsalConfig): { scopes: string[]; loginHint: string | undefined } {
  return {
    scopes: [`${config.bffApiAppIdUri}/user_impersonation`],
    loginHint: undefined,
  };
}

// ============================================================================
// Configuration Validation
// ============================================================================

/**
 * Validate runtime MSAL configuration before initialization.
 *
 * Checks that all required values are present and correctly formatted.
 * Throws descriptive error if configuration is invalid.
 *
 * @param config - Runtime configuration to validate
 * @throws Error if any required value is missing or malformed
 */
export function validateRuntimeMsalConfig(config: RuntimeMsalConfig): void {
  if (!config.clientId) {
    throw new Error(
      '[MSAL Config] clientId not resolved. ' +
        'Ensure sprk_MsalClientId is defined as a Dataverse environment variable.'
    );
  }

  // Validate GUID format for clientId
  const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!guidRegex.test(config.clientId)) {
    throw new Error(
      `[MSAL Config] clientId has invalid GUID format: "${config.clientId}". ` +
        'Expected format: 12345678-1234-1234-1234-123456789abc'
    );
  }

  if (!config.redirectUri) {
    throw new Error(
      '[MSAL Config] redirectUri not resolved. ' +
        'Ensure the Dataverse environment URL is available from Xrm context.'
    );
  }

  if (!config.redirectUri.startsWith('https://')) {
    throw new Error(
      `[MSAL Config] redirectUri must use HTTPS: "${config.redirectUri}".`
    );
  }

  if (!config.bffApiAppIdUri) {
    throw new Error(
      '[MSAL Config] bffApiAppIdUri not resolved. ' +
        'Ensure sprk_BffApiAppId is defined as a Dataverse environment variable.'
    );
  }

  console.info('[MSAL Config] Runtime configuration validation passed');
}

/**
 * Get sanitized configuration for debugging (masks sensitive values).
 *
 * @param config - Runtime configuration to sanitize
 * @returns Sanitized configuration object safe to log
 */
export function getMsalConfigDebugInfo(config: RuntimeMsalConfig): Record<string, unknown> {
  return {
    clientId: config.clientId ? config.clientId.substring(0, 8) + '...' : '(not set)',
    redirectUri: config.redirectUri,
    bffApiAppIdUri: config.bffApiAppIdUri,
    authority: 'https://login.microsoftonline.com/organizations',
    cacheLocation: 'sessionStorage',
    scopes: config.bffApiAppIdUri ? [`${config.bffApiAppIdUri}/user_impersonation`] : '(not set)',
  };
}
