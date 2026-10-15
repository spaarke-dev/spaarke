import type { IAuthConfig } from './types';
import { AuthError } from './errors';
import { resolveConfig, discoverTenantSync, PROACTIVE_REFRESH_INTERVAL_MS } from './config';
import { normalizeTenant, tenantFromAuthority } from './tenant';
import type { AuthStrategy } from './strategies/AuthStrategy';
import { BrowserMsalStrategy } from './strategies/BrowserMsalStrategy';
import { InMemoryCache } from './strategies/InMemoryCache';
import { broadcastLogout, onAuthBroadcast } from './broadcastChannel';
import { VERSION } from './version';

/**
 * Core auth provider (v2 — tasks 010, 011, 012).
 *
 * Composes a single InMemoryCache wrapping a pluggable AuthStrategy. The cache
 * gates every acquire() by JWT `exp` (5-minute buffer); on miss it delegates to
 * the strategy and stores the fresh result. Cross-tab/iframe persistence is
 * provided by MSAL.localStorage at the BrowserMsalStrategy layer (INV-1).
 *
 * The strategy parameter is pluggable: BrowserMsalStrategy for PCFs + Code
 * Pages (default); OfficeNaaStrategy for Office Add-ins (task 080).
 */
export class SpaarkeAuthProvider {
  private readonly _config: Required<IAuthConfig>;
  private readonly _cache: InMemoryCache;
  private _refreshInterval: ReturnType<typeof setInterval> | null = null;
  private _disposeBroadcastListener: (() => void) | null = null;
  /**
   * Set when initAuth() replaces this provider (#1453). Code that captured this
   * instance before the replacement (e.g. a useAuth() result from an earlier
   * render) is forwarded to the replacement instead of continuing on the old,
   * non-tenant authority.
   */
  private _successor: SpaarkeAuthProvider | null = null;

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

    const inner = strategy ?? new BrowserMsalStrategy(this._config);
    this._cache = new InMemoryCache(inner);

    console.info(`[SpaarkeAuth] v${VERSION} initialized`);

    // Listen for cross-context logout broadcasts. When another tab/iframe logs
    // out, cascade-clear our caches so MSAL state, in-memory state, and the
    // strategy's local state all match the user-intended outcome.
    this._disposeBroadcastListener = onAuthBroadcast(msg => {
      if (msg.type === 'logout') {
        console.info('[SpaarkeAuth] Received logout broadcast; cascading clearAllCaches');
        this.clearAllCaches();
      }
    });

