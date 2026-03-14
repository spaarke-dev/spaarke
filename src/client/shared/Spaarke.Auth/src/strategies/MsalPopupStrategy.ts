import type { PublicClientApplication, AuthenticationResult } from '@azure/msal-browser';
import type { ITokenResult, ITokenStrategy } from '../types';

/**
 * Acquire token via MSAL interactive popup.
 * Last-resort fallback when all silent strategies fail.
 */
export class MsalPopupStrategy implements ITokenStrategy {
  readonly name = 'msal-popup' as const;

  private readonly _getMsalInstance: () => PublicClientApplication | null;
  private readonly _scope: string;

  constructor(getMsalInstance: () => PublicClientApplication | null, scope: string) {
    this._getMsalInstance = getMsalInstance;
    this._scope = scope;
  }

  async tryAcquireToken(): Promise<ITokenResult | null> {
    const msal = this._getMsalInstance();
    if (!msal) return null;

    try {
      const result = await msal.acquireTokenPopup({
        scopes: [this._scope],
      });
      if (result?.accessToken) {
        return this._buildResult(result);
      }
    } catch {
      // Popup blocked or user cancelled
    }

    return null;
  }

  private _buildResult(result: AuthenticationResult): ITokenResult {
    return {
      accessToken: result.accessToken,
      expiresOn: result.expiresOn?.getTime() ?? Date.now() + 55 * 60 * 1000,
      source: 'msal-popup',
    };
  }
}
