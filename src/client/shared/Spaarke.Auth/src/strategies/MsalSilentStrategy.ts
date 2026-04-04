import type { PublicClientApplication, Configuration, AuthenticationResult } from '@azure/msal-browser';
import type { ITokenResult, ITokenStrategy } from '../types';

/**
 * Resolve a login hint from the Dataverse session for ssoSilent().
 *
 * Resolution order:
 *   1. Xrm.Utility.getGlobalContext().userSettings.userName (current user's UPN)
 *   2. Xrm.Page.context.getUserName() (legacy API, some forms)
 *   3. undefined (ssoSilent will attempt discovery without hint)
 */
function resolveLoginHint(): string | undefined {
  try {
    const frames: Window[] = [window];
    try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

    for (const frame of frames) {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (frame as any).Xrm;
        const userName =
          xrm?.Utility?.getGlobalContext?.()?.userSettings?.userName ??
          xrm?.Page?.context?.getUserName?.();
        if (userName) return userName;
      } catch { /* cross-origin */ }
    }
  } catch { /* */ }
  return undefined;
}

/**
 * Acquire token via MSAL.js silent flow.
 * Uses existing Azure AD session cookie (hidden iframe).
 *
 * Resolution order:
 *   1. acquireTokenSilent (cached account + refresh token)
 *   2. ssoSilent with loginHint (Azure AD session cookie + Xrm user context)
 */
export class MsalSilentStrategy implements ITokenStrategy {
  readonly name = 'msal-silent' as const;

  private _instance: PublicClientApplication | null = null;
  private _initPromise: Promise<void> | null = null;
  private readonly _msalConfig: Configuration;
  private readonly _scope: string;

  constructor(msalConfig: Configuration, scope: string) {
    this._msalConfig = msalConfig;
    this._scope = scope;
  }

  async tryAcquireToken(): Promise<ITokenResult | null> {
    const msal = await this._ensureInitialized();
    if (!msal) return null;

    const scopes = [this._scope];

    try {
      // 1. Try acquireTokenSilent with cached account
      const accounts = msal.getAllAccounts();
      console.info('[SpaarkeAuth:MsalSilent] Accounts:', accounts.length, 'scope:', this._scope);
      if (accounts.length > 0) {
        const result = await msal.acquireTokenSilent({
          scopes,
          account: accounts[0],
        });
        if (result?.accessToken) {
          return this._buildResult(result);
        }
      }

      // 2. Fall back to ssoSilent with loginHint from Xrm context.
      // The loginHint tells Azure AD which user to authenticate silently,
      // preventing the "pick an account" prompt and enabling silent token
      // acquisition even when MSAL has no cached accounts (first load).
      const loginHint = resolveLoginHint() ?? accounts[0]?.username;
      console.info('[SpaarkeAuth:MsalSilent] Trying ssoSilent...', loginHint ? `hint=${loginHint}` : '(no hint)');
      const ssoResult = await msal.ssoSilent({
        scopes,
        loginHint,
      });
      if (ssoResult?.accessToken) {
        return this._buildResult(ssoResult);
      }
    } catch (err) {
      // Silent acquisition failed — caller should try next strategy
      console.warn('[SpaarkeAuth:MsalSilent] Silent acquisition failed:', err);
    }

    return null;
  }

  /** Expose the MSAL instance for tenant ID resolution and other utilities. */
  getMsalInstance(): PublicClientApplication | null {
    return this._instance;
  }

  private async _ensureInitialized(): Promise<PublicClientApplication | null> {
    if (this._instance) return this._instance;

    if (!this._initPromise) {
      this._initPromise = (async () => {
        try {
          // Dynamic import avoids bundling MSAL if bridge/cache strategies succeed
          const { PublicClientApplication: PCA } = await import('@azure/msal-browser');
          const instance = new PCA(this._msalConfig);
          await instance.initialize();
          await instance.handleRedirectPromise();
          this._instance = instance;
        } catch (err) {
          console.warn('[SpaarkeAuth] MSAL initialization failed', err);
          this._instance = null;
        }
      })();
    }

    await this._initPromise;
    return this._instance;
  }

  private _buildResult(result: AuthenticationResult): ITokenResult {
    return {
      accessToken: result.accessToken,
      expiresOn: result.expiresOn?.getTime() ?? Date.now() + 55 * 60 * 1000,
      source: 'msal-silent',
    };
  }
}
