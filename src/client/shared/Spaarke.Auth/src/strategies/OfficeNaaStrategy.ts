import type {
  AccountInfo,
  AuthenticationResult,
  Configuration,
  IPublicClientApplication,
  PublicClientApplication,
} from '@azure/msal-browser';
import type { IAuthConfig, TokenResult } from '../types';
import type { AuthStrategy } from './AuthStrategy';
import { normalizeTenant, sameTenant, tenantFromAuthority } from '../tenant';

/** Buffer (ms) before token expiry to consider it stale. Matches config.TOKEN_EXPIRY_BUFFER_MS. */
const EXPIRY_BUFFER_MS = 5 * 60 * 1000;

/** An Entra tenant id. MSAL reports every cached account's `tenantId` in this form. */
const TENANT_GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The failure result of the `AuthStrategy` contract: no token, never a throw. */
const NO_TOKEN: TokenResult = { accessToken: '', expiresOn: 0 };

/**
 * The tenant an account signs in to: its ID token's `tid` first, then `tenantId` (#1571 review K4). Under NAA,
 * msal-browser builds the account from the Office host's account and takes `tenantId` from it, which for a B2B guest
 * is the HOME tenant; the ID token's `tid` is the tenant the token was issued in. Matching on `tenantId` alone refused
 * every guest under NAA.
 */
function accountTenant(account: AccountInfo): string {
  const tid = account.idTokenClaims?.tid;
  return typeof tid === 'string' && tid ? tid : account.tenantId;
}

/** MSAL `LogLevel.Info` — the level of the line that says whether the NAA bridge came up. */
const MSAL_LOG_LEVEL_INFO = 2;

/** The MSAL line (`NestedAppOperatingContext.initialize`) that reports whether the Office NAA bridge is available. */
const BRIDGE_AVAILABLE_LINE = /Nested App Auth Bridge available: (true|false)/;

/**
 * Whether an NAA interactive failure shows that NAA cannot serve this app on this host, so the standard popup client
 * should take over. msal-browser 3.x maps bridge statuses in `NestedAppAuthAdapter.fromBridgeError`:
 *   - Disabled, or NestedAppAuthUnavailable without a host code → `nested_app_auth_bridge_disabled`: yes. (With a
 *     host code MSAL keeps that code, which this does not recognise: NAA stays, and the user can sign in again.)
 *   - Transient / PersistentError → `ServerError(code, description)`: yes only when Entra refused the request (an
 *     `AADSTS…` code in the message: a registration without the broker redirect, a Conditional Access block).
 *   - UserCancel → `user_canceled` (the browser popup's spelling is `user_cancelled`), NoNetwork →
 *     `no_network_connectivity`, AccountUnavailable → `no_account_found`, UserInteractionRequired, and anything else:
 *     no. A cancel is an answer; the others are transient.
 */
function naaCannotServe(err: unknown): boolean {
  if (!err || typeof err !== 'object') return false;
  const e = err as { errorCode?: unknown; errorMessage?: unknown; message?: unknown; name?: unknown };
  if (e.errorCode === 'nested_app_auth_bridge_disabled') return true;
  if (e.name !== 'ServerError') return false;
  return /AADSTS\d+/.test(`${String(e.errorMessage ?? '')} ${String(e.message ?? '')}`);
}

/**
 * Decode a JWT and return its `exp` claim as a Unix-ms timestamp.
 * Returns 0 if the token is malformed or has no `exp` claim.
 *
 * Symmetric with BrowserMsalStrategy.decodeJwtExpMs — both strategies prefer the
 * JWT `exp` over MSAL's reported `expiresOn` so the InMemoryCache freshness
 * gate (also JWT-`exp`-based) agrees with what we just acquired.
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
 * Office Add-in extras layered on top of the standard `IAuthConfig`.
 *
 * `IAuthConfig` already carries `clientId`, `authority`/`tenantId`, `redirectUri`,
 * and `bffApiScope` — the OfficeNaaStrategy reuses those. Office-specific fields:
 *
 * - `fallbackRedirectUri`: the https popup redirect, used by the standard client
 *   (no NAA, or NAA without the bridge). Defaults to
 *   `${window.location.origin}/auth-callback.html`.
 * - `forceFallback`: lets callers explicitly skip NAA detection (used by tests
 *   and by hosts where NAA detection is unreliable but standard MSAL works).
 */
