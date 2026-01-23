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
  private cachedAccessToken: string | null = null;
  private tokenExpiresAt: number | null = null; // Unix timestamp in milliseconds

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

    // When NAA is disabled, use Dialog API exclusively
    // MSAL's acquireTokenSilent still tries NAA internally in Office context, so skip it
    if (!this.isNaaSupported) {
      // Check if we already have an authenticated account from a previous dialog session
      if (this.currentAccount) {
        console.log('[AuthService] Already authenticated via Dialog API, account:', this.currentAccount.username);
        // Return a minimal result - the token will be fetched via getAccessToken when needed
        return {
          account: this.currentAccount,
          accessToken: '',  // Token fetched separately via dialog
          scopes,
          expiresOn: null,
          tenantId: AUTH_CONFIG.tenantId,
          uniqueId: this.currentAccount.localAccountId,
          authority: `https://login.microsoftonline.com/${AUTH_CONFIG.tenantId}`,
          idToken: '',
          idTokenClaims: {},
          fromCache: true,
          tokenType: 'Bearer',
          correlationId: '',
        } as AuthenticationResult;
      }

      // No cached account, open dialog for authentication
      console.log('[AuthService] No cached account, opening auth dialog');
      try {
        return await this.signInWithDialog(scopes);
      } catch (error) {
        console.error('Sign in failed:', error);
        return null;
      }
    }

    // NAA path (currently disabled but kept for future use)
    try {
      // Try silent first with NAA
      if (this.currentAccount) {
        try {
          console.log('[AuthService] Attempting silent token acquisition for cached account');
          const result = await this.msalInstance.acquireTokenSilent({
            scopes,
            account: this.currentAccount,
          });
          console.log('[AuthService] Silent auth succeeded - no dialog needed');
          return result;
        } catch (silentError) {
          console.log('[AuthService] Silent auth failed, falling back to interactive:', silentError);
        }
      }

      // Interactive auth with NAA
      const result = await this.msalInstance.acquireTokenPopup({
        scopes,
      });
      this.currentAccount = result.account;
      return result;
    } catch (error) {
      console.error('Sign in failed:', error);
      return null;
    }
  }

  async signOut(): Promise<void> {
    // Clear cached auth state
    this.currentAccount = null;
    this.cachedAccessToken = null;
    this.tokenExpiresAt = null;

    if (!this.isNaaSupported) {
      // For Dialog API, just clear local state - no MSAL logout needed
      console.log('[AuthService] Signed out (Dialog API mode)');
      return;
    }

    // NAA path - use MSAL logout
    if (!this.msalInstance) {
      return;
    }

    try {
      await this.msalInstance.logoutPopup({
        account: this.currentAccount,
      });
    } catch (error) {
      console.error('Sign out failed:', error);
    }
  }

  getAccount(): AccountInfo | null {
    return this.currentAccount;
  }

  async getAccessToken(scopes: string[]): Promise<string | null> {
    if (!this.currentAccount) {
      return null;
    }

    // When NAA is disabled, use cached token from dialog auth
    if (!this.isNaaSupported) {
      // Check if token is still valid (with 5 minute buffer for clock skew)
      const now = Date.now();
      const bufferMs = 5 * 60 * 1000; // 5 minutes
      const isTokenValid = this.cachedAccessToken &&
        this.tokenExpiresAt &&
        (this.tokenExpiresAt - bufferMs) > now;

      if (isTokenValid) {
        console.log('[AuthService] Returning cached access token (expires in',
          Math.round((this.tokenExpiresAt! - now) / 1000 / 60), 'minutes)');
        return this.cachedAccessToken;
      }

      // Token expired or missing - need to re-authenticate via dialog
      if (this.cachedAccessToken && this.tokenExpiresAt) {
        console.log('[AuthService] Token expired, triggering re-authentication');
      } else {
        console.log('[AuthService] No cached token, triggering dialog auth');
      }
      const result = await this.signInWithDialog(scopes);
      return result?.accessToken || null;
    }

    // NAA path
    if (!this.msalInstance) {
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
    // NAA (Nested App Authentication) DISABLED
    //
    // NAA requires dynamic broker redirect URIs in the format: brk-{GUID}://auth
    // The GUID is dynamically generated by the Office host at runtime and varies
    // per session/host. This GUID cannot be pre-registered in Azure AD because:
    //   1. It's not the app's client ID - it's generated by Office
    //   2. Each Office host instance generates different GUIDs
    //   3. Azure AD requires exact redirect URI matches
    //
    // Error when attempting NAA:
    //   AADSTS700046: Invalid Reply Address. Reply Address must have scheme
    //   brk-{dynamic-guid}:// and be of Single Page Application type.
    //
    // Using Dialog API fallback instead, which works reliably across all Office hosts.
    // Dialog API opens a popup window for auth, which is slightly more intrusive
    // but works universally without Azure AD configuration issues.
    //
    // To re-enable NAA in the future, Microsoft would need to provide a way to
    // register wildcard broker URIs or use a fixed broker URI format.
    console.log('[AuthService] NAA disabled - using Dialog API fallback');
    console.log('[AuthService] Reason: NAA requires dynamic broker URIs that cannot be pre-registered in Azure AD');
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
            (arg: { message?: string | object; error?: number }) => {
              console.log('[AuthService] Dialog message received:', arg);
              dialog.close();

              if (arg.message) {
                try {
                  // Handle both string and object message formats
                  // Outlook web returns object, desktop may return string
                  const message = typeof arg.message === 'string'
                    ? JSON.parse(arg.message)
                    : arg.message;
                  console.log('[AuthService] Parsed message:', message);
                  if (message.success) {
                    // Token received from dialog - construct proper AccountInfo
                    this.currentAccount = {
                      homeAccountId: message.account?.homeAccountId || '',
                      environment: 'login.microsoftonline.com',
                      tenantId: AUTH_CONFIG.tenantId,
                      username: message.account?.username || '',
                      localAccountId: message.account?.homeAccountId || '',
                      name: message.account?.name,
                    } as AccountInfo;
                    // Cache the access token and expiration for later use
                    this.cachedAccessToken = message.accessToken || null;
                    // Parse expiration - message.expiresOn can be Date string or Unix timestamp
                    if (message.expiresOn) {
                      this.tokenExpiresAt = typeof message.expiresOn === 'number'
                        ? message.expiresOn
                        : new Date(message.expiresOn).getTime();
                    } else {
                      // Default to 1 hour from now if not provided
                      this.tokenExpiresAt = Date.now() + (60 * 60 * 1000);
                    }
                    console.log('[AuthService] Account set:', this.currentAccount);
                    console.log('[AuthService] Access token cached, expires at:',
                      new Date(this.tokenExpiresAt).toLocaleTimeString());
                    resolve({
                      ...message,
                      account: this.currentAccount,
                    } as AuthenticationResult);
                  } else {
                    reject(new Error(message.error));
                  }
                } catch (e) {
                  console.error('[AuthService] Parse error:', e);
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