    if (this._config.proactiveRefresh) {
      this._startProactiveRefresh();
    }
  }

  /** Acquire a token via the in-memory cache, falling through to the strategy on miss. */
  async getAccessToken(): Promise<string> {
    if (this._successor) return this._successor.getAccessToken();
    try {
      const result = await this._cache.acquire();
      if (result.accessToken) {
        console.info(`[SpaarkeAuth] Token acquired via ${this._cache.name}`);
        return result.accessToken;
      }
    } catch (err) {
      console.warn(`[SpaarkeAuth] ${this._cache.name} failed:`, err);
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
   * Invalidate the in-memory token cache to force re-acquisition on the next call.
   * Does NOT cascade to the inner strategy. Use clearAllCaches() for explicit logout (INV-7).
   */
  clearCache(): void {
    if (this._successor) return this._successor.clearCache();
    this._cache.invalidate();
  }

  /** Clear the in-memory cache AND cascade to the strategy. Use for explicit logout. */
  clearAllCaches(): void {
    if (this._successor) return this._successor.clearAllCaches();
    this._cache.clearCache();
  }

  /**
   * Full logout flow:
   *   1. Broadcast `{type:'logout'}` to all same-origin contexts (so other tabs
   *      drop their in-memory caches before the user's network of components
   *      starts firing failed requests).
   *   2. Clear in-memory + strategy-local cache state in THIS context.
   *   3. Drive the strategy through its real logout flow (MSAL.logoutPopup for
   *      BrowserMsalStrategy — clears refresh token + ends Entra session).
   *
   * Server-side OBO cache invalidation is intentionally NOT performed (per the
   * slim Phase A scope decision documented in projects/spaarke-auth-v2-and-hardening
   * task 014 notes). Real server-side revocation lands with CAE in Phase D task 061.
   */
  async logout(): Promise<void> {
    if (this._successor) return this._successor.logout();
    broadcastLogout();
    await this._cache.logout();
  }

  /** Whether a cached token is currently available (synchronous check). */
  isAuthenticated(): boolean {
    if (this._successor) return this._successor.isAuthenticated();
    return this._cache.getCachedToken() !== null;
  }

  /** Get the resolved config. */
  getConfig(): Required<IAuthConfig> {
    if (this._successor) return this._successor.getConfig();
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
    if (this._successor) return this._successor.getCachedTenantId();
    return this._extractTidFromCachedToken();
  }

  /**
   * Resolve Azure AD tenant ID (async).
   *
   * Resolution order:
   *   1. JWT `tid` claim from cached token — universal
   *   2. the tenant of this provider's authority
   *   3. window.__SPAARKE_TENANT_ID__ → persisted runtime config →
   *      Xrm.organizationSettings.tenantId — fallback for Dataverse hosts
   *      (order: "TENANT PRECEDENCE" in tenant.ts)
   * Every value is validated (#1453); '' when none is usable.
   */
  async getTenantId(): Promise<string> {
    if (this._successor) return this._successor.getTenantId();
    const tid = normalizeTenant(this._extractTidFromCachedToken());
    if (tid) return tid;

    const fromAuthority = tenantFromAuthority(this._config.authority);
    if (fromAuthority) return fromAuthority;

    return discoverTenantSync()?.tenant ?? '';
  }

  /**
   * Cascade-clean all instance state: proactive-refresh interval, broadcast
   * listener, in-memory cache, and any strategy-local cache. Called by
   * `initAuth()` on re-initialization to prevent leaks of the prior MSAL
   * instance and listener.
   *
   * @param options.preserveSharedCache When true, leave the strategy's own
   *   cache (MSAL localStorage) alone. `initAuth()` sets it when replacing a
   *   provider with one for the SAME clientId: both share that cache, and
   *   clearing it would sign the new provider out (#1453).
   */
  dispose(options?: { preserveSharedCache?: boolean }): void {
    // A superseded provider shares its MSAL cache with its successor: never clear it.
    const preserveSharedCache = options?.preserveSharedCache || this._successor !== null;
    if (this._refreshInterval) {
      clearInterval(this._refreshInterval);
      this._refreshInterval = null;
    }
    if (this._disposeBroadcastListener) {
      this._disposeBroadcastListener();
      this._disposeBroadcastListener = null;
    }
    if (preserveSharedCache) {
      this._cache.invalidate();
    } else {
      this._cache.clearCache();
    }
  }

  /**
   * Resolves once this provider has no token acquisition in flight (e.g. an open
   * sign-in popup), whatever its outcome.
   */
  whenIdle(): Promise<void> {
    return this._cache.whenIdle();
  }

  /**
   * @internal Called by initAuth() when it replaces this provider with one for the
   * same clientId (#1453). Stops this instance's timers and listener, keeps the
   * shared MSAL cache, and forwards every later call to `next`.
   */
  supersede(next: SpaarkeAuthProvider): void {
    if (next === this) return;
    this.dispose({ preserveSharedCache: true });
    this._successor = next;
  }

  private _extractTidFromCachedToken(): string {
    try {
      const token = this._cache.getCachedToken();
      if (!token) return '';
      const parts = token.split('.');
      if (parts.length !== 3) return '';
      const payload = JSON.parse(atob(parts[1]));
      return payload.tid ?? '';
    } catch {
      return '';
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
      // Proactive refresh must only REFRESH an existing session — never INITIATE
      // one (2026-08-10). With no cached token there is nothing to refresh, and a
      // cold acquire here would fall through the strategy's silent chain to an
      // interactive `acquireTokenPopup` fired from a BACKGROUND timer — out of any
      // user gesture (violates ADR-028 INV-5) and, with concurrent callers,
      // colliding as MSAL `interaction_in_progress`. Gate on an existing session:
      // when one is present, the re-acquire below is refresh-token-backed and stays
      // silent; when absent, skip until an on-demand (user-driven) acquisition
      // establishes the session.
      if (!this.isAuthenticated()) return;
      try {
        this._cache.invalidate();
        await this.getAccessToken();
      } catch {
        // Swallow — proactive refresh is best-effort
      }
    }, PROACTIVE_REFRESH_INTERVAL_MS);
  }
}
