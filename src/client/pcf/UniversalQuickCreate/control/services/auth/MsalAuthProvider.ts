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
 * MSAL Authentication Provider for Universal Dataset Grid
 *
 * Responsibilities:
 * 1. Initialize PublicClientApplication (MSAL instance)
 * 2. Handle OAuth redirect responses
 * 3. Manage user account state
 * 4. (Phase 2) Acquire user tokens via SSO silent flow
 * 5. (Phase 2) Cache tokens in sessionStorage for performance
 * 6. (Phase 2) Refresh expired tokens automatically
 *
 * ADR Compliance:
 * - ADR-002: Client-side only (no plugins)
 * - ADR-007: Implements IAuthProvider interface (minimal abstraction)
 *
 * Usage:
 *   const authProvider = MsalAuthProvider.getInstance();
 *   await authProvider.initialize();
 *   const isAuth = authProvider.isAuthenticated();
 *
 * Sprint 4 Integration (Phase 2):
 *   const token = await authProvider.getToken(["api://spe-bff-api/user_impersonation"]);
 *   // Use token in Authorization: Bearer header for Spe.Bff.Api calls
 *
 * @see https://learn.microsoft.com/en-us/azure/active-directory/develop/scenario-spa-acquire-token
 */
export class MsalAuthProvider implements IAuthProvider {
  // ============================================================================
  // Cache Configuration
  // ============================================================================

  /**
   * sessionStorage key prefix for token cache
   *
   * Format: msal.token.<scopes-hash>
   * Example: msal.token.api://spe-bff-api/user_impersonation
   */
  private static readonly CACHE_KEY_PREFIX = "msal.token.";

  /**
   * Token expiration buffer (milliseconds)
   *
   * Refresh token this many milliseconds BEFORE actual expiration
   * to avoid using expired tokens.
   *
   * 5 minutes = 300,000 ms
   */
  private static readonly EXPIRATION_BUFFER_MS = 5 * 60 * 1000;

  // ============================================================================
  // Private Fields
  // ============================================================================

  /**
   * Singleton instance
   *
   * Only one MsalAuthProvider should exist per PCF control instance.
   * Multiple instances would conflict with MSAL's internal state.
   */
  private static instance: MsalAuthProvider;

  /**
   * MSAL PublicClientApplication instance
   *
   * Core MSAL.js object for authentication operations.
   * Created during initialize(), null before initialization.
   */
  private msalInstance: PublicClientApplication | null = null;

  /**
   * Current authenticated account
   *
   * Represents the signed-in user. Used for token acquisition.
   * null if no user authenticated.
   */
  private currentAccount: AccountInfo | null = null;

  /**
   * Initialization state flag
   *
   * Prevents re-initialization and ensures initialize() called before other methods.
   */
  private isInitialized = false;

  /**
   * Track ongoing refresh operations (prevent duplicate refreshes)
   *
   * Key: scopes (comma-separated, sorted) via getCacheKey()
   * Value: Promise<void> (resolves when refresh complete)
   */
  private refreshPromises = new Map<string, Promise<void>>();

  // ============================================================================
  // Constructor (Private - Singleton Pattern)
  // ============================================================================

  /**
   * Private constructor for singleton pattern
   *
   * Use MsalAuthProvider.getInstance() instead of new MsalAuthProvider().
   */
  private constructor() {
    // Intentionally empty - singleton pattern
  }

  // ============================================================================
  // Singleton Instance Access
  // ============================================================================

  /**
   * Get singleton instance of MsalAuthProvider
   *
   * Creates instance on first call, returns existing instance on subsequent calls.
   *
   * @returns The singleton MsalAuthProvider instance
   */
  public static getInstance(): MsalAuthProvider {
    if (!MsalAuthProvider.instance) {
      MsalAuthProvider.instance = new MsalAuthProvider();
    }
    return MsalAuthProvider.instance;
  }

  // ============================================================================
  // IAuthProvider Interface Implementation
  // ============================================================================

