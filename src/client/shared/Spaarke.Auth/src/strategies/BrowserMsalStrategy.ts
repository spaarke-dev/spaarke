import type { AccountInfo, AuthenticationResult, Configuration, PublicClientApplication } from '@azure/msal-browser';
import type { IAuthConfig, TokenResult } from '../types';
import type { AuthStrategy } from './AuthStrategy';
import { normalizeTenant, sameTenant, tenantFromAuthority } from '../tenant';

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
 *   1. `username` of the account chosen by `BrowserMsalStrategy._pickAccount`
 *      (UPN — authoritative when MSAL has any cached account; the account that
 *      matches the resolved tenant, not merely the first one cached — #1453)
 *   2. `Xrm.userSettings.userPrincipalName` via frame-walk (UPN per Dataverse SDK)
 *
 * If neither source yields a UPN, returns `undefined` — `ssoSilent` will then
 * try to resolve the user via the Entra session cookie alone. Returning a
 * non-UPN value (e.g. `userSettings.userName`, which is the display name in
 * most Dataverse contexts) would trigger AADSTS50058 and force a popup;
 * `undefined` is strictly better.
 *
 * NOTE on the pre-v2 bug: the original code in `MsalSilentStrategy` read
 * `userSettings.userName` as the hint, which is the display name. The first
 * v2 implementation (task 011) preserved a display-name fallback "for
 * compatibility with old hosts where userName happened to equal the UPN" —
 * but that fallback re-triggered the same AADSTS50058 in Code Page contexts
 * where `userPrincipalName` is undefined. Fix (task 011 post-mortem): the
 * fallback is removed entirely.
 */
function resolveLoginHint(account: AccountInfo | null): string | undefined {
  if (account?.username) {
    return account.username;
  }

  if (typeof window === 'undefined') return undefined;

  const frames: Window[] = [window];
  try {
    if (window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window) frames.push(window.top);
  } catch {
    /* cross-origin */
  }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      const settings = xrm?.Utility?.getGlobalContext?.()?.userSettings;
      const upn = settings?.userPrincipalName;
      if (upn && typeof upn === 'string') return upn;
      // INTENTIONALLY NO fallback to userSettings.userName — see header comment.
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
 * The cached account is chosen, not taken by position (#1453) — see _pickAccount.
 * A browser that also holds a home-tenant session (a B2B guest) would otherwise
 * retry the wrong account on every acquire and loop through popups; a browser
 * shared by two users of the same tenant would otherwise run silently as
 * whichever user signed in first.
 *
 * The returned token's `expiresOn` is the JWT `exp` claim (preferred) or MSAL's reported
 * `expiresOn` (fallback). Callers validate freshness against a 5-min buffer; if a token
 * acquisition somehow returns an already-stale token, an empty result is returned so
 * the caller can surface a diagnostic.
 *
 * Regression invariants (preserved by literal MSAL config lift from SpaarkeAuthProvider:44-68):
 *   - INV-1: cacheLocation 'localStorage'   — survives tab/browser close
 *   - INV-2: storeAuthStateInCookie true    — see the note at the cache config below
 *   - INV-3: tenant-specific authority      — config.authority comes from resolveConfig(), which
 *            never yields a non-tenant authority inside a Dataverse host (#1453)
 *
 * v2 bug fix (vs. pre-v2 MsalSilentStrategy.resolveLoginHint):
 *   The pre-v2 code passed `Xrm.userSettings.userName` (display name) as loginHint,
 *   which AAD couldn't match (AADSTS50058) on tenants where userName != UPN. That bug
 *   was the proximate cause of popup-on-every-browser-startup. This impl uses the
 *   chosen account's `username` (always the UPN), then `userSettings.userPrincipalName`,
 *   and never the display-name `userName` field.
 */
export class BrowserMsalStrategy implements AuthStrategy {
  readonly name = 'browser-msal';

  private readonly _msalConfig: Configuration;
  private readonly _scope: string;
  private readonly _requireSilentOnly: boolean;
  /** Tenant of the authority; '' when the authority is not tenant-specific. */
  private readonly _tenant: string;
  /**
   * The account that last produced a token on THIS page (homeAccountId + tenantId).
   * In memory only: MSAL's persisted active account is deliberately not used,
   * because on a shared browser it would carry user A's choice over to user B.
   */
  private _pageAccount: { homeAccountId: string; tenantId: string } | null = null;
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
        cacheLocation: 'localStorage', // INV-1 — MUST be localStorage
        // INV-2 — MUST stay true (pinned in spaarke-sso-binding.md). Per the MSAL docs it stores the
        // auth request state needed to validate auth flows in cookies as well; it does not by itself
        // make ssoSilent succeed (that depends on the tenant-specific authority, INV-3).
        storeAuthStateInCookie: true,
      },
      system: {
        loggerOptions: {
          logLevel: 3, // Warning
          piiLoggingEnabled: false,
        },
      },
    };
    this._scope = config.bffApiScope;
    this._requireSilentOnly = config.requireSilentOnly;
    this._tenant = tenantFromAuthority(config.authority) ?? normalizeTenant(config.tenantId) ?? '';
  }

  async acquire(): Promise<TokenResult> {
    const msal = await this._ensureInitialized();
    if (!msal) return { accessToken: '', expiresOn: 0 };

    const scopes = [this._scope];
    const account = this._pickAccount(msal);

    // 1. acquireTokenSilent — refresh-token-backed silent renewal
    try {
      console.info('[BrowserMsalStrategy] cached account:', account ? 'yes' : 'none', 'scope:', this._scope);
      if (account) {
        const result = await msal.acquireTokenSilent({ scopes, account });
        const token = this._accept(result);
        if (token) return token;
      }
    } catch (err) {
      console.warn('[BrowserMsalStrategy] acquireTokenSilent failed:', err);
    }

    // 2. ssoSilent with UPN hint
    try {
      const loginHint = resolveLoginHint(account);
      console.info('[BrowserMsalStrategy] ssoSilent', loginHint ? `hint=${loginHint}` : '(no hint)');
      const result = await msal.ssoSilent({ scopes, loginHint });
      const token = this._accept(result);
      if (token) return token;
    } catch (err) {
      console.warn('[BrowserMsalStrategy] ssoSilent failed:', err);
    }

    // 3. acquireTokenPopup — last resort
    //
    // Suppressed when `requireSilentOnly` is set (e.g. by a popup/child host
    // like WorkspaceLayoutWizard that does not want to surface an involuntary
    // sign-in dialog every time its window opens with an empty MSAL cache).
    // Per ADR-028 INV-5: a popup MUST only fire when the user explicitly
    // triggered an auth-dependent action. A wizard load is NOT explicit
    // user intent to authenticate.
    if (this._requireSilentOnly) {
      console.info(
        '[BrowserMsalStrategy] silent acquisition exhausted; acquireTokenPopup suppressed (requireSilentOnly=true)'
      );
      return { accessToken: '', expiresOn: 0 };
    }

    try {
      const loginHint = resolveLoginHint(account);
      console.warn('[BrowserMsalStrategy] falling back to acquireTokenPopup (regression in steady state)');
      const result = await msal.acquireTokenPopup({ scopes, loginHint });
      const token = this._accept(result);
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
    void this._instance.clearCache().catch(err => {
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

    const account = this._pickAccount(msal) ?? undefined;
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
   * Choose the cached account for silent acquisition and the login hint (#1453):
   *   1. the account that already produced a token on this page, if still cached;
   *   2. the only cached account in the resolved tenant (or, when none is in the
   *      tenant, the only cached account at all — a guest's home account can
   *      still be redeemed silently for the tenant);
   *   3. otherwise `null`: with several candidates the library cannot tell which
   *      is the signed-in user (Xrm exposes no UPN or Entra object id —
   *      `userSettings` has only the systemuser id and display name), so step 2
   *      of acquire() lets ssoSilent use the browser's own Entra session instead.
   * Outside a tenant authority (the degraded /organizations fallback) the first
   * cached account is used, as before.
   */
  private _pickAccount(msal: PublicClientApplication): AccountInfo | null {
    const accounts = msal.getAllAccounts();
    const page = this._pageAccount;
    if (page) {
      const same = accounts.find(a => a.homeAccountId === page.homeAccountId && sameTenant(a.tenantId, page.tenantId));
      if (same) return same;
    }
    if (!this._tenant) return accounts[0] ?? null;

    const inTenant = accounts.filter(a => sameTenant(a.tenantId, this._tenant));
    const candidates = inTenant.length > 0 ? inTenant : accounts;
    return candidates.length === 1 ? candidates[0] : null;
  }

  /** Validate a result and, when it is usable, remember its account for this page. */
  private _accept(result: AuthenticationResult | null): TokenResult | null {
    const token = this._validate(result);
    if (token && result?.account?.homeAccountId) {
      this._pageAccount = { homeAccountId: result.account.homeAccountId, tenantId: result.account.tenantId };
    }
    return token;
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
