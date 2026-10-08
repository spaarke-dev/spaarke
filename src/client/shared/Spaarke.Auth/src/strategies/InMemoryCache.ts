import type { TokenResult } from '../types';
import type { AuthStrategy } from './AuthStrategy';

/** Buffer (ms) before token expiry to consider it stale. Matches config.TOKEN_EXPIRY_BUFFER_MS. */
const EXPIRY_BUFFER_MS = 5 * 60 * 1000;

/**
 * Decode a JWT and return its `exp` claim as a Unix-ms timestamp.
 * Returns 0 if the token is malformed or has no `exp` claim.
 */
function decodeJwtExpMs(jwt: string): number {
  try {
    const parts = jwt.split('.');
    if (parts.length !== 3) return 0;
    const payload = JSON.parse(atob(parts[1])) as { exp?: number };
    return typeof payload.exp === 'number' ? payload.exp * 1000 : 0;
  } catch {
    return 0;
  }
}

/**
 * InMemoryCache — per-instance token cache wrapping any AuthStrategy.
 *
 * Replaces the pre-v2 CacheStrategy + SessionStorageStrategy two-layer cascade.
 * Cross-tab and cross-iframe sharing is now handled by MSAL.localStorage at the
 * inner BrowserMsalStrategy layer (INV-1); the dedicated sessionStorage layer
 * was redundant and is removed in task 012.
 *
 * Freshness is determined by the JWT `exp` claim with a 5-minute buffer. The
 * buffer matches BrowserMsalStrategy's internal validation so cache hits and
 * freshly acquired tokens agree on what "fresh" means. If a strategy returns a
 * token without a decodable `exp` claim, the strategy-reported `expiresOn` is
 * used as a fallback.
 *
 * Failed acquisitions (inner returns an empty access token) are never cached.
 *
 * Concurrent cache misses share ONE in-flight inner acquisition (#1453): before,
 * N components fetching on a cold cache each ran the strategy's silent → popup
 * chain, so a user could get N sign-in popups (or MSAL `interaction_in_progress`).
 *
 * `clearCache()` cascades to the inner strategy (use on logout / dispose).
 * It also detaches any in-flight acquisition, whose result is then returned to
 * its own callers but not cached (a logout must not be undone by a late token).
 * `invalidate()` clears only the in-memory entry without touching the inner
 * strategy (use for proactive refresh and the 401 retry — the inner may still
 * serve silently). It keeps an in-flight acquisition shared: that acquisition started after the
 * cache was emptied, and detaching it could open a second popup alongside it.
 */
export class InMemoryCache implements AuthStrategy {
  readonly name: string;

  private _cached: TokenResult | null = null;
  private _inFlight: Promise<TokenResult> | null = null;
  /** Bumped by clearCache()/logout() so an acquisition started earlier cannot repopulate the cache. */
  private _generation = 0;

  constructor(private readonly _inner: AuthStrategy) {
    this.name = `in-memory-cache(${_inner.name})`;
  }

  acquire(): Promise<TokenResult> {
    if (this._cached && this._isFresh(this._cached)) {
      return Promise.resolve(this._cached);
    }
    if (this._inFlight) {
      return this._inFlight;
    }

    // Start the inner call on a microtask: if _inner.acquire() throws synchronously,
    // the helper's `finally` would otherwise run before this assignment and leave a
    // rejected promise stuck in _inFlight for every later caller.
    const generation = this._generation;
    this._inFlight = Promise.resolve().then(() => this._acquireFromInner(generation));
    return this._inFlight;
  }

  /**
   * Resolves once no acquisition is in flight (whatever its outcome). Used when
   * replacing a provider, so the new MSAL instance does not start an
   * interactive request while the old one's is still open (`interaction_in_progress`).
   */
  whenIdle(): Promise<void> {
    return this._inFlight
      ? this._inFlight.then(
          () => undefined,
          () => undefined
        )
      : Promise.resolve();
  }

  /**
   * One inner acquisition. Within a generation at most one runs (acquire() shares
   * it), so a matching generation means `_inFlight` is still this acquisition.
   */
  private async _acquireFromInner(generation: number): Promise<TokenResult> {
    try {
      const result = await this._inner.acquire();
      if (generation === this._generation) {
        this._cached = result.accessToken && this._isFresh(result) ? result : null;
      }
      return result;
    } finally {
      if (generation === this._generation) this._inFlight = null;
    }
  }

  clearCache(): void {
    this._drop();
    this._inner.clearCache();
  }

  async logout(): Promise<void> {
    this._drop();
    await this._inner.logout();
  }

  /**
   * Invalidate ONLY the in-memory cache without cascading to the inner strategy.
   * Used by proactive refresh to force the next `acquire()` to call the inner
   * strategy (which can still serve silently from its own caches — e.g., MSAL
   * acquireTokenSilent).
   */
  invalidate(): void {
    this._cached = null;
  }

  private _drop(): void {
    this._cached = null;
    this._inFlight = null;
    this._generation++;
  }

  /**
   * Synchronous accessor for the cached token string. Returns null if no token
   * is cached or the cached token has fallen outside the freshness buffer; the
   * stale entry is dropped on read.
   */
  getCachedToken(): string | null {
    if (!this._cached) return null;
    if (!this._isFresh(this._cached)) {
      this._cached = null;
      return null;
    }
    return this._cached.accessToken;
  }

  private _isFresh(token: TokenResult): boolean {
    const expFromJwt = decodeJwtExpMs(token.accessToken);
    const expiresOn = expFromJwt || token.expiresOn;
    if (!expiresOn) return false;
    return expiresOn - Date.now() >= EXPIRY_BUFFER_MS;
  }
}
