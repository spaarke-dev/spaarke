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

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

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

/** Environment variable schema names queried from Dataverse. */
const ENV_VAR_NAMES = {
  BFF_BASE_URL: 'sprk_BffApiBaseUrl',
  BFF_APP_ID: 'sprk_BffApiAppId',
  MSAL_CLIENT_ID: 'sprk_MsalClientId',
} as const;

// ---------------------------------------------------------------------------
// Cache
// ---------------------------------------------------------------------------

/** Cache duration: 5 minutes. */
const CACHE_DURATION_MS = 5 * 60 * 1000;

let cachedConfig: IRuntimeConfig | null = null;
let cacheTimestamp = 0;

function isCacheValid(): boolean {
  return cachedConfig !== null && Date.now() - cacheTimestamp < CACHE_DURATION_MS;
}

/** Clear the runtime config cache. Useful for testing or after env var updates. */
export function clearRuntimeConfigCache(): void {
  cachedConfig = null;
  cacheTimestamp = 0;
}

// ---------------------------------------------------------------------------
// localStorage persistence (for full-screen / new-window scenarios)
// ---------------------------------------------------------------------------

const LS_KEY = '__spaarke_rtc__';
/** TTL for localStorage cache: 60 minutes */
const LS_TTL_MS = 60 * 60 * 1000;

interface IPersistedConfig extends IRuntimeConfig {
  _ts: number;
}

function saveToLocalStorage(config: IRuntimeConfig): void {
  try {
    const entry: IPersistedConfig = { ...config, _ts: Date.now() };
    localStorage.setItem(LS_KEY, JSON.stringify(entry));
  } catch {
    /* localStorage may not be available (private browsing, storage full, etc.) */
  }
}

function loadFromLocalStorage(): IRuntimeConfig | null {
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (!raw) return null;
    const entry = JSON.parse(raw) as Partial<IPersistedConfig>;
    if (!entry._ts || Date.now() - entry._ts > LS_TTL_MS) return null;
    if (!entry.bffBaseUrl || !entry.bffOAuthScope || !entry.msalClientId) return null;
    return {
      bffBaseUrl: entry.bffBaseUrl,
      bffOAuthScope: entry.bffOAuthScope,
      msalClientId: entry.msalClientId,
      tenantId: entry.tenantId ?? '',
    };
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Xrm context resolution
// ---------------------------------------------------------------------------

/** Minimal Xrm type for getGlobalContext — available in all Dataverse web resources. */
interface XrmGlobalContext {
  getClientUrl: () => string;
  organizationSettings?: {
    tenantId?: string;
  };
}

/**
 * Resolve Xrm.Utility.getGlobalContext() by walking the frame hierarchy.
 * Available pre-auth in all Dataverse web resources (no MSAL needed).
 */
function resolveXrmContext(): XrmGlobalContext | null {
  if (typeof window === 'undefined') return null;

  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window && window.top !== window.parent) {
      frames.push(window.top);
    }
  } catch {
    /* cross-origin */
  }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      const ctx = xrm?.Utility?.getGlobalContext?.();
      if (ctx && typeof ctx.getClientUrl === 'function') {
        return ctx as XrmGlobalContext;
      }
    } catch {
      /* cross-origin */
    }
  }

  return null;
}

// ---------------------------------------------------------------------------
// Dataverse REST API query
// ---------------------------------------------------------------------------

/**
 * Query Dataverse environment variables using the session cookie (no bearer token).
 * This works in Dataverse web resources where the browser is already authenticated.
 */
