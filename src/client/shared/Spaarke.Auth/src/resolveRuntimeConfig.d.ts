/**
 * Runtime configuration resolution for Spaarke code pages and web resources.
 *
 * Resolves BFF API base URL, OAuth scope, and MSAL client ID from Dataverse
 * Environment Variables at runtime. This replaces build-time .env.production
 * resolution and per-code-page bffConfig.ts files.
 *
 * Resolution strategy (chicken-and-egg problem):
 *   1. Get org URL from Xrm.Utility.getGlobalContext().getClientUrl()
 *   2. Get MSAL client ID from window global __SPAARKE_MSAL_CLIENT_ID__
 *      or Dataverse environment variable (sprk_MsalClientId)
 *   3. Query Dataverse REST API for environment variables:
 *      - sprk_BffApiBaseUrl  (BFF API base URL)
 *      - sprk_BffApiAppId    (BFF API app registration ID, for OAuth scope)
 *      - sprk_MsalClientId   (MSAL client ID, if not already resolved)
 *   4. Cache results for 5 minutes
 *
 * Pre-auth bootstrap: The Dataverse REST API is accessible without a separate
 * MSAL token when running inside a Dataverse web resource — the browser session
 * cookie provides authentication. This means we can query environment variables
 * before MSAL initialization.
 *
 * @module resolveRuntimeConfig
 */
/** Resolved runtime configuration for Spaarke applications. */
export interface IRuntimeConfig {
  /**
   * BFF API base URL (host only, WITHOUT /api suffix).
   * Example: "https://spe-api-dev-67e2xz.azurewebsites.net"
   *
   * IMPORTANT: normalizeUrl() strips /api from the environment variable value.
   * All client-side URL paths MUST include the /api/ prefix themselves.
   * e.g., fetch(`${bffBaseUrl}/api/ai/chat/sessions`)
   */
  bffBaseUrl: string;
  /** BFF API OAuth scope (e.g., "api://<app-id>/user_impersonation"). */
  bffOAuthScope: string;
  /** Azure AD client ID for MSAL authentication. */
  msalClientId: string;
  /** Azure AD tenant ID (from Xrm organizationSettings). Empty string if not available. */
  tenantId: string;
}
/** Clear the runtime config cache. Useful for testing or after env var updates. */
export declare function clearRuntimeConfigCache(): void;
/**
 * Resolve runtime configuration from Dataverse Environment Variables.
 *
 * This function is the canonical way for code pages and web resources to
 * obtain BFF API URL, OAuth scope, and MSAL client ID at runtime.
 * It replaces all per-code-page bffConfig.ts files and build-time
 * .env.production resolution.
 *
 * Results are cached for 5 minutes.
 *
 * @throws {Error} If Xrm context is not available (not running in Dataverse)
 * @throws {Error} If required environment variables are not configured
 * @returns Resolved runtime configuration
 *
 * @example
 * ```typescript
 * import { resolveRuntimeConfig } from '@spaarke/auth';
 *
 * const config = await resolveRuntimeConfig();
 * // config.bffBaseUrl  → "https://spe-api-dev-67e2xz.azurewebsites.net" (host only, no /api)
 * // config.bffOAuthScope → "api://1e40baad-.../user_impersonation"
 * // config.msalClientId  → "170c98e1-..."
 * ```
 */
export declare function resolveRuntimeConfig(): Promise<IRuntimeConfig>;
//# sourceMappingURL=resolveRuntimeConfig.d.ts.map
