import type { IAuthConfig } from './types';

/** Default Azure AD client ID (DSM-SPE Dev 2 code pages). */
const DEFAULT_CLIENT_ID = '170c98e1-d486-4355-bcbe-170454e0207c';

/** Default multi-tenant authority (never hardcode a tenant ID). */
const DEFAULT_AUTHORITY = 'https://login.microsoftonline.com/organizations';

/** Default BFF API scope for user impersonation. */
const DEFAULT_BFF_SCOPE = 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation';

/** Buffer (ms) before token expiry to consider it stale. */
export const TOKEN_EXPIRY_BUFFER_MS = 5 * 60 * 1000; // 5 minutes

/** Proactive refresh interval (ms). */
export const PROACTIVE_REFRESH_INTERVAL_MS = 4 * 60 * 1000; // 4 minutes

/** Resolve the full config, merging user overrides with defaults and window globals. */
export function resolveConfig(userConfig?: IAuthConfig): Required<IAuthConfig> {
  return {
    clientId:
      userConfig?.clientId ??
      (typeof window !== 'undefined' ? window.__SPAARKE_MSAL_CLIENT_ID__ : undefined) ??
      DEFAULT_CLIENT_ID,
    authority: userConfig?.authority ?? DEFAULT_AUTHORITY,
    redirectUri: userConfig?.redirectUri ?? (typeof window !== 'undefined' ? window.location.origin : ''),
    bffApiScope: userConfig?.bffApiScope ?? DEFAULT_BFF_SCOPE,
    bffBaseUrl:
      userConfig?.bffBaseUrl ?? (typeof window !== 'undefined' ? window.__SPAARKE_BFF_URL__ : undefined) ?? '',
    proactiveRefresh: userConfig?.proactiveRefresh ?? false,
    requireXrm: userConfig?.requireXrm ?? false,
  };
}
