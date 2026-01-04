/**
 * MSAL Authentication Service for BFF API Access
 *
 * CRITICAL: Uses named scope api://<BFF_APP_ID>/SDAP.Access
 * DO NOT use .default scope (that's for daemon/confidential clients)
 *
 * Pattern follows working SpeDocumentViewer AuthService:
 * 1. Static redirect URI (not window.location.origin)
 * 2. handleRedirectPromise() during initialize (required MSAL v3+)
 * 3. acquireTokenSilent with account first, ssoSilent as fallback
 */

import {
    PublicClientApplication,
    AccountInfo,
    AuthenticationResult,
    SilentRequest,
    PopupRequest
} from '@azure/msal-browser';

/**
 * AuthService handles MSAL token acquisition for BFF API calls
 *
 * Architecture:
 * - PCF runs in Dataverse context (user already authenticated)
 * - Uses acquireTokenSilent() with account for best performance
 * - Falls back to ssoSilent() if no cached account
 * - Falls back to popup for consent/MFA if needed
 * - Tokens cached by MSAL browser storage
 */
export class AuthService {
    private msalInstance: PublicClientApplication;
    private namedScope: string;
    private currentAccount: AccountInfo | null = null;
    private initialized: boolean = false;

    /**
     * Initialize MSAL public client
     *
     * @param tenantId Azure AD tenant ID
     * @param clientAppId PCF Client Application ID (for MSAL authentication)
     * @param bffAppId BFF Application ID (for scope construction)
     */
    constructor(tenantId: string, clientAppId: string, bffAppId: string) {
        // CRITICAL: Named scope, NOT .default
        // Format: api://<BFF_APP_ID>/SDAP.Access
        this.namedScope = `api://${bffAppId}/SDAP.Access`;

        // CRITICAL: Static redirect URI matching Azure AD app registration
        // Must be the Dataverse org URL, NOT window.location.origin
        const redirectUri = 'https://spaarkedev1.crm.dynamics.com';

        // MSAL configuration for public client (SPA)
        this.msalInstance = new PublicClientApplication({
            auth: {
                clientId: clientAppId, // PCF Client App ID (for MSAL authentication)
                authority: `https://login.microsoftonline.com/${tenantId}`,
                redirectUri: redirectUri, // STATIC - must match Azure AD registration
                navigateToLoginRequestUrl: false // Stay on current page after login
            },
            cache: {
                cacheLocation: 'sessionStorage', // Use sessionStorage for PCF
                storeAuthStateInCookie: false // Not needed for modern browsers
            },
            system: {
                loggerOptions: {
                    loggerCallback: (_level, message, containsPii) => {
                        if (containsPii) return; // Skip PII logs
                        console.log(`[AuthService:AnalysisWorkspace] ${message}`);
                    },
                    logLevel: 3 // Warning level (reduce noise)
                }
            }
        });
    }

    /**
     * Initialize MSAL (must be called before token acquisition)
     *
     * CRITICAL: Must call handleRedirectPromise() after initialize() in MSAL v3+
     */
    public async initialize(): Promise<void> {
        if (this.initialized) {
            console.info('[AuthService:AnalysisWorkspace] Already initialized, skipping');
            return;
        }

        console.info('[AuthService:AnalysisWorkspace] Initializing MSAL...');

        // Step 1: Initialize MSAL instance (required in MSAL v3+)
        await this.msalInstance.initialize();
        console.info('[AuthService:AnalysisWorkspace] MSAL instance initialized');

        // Step 2: Handle redirect response (CRITICAL - required in MSAL v3+)
        // If user was redirected to Azure AD for login and is now returning,
        // this processes the OAuth response and extracts tokens.
        const redirectResponse = await this.msalInstance.handleRedirectPromise();
        if (redirectResponse) {
            console.info('[AuthService:AnalysisWorkspace] Redirect response processed, user authenticated via redirect');
            this.currentAccount = redirectResponse.account;
        }

        // Step 3: Set active account (if user already logged in)
        if (!this.currentAccount) {
            const accounts = this.msalInstance.getAllAccounts();
            if (accounts.length > 0) {
                this.currentAccount = accounts[0];
                console.info(`[AuthService:AnalysisWorkspace] Active account found: ${this.currentAccount.username}`);
            } else {
                console.info('[AuthService:AnalysisWorkspace] No active account found');
            }
        }

        this.initialized = true;
        console.info('[AuthService:AnalysisWorkspace] MSAL initialization complete');
    }

