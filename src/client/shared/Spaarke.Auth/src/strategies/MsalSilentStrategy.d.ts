import type { PublicClientApplication, Configuration } from '@azure/msal-browser';
import type { ITokenResult, ITokenStrategy } from '../types';
/**
 * Acquire token via MSAL.js silent flow.
 * Uses existing Azure AD session cookie (hidden iframe).
 *
 * Resolution order:
 *   1. acquireTokenSilent (cached account + refresh token)
 *   2. ssoSilent with loginHint (Azure AD session cookie + Xrm user context)
 */
export declare class MsalSilentStrategy implements ITokenStrategy {
  readonly name: 'msal-silent';
  private _instance;
  private _initPromise;
  private readonly _msalConfig;
  private readonly _scope;
  constructor(msalConfig: Configuration, scope: string);
  tryAcquireToken(): Promise<ITokenResult | null>;
  /** Expose the MSAL instance for tenant ID resolution and other utilities. */
  getMsalInstance(): PublicClientApplication | null;
  private _ensureInitialized;
  private _buildResult;
}
//# sourceMappingURL=MsalSilentStrategy.d.ts.map
