/**
 * Authentication Service for SprkChatPane Code Page
 *
 * Acquires access tokens for the BFF API using a multi-strategy approach:
 *   1. In-memory cache (fast path)
 *   2. Xrm platform token providers (if available)
 *   3. MSAL ssoSilent (primary fallback — uses existing Azure AD session)
 *
 * Pattern matches: AnalysisWorkspace/src/services/authService.ts
 *
 * @see ADR-008 - Endpoint filters for auth
 * @see docs/architecture/sdap-auth-patterns.md
 */

import { PublicClientApplication } from "@azure/msal-browser";
import { msalConfig, BFF_API_SCOPE } from "../config/msalConfig";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const LOG_PREFIX = "[SprkChatPane:AuthService]";
const TOKEN_EXPIRY_BUFFER_MS = 5 * 60 * 1000;
const MAX_RETRY_ATTEMPTS = 3;
const RETRY_BASE_DELAY_MS = 1000;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface TokenCache {
    token: string;
    expiresAt: number;
}

export class AuthError extends Error {
    public readonly isXrmUnavailable: boolean;
    public readonly isRetryable: boolean;
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
// Xrm type for frame-walk
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

interface XrmNamespace {
    Utility: {
        getGlobalContext: () => {
            getClientUrl: () => string;
            [key: string]: any;
        };
    };
    Page?: {
        data?: { entity?: any };
        context?: any;
    };
    App?: any;
}

// ---------------------------------------------------------------------------
// Xrm Frame-Walk
// ---------------------------------------------------------------------------

function findXrm(): XrmNamespace | null {
    const frames: Window[] = [window];
    try {
        if (window.parent && window.parent !== window) frames.push(window.parent);
    } catch { /* cross-origin */ }
    try {
        if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!);
    } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm as XrmNamespace | undefined;
            if (xrm?.Utility?.getGlobalContext) return xrm;
        } catch { /* cross-origin */ }
    }

    return null;
}

// ---------------------------------------------------------------------------
// Strategy 1: Xrm Platform Token Providers
// ---------------------------------------------------------------------------

async function acquireTokenFromXrmPlatform(): Promise<TokenCache | null> {
    // Strategy A: Xrm.Utility.getGlobalContext().getAccessToken() (modern Dataverse 2024+)
    try {
        const xrm = findXrm();
        if (xrm) {
            const context = xrm.Utility.getGlobalContext() as any;
            if (typeof context.getAccessToken === "function") {
                const tokenResult = await context.getAccessToken();
                if (tokenResult && typeof tokenResult === "string") {
                    return { token: tokenResult, expiresAt: parseJwtExpiry(tokenResult) };
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
        console.debug(`${LOG_PREFIX} getAccessToken() not available`);
    }

    // Strategy B: Bridge token from PCF host
    try {
        const bridgeToken = (window as any).__SPAARKE_BFF_TOKEN__ as string | undefined;
        if (bridgeToken) {
            return { token: bridgeToken, expiresAt: parseJwtExpiry(bridgeToken) };
        }
        const parentToken = (window.parent as any)?.__SPAARKE_BFF_TOKEN__ as string | undefined;
        if (parentToken) {
            return { token: parentToken, expiresAt: parseJwtExpiry(parentToken) };
        }
    } catch { /* cross-origin */ }

    return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Strategy 2: MSAL ssoSilent (Primary Fallback)
// ---------------------------------------------------------------------------

let _msalInstance: PublicClientApplication | null = null;
let _msalInitPromise: Promise<void> | null = null;

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
                console.warn(`${LOG_PREFIX} MSAL initialization failed`, err);
                _msalInstance = null;
            }
        })();
    }

    await _msalInitPromise;
    return _msalInstance;
}

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

function parseJwtExpiry(token: string): number {
    try {
        const parts = token.split(".");
        if (parts.length !== 3) return Date.now() + 60 * 60 * 1000;
        const payload = JSON.parse(atob(parts[1]));
        if (typeof payload.exp === "number") return payload.exp * 1000;
    } catch { /* malformed JWT */ }
    return Date.now() + 60 * 60 * 1000;
}

