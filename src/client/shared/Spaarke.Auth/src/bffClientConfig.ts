/**
 * The BFF's anonymous client configuration (`GET /api/config/client`), fetched
 * once and cached — the ONE client path to it.
 *
 * Two consumers read it:
 *   - tenant discovery (#1453): the BFF's `AzureAd:TenantId`, the last fallback
 *     under "TENANT PRECEDENCE" in tenant.ts (`fetchBffClientTenant`);
 *   - browser telemetry (#1537): the App Insights connection string of THIS
 *     environment (`getTelemetryConnectionString`), so no surface needs a form
 *     property or a build-time key.
 *
 * The endpoint is anonymous and rate-limited to 10 requests per minute per IP
 * (RateLimitingModule, "anonymous" policy). Staff behind one NAT share that
 * budget, so a successful answer is persisted in localStorage per BFF host (the
 * values do not change between deployments of one environment) and concurrent or
 * repeated callers on a page share one request. A failure is remembered briefly
 * so retries cannot hammer it.
 *
 * Deliberately free of MSAL, React and window-global declarations: VisualHost
 * imports this module by relative source path to stay out of the auth stack.
 *
 * @module bffClientConfig
 */

import { normalizeTenant } from './tenant';

/** What this library keeps from `/api/config/client`. */
export interface IBffClientConfig {
  /** The BFF's tenant (`AzureAd:TenantId`), validated — always a usable single tenant. */
  tenantId: string;
  /** Browser App Insights connection string for this environment; `''` when the BFF has none. */
  appInsightsConnectionString: string;
}

/**
 * localStorage key. The name predates the telemetry field: entries written before
 * #1537 hold only the tenant, so they still answer tenant lookups but count as a
 * miss for the full config (see readPersisted).
 */
const LS_KEY = '__spaarke_bff_tenant__';
const PERSIST_TTL_MS = 24 * 60 * 60 * 1000;
/**
 * A persisted answer WITHOUT a connection string counts as fresh for telemetry
 * for only an hour. A BFF deployed after the client surfaces (whose answer lacks
 * the field), or one given a connection string later, is then picked up within
 * the hour instead of a day. The tenant in that entry stays valid for the full TTL.
 */
const EMPTY_TELEMETRY_TTL_MS = 60 * 60 * 1000;
/** How long a failed lookup (BFF or env var) is remembered before it is retried. */
export const LOOKUP_FAILURE_BACKOFF_MS = 60 * 1000;
/**
 * Upper bound for each discovery request. initAuth() serializes provider
 * selection, so an unreachable BFF or Dataverse endpoint must not stall every
 * later init on the page.
 */
export const LOOKUP_TIMEOUT_MS = 8 * 1000;

/** An AbortSignal that fires after `ms`, or undefined where the host has no AbortSignal support. */
export function timeoutSignal(ms: number): AbortSignal | undefined {
  try {
    if (typeof AbortSignal !== 'undefined' && typeof AbortSignal.timeout === 'function') {
      return AbortSignal.timeout(ms);
    }
    if (typeof AbortController !== 'undefined') {
      const controller = new AbortController();
      setTimeout(() => controller.abort(), ms);
      return controller.signal;
    }
  } catch {
    /* fall through — no timeout available */
  }
  return undefined;
}

/**
 * Normalize a BFF URL: trim whitespace, strip trailing slashes, strip trailing /api.
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
export function normalizeBffBaseUrl(raw: string): string {
  return raw
    .trim()
    .replace(/\/+$/, '')
    .replace(/\/api$/i, '');
}

/** A connection string the browser SDK can use; `''` for anything else. The BFF validates it fully. */
function normalizeConnectionString(value: unknown): string {
  if (typeof value !== 'string') return '';
  const cs = value.trim();
  return /(^|;)\s*InstrumentationKey\s*=/i.test(cs) ? cs : '';
}

interface IPersisted {
  bffBaseUrl?: string;
  tenantId?: string;
  appInsightsConnectionString?: string;
  _ts?: number;
}

/**
 * The persisted answer for `bffBaseUrl`, when present and fresh. `config` is null for
 * a pre-#1537 entry, or for an entry without a connection string older than an hour.
 */
