/**
 * runtimeConfig.ts
 * Runtime configuration singleton for the Reporting Code Page.
 *
 * Config is resolved at bootstrap from Dataverse Environment Variables
 * via resolveRuntimeConfig() from @spaarke/auth.
 *
 * Usage:
 *   1. Call setRuntimeConfig() once at bootstrap (main.tsx) before rendering
 *   2. Import getBffBaseUrl / getBffOAuthScope / getMsalClientId anywhere
 *
 * MUST NOT use module-level constants that call these getters — they will
 * throw "Runtime config not initialized" before bootstrap completes.
 *
 * @see .claude/patterns/auth/spaarke-auth-initialization.md
 * @see ADR-006 - Code Pages for standalone surfaces
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
      "[Reporting] Runtime config not initialized. " +
        "Call setRuntimeConfig() in main.tsx before using getBffBaseUrl()."
    );
  }
  return _config;
}

// ---------------------------------------------------------------------------
// Public accessors — MUST use lazy functions, not module-level constants
// ---------------------------------------------------------------------------

/**
 * BFF API base URL resolved from Dataverse Environment Variables at runtime.
 * Example: "https://spe-api-dev-67e2xz.azurewebsites.net/api"
 */
export function getBffBaseUrl(): string {
  return getConfig().bffBaseUrl;
}

/**
 * BFF API OAuth scope resolved from Dataverse Environment Variables at runtime.
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
