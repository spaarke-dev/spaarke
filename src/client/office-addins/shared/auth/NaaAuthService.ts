/**
 * NAA (Nested App Authentication) Service for Office Add-ins
 *
 * Implements the Microsoft-recommended authentication pattern for Office Add-ins
 * using MSAL.js 3.x createNestablePublicClientApplication.
 *
 * Key features:
 * - Silent token acquisition when cached token exists
 * - Interactive popup fallback when silent fails
 * - Token caching via MSAL's built-in cache
 * - NAA support detection for Office host capability checking
 * - Graceful error handling with user-friendly messages
 *
 * Per auth.md constraints:
 * - MUST use sessionStorage for tokens (not localStorage)
 * - MUST try silent acquisition before popup
 * - MUST NOT skip MSAL initialization step in MSAL v3+
 *
 * @see https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in
 */

import {
  type IPublicClientApplication,
  type AccountInfo,
  type AuthenticationResult,
  createNestablePublicClientApplication,
  PublicClientApplication,
  InteractionRequiredAuthError,
  BrowserAuthError,
  AuthError,
} from '@azure/msal-browser';

import {
  type NaaAuthConfig,
  DEFAULT_AUTH_CONFIG,
  createNaaMsalConfig,
  createFallbackMsalConfig,
  getBffApiScopes,
  TOKEN_EXPIRY_BUFFER_SECONDS,
} from './authConfig';

/**
 * Authentication error codes for user-friendly messaging.
 */
export enum NaaAuthErrorCode {
  /** Service not initialized */
  NOT_INITIALIZED = 'NAA_001',
  /** Silent token acquisition failed */
  SILENT_FAILED = 'NAA_002',
  /** Interactive auth was cancelled by user */
  USER_CANCELLED = 'NAA_003',
  /** Popup was blocked by browser */
  POPUP_BLOCKED = 'NAA_004',
  /** Network error during authentication */
  NETWORK_ERROR = 'NAA_005',
  /** Invalid or expired token */
  TOKEN_INVALID = 'NAA_006',
  /** NAA not supported by this Office client */
  NAA_NOT_SUPPORTED = 'NAA_007',
  /** Unknown authentication error */
  UNKNOWN = 'NAA_999',
}

/**
 * Authentication error with user-friendly message.
 */
export class NaaAuthError extends Error {
  public readonly code: NaaAuthErrorCode;
  public readonly userMessage: string;
  public readonly originalError?: Error;

  constructor(code: NaaAuthErrorCode, userMessage: string, originalError?: Error) {
    super(userMessage);
    this.name = 'NaaAuthError';
    this.code = code;
    this.userMessage = userMessage;
    this.originalError = originalError;
  }
}

/**
 * Authentication state for UI binding.
 */
export interface NaaAuthState {
  /** Whether authentication is in progress */
  isAuthenticating: boolean;
  /** Whether the user is currently authenticated */
  isAuthenticated: boolean;
  /** Current user account information */
  account: AccountInfo | null;
  /** Last authentication error (if any) */
  error: NaaAuthError | null;
}

/**
 * Token acquisition result.
 */
export interface TokenResult {
  /** The access token */
  accessToken: string;
  /** Token expiration timestamp */
  expiresOn: Date;
  /** Scopes granted by the token */
  scopes: string[];
  /** Whether the token was acquired from cache */
  fromCache: boolean;
}

/**
 * NAA Authentication Service interface.
 */
export interface INaaAuthService {
  /**
   * Initialize the authentication service.
   * @param config - Optional configuration overrides
   */
  initialize(config?: Partial<NaaAuthConfig>): Promise<void>;

  /**
   * Check if NAA (Nested App Authentication) is supported by the current Office host.
   * @returns true if NAA is supported, false otherwise
   */
  isNaaSupported(): boolean;

  /**
   * Check if the service has been initialized.
   */
  isInitialized(): boolean;

  /**
   * Check if a user is currently authenticated.
   */
  isAuthenticated(): boolean;

  /**
   * Get the current authenticated account.
   */
  getAccount(): AccountInfo | null;

  /**
   * Sign in the user. Attempts silent authentication first, falls back to interactive.
   * @returns The authentication result or throws NaaAuthError
   */
  signIn(): Promise<AuthenticationResult>;

  /**
   * Sign out the current user.
   */
  signOut(): Promise<void>;

