import type { IAuthConfig } from './types';

/** No default client ID — must be resolved from Dataverse env var sprk_MsalClientId. */
const DEFAULT_CLIENT_ID: string | undefined = undefined;

/** Default multi-tenant authority (never hardcode a tenant ID). */
const DEFAULT_AUTHORITY = 'https://login.microsoftonline.com/organizations';

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

  return {
    clientId,
    authority: userConfig?.authority ?? DEFAULT_AUTHORITY,
    redirectUri: userConfig?.redirectUri ?? (typeof window !== 'undefined' ? window.location.origin : ''),
    bffApiScope: bffApiScope ?? '',
    bffBaseUrl,
    proactiveRefresh: userConfig?.proactiveRefresh ?? false,
    requireXrm: userConfig?.requireXrm ?? false,
  };
}
