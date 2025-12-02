/**
 * Authentication Types for Universal Dataset Grid
 *
 * These types define the authentication provider contract and related data structures
 * for MSAL.js integration in the PCF control.
 *
 * ADR Compliance:
 * - ADR-007: Simple interfaces, no over-abstraction
 * - ADR-002: Client-side only (no plugin code)
 */

/**
 * Access token with metadata
 *
 * Represents an OAuth 2.0 access token acquired from Azure AD via MSAL.js
 */
export interface AuthToken {
  /**
   * JWT access token string
   * Used in Authorization: Bearer <accessToken> header
   */
  accessToken: string;

  /**
   * Token expiration timestamp
   * Check this before reusing cached token
   */
  expiresOn: Date;

  /**
   * OAuth scopes granted for this token
   * Example: ["api://spe-bff-api/user_impersonation"]
   */
  scopes: string[];

  /**
   * Account username (email) associated with token
   * Optional - for debugging/logging only
   */
  account?: string;
}

/**
 * Authentication error information
 *
 * Provides structured error details when token acquisition fails
 */
export interface AuthError {
  /**
   * Error code from MSAL
   * Examples: "interaction_required", "consent_required", "invalid_grant"
   */
  errorCode: string;

  /**
   * Human-readable error message
   * Safe to display in UI (no PII)
   */
  errorMessage: string;

  /**
   * Whether error requires user interaction (popup login)
   * If true, fallback to acquireTokenPopup()
   */
  requiresInteraction: boolean;

  /**
   * Original error object from MSAL
   * For logging/debugging only - do not display to user
   */
  originalError?: unknown;
}

/**
 * Authentication provider interface
 *
 * Contract for authentication providers (currently only MSAL.js)
 * Follows ADR-007 principle: introduce interface when seam is needed
 *
 * Implementation: MsalAuthProvider (Task 1.4)
 */
export interface IAuthProvider {
  /**
   * Initialize authentication provider
   *
   * Must be called once during PCF control initialization before any other methods.
   * Handles MSAL PublicClientApplication setup and redirect response processing.
   *
   * @throws Error if configuration invalid or initialization fails
   */
  initialize(): Promise<void>;

  /**
   * Get access token for specified OAuth scopes
   *
   * Attempts silent token acquisition first (SSO), falls back to popup if needed.
   * Caches tokens in sessionStorage for performance.
   *
   * @param scopes - OAuth scopes to request (e.g., ["api://spe-bff-api/user_impersonation"])
   * @returns Access token string for use in Authorization header
   * @throws Error if token acquisition fails after all retry attempts
   */
  getToken(scopes: string[]): Promise<string>;

  /**
   * Clear token cache and sign out
   *
   * Removes cached tokens from MSAL and sessionStorage.
   * User will need to re-authenticate on next getToken() call.
   */
  clearCache(): void;

  /**
   * Check if user is authenticated
   *
   * @returns true if user has active account, false otherwise
   */
  isAuthenticated(): boolean;
}

/**
 * Token cache entry (sessionStorage)
 *
 * Internal type for caching tokens in sessionStorage.
 *
 * @internal
 */
export interface TokenCacheEntry {
  /**
   * Access token string
   */
  token: string;

  /**
   * Expiration timestamp (Unix epoch milliseconds)
   * Check: Date.now() < expiresAt
   */
  expiresAt: number;

  /**
   * OAuth scopes for this token
   * Used to match cache entries to requested scopes
   */
  scopes: string[];
}

/**
 * MSAL configuration options
 *
 * Extends MSAL Configuration with SDAP-specific defaults.
 * Used in msalConfig.ts (Task 1.3)
 */
export interface MsalConfigOptions {
  /**
   * Azure AD application (client) ID
   * From Azure Portal → App Registration
   */
  clientId: string;

  /**
   * Azure AD tenant ID
   * From Azure Portal → App Registration
   */
  tenantId: string;

  /**
   * Redirect URI after authentication
   * Must match Azure Portal App Registration → Authentication → Redirect URIs
   * Example: "https://org12345.crm.dynamics.com"
   */
  redirectUri: string;

  /**
   * OAuth scopes to request
   * Example: ["api://spe-bff-api/user_impersonation"]
   */
  scopes: string[];

  /**
   * Enable verbose MSAL logging
   * Default: false (only warnings/errors)
   */
  enableVerboseLogging?: boolean;
}