export interface IOfficeNaaConfig {
  /** Fallback MSAL redirect URI used when NAA is unavailable. */
  fallbackRedirectUri?: string;
  /** If true, skip NAA detection and go directly to fallback PublicClientApplication. */
  forceFallback?: boolean;
}

/**
 * Detect whether the current Office host supports Nested App Authentication (NAA), with Microsoft's documented
 * check: `Office.context.requirements.isSetSupported('NestedAppAuth', '1.1')`. That covers Outlook on the web and
 * new Outlook for Windows (both report `OfficeOnline`), classic Outlook and Office on Windows and Mac, without
 * version-number rules of our own.
 *
 * One exception stays on the standard popup: **Word on the web**. Microsoft supports NAA there only for documents
 * opened from SharePoint Online or OneDrive (whether SharePoint Embedded documents count is unconfirmed), and it needs
 * a login hint the strategy does not pass; Word on the web keeps the popup until a live test shows NAA works there.
 *
 * Until 2026-11-03 every `OfficeOnline` host was excluded, after a 2026-09-03 UAT in which NAA "did not engage" on the
 * web and Entra refused the popup with AADSTS7000471. The cause was the NAA config's `brk-multihub://` redirect:
 * when the bridge is not available, `createNestablePublicClientApplication` falls back to a standard client with the
 * SAME config, and a standard popup cannot use a `brk-` redirect. The NAA config now uses the https redirect (see
 * `_buildNaaConfig`), so a missing bridge ends in a working popup. Excluding `OfficeOnline` sent new Outlook to the
 * popup, whose request carries every login.microsoftonline.com cookie and SSO header of the browser profile (HTTP 400
 * "Request Too Long" on a machine with several work accounts).
 *
 * Returns false outside an Office host (e.g., unit tests, standalone browser).
 */
async function detectNaaSupport(): Promise<boolean> {
  try {
    // Office globals are provided by the Office.js script tag in the Add-in HTML.
    // Outside an Office host, Office is undefined and we cannot use NAA.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const OfficeRef = (globalThis as any).Office;
    if (typeof OfficeRef === 'undefined' || !OfficeRef?.context) {
      console.warn('[OfficeNaaStrategy] Not running in Office context — NAA unavailable');
      return false;
    }

    // Wait for Office to be ready (idempotent if already initialized).
    await new Promise<void>(resolve => {
      if (OfficeRef.onReady) {
        OfficeRef.onReady(() => resolve());
      } else {
        resolve();
      }
    });

    const diagnostics = OfficeRef.context.diagnostics;
    const platform = diagnostics?.platform ?? OfficeRef.context.platform;
    const host = OfficeRef.context.host ?? diagnostics?.host;
    const version: string = diagnostics?.version ?? '';
    const requirements = OfficeRef.context.requirements;
    const supported =
      typeof requirements?.isSetSupported === 'function' &&
      requirements.isSetSupported('NestedAppAuth', '1.1') === true;
    console.info(
      `[OfficeNaaStrategy] host=${host} platform=${platform} version=${version} NestedAppAuth1.1=${supported}`
    );

    if (!supported) return false;

    if (platform === OfficeRef.PlatformType?.OfficeOnline && host === OfficeRef.HostType?.Word) {
      console.info('[OfficeNaaStrategy] Word on the web: using the standard popup, not NAA');
      return false;
    }

    return true;
  } catch (err) {
    console.warn('[OfficeNaaStrategy] NAA detection threw — assuming no NAA:', err);
    return false;
  }
}