// ---------------------------------------------------------------------------
// Retry with Exponential Backoff
// ---------------------------------------------------------------------------

function sleep(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

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
            if (err instanceof AuthError && !err.isRetryable) throw err;
            if (attempt === maxAttempts - 1) break;
            const delay = baseDelay * Math.pow(2, attempt) + Math.random() * 500;
            console.debug(`${LOG_PREFIX} Retry ${attempt + 1}/${maxAttempts} after ${Math.round(delay)}ms`);
            await sleep(delay);
        }
    }
    throw lastError;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

let cachedToken: TokenCache | null = null;
let resolvedClientUrl: string | null = null;

export function isXrmAvailable(): boolean {
    return findXrm() !== null;
}

export function getClientUrl(): string {
    if (resolvedClientUrl) return resolvedClientUrl;

    const xrm = findXrm();
    if (!xrm) {
        throw new AuthError(
            "Xrm SDK is not available. This page must be opened from within Dataverse.",
            { isXrmUnavailable: true }
        );
    }

    try {
        const clientUrl = xrm.Utility.getGlobalContext().getClientUrl();
        if (!clientUrl) {
            throw new AuthError("getClientUrl() returned empty.", { isXrmUnavailable: true });
        }
        resolvedClientUrl = clientUrl;
        return clientUrl;
    } catch (err) {
        if (err instanceof AuthError) throw err;
        throw new AuthError(
            `Failed to get client URL: ${err instanceof Error ? err.message : "Unknown error"}`,
            { isXrmUnavailable: true, cause: err }
        );
    }
}

/**
 * Acquire an access token for the BFF API.
 *
 * Strategy order:
 *   1. In-memory cache (fast path)
 *   2. Xrm platform token providers (getAccessToken, bridge token)
 *   3. MSAL ssoSilent (uses existing Azure AD session — primary fallback)
 */
export async function getAccessToken(): Promise<string> {
    // 1. Check cached token
    if (cachedToken && Date.now() < cachedToken.expiresAt - TOKEN_EXPIRY_BUFFER_MS) {
        return cachedToken.token;
    }
    cachedToken = null;

    // 2. Try Xrm platform providers
    const platformToken = await acquireTokenFromXrmPlatform();
    if (platformToken) {
        cachedToken = platformToken;
        console.info(`${LOG_PREFIX} Token acquired via Xrm platform, expires ${new Date(platformToken.expiresAt).toISOString()}`);
        return platformToken.token;
    }

    // 3. MSAL ssoSilent (primary fallback)
    const msalToken = await acquireTokenViaMsal();
    if (msalToken) {
        cachedToken = msalToken;
        console.info(`${LOG_PREFIX} Token acquired via MSAL, expires ${new Date(msalToken.expiresAt).toISOString()}`);
        return msalToken.token;
    }

    throw new AuthError(
        "Could not acquire BFF API token. Neither Xrm platform providers nor MSAL ssoSilent succeeded.",
        { isRetryable: true }
    );
}

export function clearTokenCache(): void {
    cachedToken = null;
    resolvedClientUrl = null;
    console.debug(`${LOG_PREFIX} Token cache cleared`);
}

/**
 * Initialize auth: verify Xrm is available, then acquire initial token.
 */
export async function initializeAuth(): Promise<string> {
    console.info(`${LOG_PREFIX} Initializing authentication...`);

    if (!isXrmAvailable()) {
        throw new AuthError(
            "This page must be opened from within Dataverse.",
            { isXrmUnavailable: true }
        );
    }

    const clientUrl = getClientUrl();
    console.info(`${LOG_PREFIX} Dataverse org: ${clientUrl}`);

    const token = await retryWithBackoff(
        () => getAccessToken(),
        MAX_RETRY_ATTEMPTS,
        RETRY_BASE_DELAY_MS
    );

    console.info(`${LOG_PREFIX} Authentication initialized successfully`);
    return token;
}
