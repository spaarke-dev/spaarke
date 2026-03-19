/**
 * bffConfig.ts
 * BFF API base URL discovery for the Document Upload Wizard Code Page.
 *
 * Follows the same resolution pattern as LegalWorkspace/bffConfig.ts:
 *   1. Window-level global (set by host page or PCF bridge)
 *   2. Parent frame global (custom page iframe inside PCF host)
 *   3. Dataverse environment detection (ENVIRONMENT_BFF_MAP lookup)
 *   4. Build-time env var (VITE_BFF_BASE_URL)
 *
 * @see ADR-007  - All SPE operations through BFF API
 * @see ADR-008  - Endpoint filters for auth (Bearer token)
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

/**
 * Environment-to-BFF URL mapping.
 * Maps Dataverse org hostnames (lowercase) to their corresponding BFF API
 * base URLs. Allows a single build to work correctly in any environment.
 */
const ENVIRONMENT_BFF_MAP: Record<string, string> = {
    'spaarkedev1.crm.dynamics.com': 'https://spe-api-dev-67e2xz.azurewebsites.net/api',
    'spaarke-demo.crm.dynamics.com': 'https://spaarke-bff-prod.azurewebsites.net/api',
};

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
    // 1. Window-level global (highest priority)
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const globalUrl = (window as any)[GLOBAL_BFF_URL_KEY] as string | undefined;
    if (globalUrl) {
        return normalizeUrl(globalUrl);
    }

    // 2. Parent frame global (custom page iframe inside PCF host)
    try {
        const parentUrl = (window.parent as any)?.[GLOBAL_BFF_URL_KEY] as string | undefined;
        if (parentUrl) {
            return normalizeUrl(parentUrl);
        }
    } catch {
        /* cross-origin - swallow */
    }
    /* eslint-enable @typescript-eslint/no-explicit-any */

    // 3. Dataverse environment detection (maps org hostname → BFF URL)
    const envUrl = resolveFromDataverseEnvironment();
    if (envUrl) return envUrl;

    // 4. Build-time env var (VITE_BFF_BASE_URL from .env.development / .env.production)
    if (ENV_BFF_BASE_URL) return normalizeUrl(ENV_BFF_BASE_URL);

    throw new Error(
      '[Spaarke] BFF base URL not configured. Set VITE_BFF_BASE_URL in .env.development or .env.production, ' +
      'or set window.__SPAARKE_BFF_BASE_URL__ at runtime.'
    );
}

/**
 * Resolves BFF URL by detecting the current Dataverse environment.
 */
function resolveFromDataverseEnvironment(): string | null {
    try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm
            ?? (window.parent as any)?.Xrm
            ?? (window.top as any)?.Xrm;

        const clientUrl: string | undefined = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
        if (!clientUrl) return null;

        const hostname = new URL(clientUrl).hostname.toLowerCase();
        const mapped = ENVIRONMENT_BFF_MAP[hostname];
        if (mapped) {
            console.info(`[Spaarke] BFF URL resolved from environment: ${hostname} → ${mapped}`);
            return normalizeUrl(mapped);
        }
    } catch {
        /* Xrm not available or cross-origin — fall through */
    }
    return null;
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