/**
 * OfficeNaaStrategy — token acquisition for Spaarke Office Add-ins (Outlook + Word)
 * via MSAL.js Nested App Authentication.
 *
 * NAA is the Microsoft-recommended pattern for Office Add-ins (replaces the legacy
 * Dialog API + Office SSO flows). When NAA is supported, MSAL is instantiated via
 * `createNestablePublicClientApplication`, which routes auth through the Office
 * host's identity broker (no popup, no redirect, no Dialog API).
 *
 * Acquisition order (same shape as BrowserMsalStrategy so the InMemoryCache
 * freshness/expiry contract is identical):
 *   1. `acquireTokenSilent` with the chosen cached MSAL account (under NAA also with none: the Office host then
 *      answers for its own signed-in account, Microsoft's documented NAA pattern)
 *   2. `acquireTokenPopup` with `loginHint` from the chosen account
 *      (NAA-backed PCA routes this through the Office broker — not a real popup)
 *
 * Silent-only and the user gesture (#1571): with `requireSilentOnly`, `acquire()` stops after step 1 and returns the
 * contract's no-token result — it never calls an interactive API. That covers every caller that is not a user's click:
 * the eager acquire at start-up, the proactive refresh, a 401 retry. The interactive step then runs only from
 * `acquireInteractive()`, which a host calls from a click (the Sign in button). A popup opened without a gesture is
 * blocked on Office on the web, and a broker prompt nobody asked for is worse.
 *
 * Which cached account (#1571): see `_pickAccount`. A B2B guest can hold accounts of several tenants; only one in the
 * authority's tenant is ever used, silently or as a login hint, and never "the first one cached".
 *
 * Falls back to a standard `PublicClientApplication` (popup to `fallbackRedirectUri`) when:
 *   - Office is not detected (e.g., standalone browser test page)
 *   - the host does not support `NestedAppAuth` 1.1, or is Word on the web (see `detectNaaSupport`)
 *   - Caller passes `forceFallback: true`
 * and, inside MSAL, when NAA is chosen but the Office bridge is not available: `createNestablePublicClientApplication`
 * then returns a standard client with the same config, so that config's redirect is the https one too.
 * `isNaaActive()` reports the bridge MSAL actually found, not the detection result.
 *
 * Strategy-agnostic API surface:
 *   - implements `AuthStrategy` (acquire / clearCache / logout)
 *   - composed by `SpaarkeAuthProvider` via `initAuth({ strategy: new OfficeNaaStrategy(...) })`
 *
 * MSAL config differences from BrowserMsalStrategy (these are intentional):
 *   - `cacheLocation: 'sessionStorage'` — Office Add-ins live in an iframe and
 *     `localStorage` is unavailable / unstable across host suspensions; session
 *     storage matches the documented Add-in storage guidance.
 *   - `storeAuthStateInCookie: false` — Add-ins don't share an Entra session
 *     cookie with the host; NAA broker handles state.
 *   - `supportsNestedAppAuth: true` + `navigateToLoginRequestUrl: false` —
 *     required when using `createNestablePublicClientApplication`.
 *   - `allowRedirectInIframe: true` — fallback PCA path; harmless under NAA.
 */
export class OfficeNaaStrategy implements AuthStrategy {
  readonly name = 'office-naa';

  private readonly _config: Required<IAuthConfig>;
  private readonly _options: IOfficeNaaConfig;
  private _instance: IPublicClientApplication | null = null;
  private _initPromise: Promise<void> | null = null;
  private _fallbackPromise: Promise<IPublicClientApplication | null> | null = null;
  private _isNaaActive = false;
  /** The error of the last interactive attempt (null when it succeeded); decides whether NAA can serve this app. */
  private _lastPopupError: unknown = null;
  /** `requireSilentOnly`: `acquire()` never calls an interactive API (#1571). */
  private readonly _requireSilentOnly: boolean;
  /** Tenant id of the authority; '' when the authority names no tenant id (then there is nothing to match). */
  private readonly _tenant: string;
  /**
   * The account that last produced a token on THIS page (homeAccountId + tenantId), in memory only. Keeps the pick
   * stable when several accounts of the authority's tenant are cached, e.g. after an interactive sign-in chose one.
   */
  private _pageAccount: { homeAccountId: string; tenantId: string } | null = null;
  /**
   * The token the last interactive sign-in returned, handed to the next `acquire()` once (#1571 review F2): the
   * provider's cache is then filled with the token the user just signed in for, not with a second silent request.
   */
  private _handoff: TokenResult | null = null;
  /** Set by `requestFreshToken()`: the next silent step passes `forceRefresh` (honoured by the standard client only). */
  private _forceNextRefresh = false;
  /** The access token this strategy last returned: the one a caller's request used. */
  private _lastIssued: string | null = null;
  /**
   * The token the server refused, recorded by `requestFreshToken()`. A silent result equal to it counts as a failed
   * silent step (#1571 fix round 2): under NAA, msal-browser 3.30.0 drops `forceRefresh` (the NAA silent path reads only
   * `claims` and `cacheLookupPolicy`, and `NestedAppAuthAdapter.toNaaTokenRequest` does not map it), so the host answers
   * with its cached copy of the refused token. Cleared when a different token is accepted, when a Sign in click
   * returns a token (even the same one), on sign-out and on `clearCache()` (which also runs on a logout broadcast).
   */
  private _refusedToken: string | null = null;

