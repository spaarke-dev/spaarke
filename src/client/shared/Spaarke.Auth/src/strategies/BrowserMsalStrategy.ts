import type { Configuration, PublicClientApplication } from '@azure/msal-browser';
import type { IAuthConfig, TokenResult } from '../types';
import type { AuthStrategy } from './AuthStrategy';
import { MsalSilentStrategy } from './MsalSilentStrategy';
import { MsalPopupStrategy } from './MsalPopupStrategy';

/**
 * BrowserMsalStrategy — single strategy for browser-hosted Spaarke surfaces
 * (Dataverse PCFs + Code Pages). Built on MSAL.js; MSAL's localStorage cache
 * handles cross-tab/iframe sharing, so we no longer need separate Bridge,
 * SessionStorage, or Xrm strategies.
 *
 * Regression invariants (preserved by literal lift from SpaarkeAuthProvider:44-68):
 *   - INV-1: cacheLocation 'localStorage'   — survives tab/browser close
 *   - INV-2: storeAuthStateInCookie true    — ssoSilent works under 3rd-party cookie blocking
 *   - INV-3: tenant-specific authority      — resolved by resolveDefaultAuthority() in config.ts
 *
 * Task 010 status: STUB. Wraps existing MsalSilentStrategy + MsalPopupStrategy so the
 * structural refactor lands without behavior change. Task 011 folds those in directly
 * and adds JWT exp validation + UPN-as-loginHint fix.
 */
export class BrowserMsalStrategy implements AuthStrategy {
  readonly name = 'browser-msal';

  private readonly _silent: MsalSilentStrategy;
  private readonly _popup: MsalPopupStrategy;

  constructor(config: Required<IAuthConfig>) {
    const msalConfig: Configuration = {
      auth: {
        clientId: config.clientId,
        authority: config.authority,
        redirectUri: config.redirectUri,
      },
      cache: {
        cacheLocation: 'localStorage',     // INV-1 — MUST be localStorage, not sessionStorage
        storeAuthStateInCookie: true,      // INV-2 — MUST be true for ssoSilent under 3rd-party cookie blocking
      },
      system: {
        loggerOptions: {
          logLevel: 3, // Warning
          piiLoggingEnabled: false,
        },
      },
    };

    this._silent = new MsalSilentStrategy(msalConfig, config.bffApiScope);
    this._popup = new MsalPopupStrategy(
      () => this._silent.getMsalInstance(),
      config.bffApiScope
    );
  }

  async acquire(): Promise<TokenResult> {
    const silent = await this._silent.tryAcquireToken();
    if (silent?.accessToken) {
      return { accessToken: silent.accessToken, expiresOn: silent.expiresOn };
    }

    const popup = await this._popup.tryAcquireToken();
    if (popup?.accessToken) {
      return { accessToken: popup.accessToken, expiresOn: popup.expiresOn };
    }

    return { accessToken: '', expiresOn: 0 };
  }

  clearCache(): void {
    // Task 011 will call MSAL's removeAccount on the active account.
    // For now, MSAL state is reset only on explicit logout via SpaarkeAuthProvider.clearAllCaches.
  }

  /**
   * Expose the underlying MSAL instance so SpaarkeAuthProvider can resolve the
   * tenant ID from accounts as a fallback when the JWT `tid` claim isn't available.
   * Not part of the AuthStrategy interface — provider type-narrows to BrowserMsalStrategy.
   */
  getMsalInstance(): PublicClientApplication | null {
    return this._silent.getMsalInstance();
  }
}
