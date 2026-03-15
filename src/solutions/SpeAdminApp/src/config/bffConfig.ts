/**
 * bffConfig.ts
 * BFF API base URL discovery for the SPE Admin App.
 *
 * The app runs as a standalone HTML web resource inside Dataverse.
 * Unlike PCF controls, it does not have access to context.parameters.sdapApiBaseUrl.
 *
 * URL resolution order:
 *   1. Window-level global (set by host page or PCF bridge)
 *   2. Parent frame global (if nested inside another iframe)
 *   3. Hardcoded dev fallback
 */

/** Dev-environment BFF API base URL. */
const DEFAULT_BFF_BASE_URL = "https://spe-api-dev-67e2xz.azurewebsites.net";

/** Window-level property that the host page can set to override the BFF URL. */
const GLOBAL_BFF_URL_KEY = "__SPAARKE_BFF_URL__";

/**
 * Resolves the BFF API base URL.
 * Returns the URL without a trailing slash.
 */
export function getBffBaseUrl(): string {
  // 1. Window-level global (highest priority)
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const globalUrl = (window as any)[GLOBAL_BFF_URL_KEY] as string | undefined;
  if (globalUrl) return normalizeUrl(globalUrl);

  // 2. Parent frame global (nested iframe case)
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentUrl = (window.parent as any)?.[GLOBAL_BFF_URL_KEY] as string | undefined;
    if (parentUrl) return normalizeUrl(parentUrl);
  } catch {
    /* cross-origin — swallow */
  }

  // 3. Dev fallback
  return DEFAULT_BFF_BASE_URL;
}

function normalizeUrl(raw: string): string {
  const url = raw.trim();
  return url.replace(/\/+$/, "");
}