  /**
   * @param config Standard @spaarke/auth resolved config (clientId, authority,
   *               bffApiScope). `config.redirectUri` is not used: both MSAL configs use the
   *               https popup redirect (`options.fallbackRedirectUri`), and over the NAA bridge
   *               the Office host supplies the broker redirect itself.
   * @param options Office-specific overrides (fallback redirect URI; forced fallback).
   */
  constructor(config: Required<IAuthConfig>, options: IOfficeNaaConfig = {}) {
    this._config = config;
    this._options = options;
    this._requireSilentOnly = config.requireSilentOnly === true;
    const tenant = tenantFromAuthority(config.authority) ?? normalizeTenant(config.tenantId);
    this._tenant = tenant && TENANT_GUID_RE.test(tenant) ? tenant : '';
  }

  /**
   * Acquire a token for the `AuthStrategy` contract. With `requireSilentOnly` this is silent only and never calls an
   * interactive API: when no token can be had silently it returns the contract's no-token result
   * (`{ accessToken: '', expiresOn: 0 }`), and the host asks the user to sign in.
   */
  async acquire(): Promise<TokenResult> {
    const handoff = this._handoff;
    this._handoff = null;
    if (handoff && handoff.expiresOn > Date.now() + EXPIRY_BUFFER_MS) {
      this._lastIssued = handoff.accessToken;
      return handoff;
    }
    const forceRefresh = this._forceNextRefresh;
    this._forceNextRefresh = false;
    return this._acquire(!this._requireSilentOnly, forceRefresh);
  }

  /**
   * Acquire a token, prompting the user when no token can be had silently. Call it ONLY from a user gesture (a click
   * on Sign in): it may open a popup, which a browser blocks outside a gesture. This is the one interactive path when
   * the strategy was built with `requireSilentOnly`; the NAA-to-popup fallback below runs here unchanged.
   *
   * A user who clicks Sign in after the server refused the session must not be handed the refused token again
   * (#1571 review F1). The silent step passes `forceRefresh`, but only the standard client honours it: under NAA,
   * msal-browser 3.30.0 drops it and the Office host answers with its cached copy. So a silent result equal to the
   * token recorded by `requestFreshToken()` counts as a failed silent step, and the click goes on to
   * `acquireTokenPopup` (the broker's interactive call under NAA). The token it returns is also what the next
   * `acquire()` answers with, so the caller's cache is filled without a second request.
   */
  async acquireInteractive(): Promise<TokenResult> {
    this._handoff = null;
    this._forceNextRefresh = false;
    const token = await this._acquire(true, true);
    this._handoff = token.accessToken ? token : null;
    // The user's click is the answer, even when the broker hands back the token that was refused (a transient 401,
    // or the broker reissuing it): keeping it recorded would refuse every later silent acquire for its lifetime. If
    // the server refuses it again, the next 401 records it again (fix round 3).
    if (token.accessToken) this._refusedToken = null;
    return token;
  }

  /**
   * Call it before retrying a request the server answered 401 (#1571 review F1), with the token that request sent. The
   * next `acquire()` asks for a new token, still silently. The sent token is recorded as refused only while it is
   * still the token this strategy last issued: a late 401 for an older token (two requests refused at once, or a
   * proactive refresh mid-flight) never marks the newer, good token as refused (fix round 3). With no argument nothing
   * is recorded. A silent answer equal to the refused token is not used: a silent-only acquire then returns no token
   * (the host shows its sign-in prompt; nothing opens without a click), and only `acquireInteractive()` goes on to the
   * interactive step.
   */
  requestFreshToken(refusedToken?: string): void {
    this._handoff = null;
    this._forceNextRefresh = true;
    if (refusedToken && refusedToken === this._lastIssued) this._refusedToken = refusedToken;
  }

  private async _acquire(interactive: boolean, forceRefresh: boolean): Promise<TokenResult> {
    const msal = await this._ensureInitialized();
    if (!msal) return NO_TOKEN;
    const token = await this._tryAcquireWith(msal, interactive, forceRefresh);
    if (token) return token;

    // NAA cannot serve this app here (the host disabled the bridge, or Entra refused the brokered request: an app
    // registration without the broker redirect, a Conditional Access block): fall back once to the standard popup
    // client, the path these hosts used before NAA was turned on for them. Only for those failures: a cancel is an
    // answer, and a network drop or a missing account is transient; switching on them would leave the page on the
    // popup client for good, and the proactive refresh would then open popups. A silent-only acquire made no
    // interactive attempt, so it has no such failure to act on.
    if (interactive && this._isNaaActive && naaCannotServe(this._lastPopupError)) {
      const fallback = await this._switchToFallback();
      if (fallback) {
        const retried = await this._tryAcquireWith(fallback, true, forceRefresh);
        if (retried) return retried;
      }
    }
    return NO_TOKEN;
  }

