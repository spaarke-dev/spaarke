/**
 * bffConfig.ts
 * BFF API base URL discovery for the Legal Operations Workspace.
 *
 * The workspace runs as a standalone HTML custom page (web resource) inside Dataverse.
 * Unlike PCF controls, it does not have access to context.parameters.sdapApiBaseUrl.
 *
 * URL resolution order:
 *   1. Window-level global (set by host page or PCF bridge)
 *   2. Xrm globalContext client URL-derived setting
 *   3. Hardcoded dev fallback
 *
 * When migrating to PCF: replace with context.parameters.sdapApiBaseUrl?.raw
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Build-time BFF API base URL injected via Vite environment variable.
 * Set VITE_BFF_BASE_URL in .env.development / .env.production.
 */
const ENV_BFF_BASE_URL: string = import.meta.env.VITE_BFF_BASE_URL ?? '';

/**
 * Window-level property name that the host page or PCF bridge can set
 * to communicate the BFF base URL to the workspace iframe.
 */
const GLOBAL_BFF_URL_KEY = '__SPAARKE_BFF_BASE_URL__';

// ---------------------------------------------------------------------------
// URL resolution
// ---------------------------------------------------------------------------

/**
 * Resolves the BFF API base URL.
 *
 * Returns the URL with trailing `/api` but without a trailing slash.
 * Example: `https://spe-api-dev-67e2xz.azurewebsites.net/api`
 */
export function getBffBaseUrl(): string {
  // 1. Window-level global (highest priority — set by host page or PCF bridge)
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const globalUrl = (window as any)[GLOBAL_BFF_URL_KEY] as string | undefined;
  if (globalUrl) {
    return normalizeUrl(globalUrl);
  }

  // 2. Parent frame global (custom page iframe inside PCF host)
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentUrl = (window.parent as any)?.[GLOBAL_BFF_URL_KEY] as string | undefined;
    if (parentUrl) {
      return normalizeUrl(parentUrl);
    }
  } catch {
    /* cross-origin — swallow */
  }

  // 3. Build-time env var (VITE_BFF_BASE_URL from .env.development / .env.production)
  if (ENV_BFF_BASE_URL) return normalizeUrl(ENV_BFF_BASE_URL);

  throw new Error(
    '[Spaarke] BFF base URL not configured. Set VITE_BFF_BASE_URL in .env.development or .env.production, ' +
    'or set window.__SPAARKE_BFF_BASE_URL__ at runtime.'
  );
}

/**
 * Normalizes a raw URL string:
 *   - Ensures https:// prefix
 *   - Removes trailing slash
 */
function normalizeUrl(raw: string): string {
  let url = raw.trim();
  if (!url.startsWith('http://') && !url.startsWith('https://')) {
    url = `https://${url}`;
  }
  return url.replace(/\/+$/, '');
}

// ---------------------------------------------------------------------------
// OAuth scope for BFF API (used by auth provider)
// ---------------------------------------------------------------------------

/**
 * OAuth scope for the BFF API. Used when acquiring tokens via MSAL.
 * This is the BFF API app registration's user_impersonation scope.
 */
export const BFF_API_SCOPE = 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation';
