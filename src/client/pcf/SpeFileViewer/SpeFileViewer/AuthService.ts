/**
 * MSAL Authentication Service for BFF API Access
 *
 * CRITICAL: Uses named scope api://<BFF_APP_ID>/SDAP.Access
 * DO NOT use .default scope (that's for daemon/confidential clients)
 */

import {
    PublicClientApplication,
    AccountInfo,
    AuthenticationResult,
    SilentRequest,
    PopupRequest,
    InteractionRequiredAuthError
} from '@azure/msal-browser';

/**
 * AuthService handles MSAL token acquisition for BFF API calls
 *
 * Architecture:
 * - PCF runs in Dataverse context (user already authenticated)
 * - Uses ssoSilent() for seamless token acquisition
 * - Falls back to popup for consent/MFA if needed
 * - Tokens cached by MSAL browser storage
 */
export class AuthService {
    private msalInstance: PublicClientApplication;
    private bffAppId: string;
    private namedScope: string;

    /**
     * Initialize MSAL public client
     *
     * @param tenantId Azure AD tenant ID
     * @param clientAppId PCF Client Application ID (for MSAL authentication)
     * @param bffAppId BFF Application ID (for scope construction)
     */
    constructor(tenantId: string, clientAppId: string, bffAppId: string) {
        this.bffAppId = bffAppId;

        // CRITICAL: Named scope, NOT .default
        // Format: api://<BFF_APP_ID>/SDAP.Access
        this.namedScope = `api://${bffAppId}/SDAP.Access`;

        // MSAL configuration for public client (SPA)
        this.msalInstance = new PublicClientApplication({
            auth: {
                clientId: clientAppId, // PCF Client App ID (for MSAL authentication)
                authority: `https://login.microsoftonline.com/${tenantId}`,
                redirectUri: window.location.origin, // Dataverse origin
                postLogoutRedirectUri: window.location.origin
            },
            cache: {
                cacheLocation: 'sessionStorage', // Use sessionStorage for PCF (avoid cross-tab issues)
                storeAuthStateInCookie: false // Not needed for modern browsers
            },
            system: {
                loggerOptions: {
                    loggerCallback: (level, message, containsPii) => {
                        if (containsPii) return; // Skip PII logs
                        console.log(`[MSAL] ${message}`);
                    },
                    logLevel: 3 // Warning level (reduce noise)
                }
            }
        });
    }

    /**
     * Initialize MSAL (must be called before token acquisition)
     */
    public async initialize(): Promise<void> {
        await this.msalInstance.initialize();
    }

    /**
     * Get access token for BFF API
     *
     * Flow:
     * 1. Try ssoSilent (leverage Dataverse session)
     * 2. Try acquireTokenSilent (use cached tokens)
     * 3. Fall back to popup (for consent/MFA)
     *
     * @returns Access token (JWT Bearer token)
     * @throws Error if token acquisition fails
     */
    public async getAccessToken(): Promise<string> {
        try {
            // Step 1: Try SSO silent (leverage existing Dataverse session)
            console.log('[AuthService] Attempting SSO silent authentication...');
            const ssoResult = await this.attemptSsoSilent();
            if (ssoResult) {
                console.log('[AuthService] SSO silent succeeded');
                return ssoResult.accessToken;
            }

            // Step 2: Try cached token (if user previously authenticated)
            console.log('[AuthService] Attempting cached token acquisition...');
            const cachedResult = await this.attemptCachedToken();
            if (cachedResult) {
                console.log('[AuthService] Cached token acquired');
                return cachedResult.accessToken;
            }

            // Step 3: Fall back to popup (requires user interaction)
            console.log('[AuthService] Falling back to popup authentication...');
            const popupResult = await this.attemptPopupAuth();
            console.log('[AuthService] Popup authentication succeeded');
            return popupResult.accessToken;

        } catch (error) {
            console.error('[AuthService] Token acquisition failed:', error);
            throw new Error(`Failed to acquire access token: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    /**
     * Attempt SSO silent authentication (leverage Dataverse session)
     */
    private async attemptSsoSilent(): Promise<AuthenticationResult | null> {
        try {
            const ssoRequest: SilentRequest = {
                scopes: [this.namedScope] // CRITICAL: Named scope
            };

            return await this.msalInstance.ssoSilent(ssoRequest);
        } catch (error) {
            // SSO silent can fail if no SSO session exists
            console.log('[AuthService] SSO silent not available:', error instanceof Error ? error.message : String(error));
            return null;
        }
    }

    /**
     * Attempt to use cached token (from previous authentication)
     */
    private async attemptCachedToken(): Promise<AuthenticationResult | null> {
        const accounts = this.msalInstance.getAllAccounts();
        if (accounts.length === 0) {
            console.log('[AuthService] No cached accounts found');
            return null;
        }

        const account = accounts[0]; // Use first account
        console.log(`[AuthService] Found cached account: ${account.username}`);

        try {
            const silentRequest: SilentRequest = {
                scopes: [this.namedScope], // CRITICAL: Named scope
                account: account
            };

            return await this.msalInstance.acquireTokenSilent(silentRequest);
        } catch (error) {
            if (error instanceof InteractionRequiredAuthError) {
                // Token expired or consent needed
                console.log('[AuthService] Cached token invalid, interaction required');
                return null;
            }
            throw error; // Unexpected error
        }
    }

    /**
     * Fall back to popup authentication (requires user interaction)
     */
    private async attemptPopupAuth(): Promise<AuthenticationResult> {
        const popupRequest: PopupRequest = {
            scopes: [this.namedScope] // CRITICAL: Named scope
        };

        return await this.msalInstance.acquireTokenPopup(popupRequest);
    }

    /**
     * Get current account info (if authenticated)
     */
    public getCurrentAccount(): AccountInfo | null {
        const accounts = this.msalInstance.getAllAccounts();
        return accounts.length > 0 ? accounts[0] : null;
    }

    /**
     * Clear cached tokens (for testing/troubleshooting)
     */
    public async logout(): Promise<void> {
        const account = this.getCurrentAccount();
        if (account) {
            await this.msalInstance.logoutPopup({ account });
        }
    }

    /**
     * Get the named scope being used (for verification)
     */
    public getScope(): string {
        return this.namedScope;
    }
}