  /**
   * Run the silent→popup acquisition ladder against a given MSAL instance. Shared by the primary
   * (NAA or fallback) attempt and the NAA-failure fallback retry so both paths validate/expire tokens
   * identically. Returns a validated TokenResult or null if both steps fail. With `interactive` false
   * the ladder stops after the silent step: no interactive API is called. `forceRefresh` is passed to the silent
   * step (the standard client honours it; NAA drops it). A silent result equal to the refused token is not used.
   */
  private async _tryAcquireWith(
    msal: IPublicClientApplication,
    interactive: boolean,
    forceRefresh: boolean
  ): Promise<TokenResult | null> {
    const scopes = [this._config.bffApiScope];
    const account = this._pickAccount(msal);

    // 1. acquireTokenSilent — works under both NAA and fallback PCA. Under NAA it also runs with no account: the
    //    Office host answers for its own signed-in user. The standard client needs a cached account.
    if (account || this._isNaaActive) {
      try {
        console.info(
          `[OfficeNaaStrategy] acquireTokenSilent naa=${this._isNaaActive} scope=${this._config.bffApiScope}` +
            (account ? '' : ' (host account)') +
            (forceRefresh ? ' (force refresh)' : '')
        );
        const result = await msal.acquireTokenSilent({ scopes, account: account ?? undefined, forceRefresh });
        if (this._refusedToken && result?.accessToken === this._refusedToken) {
          // Under NAA `forceRefresh` is dropped, and the host hands back its cached copy of the token the server refused.
          console.warn('[OfficeNaaStrategy] acquireTokenSilent returned the refused token; the silent step failed');
        } else {
          const token = this._accept(result);
          if (token) return token;
        }
      } catch (err) {
        console.warn('[OfficeNaaStrategy] acquireTokenSilent failed:', err);
      }
    }

    if (!interactive) {
      console.info('[OfficeNaaStrategy] silent acquisition exhausted; interactive sign-in suppressed (silent only)');
      return null;
    }

    // 2. acquireTokenPopup — under NAA this routes through the Office broker (no real popup);
    //    under fallback PCA it opens an interactive popup.
    this._lastPopupError = null;
    try {
      const loginHint = account?.username;
      console.info(
        `[OfficeNaaStrategy] acquireTokenPopup naa=${this._isNaaActive}` +
          (loginHint ? ` hint=${loginHint}` : ' (no hint)')
      );
      const result = await msal.acquireTokenPopup({
        scopes,
        loginHint,
        prompt: account ? undefined : 'select_account',
      });
      const token = this._accept(result);
      if (token) return token;
    } catch (err) {
      this._lastPopupError = err;
      console.warn('[OfficeNaaStrategy] acquireTokenPopup failed:', err);
    }

    return null;
  }

  /**
   * Replace the NAA client with the standard popup client for the rest of this page's life (a reload detects again).
   * One switch even when several acquisitions fail at once.
   */
  private _switchToFallback(): Promise<IPublicClientApplication | null> {
    if (!this._fallbackPromise) {
      this._fallbackPromise = (async () => {
        try {
          console.warn('[OfficeNaaStrategy] NAA token request failed; switching to the standard popup client');
          const msalModule = await import('@azure/msal-browser');
          const pca: PublicClientApplication = new msalModule.PublicClientApplication(this._buildFallbackConfig());
          await pca.initialize();
          await this._drainRedirectIfAny(pca);
          this._instance = pca;
          this._isNaaActive = false;
          return pca;
        } catch (err) {
          console.warn('[OfficeNaaStrategy] standard popup client could not be created:', err);
          return null;
        }
      })();
    }
    return this._fallbackPromise;
  }

  clearCache(): void {
    this._pageAccount = null;
    this._handoff = null;
    this._forceNextRefresh = false;
    this._refusedToken = null;
    this._lastIssued = null;
    if (!this._instance) return;
    // Under NAA, MSAL's clearCache() is unsupported (it throws), so the local cache is cleared directly.
    if (this._isNaaActive) {
      this._clearMsalSessionStorage();
      return;
    }
    // MSAL v3 clearCache() — clears accounts + tokens for this PCA instance.
    // Fire-and-forget per AuthStrategy contract; the SpaarkeAuthProvider's
    // own logout() awaits the full chain.
    void this._instance.clearCache().catch(err => {
      console.warn('[OfficeNaaStrategy] clearCache failed:', err);
    });
  }

