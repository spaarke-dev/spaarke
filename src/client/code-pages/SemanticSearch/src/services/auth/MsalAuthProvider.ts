import {
    PublicClientApplication,
    AccountInfo,
    AuthenticationResult,
    SilentRequest,
    InteractionRequiredAuthError,
    PopupRequest,
} from "@azure/msal-browser";
import { msalConfig, BFF_API_SCOPES, validateMsalConfig } from "./msalConfig";

/**
 * Token cache entry stored in sessionStorage.
 */
interface TokenCacheEntry {
    token: string;
    expiresAt: number;
    scopes: string[];
}

/**
 * MSAL Authentication Provider for SemanticSearch Code Page
 *
 * Singleton pattern — exports a single instance, not a class.
 * Provides SSO authentication for the BFF API.
 *
 * Usage:
 *   import { msalAuthProvider } from "./services/auth/MsalAuthProvider";
 *   await msalAuthProvider.initialize();
 *   const header = await msalAuthProvider.getAuthHeader();
 *   fetch(url, { headers: { Authorization: header } });
 */
class MsalAuthProvider {
    private static readonly CACHE_KEY_PREFIX = "msal.token.";
    private static readonly EXPIRATION_BUFFER_MS = 5 * 60 * 1000;
    private msalInstance: PublicClientApplication | null = null;
    private currentAccount: AccountInfo | null = null;
    private isInitialized = false;

    public async initialize(): Promise<void> {
        if (this.isInitialized) return;

        console.info("[MsalAuthProvider] Initializing MSAL...");
        try {
            validateMsalConfig();
            this.msalInstance = new PublicClientApplication(msalConfig);
            await this.msalInstance.initialize();

            const redirectResponse = await this.msalInstance.handleRedirectPromise();
            if (redirectResponse) {
                this.currentAccount = redirectResponse.account;
            }

            if (!this.currentAccount) {
                const accounts = this.msalInstance.getAllAccounts();
                if (accounts.length > 0) {
                    this.currentAccount = accounts[0];
                }
            }

            this.isInitialized = true;
            console.info("[MsalAuthProvider] Initialization complete");
        } catch (error) {
            console.error("[MsalAuthProvider] Initialization failed", error);
            throw new Error(`MSAL initialization failed: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    public isAuthenticated(): boolean {
        return this.currentAccount !== null;
    }

    /**
     * Get a Bearer authorization header for the BFF API.
     * Acquires token silently first; falls back to popup on InteractionRequiredAuthError.
     * @returns "Bearer {accessToken}" string ready for Authorization header
     */
    public async getAuthHeader(): Promise<string> {
        const token = await this.getToken(BFF_API_SCOPES);
        return `Bearer ${token}`;
    }

    /**
     * Acquire an access token for the given scopes.
     * Checks sessionStorage cache first, then tries silent acquisition,
     * falling back to popup on InteractionRequiredAuthError.
     */
    public async getToken(scopes: string[]): Promise<string> {
        if (!this.isInitialized || !this.msalInstance) {
            throw new Error("MSAL not initialized. Call initialize() first.");
        }
        if (!scopes || scopes.length === 0) {
            throw new Error("Scopes parameter is required and must not be empty.");
        }

        const cachedToken = this.getCachedToken(scopes);
        if (cachedToken) return cachedToken;

        try {
            const tokenResponse = await this.acquireTokenSilent(scopes);
            if (tokenResponse.expiresOn) {
                this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
            }
            return tokenResponse.accessToken;
        } catch (error) {
            if (error instanceof InteractionRequiredAuthError) {
                try {
                    const tokenResponse = await this.acquireTokenPopup(scopes);
                    if (tokenResponse.expiresOn) {
                        this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
                    }
                    return tokenResponse.accessToken;
                } catch (popupError) {
                    throw new Error(`Failed to acquire token via popup: ${popupError instanceof Error ? popupError.message : "Unknown error"}`);
                }
            }
            throw new Error(`Failed to acquire token: ${error instanceof Error ? error.message : "Unknown error"}`);
        }
    }

    public clearCache(): void {
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
    }

    private async acquireTokenSilent(scopes: string[]): Promise<AuthenticationResult> {
        if (!this.msalInstance) throw new Error("MSAL instance not initialized");

        if (this.currentAccount) {
            const silentRequest: SilentRequest = { scopes, account: this.currentAccount };
            try {
                return await this.msalInstance.acquireTokenSilent(silentRequest);
            } catch {
                console.debug("[MsalAuthProvider] acquireTokenSilent failed, trying ssoSilent");
            }
        }

        const ssoRequest: SilentRequest = { scopes };
        const tokenResponse = await this.msalInstance.ssoSilent(ssoRequest);
        if (tokenResponse.account) {
            this.currentAccount = tokenResponse.account;
        }
        return tokenResponse;
    }

    private async acquireTokenPopup(scopes: string[]): Promise<AuthenticationResult> {
        if (!this.msalInstance) throw new Error("MSAL instance not initialized");

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
                if (error.message.includes("user_cancelled") || error.message.includes("popup_window_closed")) {
                    throw new Error("Authentication cancelled by user");
                }
                if (error.message.includes("popup_window_error") || error.message.includes("BrowserAuthError")) {
                    throw new Error("Popup blocked. Please allow popups for this site and try again.");
                }
            }
            throw error;
        }
    }

    private getCachedToken(scopes: string[]): string | null {
        try {
            const cacheKey = this.getCacheKey(scopes);
            const cachedData = sessionStorage.getItem(cacheKey);
            if (!cachedData) return null;

            const cacheEntry: TokenCacheEntry = JSON.parse(cachedData) as TokenCacheEntry;
            const now = Date.now();
            const bufferExpiration = cacheEntry.expiresAt - MsalAuthProvider.EXPIRATION_BUFFER_MS;

            if (now >= bufferExpiration) {
                this.removeCachedToken(scopes);
                return null;
            }
            if (!this.scopesMatch(cacheEntry.scopes, scopes)) return null;
            return cacheEntry.token;
        } catch {
            return null;
        }
    }

    private setCachedToken(token: string, expiresOn: Date, scopes: string[]): void {
        try {
            const cacheKey = this.getCacheKey(scopes);
            const cacheEntry: TokenCacheEntry = { token, expiresAt: expiresOn.getTime(), scopes };
            sessionStorage.setItem(cacheKey, JSON.stringify(cacheEntry));
        } catch {
            // Silently ignore cache write failures
        }
    }

    private removeCachedToken(scopes: string[]): void {
        try {
            sessionStorage.removeItem(this.getCacheKey(scopes));
        } catch {
            // Silently ignore
        }
    }

    private getCacheKey(scopes: string[]): string {
        return MsalAuthProvider.CACHE_KEY_PREFIX + scopes.slice().sort().join(",");
    }

    private scopesMatch(scopes1: string[], scopes2: string[]): boolean {
        if (scopes1.length !== scopes2.length) return false;
        const sorted1 = scopes1.slice().sort();
        const sorted2 = scopes2.slice().sort();
        return sorted1.every((scope, index) => scope === sorted2[index]);
    }
}

/** Singleton MSAL auth provider instance for the SemanticSearch code page */
export const msalAuthProvider = new MsalAuthProvider();
