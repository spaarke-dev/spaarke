import {
  IPublicClientApplication,
  AccountInfo,
  AuthenticationResult,
  createNestablePublicClientApplication,
  PublicClientApplication,
} from '@azure/msal-browser';

/**
 * Authentication service for Office Add-ins.
 *
 * Uses NAA (Nested App Authentication) as primary method (MSAL.js 3.x).
 * Falls back to Dialog API for older Office clients that don't support NAA.
 *
 * Per auth.md constraints:
 * - MUST use sessionStorage for tokens (not localStorage)
 * - MUST NOT use individual Graph scopes in OBO
 * - MUST use `.default` scope for BFF API calls
 */

// Configuration - loaded from environment via webpack DefinePlugin
const AUTH_CONFIG = {
  clientId: process.env.ADDIN_CLIENT_ID || 'c1258e2d-1688-49d2-ac99-a7485ebd9995',
  tenantId: process.env.TENANT_ID || 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  bffApiClientId: process.env.BFF_API_CLIENT_ID || '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  bffApiBaseUrl: process.env.BFF_API_BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',
  redirectUri: 'brk-multihub://localhost', // NAA broker redirect
  fallbackRedirectUri: '', // https://{addin-domain}/taskpane.html - set for production
};

export interface IAuthService {
  initialize(config: AuthConfig): Promise<void>;
  signIn(): Promise<AuthenticationResult | null>;
  signOut(): Promise<void>;
  getAccount(): AccountInfo | null;
  getAccessToken(scopes: string[]): Promise<string | null>;
  isAuthenticated(): boolean;
}

export interface AuthConfig {
  clientId: string;
  tenantId?: string;
  bffApiClientId?: string;
  redirectUri?: string;
  fallbackRedirectUri?: string;
}

class AuthService implements IAuthService {
  private msalInstance: IPublicClientApplication | null = null;
  private isNaaSupported: boolean = false;
  private currentAccount: AccountInfo | null = null;

  async initialize(config: AuthConfig): Promise<void> {
    const mergedConfig = { ...AUTH_CONFIG, ...config };

    // Check if NAA is supported
    this.isNaaSupported = await this.checkNaaSupport();

    if (this.isNaaSupported) {
      // Use NAA (Nested App Authentication) - preferred method
      this.msalInstance = await createNestablePublicClientApplication({
        auth: {
          clientId: mergedConfig.clientId,
          authority: `https://login.microsoftonline.com/${mergedConfig.tenantId}`,
          supportsNestedAppAuth: true,
        },
        cache: {
          cacheLocation: 'sessionStorage', // MUST use sessionStorage per auth.md
          storeAuthStateInCookie: false,
        },
      });
    } else {
      // Fallback to standard MSAL with Dialog API
      this.msalInstance = new PublicClientApplication({
        auth: {
          clientId: mergedConfig.clientId,
          authority: `https://login.microsoftonline.com/${mergedConfig.tenantId}`,
          redirectUri: mergedConfig.fallbackRedirectUri,
        },
        cache: {
          cacheLocation: 'sessionStorage',
          storeAuthStateInCookie: false,
        },
      });

      await this.msalInstance.initialize();
    }

    // Check for existing account
    const accounts = this.msalInstance.getAllAccounts();
    if (accounts.length > 0 && accounts[0]) {
      this.currentAccount = accounts[0];
    }
  }

  async signIn(): Promise<AuthenticationResult | null> {
    if (!this.msalInstance) {
      throw new Error('AuthService not initialized');
    }

    const scopes = [`api://${AUTH_CONFIG.bffApiClientId}/user_impersonation`];

    try {
      if (this.isNaaSupported) {
        // NAA: Use popup with broker
        const result = await this.msalInstance.acquireTokenPopup({
          scopes,
        });
        this.currentAccount = result.account;
        return result;
      } else {
        // Dialog API fallback
        return await this.signInWithDialog(scopes);
      }
    } catch (error) {
      console.error('Sign in failed:', error);
      return null;
    }
  }

  async signOut(): Promise<void> {
    if (!this.msalInstance || !this.currentAccount) {
      return;
    }

    try {
      await this.msalInstance.logoutPopup({
        account: this.currentAccount,
      });
      this.currentAccount = null;
    } catch (error) {
      console.error('Sign out failed:', error);
    }
  }

  getAccount(): AccountInfo | null {
    return this.currentAccount;
  }

  async getAccessToken(scopes: string[]): Promise<string | null> {
    if (!this.msalInstance || !this.currentAccount) {
      return null;
    }

    try {
      // Try silent token acquisition first
      const result = await this.msalInstance.acquireTokenSilent({
        scopes,
        account: this.currentAccount,
      });
      return result.accessToken;
    } catch {
      // Silent acquisition failed, try interactive
      try {
        const result = await this.msalInstance.acquireTokenPopup({
          scopes,
        });
        return result.accessToken;
      } catch (error) {
        console.error('Token acquisition failed:', error);
        return null;
      }
    }
  }

  isAuthenticated(): boolean {
    return this.currentAccount !== null;
  }

  private async checkNaaSupport(): Promise<boolean> {
    // NAA is supported in Office.js 1.3+ and specific Office versions
    try {
      if (typeof Office !== 'undefined' && Office.context) {
        // Check if running in a context that supports NAA
        // NAA requires Office 2016 or later on Windows/Mac, or Office on the web
        const hostInfo = Office.context.diagnostics;
        if (hostInfo) {
          // NAA is generally available in recent Office versions
          // This is a simplified check - production code should be more thorough
          return true;
        }
      }
    } catch {
      // Office.js not ready or NAA not supported
    }
    return false;
  }

  private async signInWithDialog(_scopes: string[]): Promise<AuthenticationResult | null> {
    // Dialog API fallback for older Office clients
    // This opens a dialog window for authentication
    // Note: _scopes would be used in the dialog page to request tokens
    return new Promise((resolve, reject) => {
      Office.context.ui.displayDialogAsync(
        `${window.location.origin}/auth-dialog.html`,
        { height: 60, width: 40 },
        (result) => {
          if (result.status === Office.AsyncResultStatus.Failed) {
            reject(new Error(result.error.message));
            return;
          }

          const dialog = result.value;

          dialog.addEventHandler(
            Office.EventType.DialogMessageReceived,
            (arg: { message?: string; error?: number }) => {
              dialog.close();

              if (arg.message) {
                try {
                  const message = JSON.parse(arg.message);
                  if (message.success) {
                    // Token received from dialog
                    this.currentAccount = message.account;
                    resolve(message as AuthenticationResult);
                  } else {
                    reject(new Error(message.error));
                  }
                } catch {
                  reject(new Error('Failed to parse auth response'));
                }
              }
            }
          );
        }
      );
    });
  }
}

// Export singleton instance
export const authService: IAuthService = new AuthService();
