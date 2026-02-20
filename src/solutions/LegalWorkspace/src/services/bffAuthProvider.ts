/**
 * bffAuthProvider.ts
 * Lightweight Bearer token provider for BFF API calls from the workspace custom page.
 *
 * The workspace runs as an HTML web resource inside Dataverse — MSAL is not bundled.
 * This module provides a token acquisition interface that:
 *   1. Uses a pre-set token if provided by the host (PCF bridge pattern)
 *   2. Falls back to no-auth for development (BFF dev endpoints allow anonymous)
 *
 * When migrating to full MSAL: replace getAccessToken() internals with
 * MsalAuthProvider.getInstance().getToken([BFF_API_SCOPE]).
 *
 * Token lifecycle follows the same pattern as UniversalQuickCreate:
 *   - Singleton instance
 *   - Cache with expiration buffer
 *   - 401 retry with cache clear
 */

import { BFF_API_SCOPE } from '../config/bffConfig';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface IBffAuthProvider {
  /** Acquire a Bearer token for BFF API calls. Returns empty string if unavailable. */
  getAccessToken(): Promise<string>;
  /** Clear cached token (call on 401 to force re-acquisition). */
  clearCache(): void;
  /** Whether a token is currently available. */
  isAuthenticated(): boolean;
}

// ---------------------------------------------------------------------------
// Token cache
// ---------------------------------------------------------------------------

interface TokenCacheEntry {
  token: string;
  expiresAt: number; // Unix epoch ms
}

const TOKEN_BUFFER_MS = 5 * 60 * 1000; // 5 minutes before expiry

// ---------------------------------------------------------------------------
// Singleton implementation
// ---------------------------------------------------------------------------

/**
 * Window-level property for pre-set token from PCF bridge.
 * The host PCF control can acquire a token via MSAL and pass it to the
 * custom page iframe via this global property.
 */
const GLOBAL_TOKEN_KEY = '__SPAARKE_BFF_TOKEN__';

let _cachedToken: TokenCacheEntry | null = null;

/**
 * BFF auth provider singleton.
 * Uses pre-set tokens from the PCF host or operates without auth in development.
 */
export const bffAuthProvider: IBffAuthProvider = {
  async getAccessToken(): Promise<string> {
    // 1. Check in-memory cache
    if (_cachedToken && Date.now() < _cachedToken.expiresAt - TOKEN_BUFFER_MS) {
      return _cachedToken.token;
    }

    // 2. Check window-level token from PCF bridge
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const bridgeToken = (window as any)[GLOBAL_TOKEN_KEY] as string | undefined;
    if (bridgeToken) {
      // Bridge tokens are typically valid for ~1 hour
      _cachedToken = {
        token: bridgeToken,
        expiresAt: Date.now() + 55 * 60 * 1000, // assume 55 min validity
      };
      return bridgeToken;
    }

    // 3. Check parent frame for token
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const parentToken = (window.parent as any)?.[GLOBAL_TOKEN_KEY] as string | undefined;
      if (parentToken) {
        _cachedToken = {
          token: parentToken,
          expiresAt: Date.now() + 55 * 60 * 1000,
        };
        return parentToken;
      }
    } catch {
      /* cross-origin — swallow */
    }

    // 4. No token available — return empty (dev mode / anonymous)
    // In production, the PCF host MUST set the token via bridge pattern.
    return '';
  },

  clearCache(): void {
    _cachedToken = null;
  },

  isAuthenticated(): boolean {
    if (_cachedToken && Date.now() < _cachedToken.expiresAt - TOKEN_BUFFER_MS) {
      return true;
    }
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return !!(window as any)[GLOBAL_TOKEN_KEY];
  },
};

// ---------------------------------------------------------------------------
// Authenticated fetch helper
// ---------------------------------------------------------------------------

/**
 * Performs a fetch request with BFF Bearer token authentication.
 * Includes automatic 401 retry with token cache clear.
 *
 * @param url Full URL to fetch
 * @param init Standard fetch RequestInit options
 * @returns Fetch Response
 */
export async function authenticatedFetch(
  url: string,
  init?: RequestInit
): Promise<Response> {
  const token = await bffAuthProvider.getAccessToken();

  const headers = new Headers(init?.headers);
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(url, { ...init, headers });

  // 401 retry: clear cache and try once more with fresh token
  if (response.status === 401 && token) {
    bffAuthProvider.clearCache();
    const freshToken = await bffAuthProvider.getAccessToken();
    if (freshToken && freshToken !== token) {
      const retryHeaders = new Headers(init?.headers);
      retryHeaders.set('Authorization', `Bearer ${freshToken}`);
      return fetch(url, { ...init, headers: retryHeaders });
    }
  }

  return response;
}

// Re-export scope for convenience
export { BFF_API_SCOPE };
