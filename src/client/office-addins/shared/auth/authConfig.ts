/**
 * NAA (Nested App Authentication) Configuration
 *
 * Configuration for MSAL.js 3.x with Nested App Authentication support.
 * NAA is the Microsoft-recommended authentication pattern for Office Add-ins.
 *
 * Per auth.md constraints:
 * - MUST use sessionStorage for tokens (not localStorage)
 * - MUST use scope format `api://{APP_ID}/user_impersonation` for BFF API
 * - MUST try silent token acquisition before popup/redirect
 *
 * @see https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in
 */

import type { Configuration, PopupRequest, SilentRequest } from '@azure/msal-browser';
import { LogLevel } from '@azure/msal-browser';

/**
 * Azure AD application configuration for the Office Add-in
 */
export interface NaaAuthConfig {
  /** Office Add-in client application ID */
  clientId: string;
  /** Azure AD tenant ID (or 'common' for multi-tenant) */
  tenantId: string;
  /** BFF API application ID (for user_impersonation scope) */
  bffApiClientId: string;
  /** BFF API base URL */
  bffApiBaseUrl: string;
  /** NAA redirect URI (brk-multihub://localhost for NAA broker) */
  redirectUri: string;
  /** Fallback redirect URI for Dialog API fallback */
  fallbackRedirectUri: string;
}

/**
 * Default configuration loaded from environment variables.
 * These are injected at build time via webpack DefinePlugin.
 */
export const DEFAULT_AUTH_CONFIG: NaaAuthConfig = {
  // Client ID from Azure AD app registration (Task 001)
  clientId: process.env.ADDIN_CLIENT_ID || 'c1258e2d-1688-49d2-ac99-a7485ebd9995',

  // Tenant ID for single-tenant app
  tenantId: process.env.TENANT_ID || 'a221a95e-6abc-4434-aecc-e48338a1b2f2',

  // BFF API client ID for user_impersonation scope
  bffApiClientId: process.env.BFF_API_CLIENT_ID || '1e40baad-e065-4aea-a8d4-4b7ab273458c',

  // BFF API base URL
  bffApiBaseUrl: process.env.BFF_API_BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',

  // NAA broker redirect URI (required for Nested App Authentication)
  redirectUri: 'brk-multihub://localhost',

  // Fallback redirect URI for Dialog API (when NAA is not supported)
  fallbackRedirectUri: process.env.FALLBACK_REDIRECT_URI || '',
};

/**
 * MSAL configuration for Nested App Authentication (NAA).
 *
 * Uses createNestablePublicClientApplication() factory method.
 *
 * @param config - Optional override configuration
 * @returns MSAL Configuration object for NAA
 */
export function createNaaMsalConfig(config: Partial<NaaAuthConfig> = {}): Configuration {
  const mergedConfig = { ...DEFAULT_AUTH_CONFIG, ...config };

  return {
    auth: {
      clientId: mergedConfig.clientId,
      authority: `https://login.microsoftonline.com/${mergedConfig.tenantId}`,
      // NAA requires the supportsNestedAppAuth flag
      supportsNestedAppAuth: true,
      // NAA uses broker redirect - do not navigate away
      navigateToLoginRequestUrl: false,
    },
    cache: {
      // MUST use sessionStorage per auth.md constraints
      // sessionStorage is cleared on tab close, providing better security
      cacheLocation: 'sessionStorage',
      // Cookies are not needed for Office Add-ins running in iframe
      storeAuthStateInCookie: false,
    },
    system: {
      loggerOptions: {
        loggerCallback: (level, message, containsPii) => {
          // Never log PII (Personally Identifiable Information)
          if (containsPii) return;

          switch (level) {
            case LogLevel.Error:
              console.error(`[NAA Auth] ${message}`);
              break;
            case LogLevel.Warning:
              console.warn(`[NAA Auth] ${message}`);
              break;
            case LogLevel.Info:
              // Only log info in development
              if (process.env.NODE_ENV === 'development') {
                console.info(`[NAA Auth] ${message}`);
              }
              break;
            case LogLevel.Verbose:
              // Only log verbose in development with debug flag
              if (process.env.NODE_ENV === 'development' && process.env.AUTH_DEBUG) {
                console.debug(`[NAA Auth] ${message}`);
              }
              break;
          }
        },
        logLevel: process.env.NODE_ENV === 'development' ? LogLevel.Warning : LogLevel.Error,
        piiLoggingEnabled: false,
      },
      // Allow popup and redirect handling
      allowRedirectInIframe: true,
    },
  };
}

/**
 * MSAL configuration for fallback authentication (Dialog API).
 *
 * Used when NAA is not supported by the Office host.
 *
 * @param config - Optional override configuration
 * @returns MSAL Configuration object for standard authentication
 */
export function createFallbackMsalConfig(config: Partial<NaaAuthConfig> = {}): Configuration {
  const mergedConfig = { ...DEFAULT_AUTH_CONFIG, ...config };

  return {
    auth: {
      clientId: mergedConfig.clientId,
      authority: `https://login.microsoftonline.com/${mergedConfig.tenantId}`,
      redirectUri: mergedConfig.fallbackRedirectUri || `${window.location.origin}/auth-callback.html`,
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
          if (level === LogLevel.Error) {
            console.error(`[Auth Fallback] ${message}`);
          }
        },
        logLevel: LogLevel.Error,
        piiLoggingEnabled: false,
      },
    },
  };
}

/**
 * Get the BFF API scopes for token acquisition.
 *
 * Per auth.md: MUST use scope format `api://{APP_ID}/user_impersonation`
 *
 * @param config - Optional override configuration
 * @returns Array of scopes to request
 */
export function getBffApiScopes(config: Partial<NaaAuthConfig> = {}): string[] {
  const bffApiClientId = config.bffApiClientId || DEFAULT_AUTH_CONFIG.bffApiClientId;
  return [`api://${bffApiClientId}/user_impersonation`];
}

/**
 * Create a silent token request.
 *
 * @param account - The account to acquire token for
 * @param config - Optional override configuration
 * @returns SilentRequest for acquireTokenSilent
 */
export function createSilentRequest(
  account: { homeAccountId: string; environment: string; tenantId: string; username: string; localAccountId: string; name?: string; idTokenClaims?: object; nativeAccountId?: string },
  config: Partial<NaaAuthConfig> = {}
): SilentRequest {
  return {
    scopes: getBffApiScopes(config),
    account,
    forceRefresh: false,
  };
}

/**
 * Create a popup token request.
 *
 * @param loginHint - Optional login hint (username) for the popup
 * @param config - Optional override configuration
 * @returns PopupRequest for acquireTokenPopup
 */
export function createPopupRequest(loginHint?: string, config: Partial<NaaAuthConfig> = {}): PopupRequest {
  return {
    scopes: getBffApiScopes(config),
    loginHint,
    prompt: 'select_account',
  };
}

/**
 * Token cache key prefix for manual caching operations.
 */
export const TOKEN_CACHE_PREFIX = 'spaarke_naa_';

/**
 * Token expiry buffer in seconds (5 minutes).
 * Tokens are considered expired this many seconds before actual expiry.
 */
export const TOKEN_EXPIRY_BUFFER_SECONDS = 300;
