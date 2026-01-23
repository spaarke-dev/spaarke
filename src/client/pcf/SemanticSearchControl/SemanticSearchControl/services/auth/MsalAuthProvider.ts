/**
 * MSAL Authentication Provider for SemanticSearchControl PCF
 *
 * Provides SSO authentication for the Semantic Search API.
 * Adapted from DocumentRelationshipViewer pattern.
 *
 * @see spec.md for authentication flow requirements
 */

import {
    PublicClientApplication,
    AccountInfo,
    AuthenticationResult,
    SilentRequest,
    InteractionRequiredAuthError,
    PopupRequest,
} from "@azure/msal-browser";
import { msalConfig, loginRequest, validateMsalConfig } from "./msalConfig";

/**
 * Token cache entry structure
 */
interface TokenCacheEntry {
    token: string;
    expiresAt: number;
    scopes: string[];
}

/**
 * MSAL Authentication Provider
 *
 * Singleton pattern for managing MSAL authentication in PCF controls.
 */
export class MsalAuthProvider {
    private static readonly CACHE_KEY_PREFIX = "msal.semantic.token.";
    private static readonly EXPIRATION_BUFFER_MS = 5 * 60 * 1000; // 5 minutes
    private static instance: MsalAuthProvider;
    private msalInstance: PublicClientApplication | null = null;
    private currentAccount: AccountInfo | null = null;
    private isInitialized = false;

    // eslint-disable-next-line @typescript-eslint/no-empty-function
    private constructor() {}

    /**
     * Get singleton instance of MsalAuthProvider.
     */
    public static getInstance(): MsalAuthProvider {
        if (!MsalAuthProvider.instance) {
            MsalAuthProvider.instance = new MsalAuthProvider();
        }
        return MsalAuthProvider.instance;
    }

    /**
     * Initialize MSAL with configuration.
     */
    public async initialize(): Promise<void> {
        if (this.isInitialized) {
            console.warn("[MsalAuthProvider] Already initialized, skipping");
            return;
        }

        console.info("[MsalAuthProvider] Initializing MSAL...");

        try {
            validateMsalConfig();
            this.msalInstance = new PublicClientApplication(msalConfig);
            await this.msalInstance.initialize();

            // Handle redirect response (if any)
            const redirectResponse = await this.msalInstance.handleRedirectPromise();
            if (redirectResponse) {
                this.currentAccount = redirectResponse.account;
            }

            // Set current account from cache if available
            if (!this.currentAccount) {
                const accounts = this.msalInstance.getAllAccounts();
                if (accounts.length > 0) {
                    this.currentAccount = accounts[0];
                    console.info(
                        `[MsalAuthProvider] Active account found: ${this.currentAccount.username}`
                    );
                }
            }

            this.isInitialized = true;
            console.info("[MsalAuthProvider] Initialization complete");
        } catch (error) {
            console.error("[MsalAuthProvider] Initialization failed", error);
            throw new Error(
                `MSAL initialization failed: ${error instanceof Error ? error.message : String(error)}`
            );
        }
    }

    /**
     * Check if user is currently authenticated.
     */
    public isAuthenticated(): boolean {
        return this.currentAccount !== null;
    }

    /**
     * Get access token for API calls.
     *
     * @param scopes - OAuth scopes (optional, uses default from config)
     * @returns Access token string
     */
    public async getAccessToken(scopes?: string[]): Promise<string> {
        const tokenScopes = scopes ?? loginRequest.scopes;
        return this.getToken(tokenScopes);
    }

    /**
     * Get token for specified scopes.
     */
    public async getToken(scopes: string[]): Promise<string> {
        if (!this.isInitialized || !this.msalInstance) {
            throw new Error("MSAL not initialized. Call initialize() first.");
        }

        if (!scopes || scopes.length === 0) {
            throw new Error("Scopes parameter is required and must not be empty.");
        }

        // Check cache first
        const cachedToken = this.getCachedToken(scopes);
        if (cachedToken) {
            return cachedToken;
        }

        console.info(
            `[MsalAuthProvider] Acquiring token for scopes: ${scopes.join(", ")}`
        );

        try {
            const tokenResponse = await this.acquireTokenSilent(scopes);
            console.info("[MsalAuthProvider] Token acquired successfully via silent flow");

            if (tokenResponse.expiresOn) {
                this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
            }

            return tokenResponse.accessToken;
        } catch (error) {
            if (error instanceof InteractionRequiredAuthError) {
                console.warn(
                    "[MsalAuthProvider] Silent token acquisition failed, falling back to popup..."
                );

                try {
                    const tokenResponse = await this.acquireTokenPopup(scopes);
                    console.info("[MsalAuthProvider] Token acquired successfully via popup");

                    if (tokenResponse.expiresOn) {
                        this.setCachedToken(
                            tokenResponse.accessToken,
                            tokenResponse.expiresOn,
                            scopes
                        );
                    }

                    return tokenResponse.accessToken;
                } catch (popupError) {
                    console.error("[MsalAuthProvider] Popup token acquisition failed", popupError);
                    throw new Error(
                        `Failed to acquire token via popup: ${popupError instanceof Error ? popupError.message : "Unknown error"}`
                    );
                }
            }

            console.error("[MsalAuthProvider] Token acquisition failed", error);
            throw new Error(
                `Failed to acquire token: ${error instanceof Error ? error.message : "Unknown error"}`
            );
        }
    }

