/**
 * tenant.ts — validation of Entra tenant identifiers, and detection of the
 * Dataverse host the library runs in.
 *
 * Every tenant value @spaarke/auth turns into an MSAL authority passes through
 * `normalizeTenant()` first. Two incidents motivated it:
 *   - the 2026-05-13 popup regression (ef57fc3f35): a consumer passed the string
 *     "undefined" and the library built `https://login.microsoftonline.com/undefined`;
 *   - B2B guests (#1453): a multi-tenant segment (`organizations` / `common`) signs
 *     the user in to their HOME tenant, where Spaarke's single-tenant app does not
 *     exist (AADSTS700016). Members never noticed, because their home tenant IS
 *     Spaarke's tenant.
 *
 * TENANT PRECEDENCE — the one order every lookup in this library follows
 * (resolveConfig/resolveTenantSync, resolveRuntimeConfig, discoverTenantId):
 *   1. what the caller passed (explicit `authority`, then `tenantId`);
 *   2. the deployment's declared tenant — the `sprk_TenantId` env var, or a value
 *      derived from it (`window.__SPAARKE_TENANT_ID__` published by setRuntimeConfig,
 *      the persisted runtime config `__spaarke_rtc__`). The MSAL app registration
 *      lives in that tenant, so it wins over anything the host reports;
 *   3. `Xrm…organizationSettings.tenantId` (often empty on first load);
 *   4. the BFF's `/api/config/client` (its `AzureAd:TenantId`).
 * Synchronous code can only reach the derived values of step 2, so it reads them
 * before Xrm; the env var itself is queried asynchronously.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** A verified-domain tenant name, e.g. `contoso.onmicrosoft.com` (at least one dot). */
const DOMAIN_RE = /^(?=.{4,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;

/**
 * Values that must never become an authority's tenant segment: the multi-tenant
 * aliases, the consumer (MSA) tenant by name and by GUID, the empty GUID, and the
 * stringified JavaScript nothings that produced the 2026-05-13 regression.
 */
const REJECTED_TENANTS = new Set([
  'common',
  'organizations',
  'consumers',
  'undefined',
  'null',
  '9188040d-6c67-4c5b-b112-36a304b66dad', // consumers (MSA) tenant GUID
  '00000000-0000-0000-0000-000000000000',
]);

const AUTHORITY_HOST = 'https://login.microsoftonline.com';

/**
 * Return the trimmed tenant when `value` is a usable single-tenant identifier
 * (a GUID, or a domain containing a dot), otherwise `undefined`.
 */
export function normalizeTenant(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined;
  const tenant = value.trim();
  if (!tenant || REJECTED_TENANTS.has(tenant.toLowerCase())) return undefined;
  return GUID_RE.test(tenant) || DOMAIN_RE.test(tenant) ? tenant : undefined;
}

/**
 * Whether `value` is a usable single-tenant identifier. Rejects `''`,
 * `'undefined'`, `'null'`, `'common'`, `'organizations'`, `'consumers'` and
 * anything that is neither a GUID nor a dotted domain.
 */
export function isValidTenant(value: unknown): value is string {
  return normalizeTenant(value) !== undefined;
}

/**
 * Extract the tenant segment of an authority URL, or `undefined` when the
 * authority is malformed or not single-tenant. Accepts `https://{host}/{tenant}`
 * with an optional trailing slash or `/v2.0`; anything else (no tenant, extra
 * path, query, non-https) is rejected rather than guessed at.
 */
export function tenantFromAuthority(authority: unknown): string | undefined {
  if (typeof authority !== 'string' || !authority.trim()) return undefined;
  let url: URL;
  try {
    url = new URL(authority.trim());
  } catch {
    return undefined;
  }
  if (url.protocol !== 'https:' || url.search || url.hash) return undefined;
  const segments = url.pathname.split('/').filter(Boolean);
  if (segments.length === 0 || segments.length > 2) return undefined;
  if (segments.length === 2 && segments[1].toLowerCase() !== 'v2.0') return undefined;
  try {
    return normalizeTenant(decodeURIComponent(segments[0]));
  } catch {
    return undefined;
  }
}

/** Build the tenant-specific authority for an already-validated tenant. */
export function buildTenantAuthority(tenant: string): string {
  return `${AUTHORITY_HOST}/${tenant}`;
}

/** Case-insensitive tenant comparison (GUIDs arrive in either case). */
export function sameTenant(a: string | undefined, b: string | undefined): boolean {
  return !!a && !!b && a.toLowerCase() === b.toLowerCase();
}

/** The current window plus its reachable (same-origin) parent and top. */
export function hostFrames(): Window[] {
  if (typeof window === 'undefined') return [];
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top);
  } catch {
    /* cross-origin */
  }
  return frames;
}

/** Read `Xrm.Utility.getGlobalContext()` from the first frame that exposes it. */
export function xrmGlobalContext(): any | null {
  for (const frame of hostFrames()) {
    try {
      const ctx = (frame as any).Xrm?.Utility?.getGlobalContext?.();
      if (ctx) return ctx;
    } catch {
      /* cross-origin */
    }
  }
  return null;
}

/**
 * The tenant from `Xrm…organizationSettings.tenantId`, validated. Walks every
 * frame (not just the first with Xrm), because the value is often empty in the
 * nearest frame on first load.
 */
export function xrmTenant(): string | undefined {
  for (const frame of hostFrames()) {
    try {
      const tid = (frame as any).Xrm?.Utility?.getGlobalContext?.()?.organizationSettings?.tenantId;
      const tenant = normalizeTenant(tid);
      if (tenant) return tenant;
    } catch {
      /* cross-origin */
    }
  }
  return undefined;
}

/** Dataverse (model-driven app) domains, commercial and sovereign clouds. */
const DATAVERSE_HOST_RE =
  /\.(dynamics\.com|dynamics\.cn|microsoftdynamics\.us|microsoftdynamics\.de|appsplatform\.us)$/i;

/** Whether this page is served from a Dataverse organization's own domain. */
export function isDataverseOrigin(): boolean {
  if (typeof window === 'undefined') return false;
  try {
    return DATAVERSE_HOST_RE.test(window.location.hostname);
  } catch {
    return false;
  }
}

/**
 * Whether the library is running inside Dataverse: Xrm is reachable from this
 * frame, or the page is served from a Dataverse domain. Inside Dataverse a
 * non-tenant authority is never acceptable (#1453), so this drives the default
 * of `IAuthConfig.requireTenantAuthority`.
 */
export function isDataverseHost(): boolean {
  for (const frame of hostFrames()) {
    try {
      if (typeof (frame as any).Xrm?.Utility?.getGlobalContext === 'function') return true;
    } catch {
      /* cross-origin */
    }
  }
  return isDataverseOrigin();
}

/* eslint-enable @typescript-eslint/no-explicit-any */