  /**
   * Get an access token for the BFF API.
   * Attempts silent acquisition first, falls back to interactive popup.
   * @returns Token result with access token
   */
  getAccessToken(): Promise<TokenResult>;

  /**
   * Get the current authentication state for UI binding.
   */
  getAuthState(): NaaAuthState;

  /**
   * Add a listener for authentication state changes.
   * @param callback - Function to call when auth state changes
   * @returns Unsubscribe function
   */
  onAuthStateChange(callback: (state: NaaAuthState) => void): () => void;
}

/**
 * NAA Authentication Service implementation.
 *
 * Uses singleton pattern to ensure single MSAL instance per page.
 */
class NaaAuthServiceImpl implements INaaAuthService {
  private static instance: NaaAuthServiceImpl | null = null;

  private msalInstance: IPublicClientApplication | null = null;
  private currentAccount: AccountInfo | null = null;
  private naaSupported: boolean = false;
  private initialized: boolean = false;
  private config: NaaAuthConfig = DEFAULT_AUTH_CONFIG;
  private authStateListeners: Set<(state: NaaAuthState) => void> = new Set();
  private lastError: NaaAuthError | null = null;
  private isAuthenticating: boolean = false;

  private constructor() {
    // Private constructor for singleton pattern
  }

  /**
   * Get the singleton instance of NaaAuthService.
   */
  public static getInstance(): NaaAuthServiceImpl {
    if (!NaaAuthServiceImpl.instance) {
      NaaAuthServiceImpl.instance = new NaaAuthServiceImpl();
    }
    return NaaAuthServiceImpl.instance;
  }

  /**
   * Reset the singleton instance (for testing purposes).
   */
  public static resetInstance(): void {
    NaaAuthServiceImpl.instance = null;
  }

  public async initialize(config?: Partial<NaaAuthConfig>): Promise<void> {
    if (this.initialized) {
      console.warn('[NaaAuthService] Already initialized');
      return;
    }

    this.config = { ...DEFAULT_AUTH_CONFIG, ...config };

    // Detect NAA support before creating MSAL instance
    this.naaSupported = await this.detectNaaSupport();

    if (this.naaSupported) {
      // Use NAA (Nested App Authentication) - the preferred method
      console.info('[NaaAuthService] Using Nested App Authentication (NAA)');
      this.msalInstance = await createNestablePublicClientApplication(
        createNaaMsalConfig(this.config)
      );
    } else {
      // Fallback to standard MSAL with Dialog API
      console.info('[NaaAuthService] NAA not supported, using fallback authentication');
      const pca = new PublicClientApplication(createFallbackMsalConfig(this.config));
      // MSAL v3+ requires explicit initialization
      await pca.initialize();
      this.msalInstance = pca;
    }

    // Handle redirect promise (for any pending auth flows)
    try {
      const redirectResponse = await this.msalInstance.handleRedirectPromise();
      if (redirectResponse) {
        this.currentAccount = redirectResponse.account;
        this.notifyStateChange();
      }
    } catch (error) {
      console.warn('[NaaAuthService] Redirect handling failed:', error);
    }

    // Check for existing cached account
    if (!this.currentAccount) {
      const accounts = this.msalInstance.getAllAccounts();
      if (accounts.length > 0 && accounts[0]) {
        this.currentAccount = accounts[0];
      }
    }

    this.initialized = true;
    this.notifyStateChange();
  }

  public isNaaSupported(): boolean {
    return this.naaSupported;
  }

  public isInitialized(): boolean {
    return this.initialized;
  }

  public isAuthenticated(): boolean {
    return this.currentAccount !== null;
  }

  public getAccount(): AccountInfo | null {
    return this.currentAccount;
  }

  public async signIn(): Promise<AuthenticationResult> {
    this.ensureInitialized();
    this.setAuthenticating(true);

    try {
      // First, try silent authentication (from cache or SSO)
      const silentResult = await this.tryAcquireTokenSilent();
      if (silentResult) {
        this.currentAccount = silentResult.account;
        this.lastError = null;
        this.notifyStateChange();
        return silentResult;
      }
    } catch (error) {
      // Silent failed, will try interactive below
      console.info('[NaaAuthService] Silent auth failed, trying interactive');
    }

    // Try interactive authentication
    try {
      const interactiveResult = await this.acquireTokenInteractive();
      this.currentAccount = interactiveResult.account;
      this.lastError = null;
      this.notifyStateChange();
      return interactiveResult;
    } catch (error) {
      const authError = this.mapToAuthError(error);
      this.lastError = authError;
      this.notifyStateChange();
      throw authError;
    } finally {
      this.setAuthenticating(false);
    }
  }

