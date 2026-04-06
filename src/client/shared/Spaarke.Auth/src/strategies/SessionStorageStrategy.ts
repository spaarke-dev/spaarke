import type { ITokenResult, ITokenStrategy } from '../types';
import { TOKEN_EXPIRY_BUFFER_MS } from '../config';

/**
 * SessionStorage token cache strategy — shared across ALL same-origin iframes.
 *
 * All Dataverse web resources (PCFs, Code Pages, navigateTo dialogs) run under
 * the same origin (`https://org.crm.dynamics.com`), so they share sessionStorage.
 *
 * When ANY component acquires a token (via MSAL, bridge, or Xrm), it writes to
 * sessionStorage. Every subsequent component reads it instantly (~0.1ms) without
 * triggering MSAL at all — no popups, no ssoSilent, no race conditions.
 *
 * This eliminates the recurring "first-load auth failure" class of bugs caused by
 * each component having its own MSAL instance with its own empty cache.
 *
 * Lifecycle:
 *   - Survives iframe creation/destruction (navigateTo open/close)
 *   - Survives in-page navigation (SPA route changes)
 *   - Cleared on tab close (security appropriate — sessionStorage behavior)
 *   - Cleared on explicit clearCache() call (e.g., on 401 retry)
 *
 * @see ADR-009 — "MUST use sessionStorage for client-side token cache"
 */

const STORAGE_KEY = '__spaarke_bff_token_cache__';

interface IStoredToken {
  /** The raw access token string. */
  t: string;
  /** Token expiration timestamp (ms since epoch). */
  e: number;
}

export class SessionStorageStrategy implements ITokenStrategy {
  readonly name = 'session-storage' as const;

  async tryAcquireToken(): Promise<ITokenResult | null> {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return null;

      const stored: IStoredToken = JSON.parse(raw);
      if (!stored.t || !stored.e) return null;

      // Check expiry with buffer
      if (Date.now() >= stored.e - TOKEN_EXPIRY_BUFFER_MS) {
        sessionStorage.removeItem(STORAGE_KEY);
        return null;
      }

      return {
        accessToken: stored.t,
        expiresOn: stored.e,
        source: 'session-storage',
      };
    } catch {
      // sessionStorage unavailable (private browsing edge cases) or corrupt entry
      return null;
    }
  }

  /**
   * Write a token to sessionStorage for cross-iframe sharing.
   * Called from SpaarkeAuthProvider._cacheAndPublish() after ANY successful acquisition.
   */
  store(token: string, expiresOn: number): void {
    try {
      const entry: IStoredToken = { t: token, e: expiresOn };
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(entry));
    } catch {
      // sessionStorage full or unavailable — non-fatal, in-memory cache still works
    }
  }

  /** Clear the sessionStorage token. Called on 401 retry or explicit cache clear. */
  clear(): void {
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // non-fatal
    }
  }
}
