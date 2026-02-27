/**
 * Authentication Service for AnalysisWorkspace Code Page
 *
 * Acquires access tokens for the BFF API using a multi-strategy approach
 * that supports both navigateTo dialogs and embedded web resource scenarios.
 *
 * Token acquisition strategies (in priority order):
 *   1. In-memory cache (fastest — no I/O)
 *   2. Xrm platform strategies (getAccessToken, __crmTokenProvider, etc.)
 *      — Works when opened via Xrm.Navigation.navigateTo
 *   3. MSAL ssoSilent (uses existing Azure AD session cookie)
 *      — Works when embedded as web resource on a form
 *   4. Retry with exponential backoff on transient failures
 *
 * Why dual strategies:
 *   - navigateTo mode: Xrm.Utility.getGlobalContext().getAccessToken() is
 *     available and fast. No MSAL needed.
 *   - Embedded on form: getAccessToken() is NOT available for web resource
 *     iframes. MSAL ssoSilent() acquires a token using the Azure AD session
 *     cookie (the user is already logged in to Dataverse).
 *
 * The MSAL fallback matches the LegalWorkspace pattern:
 *   src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts
 *
 * Constraints:
 *   - MUST NOT transmit auth tokens via BroadcastChannel or postMessage
 *   - MUST NOT hard-code secrets (CLIENT_ID is a public SPA client)
 *   - Graceful degradation when both Xrm and MSAL are unavailable
 *
 * @see ADR-008 - Endpoint filters for auth
 * @see docs/architecture/sdap-auth-patterns.md - Pattern 7: Code Page Embedded Auth
 */

import { PublicClientApplication } from "@azure/msal-browser";
import { msalConfig, BFF_API_SCOPE } from "../config/msalConfig";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const LOG_PREFIX = "[AnalysisWorkspace:AuthService]";

/**
 * Buffer time before token expiration to trigger proactive refresh (5 minutes).
 * Ensures tokens are refreshed before they expire, avoiding mid-request failures.
 */
const TOKEN_EXPIRY_BUFFER_MS = 5 * 60 * 1000;

/**
 * Maximum number of retry attempts for token acquisition on transient failures.
 */
const MAX_RETRY_ATTEMPTS = 3;

/**
 * Base delay for exponential backoff on retries (milliseconds).
 */
const RETRY_BASE_DELAY_MS = 1000;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Cached token entry stored in memory only (never in BroadcastChannel or storage).
 */
interface TokenCache {
    /** The Bearer access token string */
    token: string;
    /** Unix timestamp (ms) when the token expires */
    expiresAt: number;
}

/**
 * Authentication error with contextual information for user-friendly display.
 */
export class AuthError extends Error {
    /** Whether the error is due to Xrm SDK being unavailable (outside Dataverse) */
    public readonly isXrmUnavailable: boolean;
    /** Whether the error might be resolved by retrying */
    public readonly isRetryable: boolean;
    /** The underlying error that caused this auth error */
    public readonly originalCause: unknown;

    constructor(message: string, options?: { isXrmUnavailable?: boolean; isRetryable?: boolean; cause?: unknown }) {
        super(message);
        this.name = "AuthError";
        this.isXrmUnavailable = options?.isXrmUnavailable ?? false;
        this.isRetryable = options?.isRetryable ?? false;
        this.originalCause = options?.cause;
    }
}

// ---------------------------------------------------------------------------
// Xrm Frame-Walk
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Walk the frame hierarchy to locate the Xrm SDK.
 * Checks: window -> window.parent -> window.top
 *
 * @returns The Xrm namespace object, or null if not found.
 */
function findXrm(): XrmNamespace | null {
    const frames: Window[] = [window];
    try {
        if (window.parent && window.parent !== window) {
            frames.push(window.parent);
        }
    } catch {
        /* cross-origin -- cannot access parent */
    }
    try {
        if (window.top && window.top !== window && window.top !== window.parent) {
            frames.push(window.top);
        }
    } catch {
        /* cross-origin -- cannot access top */
    }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm as XrmNamespace | undefined;
            if (xrm?.Utility?.getGlobalContext) {
                return xrm;
            }
        } catch {
            /* cross-origin or property access error */
        }
    }

    return null;
}

// ---------------------------------------------------------------------------
// Xrm Platform Token Extraction
// ---------------------------------------------------------------------------

