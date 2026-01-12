import {
  PublicClientApplication,
  AccountInfo,
  AuthenticationResult,
  SilentRequest,
  InteractionRequiredAuthError,
  PopupRequest,
} from "@azure/msal-browser";
import { msalConfig, validateMsalConfig } from "./msalConfig";
import { IAuthProvider, TokenCacheEntry } from "../../types/auth";

/**
 * MSAL Authentication Provider for DocumentRelationshipViewer PCF
 *
 * Provides SSO authentication for the visualization API.
 */
export class MsalAuthProvider implements IAuthProvider {
  private static readonly CACHE_KEY_PREFIX = "msal.token.";
  private static readonly EXPIRATION_BUFFER_MS = 5 * 60 * 1000;
  private static instance: MsalAuthProvider;
  private msalInstance: PublicClientApplication | null = null;
  private currentAccount: AccountInfo | null = null;
  private isInitialized = false;
  private refreshPromises = new Map<string, Promise<void>>();

  // eslint-disable-next-line @typescript-eslint/no-empty-function
  private constructor() {}

  public static getInstance(): MsalAuthProvider {
    if (!MsalAuthProvider.instance) {
      MsalAuthProvider.instance = new MsalAuthProvider();
    }
    return MsalAuthProvider.instance;
  }

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

      const redirectResponse = await this.msalInstance.handleRedirectPromise();
      if (redirectResponse) {
        this.currentAccount = redirectResponse.account;
      }

      if (!this.currentAccount) {
        const accounts = this.msalInstance.getAllAccounts();
        if (accounts.length > 0) {
          this.currentAccount = accounts[0];
          console.info(`[MsalAuthProvider] Active account found: ${this.currentAccount.username}`);
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

  public async getToken(scopes: string[]): Promise<string> {
    if (!this.isInitialized || !this.msalInstance) {
      throw new Error("MSAL not initialized. Call initialize() first.");
    }

    if (!scopes || scopes.length === 0) {
      throw new Error("Scopes parameter is required and must not be empty.");
    }

    const cachedToken = this.getCachedToken(scopes);
    if (cachedToken) {
      return cachedToken;
    }

    console.info(`[MsalAuthProvider] Acquiring token for scopes: ${scopes.join(", ")}`);

    try {
      const tokenResponse = await this.acquireTokenSilent(scopes);
      console.info("[MsalAuthProvider] Token acquired successfully via silent flow");

      if (tokenResponse.expiresOn) {
        this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
      }

      return tokenResponse.accessToken;

    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        console.warn("[MsalAuthProvider] Silent token acquisition failed, falling back to popup...");

        try {
          const tokenResponse = await this.acquireTokenPopup(scopes);
          console.info("[MsalAuthProvider] Token acquired successfully via popup");

          if (tokenResponse.expiresOn) {
            this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
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

  public clearCache(): void {
    console.info("[MsalAuthProvider] Clearing token cache");

    this.refreshPromises.clear();

    try {
      const keysToRemove: string[] = [];
      for (let i = 0; i < sessionStorage.length; i++) {
        const key = sessionStorage.key(i);
        if (key?.startsWith(MsalAuthProvider.CACHE_KEY_PREFIX)) {
          keysToRemove.push(key);
        }
      }
      keysToRemove.forEach(key => sessionStorage.removeItem(key));
    } catch (error) {
      console.warn("[MsalAuthProvider] Failed to clear sessionStorage cache", error);
    }

    this.currentAccount = null;
    console.info("[MsalAuthProvider] Token cache cleared");
  }

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

    const ssoRequest: SilentRequest = { scopes };
    const tokenResponse = await this.msalInstance.ssoSilent(ssoRequest);

    if (tokenResponse.account) {
      this.currentAccount = tokenResponse.account;
    }

    return tokenResponse;
  }

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

  private removeCachedToken(scopes: string[]): void {
    try {
      const cacheKey = this.getCacheKey(scopes);
      sessionStorage.removeItem(cacheKey);
    } catch (error) {
      console.warn("[MsalAuthProvider] Failed to remove cached token", error);
    }
  }

  private getCacheKey(scopes: string[]): string {
    const sortedScopes = scopes.slice().sort();
    return MsalAuthProvider.CACHE_KEY_PREFIX + sortedScopes.join(",");
  }

  private scopesMatch(scopes1: string[], scopes2: string[]): boolean {
    if (scopes1.length !== scopes2.length) {
      return false;
    }
    const sorted1 = scopes1.slice().sort();
    const sorted2 = scopes2.slice().sort();
    return sorted1.every((scope, index) => scope === sorted2[index]);
  }
}