  /**
   * Initialize MSAL PublicClientApplication
   *
   * Call this once during PCF control initialization (index.ts init() method).
   *
   * Steps:
   * 1. Validate MSAL configuration (fail fast if misconfigured)
   * 2. Create PublicClientApplication instance
   * 3. Handle redirect response (if returning from Azure AD login)
   * 4. Set active account (if user already logged in)
   *
   * Idempotent: Safe to call multiple times (will skip if already initialized).
   *
   * @throws Error if configuration is invalid or initialization fails
   */
  public async initialize(): Promise<void> {
    // Skip if already initialized (idempotent)
    if (this.isInitialized) {
      console.warn("[MsalAuthProvider] Already initialized, skipping");
      return;
    }

    console.info("[MsalAuthProvider] Initializing MSAL...");

    try {
      // Step 1: Validate configuration (throws if invalid)
      validateMsalConfig();
      console.info("[MsalAuthProvider] Configuration validated ✅");

      // Step 2: Create PublicClientApplication instance
      this.msalInstance = new PublicClientApplication(msalConfig);
      console.info("[MsalAuthProvider] PublicClientApplication created ✅");

      // Step 2.5: Initialize MSAL instance (REQUIRED in MSAL.js v3+)
      // Must be called before any other MSAL methods
      await this.msalInstance.initialize();
      console.info("[MsalAuthProvider] MSAL instance initialized ✅");

      // Step 3: Handle redirect response
      // If user was redirected to Azure AD for login and is now returning,
      // this processes the OAuth response and extracts tokens.
      const redirectResponse = await this.msalInstance.handleRedirectPromise();
      if (redirectResponse) {
        console.info(
          "[MsalAuthProvider] Redirect response processed, user authenticated via redirect ✅"
        );
        this.currentAccount = redirectResponse.account;
      }

      // Step 4: Set active account (if user already logged in)
      // If no redirect response, check if user has existing session
      if (!this.currentAccount) {
        const accounts = this.msalInstance.getAllAccounts();
        if (accounts.length > 0) {
          // Use first account (typically only one account for enterprise apps)
          this.currentAccount = accounts[0];
          console.info(
            `[MsalAuthProvider] Active account found: ${this.currentAccount.username} ✅`
          );
        } else {
          console.info("[MsalAuthProvider] No active account found (user not logged in)");
        }
      }

      // Mark as initialized
      this.isInitialized = true;
      console.info("[MsalAuthProvider] Initialization complete ✅");
    } catch (error) {
      console.error("[MsalAuthProvider] Initialization failed ❌", error);
      throw new Error(`MSAL initialization failed: ${error}`);
    }
  }

  /**
   * Check if user is authenticated
   *
   * @returns true if user has active account, false otherwise
   */
  public isAuthenticated(): boolean {
    return this.currentAccount !== null;
  }

  /**
   * Get access token for specified scopes
   *
   * Flow:
   * 1. Check sessionStorage cache for unexpired token
   * 2. If cached and valid, return cached token (fast path)
   * 3. If not cached or expired, acquire new token via MSAL
   * 4. Cache newly acquired token
   * 5. Return token
   *
   * The acquired token is used in Authorization: Bearer header for Spe.Bff.Api calls.
   *
   * @param scopes - OAuth scopes to request (e.g., ["api://spe-bff-api/user_impersonation"])
   * @returns Access token string for use in Authorization header
   * @throws Error if token acquisition fails after all retry attempts
   */
  public async getToken(scopes: string[]): Promise<string> {
    // Validate initialization
    if (!this.isInitialized || !this.msalInstance) {
      throw new Error("MSAL not initialized. Call initialize() first.");
    }

    // Validate scopes parameter
    if (!scopes || scopes.length === 0) {
      throw new Error("Scopes parameter is required and must not be empty.");
    }

    // ========================================================================
    // Step 1: Check Cache (Fast Path)
    // ========================================================================
    const cachedToken = this.getCachedToken(scopes);
    if (cachedToken) {
      // Cached token found and still valid
      return cachedToken;
    }

    // ========================================================================
    // Step 2: Acquire New Token (Cache Miss or Expired)
    // ========================================================================
    console.info(`[MsalAuthProvider] Acquiring token for scopes: ${scopes.join(", ")}`);

    try {
      // Try SSO silent token acquisition
      const tokenResponse = await this.acquireTokenSilent(scopes);

      console.info("[MsalAuthProvider] Token acquired successfully via silent flow ✅");
      console.debug("[MsalAuthProvider] Token expires:", tokenResponse.expiresOn);

      // Cache the newly acquired token
      if (tokenResponse.expiresOn) {
        this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
      }

      return tokenResponse.accessToken;

    } catch (error) {
      // ========================================================================
      // Step 3: Handle InteractionRequiredAuthError (Fallback to Popup)
      // ========================================================================
      // If SSO silent fails because user interaction required (consent, MFA, etc.),
      // fall back to popup login

      if (error instanceof InteractionRequiredAuthError) {
        console.warn(
          "[MsalAuthProvider] Silent token acquisition failed, user interaction required. " +
          "Falling back to popup login..."
        );

        try {
          const tokenResponse = await this.acquireTokenPopup(scopes);

          console.info("[MsalAuthProvider] Token acquired successfully via popup ✅");
          console.debug("[MsalAuthProvider] Token expires:", tokenResponse.expiresOn);

          // Cache the token acquired via popup
          if (tokenResponse.expiresOn) {
            this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
          }

          return tokenResponse.accessToken;

        } catch (popupError) {
          console.error("[MsalAuthProvider] Popup token acquisition failed ❌", popupError);
          throw new Error(
            `Failed to acquire token via popup: ${popupError instanceof Error ? popupError.message : "Unknown error"}`
          );
        }
      }

      // ========================================================================
      // Step 4: Handle Other Errors
      // ========================================================================
      console.error("[MsalAuthProvider] Token acquisition failed ❌", error);
      throw new Error(
        `Failed to acquire token: ${error instanceof Error ? error.message : "Unknown error"}`
      );
    }
  }

