/** Configuration options for @spaarke/auth initialization. */
export interface IAuthConfig {
  /** Azure AD client ID. Defaults to window.__SPAARKE_MSAL_CLIENT_ID__; there is no built-in default (throws if absent). */
  clientId?: string;
  /**
   * Azure AD authority URL.
   *
   * If both `authority` and `tenantId` are provided, `authority` wins — but
   * only when its tenant segment is a valid single tenant (a GUID or a dotted
   * domain; `/organizations`, `/common`, `/consumers`, `/undefined`, `/null`
   * and malformed URLs are rejected and skipped).
   * If only `tenantId` is provided, authority is built as
   * `https://login.microsoftonline.com/{tenantId}`.
   * If neither yields a tenant, the library looks for one in
   * `Xrm.organizationSettings.tenantId` (frame-walk), `window.__SPAARKE_TENANT_ID__`
   * and the persisted runtime config; `initAuth()` then also tries the
   * `sprk_TenantId` env var and the BFF's `/api/config/client`. If all fail,
   * see `requireTenantAuthority`.
   */
  authority?: string;
  /**
   * Azure AD tenant GUID. Preferred over `authority` for consumers who already
   * have a tenant ID (e.g., from `resolveRuntimeConfig().tenantId`); the
   * library constructs the authority URL for them. Avoids leaking the
   * `login.microsoftonline.com/{tenant}` URL convention into every consumer.
   * Invalid values ('undefined', 'null', 'organizations', …) are ignored.
   */
  tenantId?: string;
  /** Redirect URI for MSAL. Defaults to window.location.origin. */
  redirectUri?: string;
  /** BFF API scope, e.g. 'api://{BFF app id}/user_impersonation'. No default — supply it from runtime config (sprk_BffApiAppId). */
  bffApiScope?: string;
  /** BFF API base URL. Defaults to window.__SPAARKE_BFF_URL__, else '' (relative URLs are then sent as-is). */
  bffBaseUrl?: string;
  /** If true, start a proactive 4-minute token refresh interval. */
  proactiveRefresh?: boolean;
  /** If true, throw AuthError if Xrm is not available. */
  requireXrm?: boolean;
  /**
   * If true, `BrowserMsalStrategy.acquire()` skips the interactive
   * `acquireTokenPopup` fallback (step 3) and returns an empty token result
   * when both silent paths fail. The caller is then expected to surface the
   * unauthenticated state gracefully (`authenticatedFetch` throws an
   * `AuthError` with code `no_token` instead of sending the request, or the
   * host shows a "Please reload" UI).
   *
   * Use case: hosts that launch in a popup / child window with their own
   * isolated MSAL cache (e.g. `WorkspaceLayoutWizard` opened via
   * `Xrm.Navigation.navigateTo({ target: 2 })`). The popup's empty cache
   * would otherwise force `acquireTokenPopup` on first `initAuth()`, which
   * violates ADR-028 INV-5 ("popup only when user explicitly triggers an
   * auth-dependent action"). Setting this flag accepts a degraded steady
   * state (BFF calls may 401 once until the silent path succeeds) in
   * exchange for no involuntary sign-in popups.
   *
   * Default: false (existing behavior — popup fallback is enabled).
   */
  requireSilentOnly?: boolean;
  /**
   * If true, refuse to fall back to the multi-tenant `/organizations`
   * authority: when no tenant can be resolved, `resolveConfig()` / `initAuth()`
   * throw an `AuthError` with code `tenant_unresolved`.
   *
   * Why (#1453): `/organizations` signs a B2B guest in to their HOME tenant,
   * where Spaarke's single-tenant app does not exist (AADSTS700016), so every
   * BFF call fails. Members never noticed because their home tenant is Spaarke's.
   *
   * Default: true inside a Dataverse host (Xrm reachable, or the page is served
   * from a Dataverse domain); false elsewhere, where the fallback is kept but
   * logged with `console.error`.
   */
  requireTenantAuthority?: boolean;
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
    /** Environment tenant, published by `createRuntimeConfigStore().setRuntimeConfig` (#1453). */
    __SPAARKE_TENANT_ID__?: string;
  }
}
