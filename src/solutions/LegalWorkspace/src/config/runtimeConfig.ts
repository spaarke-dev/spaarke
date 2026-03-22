/**
 * runtimeConfig.ts
 * Runtime configuration singleton for the Legal Operations Workspace.
 *
 * Replaces the old bffConfig.ts which used build-time .env.production values
 * and a hardcoded ENVIRONMENT_BFF_MAP. Config is now resolved at bootstrap
 * from Dataverse Environment Variables via resolveRuntimeConfig() from
 * @spaarke/auth.
 *
 * Usage:
 *   1. Call initRuntimeConfig() once at bootstrap (main.tsx) before rendering
 *   2. Import getBffBaseUrl / getBffOAuthScope / getMsalClientId anywhere
 *
 * @see resolveRuntimeConfig in @spaarke/auth
 */

import type { IRuntimeConfig } from '@spaarke/auth';
import { resolveTenantIdSync } from '@spaarke/auth';

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
  if (typeof window !== 'undefined') {
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
      '[LegalWorkspace] Runtime config not initialized. ' +
      'Call initRuntimeConfig() in main.tsx before using getBffBaseUrl().'
    );
  }
  return _config;
}

// ---------------------------------------------------------------------------
// Public accessors (drop-in replacements for old bffConfig.ts exports)
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
 * Example: "api://1e40baad-.../user_impersonation"
 *
 * Replaces the old hardcoded BFF_API_SCOPE constant.
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
 * Azure AD tenant ID for use in URL construction (e.g. DocumentRelationshipViewer).
 *
 * Primary: value captured from Xrm at bootstrap by resolveRuntimeConfig().
 * Fallback: resolveTenantIdSync() from @spaarke/auth — MSAL authority first,
 * then Xrm frame-walk. This handles the case where bootstrap ran before
 * Xrm.organizationSettings was populated (intermittent timing issue).
 */
export function getTenantId(): string {
  const stored = getConfig().tenantId;
  if (stored) return stored;

  // Lazy resolution: use the shared utility which tries MSAL authority first.
  const resolved = resolveTenantIdSync();
  if (resolved && _config) {
    // Cache so subsequent calls are instant.
    _config = { ..._config, tenantId: resolved };
  }
  return resolved;
}