  /**
   * Clear token cache and sign out
   *
   * Removes cached tokens from:
   * 1. Ongoing refresh operations (cancel)
   * 2. sessionStorage (SDAP cache layer)
   * 3. MSAL internal cache
   *
   * User will need to re-authenticate on next getToken() call.
   */
  public clearCache(): void {
    console.info("[MsalAuthProvider] Clearing token cache");

    // ========================================================================
    // Step 1: Cancel ongoing refresh operations
    // ========================================================================
    if (this.refreshPromises.size > 0) {
      console.debug(`[MsalAuthProvider] Cancelling ${this.refreshPromises.size} ongoing refresh operations`);
      this.refreshPromises.clear();
    }

    // ========================================================================
    // Step 2: Clear sessionStorage cache
    // ========================================================================
    try {
      // Find all keys starting with our cache prefix
      const keysToRemove: string[] = [];
      for (let i = 0; i < sessionStorage.length; i++) {
        const key = sessionStorage.key(i);
        if (key && key.startsWith(MsalAuthProvider.CACHE_KEY_PREFIX)) {
          keysToRemove.push(key);
        }
      }

      // Remove all cached tokens
      keysToRemove.forEach(key => sessionStorage.removeItem(key));

      console.debug(`[MsalAuthProvider] Removed ${keysToRemove.length} cached tokens from sessionStorage`);

    } catch (error) {
      console.warn("[MsalAuthProvider] Failed to clear sessionStorage cache", error);
    }

    // ========================================================================
    // Step 3: Clear MSAL cache
    // ========================================================================
    if (this.msalInstance) {
      // MSAL v3+ has clearCache() method that clears all cached tokens
      if ('clearCache' in this.msalInstance && typeof (this.msalInstance as { clearCache?: () => Promise<void> }).clearCache === 'function') {
        void (this.msalInstance as { clearCache: () => Promise<void> }).clearCache();
      }
    }

    // Reset current account
    this.currentAccount = null;

    console.info("[MsalAuthProvider] Token cache cleared ✅");
  }

  // ============================================================================
  // Private Helper Methods (Token Acquisition)
  // ============================================================================

