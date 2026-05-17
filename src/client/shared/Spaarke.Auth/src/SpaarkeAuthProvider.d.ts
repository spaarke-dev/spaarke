import type { IAuthConfig } from './types';
/**
 * Core auth provider — chains 6 token acquisition strategies:
 *   1. In-memory cache (~0.1ms, per-instance)
 *   2. sessionStorage cache (~0.1ms, shared across ALL same-origin iframes)
 *   3. Bridge token from parent frame walk (~0.1ms)
 *   4. Xrm platform with frame-walk
 *   5. MSAL acquireTokenSilent / ssoSilent
 *   6. MSAL popup (interactive fallback)
 *
 * On success, the token is written to ALL fast caches (in-memory + sessionStorage + bridge)
 * so that subsequent components on the same page or in child iframes get instant access
 * without triggering MSAL.
 */
export declare class SpaarkeAuthProvider {
  private readonly _config;
  private readonly _cacheStrategy;
  private readonly _sessionStorageStrategy;
  private readonly _bridgeStrategy;
  private readonly _xrmStrategy;
  private readonly _msalSilentStrategy;
  private readonly _msalPopupStrategy;
  private _refreshInterval;
  constructor(userConfig?: IAuthConfig);
  /** Acquire a token using the 6-strategy cascade. */
  getAccessToken(): Promise<string>;
  /**
   * Clear the in-memory token cache to force re-acquisition on next call.
   *
   * IMPORTANT: Does NOT clear sessionStorage. The sessionStorage token is shared
   * across all same-origin iframes. Clearing it on a single component's 401 retry
   * would cascade — every other component would lose its token and trigger MSAL
   * login prompts. Instead, only the per-instance in-memory cache is cleared,
   * and the next getAccessToken() call will try sessionStorage (which may still
   * have a valid token from another component).
   */
  clearCache(): void;
  /** Clear ALL caches including shared sessionStorage. Use only for explicit logout. */
  clearAllCaches(): void;
  /** Whether a cached token is currently available (synchronous check). */
  isAuthenticated(): boolean;
  /** Get the resolved config. */
  getConfig(): Required<IAuthConfig>;
  /**
   * Get the Azure AD tenant ID synchronously.
   *
   * Resolution order:
   *   1. Cached token JWT `tid` claim — works for ALL token sources (bridge, MSAL, Xrm)
   *   2. MSAL accounts[0].tenantId — only populated if MSAL was actually invoked
   *   3. Empty string
   */
  getCachedTenantId(): string;
  /**
   * Resolve Azure AD tenant ID (async).
   *
   * Resolution order:
   *   1. Cached token JWT `tid` claim — works for ALL token sources
   *   2. MSAL accounts[0].tenantId
   *   3. Xrm.organizationSettings.tenantId via frame-walk
   *   4. Empty string
   */
  getTenantId(): Promise<string>;
  /**
   * Extract the `tid` (tenant ID) claim from the cached access token JWT.
   * Works for ALL token sources: bridge, cache, Xrm, MSAL.
   */
  private _extractTidFromCachedToken;
  /** Stop proactive refresh (cleanup). */
  dispose(): void;
  private _cacheAndPublish;
  private _hasValidCache;
  private _isXrmAvailable;
  private _startProactiveRefresh;
}
//# sourceMappingURL=SpaarkeAuthProvider.d.ts.map