    /**
     * Clear token cache.
     */
    public clearCache(): void {
        console.info("[MsalAuthProvider] Clearing token cache");

        try {
            const keysToRemove: string[] = [];
            for (let i = 0; i < sessionStorage.length; i++) {
                const key = sessionStorage.key(i);
                if (key?.startsWith(MsalAuthProvider.CACHE_KEY_PREFIX)) {
                    keysToRemove.push(key);
                }
            }
            keysToRemove.forEach((key) => sessionStorage.removeItem(key));
        } catch (error) {
            console.warn("[MsalAuthProvider] Failed to clear sessionStorage cache", error);
        }

        this.currentAccount = null;
        console.info("[MsalAuthProvider] Token cache cleared");
    }

    /**
     * Acquire token silently using cached account.
     */
    private async acquireTokenSilent(scopes: string[]): Promise<AuthenticationResult> {
        if (!this.msalInstance) {
            throw new Error("MSAL instance not initialized");
        }

        if (this.currentAccount) {
            const silentRequest: SilentRequest = {
                scopes,
                account: this.currentAccount,
            };

            try {
                return await this.msalInstance.acquireTokenSilent(silentRequest);
            } catch {
                console.debug("[MsalAuthProvider] acquireTokenSilent failed, trying ssoSilent");
            }
        }

        // Try SSO silent without account hint
        const ssoRequest: SilentRequest = { scopes };
        const tokenResponse = await this.msalInstance.ssoSilent(ssoRequest);

        if (tokenResponse.account) {
            this.currentAccount = tokenResponse.account;
        }

        return tokenResponse;
    }

    /**
     * Acquire token via popup interaction.
     */
    private async acquireTokenPopup(scopes: string[]): Promise<AuthenticationResult> {
        if (!this.msalInstance) {
            throw new Error("MSAL instance not initialized");
        }

        const popupRequest: PopupRequest = {
            scopes,
            loginHint: this.currentAccount?.username,
        };

        try {
            const tokenResponse = await this.msalInstance.acquireTokenPopup(popupRequest);

            if (tokenResponse.account) {
                this.currentAccount = tokenResponse.account;
            }

            return tokenResponse;
        } catch (error) {
            if (error instanceof Error) {
                if (
                    error.message.includes("user_cancelled") ||
                    error.message.includes("popup_window_closed")
                ) {
                    throw new Error("Authentication cancelled by user");
                }
                if (
                    error.message.includes("popup_window_error") ||
                    error.message.includes("BrowserAuthError")
                ) {
                    throw new Error(
                        "Popup blocked. Please allow popups for this site and try again."
                    );
                }
            }
            throw error;
        }
    }

    /**
     * Get token from session storage cache.
     */
    private getCachedToken(scopes: string[]): string | null {
        try {
            const cacheKey = this.getCacheKey(scopes);
            const cachedData = sessionStorage.getItem(cacheKey);

            if (!cachedData) {
                return null;
            }

            const cacheEntry: TokenCacheEntry = JSON.parse(cachedData) as TokenCacheEntry;
            const now = Date.now();
            const bufferExpiration = cacheEntry.expiresAt - MsalAuthProvider.EXPIRATION_BUFFER_MS;

            if (now >= bufferExpiration) {
                this.removeCachedToken(scopes);
                return null;
            }

            if (!this.scopesMatch(cacheEntry.scopes, scopes)) {
                return null;
            }

            return cacheEntry.token;
        } catch (error) {
            console.warn("[MsalAuthProvider] Failed to read cached token", error);
            return null;
        }
    }

    /**
     * Store token in session storage cache.
     */
    private setCachedToken(token: string, expiresOn: Date, scopes: string[]): void {
        try {
            const cacheKey = this.getCacheKey(scopes);
            const cacheEntry: TokenCacheEntry = {
                token,
                expiresAt: expiresOn.getTime(),
                scopes,
            };
            sessionStorage.setItem(cacheKey, JSON.stringify(cacheEntry));
        } catch (error) {
            console.warn("[MsalAuthProvider] Failed to cache token", error);
        }
    }

    /**
     * Remove token from cache.
     */
    private removeCachedToken(scopes: string[]): void {
        try {
            const cacheKey = this.getCacheKey(scopes);
            sessionStorage.removeItem(cacheKey);
        } catch (error) {
            console.warn("[MsalAuthProvider] Failed to remove cached token", error);
        }
    }

    /**
     * Generate cache key from scopes.
     */
    private getCacheKey(scopes: string[]): string {
        const sortedScopes = scopes.slice().sort();
        return MsalAuthProvider.CACHE_KEY_PREFIX + sortedScopes.join(",");
    }

    /**
     * Check if two scope arrays match.
     */
    private scopesMatch(scopes1: string[], scopes2: string[]): boolean {
        if (scopes1.length !== scopes2.length) {
            return false;
        }
        const sorted1 = scopes1.slice().sort();
        const sorted2 = scopes2.slice().sort();
        return sorted1.every((scope, index) => scope === sorted2[index]);
    }
}

export default MsalAuthProvider;