function readPersisted(bffBaseUrl: string): { tenantId: string; config: IBffClientConfig | null } | null {
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (!raw) return null;
    const entry = JSON.parse(raw) as IPersisted;
    if (entry.bffBaseUrl !== bffBaseUrl || !entry._ts || Date.now() - entry._ts > PERSIST_TTL_MS) return null;
    const tenantId = normalizeTenant(entry.tenantId);
    if (!tenantId) return null;
    if (typeof entry.appInsightsConnectionString !== 'string') return { tenantId, config: null };
    const appInsightsConnectionString = normalizeConnectionString(entry.appInsightsConnectionString);
    const stale = !appInsightsConnectionString && Date.now() - entry._ts > EMPTY_TELEMETRY_TTL_MS;
    return { tenantId, config: stale ? null : { tenantId, appInsightsConnectionString } };
  } catch {
    return null;
  }
}

function persist(bffBaseUrl: string, config: IBffClientConfig): void {
  try {
    const entry: IPersisted = { bffBaseUrl, ...config, _ts: Date.now() };
    localStorage.setItem(LS_KEY, JSON.stringify(entry));
  } catch {
    /* localStorage may not be available */
  }
}

const requests = new Map<string, { promise: Promise<IBffClientConfig | null>; at: number; ok: boolean }>();

/** Drop the in-memory requests (the persisted answer stays). Called by clearRuntimeConfigCache(). */
export function clearBffClientConfigRequests(): void {
  requests.clear();
}

/**
 * The BFF's client configuration, or `null` when it cannot be had (no or invalid
 * BFF URL, network error, non-2xx, or a response without a usable tenant — the
 * check that this really is a Spaarke BFF answering). Never throws.
 */
export function fetchBffClientConfig(bffBaseUrl: string | undefined): Promise<IBffClientConfig | null> {
  const base = typeof bffBaseUrl === 'string' ? normalizeBffBaseUrl(bffBaseUrl) : '';
  if (!/^https?:\/\//i.test(base)) return Promise.resolve(null);

  const persisted = readPersisted(base)?.config;
  if (persisted) return Promise.resolve(persisted);

  const existing = requests.get(base);
  if (existing && (existing.ok || Date.now() - existing.at < LOOKUP_FAILURE_BACKOFF_MS)) {
    return existing.promise;
  }

  const entry = { promise: Promise.resolve<IBffClientConfig | null>(null), at: Date.now(), ok: false };
  entry.promise = (async () => {
    try {
      const resp = await fetch(`${base}/api/config/client`, {
        method: 'GET',
        headers: { Accept: 'application/json' },
        signal: timeoutSignal(LOOKUP_TIMEOUT_MS),
      });
      if (!resp.ok) {
        console.warn(`[Spaarke.RuntimeConfig] /api/config/client returned ${resp.status} (non-fatal)`);
        return null;
      }
      const body = (await resp.json()) as { tenantId?: unknown; appInsightsConnectionString?: unknown };
      const tenantId = normalizeTenant(body.tenantId);
      if (!tenantId) {
        console.warn('[Spaarke.RuntimeConfig] /api/config/client returned no usable tenantId (non-fatal)');
        return null;
      }
      const config: IBffClientConfig = {
        tenantId,
        appInsightsConnectionString: normalizeConnectionString(body.appInsightsConnectionString),
      };
      entry.ok = true;
      persist(base, config);
      return config;
    } catch (err) {
      console.warn('[Spaarke.RuntimeConfig] /api/config/client lookup failed (non-fatal):', err);
      return null;
    }
  })();
  requests.set(base, entry);
  return entry.promise;
}

/**
 * The tenant the BFF is configured for (`AzureAd:TenantId`, via the anonymous
 * `/api/config/client`), validated; `''` when unavailable. Never throws.
 */
export async function fetchBffClientTenant(bffBaseUrl: string | undefined): Promise<string> {
  const base = typeof bffBaseUrl === 'string' ? normalizeBffBaseUrl(bffBaseUrl) : '';
  // A tenant persisted before #1537 still answers without a request.
  const persistedTenant = /^https?:\/\//i.test(base) ? readPersisted(base)?.tenantId : undefined;
  if (persistedTenant) return persistedTenant;
  return (await fetchBffClientConfig(base))?.tenantId ?? '';
}

/**
 * The App Insights connection string browser telemetry for this environment
 * sends to, from the BFF the caller already talks to (#1537); `''` when the BFF
 * has none configured or cannot be reached — telemetry is then off. Never throws.
 */
export async function getTelemetryConnectionString(bffBaseUrl: string | undefined): Promise<string> {
  return (await fetchBffClientConfig(bffBaseUrl))?.appInsightsConnectionString ?? '';
}
