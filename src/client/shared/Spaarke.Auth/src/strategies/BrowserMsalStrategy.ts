import type {
  AuthenticationResult,
  Configuration,
  PublicClientApplication,
} from '@azure/msal-browser';
import type { IAuthConfig, TokenResult } from '../types';
import type { AuthStrategy } from './AuthStrategy';

/** Buffer (ms) before token expiry to consider it stale. Matches config.TOKEN_EXPIRY_BUFFER_MS. */
const EXPIRY_BUFFER_MS = 5 * 60 * 1000;

/**
 * Resolve a login hint for `ssoSilent`.
 *
 * AAD matches `loginHint` against the **UPN** (e.g. `user@tenant.onmicrosoft.com`)
 * of currently-signed-in accounts. Passing the user's display name (e.g. "Ralph
 * Schroeder") fails with AADSTS50058 → ssoSilent returns null → popup fires.
 *
 * Resolution order:
 *   1. MSAL's own `getAllAccounts()[0].username` (UPN — authoritative when MSAL
 *      has any cached account)
 *   2. `Xrm.userSettings.userPrincipalName` via frame-walk (UPN per Dataverse SDK)
 *   3. `Xrm.userSettings.userName` (display name — last-resort, expected to fail
 *      AAD match on most tenants; included for compatibility with old hosts where
 *      `userName` happens to equal the UPN)
 */
function resolveLoginHint(msal: PublicClientApplication | null): string | undefined {
  if (msal) {
    const accounts = msal.getAllAccounts();
    if (accounts.length > 0 && accounts[0].username) {
      return accounts[0].username;
    }
  }

  if (typeof window === 'undefined') return undefined;

  const frames: Window[] = [window];
  try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
  try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      const settings = xrm?.Utility?.getGlobalContext?.()?.userSettings;
      const upn = settings?.userPrincipalName;
      if (upn && typeof upn === 'string') return upn;
      const userName = settings?.userName;
      if (userName && typeof userName === 'string') return userName;
    } catch {
      /* cross-origin */
    }
  }

  return undefined;
}

/**
 * Decode a JWT and return its expiry as a Unix-ms timestamp.
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
 * BrowserMsalStrategy — token acquisition for browser-hosted Spaarke surfaces
 * (Dataverse PCFs + Code Pages) via MSAL.js.
 *
 * Resolution order on `acquire()`:
 *   1. `acquireTokenSilent` with a cached MSAL account (refresh-token-backed)
 *   2. `ssoSilent` with a UPN login hint (uses AAD session cookie)
 *   3. `acquireTokenPopup` (interactive — last resort, expected NOT to fire in steady state)
 *
 * The returned token's `expiresOn` is the JWT `exp` claim (preferred) or MSAL's reported
 * `expiresOn` (fallback). Callers validate freshness against a 5-min buffer; if a token
 * acquisition somehow returns an already-stale token, an empty result is returned so
 * the caller can surface a diagnostic.
 *
 * Regression invariants (preserved by literal MSAL config lift from SpaarkeAuthProvider:44-68):
 *   - INV-1: cacheLocation 'localStorage'   — survives tab/browser close
 *   - INV-2: storeAuthStateInCookie true    — ssoSilent works under 3rd-party cookie blocking
 *   - INV-3: tenant-specific authority      — config.authority is resolved by resolveDefaultAuthority()
 *
 * v2 bug fix (vs. pre-v2 MsalSilentStrategy.resolveLoginHint):
 *   The pre-v2 code passed `Xrm.userSettings.userName` (display name) as loginHint,
 *   which AAD couldn't match (AADSTS50058) on tenants where userName != UPN. That bug
 *   was the proximate cause of popup-on-every-browser-startup. This impl prefers
 *   `getAllAccounts()[0].username` (always the UPN) and falls back to
 *   `userSettings.userPrincipalName` before the legacy `userName` field.
 */
export class BrowserMsalStrategy implements AuthStrategy {
  readonly name = 'browser-msal';

  private readonly _msalConfig: Configuration;
  private readonly _scope: string;
  private _instance: PublicClientApplication | null = null;
  private _initPromise: Promise<void> | null = null;

  constructor(config: Required<IAuthConfig>) {
    this._msalConfig = {
      auth: {
        clientId: config.clientId,
        authority: config.authority,
        redirectUri: config.redirectUri,
      },
      cache: {
        cacheLocation: 'localStorage',     // INV-1 — MUST be localStorage
        storeAuthStateInCookie: true,      // INV-2 — MUST be true for ssoSilent
      },
      system: {
        loggerOptions: {
          logLevel: 3, // Warning
          piiLoggingEnabled: false,
        },
      },
    };
    this._scope = config.bffApiScope;
  }