  /**
   * Acquire token silently using SSO
   *
   * This is the primary token acquisition method.
   * Attempts to get token without user interaction.
   *
   * Flow:
   * 1. If currentAccount exists, use acquireTokenSilent (with account hint)
   * 2. If no currentAccount, use ssoSilent (discovers account from browser session)
   * 3. Update currentAccount if token acquired via ssoSilent
   *
   * @param scopes - OAuth scopes to request
   * @returns AuthenticationResult with access token and metadata
   * @throws InteractionRequiredAuthError if user interaction needed (consent, MFA, login)
   * @throws Error for other token acquisition failures
   */
  private async acquireTokenSilent(scopes: string[]): Promise<AuthenticationResult> {
    if (!this.msalInstance) {
      throw new Error("MSAL instance not initialized");
    }

    // ========================================================================
    // Option 1: acquireTokenSilent (if we have an active account)
    // ========================================================================
    // If user previously authenticated and we have their account info,
    // use acquireTokenSilent with account parameter for best performance

    if (this.currentAccount) {
      console.debug(
        `[MsalAuthProvider] Using acquireTokenSilent with account: ${this.currentAccount.username}`
      );

      const silentRequest: SilentRequest = {
        scopes,
        account: this.currentAccount,
      };

      try {
        const tokenResponse = await this.msalInstance.acquireTokenSilent(silentRequest);
        console.debug("[MsalAuthProvider] acquireTokenSilent succeeded ✅");
        return tokenResponse;

      } catch {
        // If acquireTokenSilent fails, fall through to ssoSilent as backup
        console.debug(
          "[MsalAuthProvider] acquireTokenSilent failed, trying ssoSilent as fallback"
        );
        // Continue to ssoSilent below
      }
    }

    // ========================================================================
    // Option 2: ssoSilent (discover account from browser session)
    // ========================================================================
    // If no currentAccount or acquireTokenSilent failed, use ssoSilent.
    // This attempts to acquire token using existing browser session
    // (user already logged into Model-driven apps, so session exists)

    console.debug("[MsalAuthProvider] Using ssoSilent to discover account from browser session");

    const ssoRequest: SilentRequest = {
      scopes,
      // Note: MSAL v4 doesn't support loginHint in SilentRequest
      // It will automatically discover the account from the browser session
    };

    const tokenResponse = await this.msalInstance.ssoSilent(ssoRequest);

    // Update currentAccount from SSO response
    if (tokenResponse.account) {
      this.currentAccount = tokenResponse.account;
      console.debug(
        `[MsalAuthProvider] Account discovered via ssoSilent: ${this.currentAccount.username} ✅`
      );
    }

    console.debug("[MsalAuthProvider] ssoSilent succeeded ✅");
    return tokenResponse;
  }

  /**
   * Acquire token using popup login (fallback)
   *
   * Used when SSO silent fails (e.g., user not logged in, consent required, MFA).
   * Opens popup window for user authentication.
   *
   * @param scopes - OAuth scopes to request
   * @returns AuthenticationResult with access token and metadata
   * @throws Error if popup blocked, user cancels, or authentication fails
   */
  private async acquireTokenPopup(scopes: string[]): Promise<AuthenticationResult> {
    if (!this.msalInstance) {
      throw new Error("MSAL instance not initialized");
    }

    console.info("[MsalAuthProvider] Opening popup for user authentication...");

    const popupRequest: PopupRequest = {
      scopes,
      loginHint: this.currentAccount?.username, // Pre-fill email if known
    };

    try {
      const tokenResponse = await this.msalInstance.acquireTokenPopup(popupRequest);

      // Update currentAccount from popup response
      if (tokenResponse.account) {
        this.currentAccount = tokenResponse.account;
        console.info(
          `[MsalAuthProvider] User authenticated via popup: ${this.currentAccount.username} ✅`
        );
      }

      return tokenResponse;

    } catch (error) {
      // Handle specific popup errors
      if (error instanceof Error) {
        // User closed popup without completing authentication
        if (error.message.includes("user_cancelled") || error.message.includes("popup_window_closed")) {
          console.warn("[MsalAuthProvider] User cancelled popup authentication");
          throw new Error("Authentication cancelled by user");
        }

        // Popup blocked by browser
        if (error.message.includes("popup_window_error") || error.message.includes("BrowserAuthError")) {
          console.error("[MsalAuthProvider] Popup window blocked by browser");
          throw new Error(
            "Popup blocked. Please allow popups for this site and try again."
          );
        }
      }

      // Other errors
      console.error("[MsalAuthProvider] Popup authentication failed", error);
      throw error;
    }
  }

  // ============================================================================
  // Token Cache Management (sessionStorage)
  // ============================================================================