  /**
   * Sign the user out.
   *
   * Under NAA the Office host owns the account: MSAL's `logoutPopup` and `clearCache` are unsupported there (both
   * throw in msal-browser 3.x), so this clears the add-in's MSAL session cache directly. The host stays signed in, so
   * the next sign-in may be silent. Under the standard client this opens an MSAL logout popup; if the popup is
   * blocked / unavailable, falls back to `clearCache()` so the local cache state matches the user-intended outcome —
   * the Entra session may remain alive in that degraded path; the user's next interactive login resyncs.
   */
  async logout(): Promise<void> {
    this._handoff = null;
    this._forceNextRefresh = false;
    this._refusedToken = null;
    this._lastIssued = null;
    const msal = await this._ensureInitialized();
    if (!msal) return;

    if (this._isNaaActive) {
      this._pageAccount = null;
      this._clearMsalSessionStorage();
      return;
    }

    const account = this._pickAccount(msal);
    this._pageAccount = null;
    if (!account) {
      // No account this strategy would sign in with (none cached, or none in the authority's tenant): nothing to end
      // at Entra, but the local cache still goes, so sign-out leaves no account behind.
      try {
        await msal.clearCache();
      } catch {
        /* best-effort */
      }
      return;
    }

    try {
      await msal.logoutPopup({ account });
    } catch (err) {
      console.warn(
        '[OfficeNaaStrategy] logoutPopup failed; falling back to clearCache. Entra session may persist.',
        err
      );
      try {
        await msal.clearCache();
      } catch {
        /* best-effort */
      }
    }
  }

  /** Whether the active MSAL instance was created via `createNestablePublicClientApplication`. */
  isNaaActive(): boolean {
    return this._isNaaActive;
  }

  /** Expose the underlying MSAL instance for callers that need account/idToken claims. */
  getMsalInstance(): IPublicClientApplication | null {
    return this._instance;
  }

  /**
   * Validate an MSAL AuthenticationResult: prefer the JWT `exp` claim (canonical),
   * fall back to MSAL's reported `expiresOn`. Reject if no access token, or if the
   * token is already within the 5-min expiry buffer (symmetric with BrowserMsalStrategy).
   */
  private _validate(result: AuthenticationResult | null): TokenResult | null {
    if (!result?.accessToken) return null;
    const expFromJwt = decodeJwtExpMs(result.accessToken);
    const expFromMsal = result.expiresOn?.getTime() ?? 0;
    const expiresOn = expFromJwt || expFromMsal;
    if (!expiresOn) return null;
    if (expiresOn - Date.now() < EXPIRY_BUFFER_MS) {
      // ERROR (not WARN): MSAL handed us a token that's structurally near its
      // `exp` claim. Same diagnostic policy as BrowserMsalStrategy — this
      // surfaces stale-refresh / clock-skew problems to Application Insights.
      console.error('[OfficeNaaStrategy] acquired token already within expiry buffer; rejecting and falling through', {
        msToExpiry: expiresOn - Date.now(),
        bufferMs: EXPIRY_BUFFER_MS,
      });
      return null;
    }
    return { accessToken: result.accessToken, expiresOn };
  }

  /**
   * Clear a stale MSAL "interaction in progress" lock from session storage. MSAL sets
   * `msal.<clientId>.interaction.status` = `interaction_in_progress` while an interactive request runs
   * and clears it on completion — but a popup the user closes / a request that errored mid-flight (e.g.
   * the prior AADSTS7000471 attempts) can leave it stuck, after which every subsequent interactive call
   * throws `interaction_in_progress`. This removes ONLY that lock key (tokens + accounts untouched) so a
   * fresh attempt can proceed. Best-effort; never throws.
   */
  private _clearStaleInteractionLock(): void {
    try {
      if (typeof sessionStorage === 'undefined') return;
      for (const key of Object.keys(sessionStorage)) {
        if (key.includes('interaction.status')) {
          sessionStorage.removeItem(key);
        }
      }
    } catch {
      /* best-effort */
    }
  }

