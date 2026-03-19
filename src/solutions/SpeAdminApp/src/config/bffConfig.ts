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
 *   3. Dataverse environment detection (ENVIRONMENT_BFF_MAP lookup)
 *   4. Build-time env var (VITE_BFF_BASE_URL)
 */

/**
 * Build-time BFF API base URL injected via Vite environment variable.
 * Set VITE_BFF_BASE_URL in .env.development / .env.production.
 */
const ENV_BFF_BASE_URL: string = import.meta.env.VITE_BFF_BASE_URL ?? '';

/** Window-level property that the host page can set to override the BFF URL. */
const GLOBAL_BFF_URL_KEY = "__SPAARKE_BFF_URL__";

/**
 * Environment-to-BFF URL mapping.
 * Maps Dataverse org hostnames (lowercase) to their corresponding BFF API
 * base URLs. Allows a single build to work correctly in any environment.
 * Note: SpeAdminApp URLs do NOT include the /api suffix.
 */
const ENVIRONMENT_BFF_MAP: Record<string, string> = {
  'spaarkedev1.crm.dynamics.com': 'https://spe-api-dev-67e2xz.azurewebsites.net',
  'spaarke-demo.crm.dynamics.com': 'https://spaarke-bff-prod.azurewebsites.net',
};

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

  // 3. Dataverse environment detection (maps org hostname → BFF URL)
  const envUrl = resolveFromDataverseEnvironment();
  if (envUrl) return envUrl;

  // 4. Build-time env var (VITE_BFF_BASE_URL from .env.development / .env.production)
  if (ENV_BFF_BASE_URL) return normalizeUrl(ENV_BFF_BASE_URL);

  throw new Error(
    '[Spaarke] BFF base URL not configured. Set VITE_BFF_BASE_URL in .env.development or .env.production, ' +
    'or set window.__SPAARKE_BFF_URL__ at runtime.'
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

function normalizeUrl(raw: string): string {
  const url = raw.trim();
  return url.replace(/\/+$/, "");
}
