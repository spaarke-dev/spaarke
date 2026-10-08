import type { IAuthConfig } from './types';
import { AuthError } from './errors';
import { readCachedRuntimeTenant } from './resolveRuntimeConfig';
import { buildTenantAuthority, isDataverseHost, normalizeTenant, tenantFromAuthority, xrmTenant } from './tenant';

/** No default client ID — must be resolved from Dataverse env var sprk_MsalClientId. */
const DEFAULT_CLIENT_ID: string | undefined = undefined;

/**
 * Degraded authority, used ONLY outside a Dataverse host when no tenant can be
 * found and the caller did not set `requireTenantAuthority`. `/organizations`
 * signs the user in to their HOME tenant: for a B2B guest that is not Spaarke's
 * tenant, so sign-in fails with AADSTS700016 (#1453); for everyone it also makes
 * ssoSilent fail in iframes and forces a popup. Inside Dataverse the library
 * throws instead of using it.
 */
const FALLBACK_AUTHORITY = 'https://login.microsoftonline.com/organizations';

/** Where a resolved tenant came from (diagnostics only). */
export type TenantSource = 'authority' | 'tenantId' | 'xrm' | 'window' | 'runtime-cache';

/** A tenant resolved for an authority. */
export interface ResolvedTenant {
  tenant: string;
  authority: string;
  source: TenantSource;
}

/**
 * The host's own tenant, synchronously and validated, in the order stated once
 * under "TENANT PRECEDENCE" in tenant.ts: the env-var-derived values
 * (`window.__SPAARKE_TENANT_ID__`, published by `setRuntimeConfig`, then the
 * persisted runtime config `__spaarke_rtc__`) before
 * `Xrm…organizationSettings.tenantId`. `null` when none yields a valid tenant.
 */
export function discoverTenantSync(): { tenant: string; source: TenantSource } | null {
  const fromWindow = typeof window !== 'undefined' ? normalizeTenant(window.__SPAARKE_TENANT_ID__) : undefined;
  if (fromWindow) return { tenant: fromWindow, source: 'window' };

  const fromCache = readCachedRuntimeTenant();
  if (fromCache) return { tenant: fromCache, source: 'runtime-cache' };

  const fromXrm = xrmTenant();
  if (fromXrm) return { tenant: fromXrm, source: 'xrm' };

  return null;
}

/**
 * Resolve the tenant for the MSAL authority synchronously:
 * explicit `authority` → `tenantId` → the host chain in {@link discoverTenantSync}.
 * Every candidate is validated; a rejected one is logged (when `log`) and skipped.
 * `null` means unresolved — `initAuth` then tries the async sources.
 */
export function resolveTenantSync(userConfig?: IAuthConfig, log = true): ResolvedTenant | null {
  const rawAuthority = userConfig?.authority;
  if (typeof rawAuthority === 'string' && rawAuthority.trim()) {
    const tenant = tenantFromAuthority(rawAuthority);
    if (tenant) return { tenant, authority: rawAuthority.trim(), source: 'authority' };
    if (log)
      console.warn('[SpaarkeAuth] resolveConfig: authority has no valid tenant; ignoring it. Got:', rawAuthority);
  }

  // Defensive: the typeof guard prevents a TypeError if a consumer accidentally
  // passes a non-string tenantId (e.g. a Promise<string> — happens when an
  // async getter shadows a sync one of the same name via import collision; we
  // hit this once in SpaarkeAi authInit.ts). Falls through to discovery rather
  // than crashing the whole auth bootstrap.
  const rawTenantId = userConfig?.tenantId;
  if (rawTenantId !== undefined && typeof rawTenantId !== 'string') {
    if (log) console.warn('[SpaarkeAuth] resolveConfig: tenantId is not a string; ignoring. Got:', typeof rawTenantId);
  } else if (typeof rawTenantId === 'string' && rawTenantId.trim()) {
    const tenant = normalizeTenant(rawTenantId);
    if (tenant) return { tenant, authority: buildTenantAuthority(tenant), source: 'tenantId' };
    // e.g. 'undefined' — the 2026-05-13 /undefined authority regression (ef57fc3f35).
    if (log) console.warn('[SpaarkeAuth] resolveConfig: tenantId is not a valid tenant; ignoring. Got:', rawTenantId);
  }

  const discovered = discoverTenantSync();
  return discovered ? { ...discovered, authority: buildTenantAuthority(discovered.tenant) } : null;
}

