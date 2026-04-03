import type { PublicClientApplication, Configuration, AuthenticationResult } from '@azure/msal-browser';
import type { ITokenResult, ITokenStrategy } from '../types';

/**
 * Acquire token via MSAL.js silent flow.
 * Uses existing Azure AD session cookie (hidden iframe).
 *
 * Resolution order:
 *   1. acquireTokenSilent (cached account + refresh token)
 *   2. ssoSilent (Azure AD session cookie)
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

      // 2. Fall back to ssoSilent (Azure AD session cookie)
      console.info('[SpaarkeAuth:MsalSilent] Trying ssoSilent...');
      const ssoResult = await msal.ssoSilent({ scopes });
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