/**
 * Extract an access token from the PowerApps platform runtime.
 *
 * Uses a multi-strategy approach for Xrm-based token acquisition:
 *   1. Xrm.Utility.getGlobalContext().getAccessToken() (modern Dataverse 2024+)
 *   2. __crmTokenProvider.getToken() (legacy platform global)
 *   3. AUTHENTICATION_TOKEN global (some configurations)
 *   4. Xrm.Page.context.getAuthToken() (deprecated but functional)
 *
 * These strategies work when opened via Xrm.Navigation.navigateTo but
 * typically fail when embedded as a web resource iframe on a form.
 *
 * @returns TokenCache if successful, null if platform token is unavailable
 */
async function extractPlatformToken(): Promise<TokenCache | null> {
    // Strategy 1: Xrm.Utility.getGlobalContext().getAccessToken() (modern)
    try {
        const xrm = findXrm();
        if (xrm) {
            const context = xrm.Utility.getGlobalContext();
            const contextAny = context as any;

            if (typeof contextAny.getAccessToken === "function") {
                const tokenResult = await contextAny.getAccessToken();
                if (tokenResult && typeof tokenResult === "string") {
                    const expiry = parseJwtExpiry(tokenResult);
                    return { token: tokenResult, expiresAt: expiry };
                }
                if (tokenResult && typeof tokenResult.token === "string") {
                    const expiresAt = tokenResult.expiresOn
                        ? new Date(tokenResult.expiresOn).getTime()
                        : Date.now() + 60 * 60 * 1000;
                    return { token: tokenResult.token, expiresAt };
                }
            }
        }
    } catch {
        console.debug(`${LOG_PREFIX} getAccessToken() not available, trying alternative`);
    }

    // Strategy 2: CRM token from global scope (legacy but widely available)
    try {
        const frames: Window[] = [window];
        try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
        try { if (window.top && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

        for (const frame of frames) {
            try {
                // __crmTokenProvider (injected by platform)
                const provider = (frame as any).__crmTokenProvider;
                if (provider && typeof provider.getToken === "function") {
                    const tokenResult = await provider.getToken();
                    if (tokenResult && typeof tokenResult === "string") {
                        return { token: tokenResult, expiresAt: parseJwtExpiry(tokenResult) };
                    }
                }

                // AUTHENTICATION_TOKEN global
                const globalToken = (frame as any).AUTHENTICATION_TOKEN;
                if (globalToken && typeof globalToken === "string") {
                    return { token: globalToken, expiresAt: parseJwtExpiry(globalToken) };
                }

                // Xrm.Page.context.getAuthToken (deprecated but functional)
                const xrmPage = (frame as any).Xrm?.Page?.context;
                if (xrmPage && typeof xrmPage.getAuthToken === "function") {
                    const tokenResult = await xrmPage.getAuthToken();
                    if (tokenResult && typeof tokenResult === "string") {
                        return { token: tokenResult, expiresAt: parseJwtExpiry(tokenResult) };
                    }
                }
            } catch {
                /* cross-origin or unavailable */
            }
        }
    } catch {
        console.debug(`${LOG_PREFIX} Legacy token providers not available`);
    }

    // Strategy 3: Window-level token from PCF bridge (__SPAARKE_BFF_TOKEN__)
    try {
        const bridgeToken = (window as any).__SPAARKE_BFF_TOKEN__ as string | undefined;
        if (bridgeToken) {
            return { token: bridgeToken, expiresAt: parseJwtExpiry(bridgeToken) };
        }

        const parentToken = (window.parent as any)?.__SPAARKE_BFF_TOKEN__ as string | undefined;
        if (parentToken) {
            return { token: parentToken, expiresAt: parseJwtExpiry(parentToken) };
        }
    } catch {
        /* cross-origin — swallow */
    }

    return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// MSAL Token Acquisition (Embedded Fallback)
// ---------------------------------------------------------------------------

/**
 * MSAL PublicClientApplication singleton — lazily initialized on first use
 * to avoid blocking the initial render.
 */
let _msalInstance: PublicClientApplication | null = null;
let _msalInitPromise: Promise<void> | null = null;

/**
 * Lazily initialize the MSAL PublicClientApplication.
 * Safe to call multiple times — returns the same promise.
 */
async function ensureMsalInitialized(): Promise<PublicClientApplication | null> {
    if (_msalInstance) return _msalInstance;

    if (!_msalInitPromise) {
        _msalInitPromise = (async () => {
            try {
                const instance = new PublicClientApplication(msalConfig);
                await instance.initialize();
                await instance.handleRedirectPromise();
                _msalInstance = instance;
                console.info(`${LOG_PREFIX} MSAL initialized successfully`);
            } catch (err) {
                console.warn(`${LOG_PREFIX} MSAL initialization failed — Xrm strategies only`, err);
                _msalInstance = null;
            }
        })();
    }

    await _msalInitPromise;
    return _msalInstance;
}

/**
 * Acquire a token via MSAL ssoSilent (hidden iframe).
 *
 * Uses the existing Azure AD session cookie — the user is already
 * authenticated in Dataverse, so no interactive login is required.
 *
 * This is the same pattern used by:
 *   src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts
 *
 * @returns TokenCache if successful, null on failure
 */
async function acquireTokenViaMsal(): Promise<TokenCache | null> {
    const msal = await ensureMsalInitialized();
    if (!msal) return null;

    const scopes = [BFF_API_SCOPE];

    try {
        // Try acquireTokenSilent first (uses cached token / refresh token)
        const accounts = msal.getAllAccounts();
        if (accounts.length > 0) {
            const result = await msal.acquireTokenSilent({
                scopes,
                account: accounts[0],
            });
            if (result?.accessToken) {
                console.info(`${LOG_PREFIX} Token acquired via MSAL silent (cached account)`);
                return {
                    token: result.accessToken,
                    expiresAt: result.expiresOn ? result.expiresOn.getTime() : parseJwtExpiry(result.accessToken),
                };
            }
        }

        // Fall back to ssoSilent (uses existing Azure AD session cookie)
        const ssoResult = await msal.ssoSilent({ scopes });
        if (ssoResult?.accessToken) {
            console.info(`${LOG_PREFIX} Token acquired via MSAL ssoSilent`);
            return {
                token: ssoResult.accessToken,
                expiresAt: ssoResult.expiresOn ? ssoResult.expiresOn.getTime() : parseJwtExpiry(ssoResult.accessToken),
            };
        }
    } catch (err) {
        console.warn(`${LOG_PREFIX} MSAL token acquisition failed`, err);
    }

    return null;
}

// ---------------------------------------------------------------------------
// JWT Parsing
// ---------------------------------------------------------------------------

/**
 * Parse the expiration time from a JWT access token.
 * Returns the `exp` claim value as Unix milliseconds, or a default of
 * now + 1 hour if the token cannot be parsed.
 */
function parseJwtExpiry(token: string): number {
    try {
        const parts = token.split(".");
        if (parts.length !== 3) {
            return Date.now() + 60 * 60 * 1000;
        }
        const payload = JSON.parse(atob(parts[1]));
        if (typeof payload.exp === "number") {
            return payload.exp * 1000;
        }
    } catch {
        /* malformed JWT */
    }
    return Date.now() + 60 * 60 * 1000;
}

// ---------------------------------------------------------------------------
// Retry with Exponential Backoff
// ---------------------------------------------------------------------------

/**
 * Sleep for the specified duration (ms).
 */
function sleep(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Retry an async operation with exponential backoff.
 */
async function retryWithBackoff<T>(
    operation: () => Promise<T>,
    maxAttempts: number,
    baseDelay: number
): Promise<T> {
    let lastError: unknown;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        try {
            return await operation();
        } catch (err) {
            lastError = err;

            if (err instanceof AuthError && !err.isRetryable) {
                throw err;
            }

            if (attempt === maxAttempts - 1) {
                break;
            }

            const delay = baseDelay * Math.pow(2, attempt) + Math.random() * 500;
            console.debug(`${LOG_PREFIX} Retry attempt ${attempt + 1}/${maxAttempts} after ${Math.round(delay)}ms`);
            await sleep(delay);
        }
    }

    throw lastError;
}

// ---------------------------------------------------------------------------
// Combined Token Acquisition
// ---------------------------------------------------------------------------

/**
 * Acquire a BFF API token using all available strategies.
 *
 * Priority:
 *   1. Xrm platform strategies (getAccessToken, legacy providers, bridge)
 *   2. MSAL ssoSilent (for embedded web resource mode)
 *
 * @returns TokenCache with token and expiration
 * @throws AuthError if no strategy succeeds
 */
async function acquireToken(): Promise<TokenCache> {
    // Strategy group 1: Xrm platform token extraction
    const platformToken = await extractPlatformToken();
    if (platformToken) {
        console.info(`${LOG_PREFIX} Token acquired via Xrm platform strategy`);
        return platformToken;
    }

    console.debug(`${LOG_PREFIX} Xrm platform strategies exhausted, trying MSAL ssoSilent...`);

    // Strategy group 2: MSAL ssoSilent (embedded web resource fallback)
    const msalToken = await acquireTokenViaMsal();
    if (msalToken) {
        return msalToken;
    }

    throw new AuthError(
        "Could not acquire BFF API token. " +
        "Xrm platform token and MSAL ssoSilent both failed. " +
        "Ensure this page is running within Dataverse.",
        { isRetryable: false }
    );
}

// ---------------------------------------------------------------------------
// Public API -- Singleton Auth Service
// ---------------------------------------------------------------------------

/** In-memory token cache. Never stored externally or transmitted via messaging. */
let cachedToken: TokenCache | null = null;

/**
 * Check if the Xrm SDK is available (running inside Dataverse).
 *
 * @returns true if Xrm.Utility.getGlobalContext() is accessible
 */
export function isXrmAvailable(): boolean {
    return findXrm() !== null;
}

/**
 * Get the Dataverse org URL from Xrm.Utility.getGlobalContext().
 *
 * @returns The org URL (e.g., "https://orgname.crm.dynamics.com"), or null if Xrm unavailable
 */
export function getClientUrl(): string | null {
    const xrm = findXrm();
    if (!xrm) return null;

    try {
        const context = xrm.Utility.getGlobalContext();
        return context.getClientUrl() || null;
    } catch {
        return null;
    }
}

/**
 * Acquire an access token for the BFF API.
 *
 * Uses a combined strategy that works in both navigateTo and embedded modes:
 *   1. Check in-memory cache — return if valid (with 5-min buffer)
 *   2. Try Xrm platform strategies (getAccessToken, legacy providers)
 *   3. Fall back to MSAL ssoSilent (for embedded web resource mode)
 *   4. Retry with exponential backoff on transient failures
 *
 * @returns The Bearer access token string (without "Bearer " prefix)
 * @throws AuthError when no authentication strategy succeeds
 */
export async function getAccessToken(): Promise<string> {
    // Check cached token (with expiry buffer)
    if (cachedToken) {
        const now = Date.now();
        if (now < cachedToken.expiresAt - TOKEN_EXPIRY_BUFFER_MS) {
            return cachedToken.token;
        }
        console.debug(`${LOG_PREFIX} Token expired or near expiry, refreshing...`);
        cachedToken = null;
    }

    const tokenCache = await retryWithBackoff(
        () => acquireToken(),
        MAX_RETRY_ATTEMPTS,
        RETRY_BASE_DELAY_MS
    );

    cachedToken = tokenCache;
    console.info(`${LOG_PREFIX} Token acquired, expires at ${new Date(tokenCache.expiresAt).toISOString()}`);

    return tokenCache.token;
}

/**
 * Clear the cached token. Call this when the user session changes or on auth errors.
 */
export function clearTokenCache(): void {
    cachedToken = null;
    console.debug(`${LOG_PREFIX} Token cache cleared`);
}

/**
 * Initialize the auth service by verifying Dataverse context and acquiring
 * the first token. Call this during app startup.
 *
 * Supports both navigateTo (Xrm available) and embedded (MSAL fallback) modes.
 *
 * @returns The initial access token
 * @throws AuthError if no authentication strategy succeeds
 */
export async function initializeAuth(): Promise<string> {
    console.info(`${LOG_PREFIX} Initializing authentication...`);

    const clientUrl = getClientUrl();
    if (clientUrl) {
        console.info(`${LOG_PREFIX} Dataverse org: ${clientUrl}`);
    } else {
        console.info(`${LOG_PREFIX} Xrm SDK not available, will use MSAL ssoSilent`);
    }

    const token = await getAccessToken();
    console.info(`${LOG_PREFIX} Authentication initialized successfully`);

    return token;
}
