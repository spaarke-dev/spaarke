/** Configuration options for @spaarke/auth initialization. */
export interface IAuthConfig {
  /** Azure AD client ID. Defaults to window.__SPAARKE_MSAL_CLIENT_ID__ or built-in dev ID. */
  clientId?: string;
  /** Azure AD authority. Defaults to 'https://login.microsoftonline.com/organizations'. */
  authority?: string;
  /** Redirect URI for MSAL. Defaults to window.location.origin. */
  redirectUri?: string;
  /** BFF API scope. Defaults to 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'. */
  bffApiScope?: string;
  /** BFF API base URL. Defaults to window.__SPAARKE_BFF_URL__ or '/api'. */
  bffBaseUrl?: string;
  /** If true, start a proactive 4-minute token refresh interval. */
  proactiveRefresh?: boolean;
  /** If true, throw AuthError if Xrm is not available. */
  requireXrm?: boolean;
}

/**
 * Result returned by `AuthStrategy.acquire()`.
 *
 * `accessToken` is empty (`''`) when acquisition failed across all of the
 * strategy's internal mechanisms — callers should treat that as "no token"
 * rather than an exception. `expiresOn` is the JWT `exp` claim (preferred)
 * or the strategy-reported expiry (fallback).
 */
export interface TokenResult {
  /** The access token string. Empty when acquisition failed. */
  accessToken: string;
  /** Token expiry time (Unix ms). 0 when acquisition failed. */
  expiresOn: number;
  /** Optional tenant ID parsed from the token (JWT `tid` claim). */
  tenantId?: string;
}

/**
 * Signature of `authenticatedFetch`. Exposed as a type so React hook return
 * shapes and component props can reference it without importing the function
 * (avoids circular import patterns through `useAuth`).
 */
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

/** RFC 7807 ProblemDetails shape returned by the BFF API. */
export interface IProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [key: string]: unknown;
}

/** Window globals used for configuration. */
declare global {
  interface Window {
    __SPAARKE_MSAL_CLIENT_ID__?: string;
    __SPAARKE_BFF_URL__?: string;
    __SPAARKE_BFF_API_SCOPE__?: string;
  }
}
