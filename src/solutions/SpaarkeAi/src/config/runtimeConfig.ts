/**
 * runtimeConfig.ts
 * Runtime configuration singleton for the SpaarkeAi Code Page.
 *
 * Config is resolved at bootstrap from Dataverse Environment Variables
 * via resolveRuntimeConfig() from @spaarke/auth. Never use module-level
 * constants that call these getters — they will throw before bootstrap.
 *
 * Usage:
 *   1. Call setRuntimeConfig() once at bootstrap (main.tsx) after resolveRuntimeConfig()
 *   2. Import getBffBaseUrl / getBffOAuthScope / getMsalClientId anywhere via lazy functions
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see resolveRuntimeConfig in @spaarke/auth
 */

import type { IRuntimeConfig } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Singleton
// ---------------------------------------------------------------------------

let _config: IRuntimeConfig | null = null;

/**
 * Store the resolved runtime config. Called once from main.tsx bootstrap.
 * Also sets window globals so that @spaarke/auth resolveConfig() can find them.
 */
export function setRuntimeConfig(config: IRuntimeConfig): void {
  _config = config;

  // Set window globals for @spaarke/auth resolveConfig() and MSAL
  if (typeof window !== "undefined") {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_BFF_BASE_URL__ = config.bffBaseUrl;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ = config.msalClientId;
  }
}

/**
 * Get the resolved runtime config. Throws if not initialized.
 */
function getConfig(): IRuntimeConfig {
  if (!_config) {
    throw new Error(
      "[SpaarkeAi] Runtime config not initialized. " +
        "Call setRuntimeConfig() in main.tsx before using config getters."
    );
  }
  return _config;
}

// ---------------------------------------------------------------------------
// Public accessors — always lazy functions (never module-level constants)
// ---------------------------------------------------------------------------

/**
 * BFF API base URL resolved from Dataverse Environment Variables at runtime.
 * Returns HOST ONLY — the /api suffix is stripped by normalizeUrl() in @spaarke/auth.
 * Example: "https://spe-api-dev-67e2xz.azurewebsites.net"
 */
export function getBffBaseUrl(): string {
  return getConfig().bffBaseUrl;
}

/**
 * BFF API OAuth scope resolved from Dataverse Environment Variables at runtime.
 * Example: "api://1e40baad-.../user_impersonation"
 */
export function getBffOAuthScope(): string {
  return getConfig().bffOAuthScope;
}

/**
 * MSAL client ID resolved from Dataverse Environment Variables at runtime.
 */
export function getMsalClientId(): string {
  return getConfig().msalClientId;
}

/**
 * Azure AD tenant ID captured at bootstrap from Xrm.organizationSettings.
 */
export function getTenantId(): string {
  return getConfig().tenantId ?? "";
}