  /**
   * Get cached token from sessionStorage
   *
   * Checks sessionStorage for cached token matching requested scopes.
   * If token found but nearing expiration, triggers background refresh.
   *
   * @param scopes - OAuth scopes to match
   * @returns Cached token string, or null if not found/expired
   */
  private getCachedToken(scopes: string[]): string | null {
    try {
      const cacheKey = this.getCacheKey(scopes);
      const cachedData = sessionStorage.getItem(cacheKey);

      if (!cachedData) {
        console.debug("[MsalAuthProvider] No cached token found for scopes:", scopes);
        return null;
      }

      // Parse cached token entry
      const cacheEntry: TokenCacheEntry = JSON.parse(cachedData);

      const now = Date.now();
      const expiresAt = cacheEntry.expiresAt;
      const bufferExpiration = expiresAt - MsalAuthProvider.EXPIRATION_BUFFER_MS;

      // ========================================================================
      // Case 1: Token Expired (Past Buffer)
      // ========================================================================
      if (now >= bufferExpiration) {
        console.debug(
          "[MsalAuthProvider] Cached token expired or past expiration buffer. " +
          `Expires: ${new Date(expiresAt).toISOString()}, Now: ${new Date(now).toISOString()}`
        );

        // Remove expired token
        this.removeCachedToken(scopes);
        return null; // Caller will acquire new token
      }

      // ========================================================================
      // Case 2: Token Valid but Nearing Expiration (Proactive Refresh)
      // ========================================================================
      // Calculate refresh threshold (halfway between now and expiration buffer)
      // Example: Token expires in 60 min, buffer is 5 min, refresh at 32.5 min remaining
      const timeUntilBuffer = bufferExpiration - now;
      const refreshThreshold = bufferExpiration - (timeUntilBuffer / 2);

      if (now >= refreshThreshold) {
        const minutesUntilExpiration = Math.round((expiresAt - now) / 1000 / 60);
        console.info(
          `[MsalAuthProvider] Token nearing expiration (${minutesUntilExpiration} min remaining), ` +
          "triggering background refresh..."
        );

        // Trigger non-blocking background refresh
        this.refreshTokenInBackground(scopes);

        // Still return current token (valid for now)
        // Next call will get refreshed token from cache
      }

      // ========================================================================
      // Case 3: Token Valid and Fresh
      // ========================================================================
      // Check if scopes match
      const scopesMatch = this.scopesMatch(cacheEntry.scopes, scopes);
      if (!scopesMatch) {
        console.debug("[MsalAuthProvider] Cached token scopes don't match requested scopes");
        return null;
      }

      const minutesRemaining = Math.round((expiresAt - now) / 1000 / 60);
      console.debug(
        `[MsalAuthProvider] Using cached token ✅ (expires in ${minutesRemaining} minutes)`
      );

      return cacheEntry.token;

    } catch (error) {
      console.warn("[MsalAuthProvider] Failed to read cached token, will reacquire", error);
      return null;
    }
  }

  /**
   * Cache token in sessionStorage
   *
   * Stores token with expiration timestamp and scopes.
   *
   * @param token - Access token string
   * @param expiresOn - Token expiration date from MSAL
   * @param scopes - OAuth scopes for this token
   */
  private setCachedToken(token: string, expiresOn: Date, scopes: string[]): void {
    try {
      const cacheKey = this.getCacheKey(scopes);

      const cacheEntry: TokenCacheEntry = {
        token,
        expiresAt: expiresOn.getTime(), // Convert Date to Unix epoch milliseconds
        scopes,
      };

      sessionStorage.setItem(cacheKey, JSON.stringify(cacheEntry));

      console.debug(
        "[MsalAuthProvider] Token cached ✅ " +
        `(expires: ${expiresOn.toISOString()})`
      );

    } catch (error) {
      // sessionStorage can throw if quota exceeded or in private browsing mode
      console.warn("[MsalAuthProvider] Failed to cache token (will continue without cache)", error);
    }
  }

  /**
   * Remove cached token from sessionStorage
   *
   * @param scopes - OAuth scopes to remove cache for
   */
  private removeCachedToken(scopes: string[]): void {
    try {
      const cacheKey = this.getCacheKey(scopes);
      sessionStorage.removeItem(cacheKey);
      console.debug("[MsalAuthProvider] Cached token removed");
    } catch (error) {
      console.warn("[MsalAuthProvider] Failed to remove cached token", error);
    }
  }

