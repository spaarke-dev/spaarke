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

/** NAA broker redirect URI — required when running inside Office hosts that support NAA. */
const NAA_REDIRECT_URI = 'brk-multihub://localhost';

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
 * - `fallbackRedirectUri`: only consulted when Office host does NOT support NAA
 *   and `PublicClientApplication` is used as fallback. Defaults to
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
 * Detect whether the current Office host supports Nested App Authentication (NAA).
 *
 * NAA is supported in:
 *   - Office on the web (all browsers)
 *   - Office on Windows (build 16.0.13530.20424 or later)
 *   - Office on Mac (version 16.44 or later)
 *   - New Outlook for Windows
 *
 * Returns false outside an Office host (e.g., unit tests, standalone browser).
 *
 * Lifted from the pre-v2 `NaaAuthService.detectNaaSupport` so OfficeNaaStrategy
 * is the single source of truth post-task-080.
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
    if (!diagnostics) {
      console.warn('[OfficeNaaStrategy] Office diagnostics unavailable — assuming no NAA');
      return false;
    }

    const platform = diagnostics.platform;
    const version: string = diagnostics.version ?? '';
    console.info(`[OfficeNaaStrategy] platform=${platform} version=${version}`);

    if (platform === OfficeRef.PlatformType?.OfficeOnline) {
      return true;
    }

    if (platform === OfficeRef.PlatformType?.PC) {
      // Windows version format: 16.0.XXXXX.YYYYY
      const parts = version.split('.');
      if (parts.length >= 3) {
        const build = parseInt(parts[2] || '0', 10);
        return build >= 13530;
      }
      return false;
    }

    if (platform === OfficeRef.PlatformType?.Mac) {
      // Mac version format: 16.XX
      const major = parseFloat(version);
      return major >= 16.44;
    }

    // Unknown platform — let NAA attempt; it will fail gracefully if unsupported.
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
 * Falls back to a standard `PublicClientApplication` when:
 *   - Office is not detected (e.g., standalone browser test page)
 *   - Office host predates NAA (Office build < 13530 / Mac < 16.44)
 *   - Caller passes `forceFallback: true`
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
  private _isNaaActive = false;

  /**
   * @param config Standard @spaarke/auth resolved config (clientId, authority,
   *               redirectUri, bffApiScope). The Office redirectUri default of
   *               `brk-multihub://localhost` is applied internally when NAA is
   *               active — callers do NOT need to override `config.redirectUri`.
   * @param options Office-specific overrides (fallback redirect URI; forced fallback).
   */
  constructor(config: Required<IAuthConfig>, options: IOfficeNaaConfig = {}) {
    this._config = config;
    this._options = options;
  }

  async acquire(): Promise<TokenResult> {
    const msal = await this._ensureInitialized();
    if (!msal) return { accessToken: '', expiresOn: 0 };

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

    // 2. acquireTokenPopup — under NAA this routes through the Office broker
    //    (no real popup); under fallback PCA it opens an interactive popup.
    //    Either way it's the documented last-resort for getting the user signed in.
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
      console.warn('[OfficeNaaStrategy] acquireTokenPopup failed:', err);
    }

    return { accessToken: '', expiresOn: 0 };
  }

  clearCache(): void {
    if (!this._instance) return;
    // MSAL v3 clearCache() — clears accounts + tokens for this PCA instance.
    // Fire-and-forget per AuthStrategy contract; the SpaarkeAuthProvider's
    // own logout() awaits the full chain.
    void this._instance.clearCache().catch(err => {
      console.warn('[OfficeNaaStrategy] clearCache failed:', err);
    });
  }

  /**
   * Sign the user out at the identity-provider layer.
   *
   * Under NAA, `logoutPopup` is routed through the Office host broker (no real
   * popup window). Under the fallback PCA path, this opens an MSAL logout popup.
   * If the popup is blocked / unavailable, falls back to `clearCache()` so the
   * local cache state matches the user-intended outcome — the Entra session may
   * remain alive in that degraded path; the user's next interactive login resyncs.
   */
  async logout(): Promise<void> {
    const msal = await this._ensureInitialized();
    if (!msal) return;

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
          const useNaa = !this._options.forceFallback && (await detectNaaSupport());

          // Dynamic import keeps MSAL out of the bundle if a non-Office strategy is used
          // and lets us pick between the NAA factory and the standard PCA constructor
          // at runtime based on host capability.
          const msalModule = await import('@azure/msal-browser');

          if (useNaa) {
            const instance = await msalModule.createNestablePublicClientApplication(this._buildNaaConfig());
            // createNestablePublicClientApplication returns an already-initialized
            // instance; no extra .initialize() call required.
            await this._drainRedirectIfAny(instance);
            this._instance = instance;
            this._isNaaActive = true;
            console.info('[OfficeNaaStrategy] initialized via NAA broker');
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
   *   - supportsNestedAppAuth: true   — required by NAA factory
   *   - navigateToLoginRequestUrl: false — NAA broker handles redirect; we never navigate
   *   - cacheLocation: 'sessionStorage' — Add-in iframe storage guidance
   *   - storeAuthStateInCookie: false — no Entra cookie shared with host
   */
  private _buildNaaConfig(): Configuration {
    return {
      auth: {
        clientId: this._config.clientId,
        authority: this._config.authority,
        // NAA always uses the brk-multihub broker URI regardless of caller-provided redirectUri.
        // Honor caller override only if it's the canonical brk-multihub form, otherwise force.
        redirectUri: this._config.redirectUri?.startsWith('brk-') ? this._config.redirectUri : NAA_REDIRECT_URI,
        supportsNestedAppAuth: true,
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

  /** MSAL config for the fallback PCA path (Office host without NAA support). */
  private _buildFallbackConfig(): Configuration {
    const fallbackRedirect =
      this._options.fallbackRedirectUri ||
      (typeof window !== 'undefined' ? `${window.location.origin}/auth-callback.html` : '');

    return {
      auth: {
        clientId: this._config.clientId,
        authority: this._config.authority,
        redirectUri: fallbackRedirect,
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
