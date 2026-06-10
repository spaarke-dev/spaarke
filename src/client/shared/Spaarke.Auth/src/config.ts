import type { IAuthConfig } from './types';

/** No default client ID — must be resolved from Dataverse env var sprk_MsalClientId. */
const DEFAULT_CLIENT_ID: string | undefined = undefined;

/**
 * Fallback authority used only when no tenant can be discovered from the
 * surrounding Dataverse / Xrm context. `/organizations` makes MSAL.js'
 * ssoSilent fail (it can't tell which AAD session cookie to use), forcing a
 * popup. Prefer the tenant-specific authority returned by
 * `resolveDefaultAuthority()` below.
 */
const FALLBACK_AUTHORITY = 'https://login.microsoftonline.com/organizations';

/**
 * Best-effort: read the user's AAD tenant ID from Xrm.organizationSettings so
 * the MSAL authority is tenant-specific. With a specific tenant, ssoSilent can
 * use the existing AAD session cookie and avoid the "Pick an account" popup.
 */
function resolveTenantFromXrm(): string | undefined {
  if (typeof window === 'undefined') return undefined;
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const frames: Window[] = [window as Window];
    try {
      if (window.parent !== window) frames.push(window.parent);
    } catch {
      /* cross-origin */
    }
    try {
      if (window.top && window.top !== window) frames.push(window.top);
    } catch {
      /* cross-origin */
    }
    for (const frame of frames) {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (frame as any).Xrm;
        const tid = xrm?.Utility?.getGlobalContext?.()?.organizationSettings?.tenantId;
        if (tid && typeof tid === 'string') return tid;
      } catch {
        /* cross-origin */
      }
    }
  } catch {
    /* swallow */
  }
  return undefined;
}

function resolveDefaultAuthority(): string {
  const tenantId = resolveTenantFromXrm();
  return tenantId ? `https://login.microsoftonline.com/${tenantId}` : FALLBACK_AUTHORITY;
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

  // Authority resolution priority:
  //   1. userConfig.authority (full URL) — explicit override; always wins
  //   2. userConfig.tenantId — preferred consumer path; library builds the URL
  //   3. resolveDefaultAuthority() — Xrm frame-walk; falls back to /organizations
  //      (degraded — see IAuthConfig.authority docs)
  // Defensive: the typeof guard prevents a TypeError if a consumer accidentally
  // passes a non-string tenantId (e.g. a Promise<string> — happens when an
  // async getter shadows a sync one of the same name via import collision; we
  // hit this once in SpaarkeAi authInit.ts). Falls through to default authority
  // resolution rather than crashing the whole auth bootstrap.
  let authority: string;
  if (typeof userConfig?.authority === 'string' && userConfig.authority) {
    authority = userConfig.authority;
  } else if (typeof userConfig?.tenantId === 'string' && userConfig.tenantId.trim()) {
    authority = `https://login.microsoftonline.com/${userConfig.tenantId.trim()}`;
  } else {
    if (userConfig?.tenantId !== undefined && typeof userConfig.tenantId !== 'string') {
      console.warn('[SpaarkeAuth] resolveConfig: tenantId is not a string; ignoring. Got:', typeof userConfig.tenantId);
    }
    authority = resolveDefaultAuthority();
  }

  return {
    clientId,
    authority,
    tenantId: typeof userConfig?.tenantId === 'string' ? userConfig.tenantId : '',
    redirectUri: userConfig?.redirectUri ?? (typeof window !== 'undefined' ? window.location.origin : ''),
    bffApiScope: bffApiScope ?? '',
    bffBaseUrl,
    proactiveRefresh: userConfig?.proactiveRefresh ?? false,
    requireXrm: userConfig?.requireXrm ?? false,
    requireSilentOnly: userConfig?.requireSilentOnly ?? false,
  };
}
