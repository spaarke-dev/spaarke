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

/** Result of a token acquisition attempt. */
export interface ITokenResult {
  /** The access token string. */
  accessToken: string;
  /** Token expiry time (Unix ms). */
  expiresOn: number;
  /** Which strategy provided the token. */
  source: TokenSource;
}

/** Identifies which strategy provided a token. */
export type TokenSource = 'bridge' | 'cache' | 'xrm' | 'msal-silent' | 'msal-popup';

/** Entry stored in the in-memory token cache. */
export interface TokenCacheEntry {
  accessToken: string;
  expiresOn: number;
}

/** Strategy interface — each token acquisition method implements this. */
export interface ITokenStrategy {
  readonly name: TokenSource;
  tryAcquireToken(): Promise<ITokenResult | null>;
}

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
    __SPAARKE_BFF_TOKEN__?: string;
    __SPAARKE_MSAL_CLIENT_ID__?: string;
    __SPAARKE_BFF_URL__?: string;
  }
}