async function queryEnvironmentVariables(clientUrl: string, schemaNames: string[]): Promise<Map<string, string>> {
  const results = new Map<string, string>();
  const apiBase = `${clientUrl}/api/data/v9.2`;

  // Build OData filter for all requested schema names
  const filter = schemaNames.map(name => `schemaname eq '${name}'`).join(' or ');

  // 1. Fetch definitions
  const defResponse = await fetch(
    `${apiBase}/environmentvariabledefinitions?$filter=${filter}&$select=environmentvariabledefinitionid,schemaname,defaultvalue`,
    {
      method: 'GET',
      headers: {
        Accept: 'application/json',
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
      },
      credentials: 'include', // Use session cookie
    }
  );

  if (!defResponse.ok) {
    throw new Error(
      `[Spaarke.RuntimeConfig] Failed to query environment variable definitions: ` +
        `${defResponse.status} ${defResponse.statusText}`
    );
  }

  const defData = await defResponse.json();
  const definitions: Array<{ id: string; schemaName: string; defaultValue?: string }> = [];

  for (const entity of defData.value ?? []) {
    definitions.push({
      id: entity.environmentvariabledefinitionid,
      schemaName: entity.schemaname,
      defaultValue: entity.defaultvalue ?? undefined,
    });
  }

  if (definitions.length === 0) {
    return results;
  }

  // 2. Fetch override values
  const valueFilter = definitions.map(d => `_environmentvariabledefinitionid_value eq '${d.id}'`).join(' or ');

  const valResponse = await fetch(
    `${apiBase}/environmentvariablevalues?$filter=${valueFilter}&$select=_environmentvariabledefinitionid_value,value`,
    {
      method: 'GET',
      headers: {
        Accept: 'application/json',
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
      },
      credentials: 'include',
    }
  );

  const overrides = new Map<string, string>();
  if (valResponse.ok) {
    const valData = await valResponse.json();
    for (const entity of valData.value ?? []) {
      overrides.set(entity._environmentvariabledefinitionid_value, entity.value);
    }
  }

  // 3. Merge: override value takes precedence over default
  for (const def of definitions) {
    const value = overrides.get(def.id) ?? def.defaultValue;
    if (value !== undefined) {
      results.set(def.schemaName, value);
    }
  }

  return results;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

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
export async function resolveRuntimeConfig(): Promise<IRuntimeConfig> {
  // Return cached config if valid
  if (isCacheValid()) {
    return cachedConfig!;
  }

  // 1. Resolve Xrm context for org URL
  const xrmContext = resolveXrmContext();
  if (!xrmContext) {
    // Fallback: use localStorage cache (covers full-screen / new-window scenarios
    // where Xrm is not available but the user previously accessed from within Dataverse)
    const lsCached = loadFromLocalStorage();
    if (lsCached) {
      console.warn(
        '[Spaarke.RuntimeConfig] Xrm not available — using localStorage cache. ' + `bffBaseUrl=${lsCached.bffBaseUrl}`
      );
      cachedConfig = lsCached;
      cacheTimestamp = Date.now();
      return lsCached;
    }
    throw new Error(
      '[Spaarke.RuntimeConfig] Xrm.Utility.getGlobalContext() not available. ' +
        'resolveRuntimeConfig() must be called from within a Dataverse web resource.'
    );
  }

  const clientUrl = xrmContext.getClientUrl();
  if (!clientUrl) {
    throw new Error(
      '[Spaarke.RuntimeConfig] Xrm.Utility.getGlobalContext().getClientUrl() returned empty. ' +
        'Cannot determine Dataverse organization URL.'
    );
  }

  // Capture tenantId from Xrm organizationSettings while we have Xrm access
  const xrmTenantId = xrmContext.organizationSettings?.tenantId ?? '';

  // 2. Query all three environment variables in a single batch
  const envVars = await queryEnvironmentVariables(clientUrl, [
    ENV_VAR_NAMES.BFF_BASE_URL,
    ENV_VAR_NAMES.BFF_APP_ID,
    ENV_VAR_NAMES.MSAL_CLIENT_ID,
  ]);

  // 3. Extract and validate — fail loudly if missing
  const bffBaseUrl = envVars.get(ENV_VAR_NAMES.BFF_BASE_URL);
  if (!bffBaseUrl) {
    throw new Error(
      `[Spaarke.RuntimeConfig] Environment variable '${ENV_VAR_NAMES.BFF_BASE_URL}' not found in Dataverse. ` +
        'Ensure the SpaarkeCore solution is imported and the variable has a value.'
    );
  }

  const bffAppId = envVars.get(ENV_VAR_NAMES.BFF_APP_ID);
  if (!bffAppId) {
    throw new Error(
      `[Spaarke.RuntimeConfig] Environment variable '${ENV_VAR_NAMES.BFF_APP_ID}' not found in Dataverse. ` +
        'Ensure the SpaarkeCore solution is imported and the variable has a value.'
    );
  }

  // MSAL client ID: try env var first, then window global
  let msalClientId = envVars.get(ENV_VAR_NAMES.MSAL_CLIENT_ID);
  if (!msalClientId) {
    msalClientId = typeof window !== 'undefined' ? window.__SPAARKE_MSAL_CLIENT_ID__ : undefined;
  }
  if (!msalClientId) {
    throw new Error(
      `[Spaarke.RuntimeConfig] MSAL client ID not available. ` +
        `Either set environment variable '${ENV_VAR_NAMES.MSAL_CLIENT_ID}' in Dataverse ` +
        'or set window.__SPAARKE_MSAL_CLIENT_ID__ before calling resolveRuntimeConfig().'
    );
  }

  // 4. Build config — normalize URL and construct OAuth scope
  const config: IRuntimeConfig = {
    bffBaseUrl: normalizeUrl(bffBaseUrl),
    bffOAuthScope: `api://${bffAppId}/user_impersonation`,
    msalClientId,
    tenantId: xrmTenantId,
  };

  // 5. Cache in memory + localStorage (localStorage enables full-screen fallback)
  cachedConfig = config;
  cacheTimestamp = Date.now();
  saveToLocalStorage(config);

  console.log(
    `[Spaarke.RuntimeConfig] Resolved: bffBaseUrl=${config.bffBaseUrl}, ` +
      `scope=api://${bffAppId.substring(0, 8)}..., clientId=${msalClientId.substring(0, 8)}..., ` +
      `tenantId=${xrmTenantId ? xrmTenantId.substring(0, 8) + '...' : '(empty)'}`
  );

  return config;
}

/**
 * Normalize a URL: trim whitespace, strip trailing slashes, strip trailing /api.
 *
 * WHY: The Dataverse env var (sprk_BffApiBaseUrl) stores the BFF URL as
 * "https://host/api", but all client-side route constants MUST include the
 * /api prefix (e.g., `${bffBaseUrl}/api/ai/chat/sessions`). Stripping /api
 * here prevents the double /api/api/ bug and establishes a single convention:
 *
 *   bffBaseUrl = host only (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net")
 *   fetch URLs = `${bffBaseUrl}/api/...`
 *
 * If you are adding a new fetch() call, remember: the /api prefix is YOUR
 * responsibility, NOT included in bffBaseUrl.
 */
function normalizeUrl(raw: string): string {
  return raw
    .trim()
    .replace(/\/+$/, '')
    .replace(/\/api$/i, '');
}
