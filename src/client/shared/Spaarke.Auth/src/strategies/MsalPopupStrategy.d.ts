import type { PublicClientApplication } from '@azure/msal-browser';
import type { ITokenResult, ITokenStrategy } from '../types';
/**
 * Acquire token via MSAL interactive popup.
 * Last-resort fallback when all silent strategies fail.
 */
export declare class MsalPopupStrategy implements ITokenStrategy {
  readonly name: 'msal-popup';
  private readonly _getMsalInstance;
  private readonly _scope;
  constructor(getMsalInstance: () => PublicClientApplication | null, scope: string);
  tryAcquireToken(): Promise<ITokenResult | null>;
  private _buildResult;
}
//# sourceMappingURL=MsalPopupStrategy.d.ts.map