  public async signOut(): Promise<void> {
    this.ensureInitialized();

    if (!this.currentAccount) {
      return;
    }

    try {
      await this.msalInstance!.logoutPopup({
        account: this.currentAccount,
        postLogoutRedirectUri: window.location.origin,
      });
    } catch (error) {
      console.warn('[NaaAuthService] Logout failed:', error);
      // Even if logout fails, clear local state
    }

    this.currentAccount = null;
    this.lastError = null;
    this.notifyStateChange();
  }

  public async getAccessToken(): Promise<TokenResult> {
    this.ensureInitialized();

    // Try silent acquisition first (from cache)
    try {
      const silentResult = await this.tryAcquireTokenSilent();
      if (silentResult) {
        return this.mapToTokenResult(silentResult, true);
      }
    } catch {
      // Silent failed, will try interactive
    }

    // Fall back to interactive
    try {
      const interactiveResult = await this.acquireTokenInteractive();
      this.currentAccount = interactiveResult.account;
      this.notifyStateChange();
      return this.mapToTokenResult(interactiveResult, false);
    } catch (error) {
      throw this.mapToAuthError(error);
    }
  }

  public getAuthState(): NaaAuthState {
    return {
      isAuthenticating: this.isAuthenticating,
      isAuthenticated: this.isAuthenticated(),
      account: this.currentAccount,
      error: this.lastError,
    };
  }

  public onAuthStateChange(callback: (state: NaaAuthState) => void): () => void {
    this.authStateListeners.add(callback);

    // Immediately notify with current state
    callback(this.getAuthState());

    // Return unsubscribe function
    return () => {
      this.authStateListeners.delete(callback);
    };
  }

  // ============================================
  // Private methods
  // ============================================

  private ensureInitialized(): asserts this is { msalInstance: IPublicClientApplication } {
    if (!this.initialized || !this.msalInstance) {
      throw new NaaAuthError(
        NaaAuthErrorCode.NOT_INITIALIZED,
        'Authentication service is not initialized. Call initialize() first.'
      );
    }
  }

  private setAuthenticating(value: boolean): void {
    this.isAuthenticating = value;
    this.notifyStateChange();
  }

  private notifyStateChange(): void {
    const state = this.getAuthState();
    this.authStateListeners.forEach((listener) => {
      try {
        listener(state);
      } catch (error) {
        console.error('[NaaAuthService] Auth state listener error:', error);
      }
    });
  }

