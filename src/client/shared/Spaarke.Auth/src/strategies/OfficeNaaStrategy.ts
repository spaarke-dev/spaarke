import type {
  AccountInfo,
  AuthenticationResult,
  Configuration,
  IPublicClientApplication,
  PublicClientApplication,
} from '@azure/msal-browser';
import type { IAuthConfig, TokenResult } from '../types';
import type { AuthStrategy } from './AuthStrategy';

/** Buffer (ms) before token expiry to consider it stale. Matches config.TOKEN_EXPIRY_BUFFER_MS. */
const EXPIRY_BUFFER_MS = 5 * 60 * 1000;

/** MSAL `LogLevel.Info` — the level of the line that says whether the NAA bridge came up. */
const MSAL_LOG_LEVEL_INFO = 2;

/** The MSAL line (`NestedAppOperatingContext.initialize`) that reports whether the Office NAA bridge is available. */
const BRIDGE_AVAILABLE_LINE = /Nested App Auth Bridge available: (true|false)/;

/**
 * Whether an NAA interactive failure shows that NAA cannot serve this app on this host, so the standard popup client
 * should take over. msal-browser 3.x maps bridge statuses in `NestedAppAuthAdapter.fromBridgeError`:
 *   - Disabled / NestedAppAuthUnavailable → `nested_app_auth_bridge_disabled` (or a host code): yes.
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
 *   1. `acquireTokenSilent` with a cached MSAL account
 *   2. `acquireTokenPopup` with `loginHint` from the cached account
 *      (NAA-backed PCA routes this through the Office broker — not a real popup)
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
  }

  async acquire(): Promise<TokenResult> {
    const msal = await this._ensureInitialized();
    if (!msal) return { accessToken: '', expiresOn: 0 };
    const token = await this._tryAcquireWith(msal);
    if (token) return token;

    // NAA cannot serve this app here (the host disabled the bridge, or Entra refused the brokered request: an app
    // registration without the broker redirect, a Conditional Access block): fall back once to the standard popup
    // client, the path these hosts used before NAA was turned on for them. Only for those failures: a cancel is an
    // answer, and a network drop or a missing account is transient; switching on them would leave the page on the
    // popup client for good, and the proactive refresh would then open popups.
    if (this._isNaaActive && naaCannotServe(this._lastPopupError)) {
      const fallback = await this._switchToFallback();
      if (fallback) {
        const retried = await this._tryAcquireWith(fallback);
        if (retried) return retried;
      }
    }
    return { accessToken: '', expiresOn: 0 };
  }

  /**
   * Run the silent→popup acquisition ladder against a given MSAL instance. Shared by the primary
   * (NAA or fallback) attempt and the NAA-failure fallback retry so both paths validate/expire tokens
   * identically. Returns a validated TokenResult or null if both steps fail.
   */
  private async _tryAcquireWith(msal: IPublicClientApplication): Promise<TokenResult | null> {
    const scopes = [this._config.bffApiScope];
    const account = this._pickAccount(msal);

    // 1. acquireTokenSilent — works under both NAA and fallback PCA
    if (account) {
      try {
        console.info(
          `[OfficeNaaStrategy] acquireTokenSilent naa=${this._isNaaActive} scope=${this._config.bffApiScope}`
        );
        const result = await msal.acquireTokenSilent({ scopes, account, forceRefresh: false });
        const token = this._validate(result);
        if (token) return token;
      } catch (err) {
        console.warn('[OfficeNaaStrategy] acquireTokenSilent failed:', err);
      }
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
      const token = this._validate(result);
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
    const msal = await this._ensureInitialized();
    if (!msal) return;

    if (this._isNaaActive) {
      this._clearMsalSessionStorage();
      return;
    }

    const account = this._pickAccount(msal);
    if (!account) return;

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

  /** Pick the most recently signed-in account, or null if none cached. */
  private _pickAccount(msal: IPublicClientApplication): AccountInfo | null {
    const accounts = msal.getAllAccounts();
    return accounts.length > 0 ? (accounts[0] ?? null) : null;
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
