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
 * `clearCache()` cascades to the inner strategy (use on logout / 401 retry).
 * `invalidate()` clears only the in-memory entry without touching the inner
 * strategy (use for proactive refresh — the inner may still serve silently).
 */
export class InMemoryCache implements AuthStrategy {
  readonly name: string;

  private _cached: TokenResult | null = null;

  constructor(private readonly _inner: AuthStrategy) {
    this.name = `in-memory-cache(${_inner.name})`;
  }

  async acquire(): Promise<TokenResult> {
    if (this._cached && this._isFresh(this._cached)) {
      return this._cached;
    }

    const result = await this._inner.acquire();
    this._cached = result.accessToken && this._isFresh(result) ? result : null;
    return result;
  }

  clearCache(): void {
    this._cached = null;
    this._inner.clearCache();
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
