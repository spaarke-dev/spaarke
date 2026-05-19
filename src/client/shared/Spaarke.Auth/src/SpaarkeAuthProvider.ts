import type { IAuthConfig, ITokenResult } from './types';
import { AuthError } from './errors';
import { resolveConfig, PROACTIVE_REFRESH_INTERVAL_MS } from './config';
import { CacheStrategy } from './strategies/CacheStrategy';
import { SessionStorageStrategy } from './strategies/SessionStorageStrategy';
import type { AuthStrategy } from './strategies/AuthStrategy';
import { BrowserMsalStrategy } from './strategies/BrowserMsalStrategy';

/**
 * Core auth provider (v2 — task 010).
 *
 * Composes:
 *   1. In-memory CacheStrategy            (per-instance, fastest read)
 *   2. SessionStorageStrategy             (same-origin iframe sharing — task 012 removes this)
 *   3. Pluggable AuthStrategy             (BrowserMsalStrategy for PCFs+CodePages; OfficeNaaStrategy in 080)
 *
 * The strategy parameter encapsulates the actual MSAL/NAA token acquisition logic.
 * This replaces the pre-v2 6-strategy cascade (Bridge + Xrm + MsalSilent + MsalPopup
 * inline) with a single strategy.acquire() call.
 */
export class SpaarkeAuthProvider {
  private readonly _config: Required<IAuthConfig>;
  private readonly _cacheStrategy: CacheStrategy;
  private readonly _sessionStorageStrategy: SessionStorageStrategy;
  private readonly _strategy: AuthStrategy;
  private _refreshInterval: ReturnType<typeof setInterval> | null = null;

  /**
   * @param userConfig Optional config overrides — merged with defaults via resolveConfig().
   * @param strategy   Pluggable token acquisition strategy. Defaults to BrowserMsalStrategy
   *                   for the common browser-hosted case. Pass OfficeNaaStrategy (task 080)
   *                   for Office Add-ins, or a test stub for unit tests.
   */
  constructor(userConfig?: IAuthConfig, strategy?: AuthStrategy) {
    this._config = resolveConfig(userConfig);

    if (this._config.requireXrm && !this._isXrmAvailable()) {
      throw new AuthError('Xrm is required but not available in this context', 'xrm_required');
    }

    this._cacheStrategy = new CacheStrategy();
    this._sessionStorageStrategy = new SessionStorageStrategy();
    this._strategy = strategy ?? new BrowserMsalStrategy(this._config);

    if (this._config.proactiveRefresh) {
      this._startProactiveRefresh();
    }
  }

  /** Acquire a token. Tries in-memory → sessionStorage → strategy.acquire(). */
  async getAccessToken(): Promise<string> {
    // 1. In-memory cache (fastest, per-instance)
    const cached = await this._cacheStrategy.tryAcquireToken();
    if (cached) return cached.accessToken;

    // 2. sessionStorage cache (shared across same-origin iframes — task 012 removes this layer)
    const sessionCached = await this._sessionStorageStrategy.tryAcquireToken();
    if (sessionCached) {
      console.info('[SpaarkeAuth] Token acquired via sessionStorage (cross-iframe cache)');
      this._cacheToken(sessionCached.accessToken, sessionCached.expiresOn);
      return sessionCached.accessToken;
    }

    // 3. Strategy (BrowserMsalStrategy / OfficeNaaStrategy / ...)
    try {
      const result = await this._strategy.acquire();
      if (result.accessToken) {
        console.info(`[SpaarkeAuth] Token acquired via ${this._strategy.name}`);
        this._cacheToken(result.accessToken, result.expiresOn);
        return result.accessToken;
      }
      console.warn(`[SpaarkeAuth] Strategy ${this._strategy.name} returned empty token`);
    } catch (err) {
      console.warn(`[SpaarkeAuth] Strategy ${this._strategy.name} failed:`, err);
    }

    console.error('[SpaarkeAuth] All token acquisition exhausted. Config:', {
      clientId: this._config.clientId?.substring(0, 8) + '...',
      bffApiScope: this._config.bffApiScope,
      authority: this._config.authority,
      bffBaseUrl: this._config.bffBaseUrl,
    });
    return '';
  }

