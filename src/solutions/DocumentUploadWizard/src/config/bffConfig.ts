/**
 * bffConfig.ts
 * BFF API base URL discovery for the Document Upload Wizard Code Page.
 *
 * Follows the same resolution pattern as LegalWorkspace/bffConfig.ts:
 *   1. Window-level global (set by host page or PCF bridge)
 *   2. Parent frame global (custom page iframe inside PCF host)
 *   3. Hardcoded dev fallback
 *
 * @see ADR-007  - All SPE operations through BFF API
 * @see ADR-008  - Endpoint filters for auth (Bearer token)
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Dev-environment BFF API base URL (includes /api path segment). */
const DEFAULT_BFF_BASE_URL = 'https://spe-api-dev-67e2xz.azurewebsites.net/api';

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

    // 3. Fallback to default dev URL
    return DEFAULT_BFF_BASE_URL;
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