  /**
   * Remove this add-in's MSAL cache from session storage: every `msal.*` key (the key indexes, interaction state) and
   * the account and token entries those indexes list (`msal.account.keys`, `msal.token.keys.<clientId>`). Used under
   * NAA, where MSAL offers no way to clear its own cache. Best-effort; never throws.
   */
  private _clearMsalSessionStorage(): void {
    try {
      if (typeof sessionStorage === 'undefined') return;
      const listed = new Set<string>();
      const collect = (indexKey: string): void => {
        try {
          const value: unknown = JSON.parse(sessionStorage.getItem(indexKey) ?? 'null');
          const lists = Array.isArray(value) ? [value] : value && typeof value === 'object' ? Object.values(value) : [];
          for (const list of lists) {
            if (Array.isArray(list)) list.forEach(key => typeof key === 'string' && listed.add(key));
          }
        } catch {
          /* an unreadable index lists nothing */
        }
      };
      collect('msal.account.keys');
      collect(`msal.token.keys.${this._config.clientId}`);
      for (const key of Object.keys(sessionStorage)) {
        if (key.startsWith('msal.') || listed.has(key)) sessionStorage.removeItem(key);
      }
    } catch (err) {
      console.warn('[OfficeNaaStrategy] clearing the MSAL session cache failed:', err);
    }
  }

  /**
   * Choose the cached account for silent acquisition and the login hint (#1571), never by position:
   *   1. the account that already produced a token on this page, if it is still cached and in the authority's tenant;
   *   2. the only cached account in the authority's tenant;
   *   3. otherwise `null` — no account in that tenant, or several with nothing to tell them apart. No other account is
   *      used, not even as a login hint: a B2B guest's home-tenant account, or another user's, is never picked. The
   *      silent step then has no account (under NAA the Office host answers for its own user), and only a user's click
   *      can sign in.
   * An account's tenant is its ID token's `tid`, else its `tenantId` (`accountTenant`: a guest under NAA).
   * When the authority names no tenant id (not a tenant GUID), there is nothing to match: the only cached account is
   * used, as before, and with several none is.
   */
  private _pickAccount(msal: IPublicClientApplication): AccountInfo | null {
    const accounts = msal.getAllAccounts();
    const inScope = this._tenant ? accounts.filter(a => sameTenant(accountTenant(a), this._tenant)) : accounts;
    const page = this._pageAccount;
    if (page) {
      const same = inScope.find(
        a => a.homeAccountId === page.homeAccountId && sameTenant(accountTenant(a), page.tenantId)
      );
      if (same) return same;
    }
    return inScope.length === 1 ? (inScope[0] ?? null) : null;
  }

  /**
   * The cached account this strategy signs in with — the same choice as the silent step (`_pickAccount`), so a host
   * that shows or sends the account's tenant or name never reports a different account than the token's. Null when
   * none qualifies.
   */
  getAccount(): AccountInfo | null {
    return this._instance ? this._pickAccount(this._instance) : null;
  }

  /**
   * Validate a result and, when it is usable, remember its account as this page's account and the token as the last
   * one returned. A usable token other than the refused one clears the refused record.
   */
  private _accept(result: AuthenticationResult | null): TokenResult | null {
    const token = this._validate(result);
    if (token) {
      this._lastIssued = token.accessToken;
      if (token.accessToken !== this._refusedToken) this._refusedToken = null;
    }
    if (token && result?.account?.homeAccountId) {
      this._pageAccount = { homeAccountId: result.account.homeAccountId, tenantId: accountTenant(result.account) };
    }
    return token;
  }

