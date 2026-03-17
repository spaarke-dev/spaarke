/**
 * @deprecated Use @spaarke/auth (via authInit.ts) instead. This file is scheduled for removal.
 * BFF base URL resolution is now handled by @spaarke/auth's resolveConfig().
 */

/**
 * BFF API base URL discovery for the AnalysisWorkspace Code Page.
 *
 * The workspace runs as an HTML web resource inside Dataverse — either
 * as a dialog (via navigateTo) or embedded as an iframe on a form.
 * The page origin is always the Dataverse org (e.g., spaarkedev1.crm.dynamics.com),
 * so relative URLs like "/api" would hit Dataverse, not the BFF API.
 *
 * URL resolution order:
 *   1. Window-level global (__SPAARKE_BFF_BASE_URL__) set by host page or PCF bridge
 *   2. Parent frame global (when embedded in a PCF host frame)
 *   3. Hardcoded dev fallback
 *
 * Pattern copied from: src/solutions/LegalWorkspace/src/config/bffConfig.ts
 *
 * @see docs/architecture/sdap-bff-api-patterns.md - Code Page → BFF API Authentication
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
  /* eslint-disable @typescript-eslint/no-explicit-any */
  const globalUrl = (window as any)[GLOBAL_BFF_URL_KEY] as string | undefined;
  if (globalUrl) {
    return normalizeUrl(globalUrl);
  }

  // 2. Parent frame global (when embedded in a PCF host frame)
  try {
    const parentUrl = (window.parent as any)?.[GLOBAL_BFF_URL_KEY] as string | undefined;
    if (parentUrl) {
      return normalizeUrl(parentUrl);
    }
  } catch {
    /* cross-origin — swallow */
  }
  /* eslint-enable @typescript-eslint/no-explicit-any */

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