  async acquire(): Promise<TokenResult> {
    const msal = await this._ensureInitialized();
    if (!msal) return { accessToken: '', expiresOn: 0 };

    const scopes = [this._scope];

    // 1. acquireTokenSilent — refresh-token-backed silent renewal
    try {
      const accounts = msal.getAllAccounts();
      console.info('[BrowserMsalStrategy] cached accounts:', accounts.length, 'scope:', this._scope);
      if (accounts.length > 0) {
        const result = await msal.acquireTokenSilent({ scopes, account: accounts[0] });
        const token = this._validate(result);
        if (token) return token;
      }
    } catch (err) {
      console.warn('[BrowserMsalStrategy] acquireTokenSilent failed:', err);
    }

    // 2. ssoSilent with UPN hint
    try {
      const loginHint = resolveLoginHint(msal);
      console.info('[BrowserMsalStrategy] ssoSilent', loginHint ? `hint=${loginHint}` : '(no hint)');
      const result = await msal.ssoSilent({ scopes, loginHint });
      const token = this._validate(result);
      if (token) return token;
    } catch (err) {
      console.warn('[BrowserMsalStrategy] ssoSilent failed:', err);
    }

    // 3. acquireTokenPopup — last resort
    try {
      const loginHint = resolveLoginHint(msal);
      console.warn('[BrowserMsalStrategy] falling back to acquireTokenPopup (regression in steady state)');
      const result = await msal.acquireTokenPopup({ scopes, loginHint });
      const token = this._validate(result);
      if (token) return token;
    } catch (err) {
      console.warn('[BrowserMsalStrategy] acquireTokenPopup failed:', err);
    }

    return { accessToken: '', expiresOn: 0 };
  }

  clearCache(): void {
    if (!this._instance) return;
    // MSAL v3: clearCache() clears the entire cache for this PCA instance
    // (accounts, tokens, telemetry). Fire-and-forget — logout is the primary caller
    // and doesn't need to await per-account cleanup.
    void this._instance.clearCache().catch((err) => {
      console.warn('[BrowserMsalStrategy] clearCache failed:', err);
    });
  }

  /**
   * MSAL logout via popup. Clears the refresh token from `localStorage` AND ends
   * the Entra session (kills the session cookie). After this resolves, neither
   * `acquireTokenSilent` nor `ssoSilent` will succeed until the user re-auths.
   *
   * Popup-blocking fallback: if `logoutPopup` throws (browser blocked the popup,
   * or no foreground window), at least call `clearCache()` so local MSAL state
   * matches the user-intended logout. The Entra session may remain alive in this
   * degraded path; the user's next interactive login resyncs everything.
   */
  async logout(): Promise<void> {
    const msal = await this._ensureInitialized();
    if (!msal) return;

    const account = msal.getAllAccounts()[0];
    try {
      await msal.logoutPopup({ account });
    } catch (err) {
      console.warn(
        '[BrowserMsalStrategy] logoutPopup failed; falling back to clearCache. Entra session may persist.',
        err
      );
      try {
        await msal.clearCache();
      } catch {
        /* best-effort */
      }
    }
  }

  /** Expose the underlying MSAL instance so SpaarkeAuthProvider can resolve tenant ID from accounts. */
  getMsalInstance(): PublicClientApplication | null {
    return this._instance;
  }

  /**
   * Validate an MSAL AuthenticationResult: prefer the JWT `exp` claim (canonical),
   * fall back to MSAL's reported `expiresOn`. Reject if no access token, or if the
   * token is already within the 5-min expiry buffer.
   */
  private _validate(result: AuthenticationResult | null): TokenResult | null {
    if (!result?.accessToken) return null;
    const expFromJwt = decodeJwtExpMs(result.accessToken);
    const expFromMsal = result.expiresOn?.getTime() ?? 0;
    const expiresOn = expFromJwt || expFromMsal;
    if (!expiresOn) return null;
    if (expiresOn - Date.now() < EXPIRY_BUFFER_MS) {
      // ERROR (not WARN): MSAL handed us a token that's structurally near
      // its `exp` claim. This usually signals a real refresh-logic problem
      // upstream (cached refresh token is stale; clock skew; etc.) and the
      // operator should see it in Application Insights.
      console.error(
        '[BrowserMsalStrategy] acquired token already within expiry buffer; rejecting and falling through',
        { msToExpiry: expiresOn - Date.now(), bufferMs: EXPIRY_BUFFER_MS }
      );
      return null;
    }
    return { accessToken: result.accessToken, expiresOn };
  }

  private async _ensureInitialized(): Promise<PublicClientApplication | null> {
    if (this._instance) return this._instance;

    if (!this._initPromise) {
      this._initPromise = (async () => {
        try {
          // Dynamic import keeps MSAL out of the bundle if a non-browser strategy is used
          const { PublicClientApplication: PCA } = await import('@azure/msal-browser');
          const instance = new PCA(this._msalConfig);
          await instance.initialize();
          await instance.handleRedirectPromise();
          this._instance = instance;
        } catch (err) {
          console.warn('[BrowserMsalStrategy] MSAL initialization failed:', err);
          this._instance = null;
        }
      })();
    }

    await this._initPromise;
    return this._instance;
  }
}