    /**
     * Get access token for BFF API
     *
     * Flow:
     * 1. Try acquireTokenSilent with cached account (fastest)
     * 2. Try ssoSilent (discover account from browser session)
     * 3. Fall back to popup (for consent/MFA)
     *
     * @returns Access token (JWT Bearer token)
     * @throws Error if token acquisition fails
     */
    public async getAccessToken(): Promise<string> {
        // Ensure initialized
        if (!this.initialized) {
            await this.initialize();
        }

        try {
            // Step 1: Try acquireTokenSilent with cached account (fastest path)
            if (this.currentAccount) {
                console.log('[AuthService:AnalysisWorkspace] Attempting acquireTokenSilent with cached account...');
                try {
                    const silentRequest: SilentRequest = {
                        scopes: [this.namedScope],
                        account: this.currentAccount
                    };
                    const result = await this.msalInstance.acquireTokenSilent(silentRequest);
                    console.log('[AuthService:AnalysisWorkspace] acquireTokenSilent succeeded');
                    return result.accessToken;
                } catch (silentError) {
                    console.log('[AuthService:AnalysisWorkspace] acquireTokenSilent failed, trying ssoSilent...');
                    // Fall through to ssoSilent
                }
            }

            // Step 2: Try ssoSilent (discover account from browser session)
            console.log('[AuthService:AnalysisWorkspace] Attempting ssoSilent authentication...');
            const ssoResult = await this.attemptSsoSilent();
            if (ssoResult) {
                console.log('[AuthService:AnalysisWorkspace] ssoSilent succeeded');
                // Update cached account
                if (ssoResult.account) {
                    this.currentAccount = ssoResult.account;
                }
                return ssoResult.accessToken;
            }

            // Step 3: Fall back to popup (requires user interaction)
            console.log('[AuthService:AnalysisWorkspace] Falling back to popup authentication...');
            const popupResult = await this.attemptPopupAuth();
            console.log('[AuthService:AnalysisWorkspace] Popup authentication succeeded');
            // Update cached account
            if (popupResult.account) {
                this.currentAccount = popupResult.account;
            }
            return popupResult.accessToken;

        } catch (error) {
            console.error('[AuthService:AnalysisWorkspace] Token acquisition failed:', error);
            throw new Error(`Failed to acquire access token: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    /**
     * Attempt SSO silent authentication (discover account from browser session)
     */
    private async attemptSsoSilent(): Promise<AuthenticationResult | null> {
        try {
            const ssoRequest: SilentRequest = {
                scopes: [this.namedScope]
            };

            return await this.msalInstance.ssoSilent(ssoRequest);
        } catch (error) {
            // SSO silent can fail if no SSO session exists
            console.log('[AuthService:AnalysisWorkspace] ssoSilent not available:', error instanceof Error ? error.message : String(error));
            return null;
        }
    }

    /**
     * Fall back to popup authentication (requires user interaction)
     */
    private async attemptPopupAuth(): Promise<AuthenticationResult> {
        const popupRequest: PopupRequest = {
            scopes: [this.namedScope],
            loginHint: this.currentAccount?.username // Pre-fill email if known
        };

        try {
            return await this.msalInstance.acquireTokenPopup(popupRequest);
        } catch (error) {
            // Handle specific popup errors
            if (error instanceof Error) {
                if (error.message.includes('popup_window_error') || error.message.includes('BrowserAuthError')) {
                    throw new Error('Popup blocked. Please allow popups for this site and try again.');
                }
                if (error.message.includes('user_cancelled') || error.message.includes('popup_window_closed')) {
                    throw new Error('Authentication cancelled by user');
                }
            }
            throw error;
        }
    }

    /**
     * Get current account info (if authenticated)
     */
    public getCurrentAccount(): AccountInfo | null {
        return this.currentAccount;
    }

    /**
     * Check if service is initialized
     */
    public isInitialized(): boolean {
        return this.initialized;
    }

    /**
     * Get the named scope being used (for verification)
     */
    public getScope(): string {
        return this.namedScope;
    }
}