  private async tryAcquireTokenSilent(): Promise<AuthenticationResult | null> {
    if (!this.msalInstance) return null;

    // Need an account for silent acquisition
    if (!this.currentAccount) {
      const accounts = this.msalInstance.getAllAccounts();
      if (accounts.length === 0) {
        return null;
      }
      this.currentAccount = accounts[0] || null;
    }

    if (!this.currentAccount) {
      return null;
    }

    try {
      const result = await this.msalInstance.acquireTokenSilent({
        scopes: getBffApiScopes(this.config),
        account: this.currentAccount,
        forceRefresh: false,
      });

      // Check if token is about to expire (within buffer)
      if (result.expiresOn) {
        const expiryTime = result.expiresOn.getTime();
        const now = Date.now();
        const bufferMs = TOKEN_EXPIRY_BUFFER_SECONDS * 1000;

        if (expiryTime - now < bufferMs) {
          // Token is about to expire, force refresh
          return await this.msalInstance.acquireTokenSilent({
            scopes: getBffApiScopes(this.config),
            account: this.currentAccount,
            forceRefresh: true,
          });
        }
      }

      return result;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        // Interaction required, return null to trigger interactive flow
        return null;
      }
      throw error;
    }
  }

  private async acquireTokenInteractive(): Promise<AuthenticationResult> {
    if (!this.msalInstance) {
      throw new NaaAuthError(
        NaaAuthErrorCode.NOT_INITIALIZED,
        'Authentication service is not initialized.'
      );
    }

    const loginHint = this.currentAccount?.username;

    return await this.msalInstance.acquireTokenPopup({
      scopes: getBffApiScopes(this.config),
      loginHint,
      prompt: this.currentAccount ? undefined : 'select_account',
    });
  }

  private mapToTokenResult(
    authResult: AuthenticationResult,
    fromCache: boolean
  ): TokenResult {
    return {
      accessToken: authResult.accessToken,
      expiresOn: authResult.expiresOn || new Date(Date.now() + 3600 * 1000),
      scopes: authResult.scopes,
      fromCache,
    };
  }

  private mapToAuthError(error: unknown): NaaAuthError {
    // Handle MSAL errors
    if (error instanceof InteractionRequiredAuthError) {
      return new NaaAuthError(
        NaaAuthErrorCode.SILENT_FAILED,
        'Sign-in required. Please sign in to continue.',
        error
      );
    }

    if (error instanceof BrowserAuthError) {
      // Check for user cancellation
      if (error.errorCode === 'user_cancelled') {
        return new NaaAuthError(
          NaaAuthErrorCode.USER_CANCELLED,
          'Sign-in was cancelled.',
          error
        );
      }

      // Check for popup blocked
      if (error.errorCode === 'popup_window_error') {
        return new NaaAuthError(
          NaaAuthErrorCode.POPUP_BLOCKED,
          'Sign-in popup was blocked. Please allow popups for this site.',
          error
        );
      }

      // Network errors
      if (error.errorCode === 'network_error') {
        return new NaaAuthError(
          NaaAuthErrorCode.NETWORK_ERROR,
          'Network error during sign-in. Please check your connection.',
          error
        );
      }
    }

    if (error instanceof AuthError) {
      return new NaaAuthError(
        NaaAuthErrorCode.UNKNOWN,
        `Authentication failed: ${error.errorMessage}`,
        error
      );
    }

    // Unknown error
    const message = error instanceof Error ? error.message : 'Unknown authentication error';
    return new NaaAuthError(
      NaaAuthErrorCode.UNKNOWN,
      message,
      error instanceof Error ? error : undefined
    );
  }

  /**
   * Detect if NAA (Nested App Authentication) is supported by the current Office host.
   *
   * NAA is supported in:
   * - Office on the web (all browsers)
   * - Office on Windows (version 16.0.13530.20424 or later)
   * - Office on Mac (version 16.44 or later)
   * - New Outlook for Windows
   *
   * NAA is NOT supported in:
   * - Classic Outlook (COM add-in)
   * - Very old Office versions
   * - Non-Office contexts
   */
  private async detectNaaSupport(): Promise<boolean> {
    try {
      // Check if we're in an Office context
      if (typeof Office === 'undefined' || !Office.context) {
        console.warn('[NaaAuthService] Not running in Office context');
        return false;
      }

      // Wait for Office to be ready
      await new Promise<void>((resolve) => {
        if (Office.onReady) {
          Office.onReady(() => resolve());
        } else {
          resolve();
        }
      });

      // Get diagnostic info
      const diagnostics = Office.context.diagnostics;
      if (!diagnostics) {
        // If diagnostics are not available, assume NAA might not be supported
        console.warn('[NaaAuthService] Office diagnostics not available');
        return false;
      }

      // Check platform
      const platform = diagnostics.platform;
      const version = diagnostics.version;

      console.info(`[NaaAuthService] Office platform: ${platform}, version: ${version}`);

      // Office on the web always supports NAA
      if (platform === Office.PlatformType.OfficeOnline) {
        return true;
      }

      // For desktop, check version requirements
      // NAA requires minimum versions:
      // - Windows: 16.0.13530.20424
      // - Mac: 16.44

      if (platform === Office.PlatformType.PC) {
        // Parse Windows version (format: 16.0.XXXXX.YYYYY)
        const versionParts = version.split('.');
        if (versionParts.length >= 3) {
          const build = parseInt(versionParts[2] || '0', 10);
          // NAA support started at build 13530
          return build >= 13530;
        }
      }

      if (platform === Office.PlatformType.Mac) {
        // Parse Mac version (format: 16.XX)
        const majorVersion = parseFloat(version);
        // NAA support started at version 16.44
        return majorVersion >= 16.44;
      }

      // For unknown platforms, try NAA (it will fail gracefully if not supported)
      return true;
    } catch (error) {
      console.warn('[NaaAuthService] NAA detection failed:', error);
      // Default to false on error
      return false;
    }
  }
}

/**
 * Get the singleton NAA Authentication Service instance.
 */
export const naaAuthService: INaaAuthService = NaaAuthServiceImpl.getInstance();

/**
 * Export the class for testing purposes.
 */
export { NaaAuthServiceImpl };