  private async _ensureInitialized(): Promise<IPublicClientApplication | null> {
    if (this._instance) return this._instance;

    if (!this._initPromise) {
      this._initPromise = (async () => {
        try {
          // Release any stale MSAL interaction lock left by a prior FAILED interactive attempt
          // (a closed/blocked popup leaves `msal.*.interaction.status` set → the next acquire throws
          // `interaction_in_progress` forever). Only the lock is cleared, never tokens/accounts.
          this._clearStaleInteractionLock();

          const useNaa = !this._options.forceFallback && (await detectNaaSupport());

          // Dynamic import keeps MSAL out of the bundle if a non-Office strategy is used
          // and lets us pick between the NAA factory and the standard PCA constructor
          // at runtime based on host capability.
          const msalModule = await import('@azure/msal-browser');

          if (useNaa) {
            // MSAL has no public "is this the NAA controller?" API; it logs whether the bridge came up, and
            // falls back to a standard client silently when it did not. Read that line to know which path runs.
            let bridgeAvailable = false;
            const instance = await msalModule.createNestablePublicClientApplication(
              this._buildNaaConfig(available => {
                bridgeAvailable = available;
              })
            );
            // createNestablePublicClientApplication returns an already-initialized
            // instance; no extra .initialize() call required.
            await this._drainRedirectIfAny(instance);
            this._instance = instance;
            this._isNaaActive = bridgeAvailable;
            console.info(
              bridgeAvailable
                ? '[OfficeNaaStrategy] initialized via NAA broker'
                : '[OfficeNaaStrategy] NAA bridge not available; initialized via standard popup client'
            );
          } else {
            const pca: PublicClientApplication = new msalModule.PublicClientApplication(this._buildFallbackConfig());
            await pca.initialize();
            await this._drainRedirectIfAny(pca);
            this._instance = pca;
            this._isNaaActive = false;
            console.info('[OfficeNaaStrategy] initialized via fallback PCA (no NAA)');
          }
        } catch (err) {
          console.warn('[OfficeNaaStrategy] MSAL initialization failed:', err);
          this._instance = null;
        }
      })();
    }

    await this._initPromise;
    return this._instance;
  }

  /**
   * MSAL config for the NAA path. Distinguishing from BrowserMsalStrategy:
   *   - redirectUri: the https fallback redirect, NEVER `brk-multihub://…`. Over the bridge MSAL does not use it (the
   *     Office host supplies the broker redirect, which stays registered in Entra as a SPA redirect). Without the
   *     bridge, `createNestablePublicClientApplication` builds a standard client from this same config, and a standard
   *     popup with a `brk-` redirect is refused (AADSTS7000471, the 2026-09-03 web failure).
   *   - supportsNestedAppAuth: true — read by MSAL's controller factory for NAA
   *   - navigateToLoginRequestUrl: false — NAA broker handles redirect; we never navigate
   *   - cacheLocation: 'sessionStorage' — Add-in iframe storage guidance
   *   - storeAuthStateInCookie: false — no Entra cookie shared with host
   *   - a logger at Info whose only job is to report the "Nested App Auth Bridge available" line; nothing is printed
   */
  private _buildNaaConfig(onBridge: (available: boolean) => void): Configuration {
    return {
      auth: {
        clientId: this._config.clientId,
        authority: this._config.authority,
        redirectUri: this._fallbackRedirectUri(),
        supportsNestedAppAuth: true,
        navigateToLoginRequestUrl: false,
      },
      cache: {
        cacheLocation: 'sessionStorage',
        storeAuthStateInCookie: false,
      },
      system: {
        loggerOptions: {
          logLevel: MSAL_LOG_LEVEL_INFO,
          piiLoggingEnabled: false,
          loggerCallback: (_level: number, message: string) => {
            const match = BRIDGE_AVAILABLE_LINE.exec(message);
            if (match) onBridge(match[1] === 'true');
          },
        },
        allowRedirectInIframe: true,
      },
    };
  }

  /** The https popup redirect: `fallbackRedirectUri`, else `<origin>/auth-callback.html` (registered as a SPA redirect). */
  private _fallbackRedirectUri(): string {
    return (
      this._options.fallbackRedirectUri ||
      (typeof window !== 'undefined' ? `${window.location.origin}/auth-callback.html` : '')
    );
  }

  /** MSAL config for the fallback PCA path (Office host without NAA support). */
  private _buildFallbackConfig(): Configuration {
    return {
      auth: {
        clientId: this._config.clientId,
        authority: this._config.authority,
        redirectUri: this._fallbackRedirectUri(),
        navigateToLoginRequestUrl: false,
      },
      cache: {
        cacheLocation: 'sessionStorage',
        storeAuthStateInCookie: false,
      },
      system: {
        loggerOptions: {
          logLevel: 3, // Warning
          piiLoggingEnabled: false,
        },
        allowRedirectInIframe: true,
      },
    };
  }

  /**
   * Drain any pending redirect result from a prior auth attempt. Symmetric with
   * BrowserMsalStrategy's handleRedirectPromise() call — fire-and-forget; failures
   * here are non-fatal (the next `acquire()` will pick up a cached account if any).
   */
  private async _drainRedirectIfAny(instance: IPublicClientApplication): Promise<void> {
    try {
      await instance.handleRedirectPromise();
    } catch (err) {
      console.warn('[OfficeNaaStrategy] handleRedirectPromise failed:', err);
    }
  }
}