  /**
   * Clear the in-memory token cache to force re-acquisition on next call.
   * Does NOT clear sessionStorage (would cascade other components on shared origin).
   * Use clearAllCaches() for explicit logout (INV-7).
   */
  clearCache(): void {
    this._cacheStrategy.clear();
  }

  /** Clear ALL caches including shared sessionStorage AND strategy-local state. Use for explicit logout. */
  clearAllCaches(): void {
    this._cacheStrategy.clear();
    this._sessionStorageStrategy.clear();
    this._strategy.clearCache();
  }

  /** Whether a cached token is currently available (synchronous check). */
  isAuthenticated(): boolean {
    return this._cacheStrategy.tryAcquireToken !== undefined && this._hasValidCache();
  }

  /** Get the resolved config. */
  getConfig(): Required<IAuthConfig> {
    return this._config;
  }

  /**
   * Get the Azure AD tenant ID synchronously.
   *
   * v2 simplification: relies on the JWT `tid` claim of the cached token.
   * Works regardless of which strategy provided the token (MSAL, NAA, etc.) since
   * every Entra-issued access token includes `tid`.
   */
  getCachedTenantId(): string {
    return this._extractTidFromCachedToken();
  }

  /**
   * Resolve Azure AD tenant ID (async).
   *
   * Resolution order:
   *   1. JWT `tid` claim from cached token — universal
   *   2. Xrm.organizationSettings.tenantId via frame-walk — fallback for Dataverse hosts
   */
  async getTenantId(): Promise<string> {
    const tid = this._extractTidFromCachedToken();
    if (tid) return tid;

    try {
      const frames: Window[] = [window];
      try {
        if (window.parent !== window) frames.push(window.parent);
      } catch {
        /* */
      }
      try {
        if (window.top && window.top !== window) frames.push(window.top);
      } catch {
        /* */
      }

      for (const frame of frames) {
        try {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const xrm = (frame as any).Xrm;
          const xrmTid = xrm?.Utility?.getGlobalContext?.()?.organizationSettings?.tenantId;
          if (xrmTid) return xrmTid;
        } catch {
          /* cross-origin */
        }
      }
    } catch {
      /* */
    }

    return '';
  }

  /** Stop proactive refresh (cleanup). */
  dispose(): void {
    if (this._refreshInterval) {
      clearInterval(this._refreshInterval);
      this._refreshInterval = null;
    }
  }

  private _cacheToken(token: string, expiresOn: number): void {
    this._cacheStrategy.store(token, expiresOn);
    this._sessionStorageStrategy.store(token, expiresOn);
  }

  private _extractTidFromCachedToken(): string {
    try {
      const token = this._cacheStrategy.getCachedToken();
      if (!token) return '';
      const parts = token.split('.');
      if (parts.length !== 3) return '';
      const payload = JSON.parse(atob(parts[1]));
      return payload.tid ?? '';
    } catch {
      return '';
    }
  }

  private _hasValidCache(): boolean {
    try {
      return this._cacheStrategy !== null;
    } catch {
      return false;
    }
  }

  private _isXrmAvailable(): boolean {
    if (typeof window === 'undefined') return false;
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
      return !!xrm?.Utility?.getGlobalContext;
    } catch {
      return false;
    }
  }

  private _startProactiveRefresh(): void {
    this._refreshInterval = setInterval(async () => {
      try {
        this._cacheStrategy.clear();
        await this.getAccessToken();
      } catch {
        // Swallow — proactive refresh is best-effort
      }
    }, PROACTIVE_REFRESH_INTERVAL_MS);
  }
}

// Eliminate unused-import warning for ITokenResult — the type is intentionally
// re-exported in case consumers were importing it transitively from this module.
export type { ITokenResult };