  /**
   * Generate cache key for scopes
   *
   * Format: msal.token.<scopes-joined>
   * Example: msal.token.api://spe-bff-api/user_impersonation
   *
   * @param scopes - OAuth scopes
   * @returns Cache key string
   */
  private getCacheKey(scopes: string[]): string {
    // Sort scopes for consistent cache keys
    // ["scope2", "scope1"] and ["scope1", "scope2"] → same key
    const sortedScopes = scopes.slice().sort();
    return MsalAuthProvider.CACHE_KEY_PREFIX + sortedScopes.join(",");
  }

  /**
   * Check if two scope arrays match
   *
   * Order-independent comparison.
   *
   * @param scopes1 - First scope array
   * @param scopes2 - Second scope array
   * @returns true if scopes match (ignoring order)
   */
  private scopesMatch(scopes1: string[], scopes2: string[]): boolean {
    if (scopes1.length !== scopes2.length) {
      return false;
    }

    const sorted1 = scopes1.slice().sort();
    const sorted2 = scopes2.slice().sort();

    return sorted1.every((scope, index) => scope === sorted2[index]);
  }

  /**
   * Proactively refresh token before expiration
   *
   * Called when cached token is near expiration (within EXPIRATION_BUFFER_MS).
   * Acquires new token in background and updates cache.
   *
   * Non-blocking: Returns immediately, refresh happens asynchronously.
   *
   * @param scopes - OAuth scopes to refresh token for
   */
  private refreshTokenInBackground(scopes: string[]): void {
    const scopesKey = this.getCacheKey(scopes);

    // Check if refresh already in progress for these scopes
    if (this.refreshPromises.has(scopesKey)) {
      console.debug("[MsalAuthProvider] Token refresh already in progress for scopes:", scopes);
      return;
    }

    console.info("[MsalAuthProvider] Starting background token refresh for scopes:", scopes);

    // Create refresh promise
    const refreshPromise = (async () => {
      try {
        // Acquire new token silently
        const tokenResponse = await this.acquireTokenSilent(scopes);

        console.info("[MsalAuthProvider] Background token refresh succeeded ✅");

        // Update cache with new token
        if (tokenResponse.expiresOn) {
          this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
        }

      } catch (error) {
        // Log error but don't throw (background operation should not break app)
        console.warn(
          "[MsalAuthProvider] Background token refresh failed (will retry on next call)",
          error
        );

        // Remove failed token from cache so next getToken() will acquire fresh token
        this.removeCachedToken(scopes);

      } finally {
        // Remove from refresh tracking
        this.refreshPromises.delete(scopesKey);
      }
    })();

    // Track refresh promise
    this.refreshPromises.set(scopesKey, refreshPromise);
  }

  // ============================================================================
  // Helper Methods (For Debugging/Testing)
  // ============================================================================

  /**
   * Get current account info
   *
   * Returns the currently authenticated account, or null if not authenticated.
   * Useful for debugging and testing.
   *
   * @returns Current account or null
   */
  public getCurrentAccount(): AccountInfo | null {
    return this.currentAccount;
  }

  /**
   * Get initialization state
   *
   * @returns true if initialize() has been called successfully, false otherwise
   */
  public isInitializedState(): boolean {
    return this.isInitialized;
  }

  /**
   * Get the tenant ID of the current authenticated user.
   *
   * Returns the Azure AD tenant ID from the current account,
   * or null if not authenticated.
   *
   * @returns Tenant ID or null
   */
  public getTenantId(): string | null {
    return this.currentAccount?.tenantId ?? null;
  }

  /**
   * Get account info for logging (sanitized)
   *
   * Returns sanitized account info safe to log (no tokens).
   *
   * @returns Sanitized account info object
   */
  public getAccountDebugInfo(): Record<string, unknown> | null {
    if (!this.currentAccount) {
      return null;
    }

    return {
      username: this.currentAccount.username,
      name: this.currentAccount.name,
      tenantId: this.currentAccount.tenantId,
      homeAccountId: this.currentAccount.homeAccountId,
      environment: this.currentAccount.environment,
    };
  }
}