/** No default BFF scope — must be resolved from Dataverse env var sprk_BffApiAppId. */
const DEFAULT_BFF_SCOPE: string | undefined = undefined;

/** Buffer (ms) before token expiry to consider it stale. */
export const TOKEN_EXPIRY_BUFFER_MS = 5 * 60 * 1000; // 5 minutes

/** Proactive refresh interval (ms). */
export const PROACTIVE_REFRESH_INTERVAL_MS = 4 * 60 * 1000; // 4 minutes

/**
 * Resolve the full config, merging user overrides with defaults and window globals.
 * Throws if required values (clientId, bffApiScope) are not provided via config, window globals,
 * or Dataverse environment variables. No silent fallback to dev values.
 *
 * Throws an `AuthError` (`tenant_unresolved`) when no tenant can be found and
 * `requireTenantAuthority` is in effect (the default inside a Dataverse host).
 * This is synchronous; `initAuth()` runs the async discovery first.
 */
export function resolveConfig(userConfig?: IAuthConfig): Required<IAuthConfig> {
  const clientId =
    userConfig?.clientId ??
    (typeof window !== 'undefined' ? window.__SPAARKE_MSAL_CLIENT_ID__ : undefined) ??
    DEFAULT_CLIENT_ID;

  const bffApiScope = userConfig?.bffApiScope ?? DEFAULT_BFF_SCOPE;
  const bffBaseUrl =
    userConfig?.bffBaseUrl ?? (typeof window !== 'undefined' ? window.__SPAARKE_BFF_URL__ : undefined) ?? '';

  if (!clientId) {
    throw new Error(
      'MSAL Client ID not configured. Set sprk_MsalClientId environment variable in Dataverse, ' +
        'or provide clientId via resolveRuntimeConfig() or window.__SPAARKE_MSAL_CLIENT_ID__.'
    );
  }

  // Authority resolution priority (#1453):
  //   1. userConfig.authority (full URL) — explicit override, if its tenant is valid
  //   2. userConfig.tenantId — preferred consumer path; library builds the URL
  //   3. window.__SPAARKE_TENANT_ID__ → persisted runtime config → Xrm frame-walk
  //      (the single order is stated under "TENANT PRECEDENCE" in tenant.ts)
  //   4. unresolved: inside Dataverse (or requireTenantAuthority) → throw;
  //      otherwise the degraded /organizations fallback, logged as an error.
  const requireTenantAuthority =
    typeof userConfig?.requireTenantAuthority === 'boolean' ? userConfig.requireTenantAuthority : isDataverseHost();

  const resolved = resolveTenantSync(userConfig);
  let authority: string;
  let tenantId: string;
  if (resolved) {
    authority = resolved.authority;
    tenantId = resolved.tenant;
  } else if (requireTenantAuthority) {
    throw new AuthError(
      'No tenant could be resolved for the sign-in authority. Set the sprk_TenantId environment variable ' +
        'in Dataverse, or pass tenantId to initAuth(). Refusing to sign in against /organizations, which ' +
        'fails for B2B guests (AADSTS700016).',
      'tenant_unresolved'
    );
  } else {
    console.error(
      '[SpaarkeAuth] resolveConfig: no tenant resolved — falling back to /organizations. ' +
        'B2B guests cannot sign in this way, and ssoSilent will usually fail. Pass tenantId.'
    );
    authority = FALLBACK_AUTHORITY;
    tenantId = '';
  }

  return {
    clientId,
    authority,
    tenantId,
    redirectUri: userConfig?.redirectUri ?? (typeof window !== 'undefined' ? window.location.origin : ''),
    bffApiScope: bffApiScope ?? '',
    bffBaseUrl,
    proactiveRefresh: userConfig?.proactiveRefresh ?? false,
    requireXrm: userConfig?.requireXrm ?? false,
    requireSilentOnly: userConfig?.requireSilentOnly ?? false,
    requireTenantAuthority,
  };
}
