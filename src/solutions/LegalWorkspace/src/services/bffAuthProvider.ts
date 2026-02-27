/**
 * bffAuthProvider.ts
 * Bearer token provider for BFF API calls from the workspace custom page.
 *
 * Token acquisition order:
 *   1. In-memory cache (fastest — no I/O)
 *   2. Window-level global from PCF bridge (__SPAARKE_BFF_TOKEN__)
 *   3. Parent frame global (custom page iframe inside PCF host)
 *   4. MSAL ssoSilent (uses existing Azure AD session — hidden iframe)
 *   5. Empty string (dev fallback — BFF dev endpoints may allow anonymous)
 *
 * MSAL is lazily initialized on first use to avoid blocking the initial render.
 * The configuration reuses the same Azure AD app registration as the PCF controls
 * (CLIENT_ID, TENANT_ID, REDIRECT_URI) — see msalConfig.ts.
 */

import { PublicClientApplication } from '@azure/msal-browser';
import { BFF_API_SCOPE } from '../config/bffConfig';
import { msalConfig } from '../config/msalConfig';

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
// MSAL singleton (lazy init)
// ---------------------------------------------------------------------------

let _msalInstance: PublicClientApplication | null = null;
let _msalInitPromise: Promise<void> | null = null;

/**
 * Lazily initialize the MSAL PublicClientApplication.
 * Safe to call multiple times — returns the same promise.
 */
async function ensureMsalInitialized(): Promise<PublicClientApplication | null> {
  if (_msalInstance) return _msalInstance;

  if (!_msalInitPromise) {
    _msalInitPromise = (async () => {
      try {
        const instance = new PublicClientApplication(msalConfig);
        await instance.initialize();
        await instance.handleRedirectPromise();
        _msalInstance = instance;
        console.info('[BffAuth] MSAL initialized successfully');
      } catch (err) {
        console.warn('[BffAuth] MSAL initialization failed — falling back to anonymous', err);
        _msalInstance = null;
      }
    })();
  }

  await _msalInitPromise;
  return _msalInstance;
}

/**
 * Acquire a token via MSAL ssoSilent (hidden iframe).
 * Returns the access token string or empty string on failure.
 */
async function acquireTokenViaMsal(): Promise<string> {
  const msal = await ensureMsalInitialized();
  if (!msal) return '';

  const scopes = [BFF_API_SCOPE];

  try {
    // Try acquireTokenSilent first (uses cached token / refresh token)
    const accounts = msal.getAllAccounts();
    if (accounts.length > 0) {
      const result = await msal.acquireTokenSilent({
        scopes,
        account: accounts[0],
      });
      if (result?.accessToken) {
        console.info('[BffAuth] Token acquired via MSAL silent (cached account)');
        return result.accessToken;
      }
    }

    // Fall back to ssoSilent (uses existing Azure AD session cookie)
    const ssoResult = await msal.ssoSilent({ scopes });
    if (ssoResult?.accessToken) {
      console.info('[BffAuth] Token acquired via MSAL ssoSilent');
      return ssoResult.accessToken;
    }
  } catch (err) {
    console.warn('[BffAuth] MSAL token acquisition failed', err);
  }

  return '';
}

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
 * Uses pre-set tokens from the PCF host, then MSAL ssoSilent, then anonymous.
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
      _cachedToken = {
        token: bridgeToken,
        expiresAt: Date.now() + 55 * 60 * 1000,
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

    // 4. Acquire token via MSAL (ssoSilent — uses existing Azure AD session)
    const msalToken = await acquireTokenViaMsal();
    if (msalToken) {
      _cachedToken = {
        token: msalToken,
        expiresAt: Date.now() + 55 * 60 * 1000,
      };
      return msalToken;
    }

    // 5. No token available — return empty (dev mode / anonymous)
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

// ---------------------------------------------------------------------------
// Tenant ID helper
// ---------------------------------------------------------------------------

/**
 * Resolve the Azure AD tenant ID.
 *
 * Resolution order:
 *   1. MSAL account tenantId (most reliable — populated after authentication)
 *   2. Xrm.Utility.getGlobalContext().organizationSettings.tenantId
 *   3. Empty string (caller must handle)
 */
export async function getTenantId(): Promise<string> {
  // 1. MSAL account
  const msal = await ensureMsalInitialized();
  if (msal) {
    const accounts = msal.getAllAccounts();
    if (accounts.length > 0 && accounts[0].tenantId) {
      return accounts[0].tenantId;
    }
  }

  // 2. Xrm global context
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window.top as any)?.Xrm ?? (window.parent as any)?.Xrm ?? (window as any)?.Xrm;
    const tid = xrm?.Utility?.getGlobalContext?.()?.organizationSettings?.tenantId;
    if (tid) return tid;
  } catch {
    /* cross-origin or unavailable — swallow */
  }

  return '';
}

// Re-export scope for convenience
export { BFF_API_SCOPE };
