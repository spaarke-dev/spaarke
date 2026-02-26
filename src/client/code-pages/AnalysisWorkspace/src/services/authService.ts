/**
 * Authentication Service for AnalysisWorkspace Code Page
 *
 * Acquires access tokens for the BFF API using the Xrm SDK global context.
 * When running inside a Dataverse-hosted iframe (dialog), the user is already
 * authenticated via the Dataverse session. This service leverages that session
 * to obtain a Bearer token scoped to the BFF API resource.
 *
 * Authentication flow:
 *   1. Locate Xrm SDK via frame-walk (window -> parent -> top)
 *   2. Use Xrm.Utility.getGlobalContext().getClientUrl() to get the org URL
 *   3. Acquire a token via the Dataverse token endpoint (same-origin fetch)
 *   4. Cache the token in memory with expiration tracking
 *   5. Auto-refresh before expiry (5-minute buffer)
 *
 * Constraints:
 *   - MUST use Xrm.Utility.getGlobalContext() -- NOT MSAL browser directly
 *   - MUST NOT transmit auth tokens via BroadcastChannel or postMessage
 *   - MUST NOT hard-code client IDs, tenant IDs, or secrets
 *   - Graceful degradation when Xrm SDK is unavailable (outside Dataverse)
 *
 * Pattern: Copied from SprkChatPane/src/services/authService.ts (task 013)
 *
 * @see ADR-008 - Endpoint filters for auth
 * @see .claude/constraints/api.md
 */

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

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Token Acquisition via Dataverse
// ---------------------------------------------------------------------------

/**
 * Acquire an access token for the BFF API from the Dataverse environment.
 *
 * Dataverse-hosted web resources share the user's authenticated session.
 * We fetch a token from the Dataverse internal token proxy endpoint, which
 * returns a token scoped to the BFF API resource without requiring client
 * IDs or secrets to be embedded in the frontend code.
 *
 * @param clientUrl - The Dataverse org URL (from Xrm.Utility.getGlobalContext().getClientUrl())
 * @returns The token cache entry with token and expiration
 * @throws AuthError on acquisition failure
 */
