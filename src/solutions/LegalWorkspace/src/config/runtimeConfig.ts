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
 * Azure AD tenant ID resolved from Xrm organizationSettings.
 *
 * Primary: uses the value captured at bootstrap by resolveRuntimeConfig().
 * Fallback: if bootstrap ran before Xrm had organizationSettings populated
 * (intermittent timing issue), walks the frame hierarchy to obtain the value
 * on demand and caches it for subsequent calls.
 */
export function getTenantId(): string {
  const stored = getConfig().tenantId;
  if (stored) return stored;

  // Bootstrap captured an empty tenantId — Xrm.organizationSettings may not have
  // been ready yet. Try to read it synchronously now (safe to call from click handlers).
  if (typeof window !== 'undefined') {
    const frames: Window[] = [window];
    try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

    for (const frame of frames) {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const tenantId = (frame as any).Xrm?.Utility?.getGlobalContext?.()?.organizationSettings?.tenantId as string | undefined;
        if (tenantId) {
          // Cache it so subsequent calls don't need to walk frames again.
          if (_config) _config = { ..._config, tenantId };
          return tenantId;
        }
      } catch { /* cross-origin */ }
    }
  }

  return '';
}