async function acquireTokenFromDataverse(clientUrl: string): Promise<TokenCache> {
    const baseUrl = clientUrl.replace(/\/+$/, "");

    try {
        // Verify Dataverse session is valid via lightweight WhoAmI call
        const response = await fetch(`${baseUrl}/api/data/v9.2/WhoAmI`, {
            method: "GET",
            headers: {
                "Accept": "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            credentials: "include",
        });

        if (!response.ok) {
            throw new AuthError(
                `Dataverse authentication failed (${response.status}): ${response.statusText}`,
                { isRetryable: response.status >= 500 || response.status === 429 }
            );
        }

        // Extract platform token using multi-strategy approach
        const token = await extractPlatformToken(baseUrl);

        if (token) {
            return token;
        }

        throw new AuthError(
            "Could not acquire BFF API token from Dataverse session. " +
            "The BFF API may need to be configured for Dataverse session-based auth.",
            { isRetryable: false }
        );
    } catch (err) {
        if (err instanceof AuthError) {
            throw err;
        }
        throw new AuthError(
            `Token acquisition failed: ${err instanceof Error ? err.message : "Unknown error"}`,
            { isRetryable: true, cause: err }
        );
    }
}

/**
 * Extract an access token from the PowerApps platform runtime.
 *
 * Uses a multi-strategy approach:
 *   1. Xrm.Utility.getGlobalContext().getAccessToken() (modern Dataverse 2024+)
 *   2. __crmTokenProvider.getToken() (legacy platform global)
 *   3. AUTHENTICATION_TOKEN global (some configurations)
 *   4. Xrm.Page.context.getAuthToken() (deprecated but functional)
 *   5. Dataverse Web API authorization header extraction
 *
 * @param clientUrl - The Dataverse org URL
 * @returns TokenCache if successful, null if platform token is unavailable
 */
async function extractPlatformToken(clientUrl: string): Promise<TokenCache | null> {
    /* eslint-disable @typescript-eslint/no-explicit-any */

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

    // Strategy 3: Dataverse Web API authorization header extraction
    try {
        const baseUrl = clientUrl.replace(/\/+$/, "");
        const response = await fetch(`${baseUrl}/api/data/v9.2/RetrieveCurrentOrganization`, {
            method: "GET",
            headers: {
                "Accept": "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            credentials: "include",
        });

        if (response.ok) {
            const authHeader = response.headers.get("Authorization");
            if (authHeader?.startsWith("Bearer ")) {
                const token = authHeader.substring(7);
                return { token, expiresAt: parseJwtExpiry(token) };
            }
        }
    } catch {
        console.debug(`${LOG_PREFIX} Dataverse token service unavailable`);
    }

    /* eslint-enable @typescript-eslint/no-explicit-any */

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
// Public API -- Singleton Auth Service
// ---------------------------------------------------------------------------

/** In-memory token cache. Never stored externally or transmitted via messaging. */
let cachedToken: TokenCache | null = null;

/** The resolved Xrm global context, cached after first discovery. */
let resolvedClientUrl: string | null = null;

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
 * Caches the result after first resolution.
 *
 * @returns The org URL (e.g., "https://orgname.crm.dynamics.com")
 * @throws AuthError if Xrm SDK is not available
 */
export function getClientUrl(): string {
    if (resolvedClientUrl) {
        return resolvedClientUrl;
    }

    const xrm = findXrm();
    if (!xrm) {
        throw new AuthError(
            "Xrm SDK is not available. This page must be opened from within Dataverse.",
            { isXrmUnavailable: true }
        );
    }

    try {
        const context = xrm.Utility.getGlobalContext();
        const clientUrl = context.getClientUrl();

        if (!clientUrl) {
            throw new AuthError(
                "Xrm.Utility.getGlobalContext().getClientUrl() returned empty.",
                { isXrmUnavailable: true }
            );
        }

        resolvedClientUrl = clientUrl;
        return clientUrl;
    } catch (err) {
        if (err instanceof AuthError) throw err;
        throw new AuthError(
            `Failed to get client URL from Xrm: ${err instanceof Error ? err.message : "Unknown error"}`,
            { isXrmUnavailable: true, cause: err }
        );
    }
}

/**
 * Acquire an access token for the BFF API.
 *
 * Uses Xrm.Utility.getGlobalContext() to authenticate via the Dataverse session.
 * Implements in-memory caching with automatic refresh before expiry.
 *
 * Token lifecycle:
 *   1. Check in-memory cache -- return if valid (with 5-min buffer)
 *   2. Use Xrm context to acquire fresh token from Dataverse platform
 *   3. Cache the new token in memory
 *   4. Retry with exponential backoff on transient failures
 *
 * @returns The Bearer access token string (without "Bearer " prefix)
 * @throws AuthError with isXrmUnavailable=true when outside Dataverse
 * @throws AuthError with isRetryable=true on transient network failures
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

    const clientUrl = getClientUrl();

    const tokenCache = await retryWithBackoff(
        () => acquireTokenFromDataverse(clientUrl),
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
    resolvedClientUrl = null;
    console.debug(`${LOG_PREFIX} Token cache cleared`);
}

/**
 * Initialize the auth service by verifying Xrm availability and acquiring
 * the first token. Call this during app startup.
 *
 * @returns The initial access token
 * @throws AuthError if Xrm is unavailable or initial token acquisition fails
 */
export async function initializeAuth(): Promise<string> {
    console.info(`${LOG_PREFIX} Initializing authentication...`);

    if (!isXrmAvailable()) {
        throw new AuthError(
            "This page must be opened from within Dataverse. " +
            "Xrm SDK is not available in the current context.",
            { isXrmUnavailable: true }
        );
    }

    const clientUrl = getClientUrl();
    console.info(`${LOG_PREFIX} Dataverse org: ${clientUrl}`);

    const token = await getAccessToken();
    console.info(`${LOG_PREFIX} Authentication initialized successfully`);

    return token;
}
