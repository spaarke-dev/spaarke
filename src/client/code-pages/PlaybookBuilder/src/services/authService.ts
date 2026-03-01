/**
 * Authentication Service for PlaybookBuilder Code Page
 *
 * Multi-strategy token acquisition (copied from AnalysisWorkspace):
 *   1. In-memory cache
 *   2. Xrm platform strategies (5 methods)
 *   3. MSAL ssoSilent fallback
 *   4. Retry with exponential backoff
 *
 * Also provides Dataverse org URL resolution for DataverseClient.
 *
 * @see ADR-008 - Endpoint filters for auth
 */

import { PublicClientApplication } from "@azure/msal-browser";
import { msalConfig, BFF_API_SCOPE } from "../config/msalConfig";

const LOG_PREFIX = "[PlaybookBuilder:AuthService]";
const TOKEN_EXPIRY_BUFFER_MS = 5 * 60 * 1000;
const TOKEN_REFRESH_INTERVAL_MS = 4 * 60 * 1000;
const MAX_RETRY_ATTEMPTS = 3;
const RETRY_BASE_DELAY_MS = 1000;

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

/* eslint-disable @typescript-eslint/no-explicit-any */

function findXrm(): any | null {
    const frames: Window[] = [window];
    try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm;
            if (xrm?.Utility?.getGlobalContext) return xrm;
        } catch { /* cross-origin */ }
    }
    return null;
}

async function extractPlatformToken(): Promise<TokenCache | null> {
    // Strategy 1: Xrm.Utility.getGlobalContext().getAccessToken()
    try {
        const xrm = findXrm();
        if (xrm) {
            const context = xrm.Utility.getGlobalContext();
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

    // Strategy 2: __crmTokenProvider, AUTHENTICATION_TOKEN, Xrm.Page.context.getAuthToken
    try {
        const frames: Window[] = [window];
        try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
        try { if (window.top && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

        for (const frame of frames) {
            try {
                const provider = (frame as any).__crmTokenProvider;
                if (provider && typeof provider.getToken === "function") {
                    const tokenResult = await provider.getToken();
                    if (tokenResult && typeof tokenResult === "string") {
                        return { token: tokenResult, expiresAt: parseJwtExpiry(tokenResult) };
                    }
                }
                const globalToken = (frame as any).AUTHENTICATION_TOKEN;
                if (globalToken && typeof globalToken === "string") {
                    return { token: globalToken, expiresAt: parseJwtExpiry(globalToken) };
                }
                const xrmPage = (frame as any).Xrm?.Page?.context;
                if (xrmPage && typeof xrmPage.getAuthToken === "function") {
                    const tokenResult = await xrmPage.getAuthToken();
                    if (tokenResult && typeof tokenResult === "string") {
                        return { token: tokenResult, expiresAt: parseJwtExpiry(tokenResult) };
                    }
                }
            } catch { /* cross-origin */ }
        }
    } catch {
        console.debug(`${LOG_PREFIX} Legacy token providers not available`);
    }

    // Strategy 3: Bridge token (__SPAARKE_BFF_TOKEN__)
    try {
        const bridgeToken = (window as any).__SPAARKE_BFF_TOKEN__;
        if (bridgeToken) return { token: bridgeToken, expiresAt: parseJwtExpiry(bridgeToken) };
        const parentToken = (window.parent as any)?.__SPAARKE_BFF_TOKEN__;
        if (parentToken) return { token: parentToken, expiresAt: parseJwtExpiry(parentToken) };
    } catch { /* cross-origin */ }

    return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

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
        const accounts = msal.getAllAccounts();
        if (accounts.length > 0) {
            const result = await msal.acquireTokenSilent({ scopes, account: accounts[0] });
            if (result?.accessToken) {
                return {
                    token: result.accessToken,
                    expiresAt: result.expiresOn ? result.expiresOn.getTime() : parseJwtExpiry(result.accessToken),
                };
            }
        }
        const ssoResult = await msal.ssoSilent({ scopes });
        if (ssoResult?.accessToken) {
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

function parseJwtExpiry(token: string): number {
    try {
        const parts = token.split(".");
        if (parts.length !== 3) return Date.now() + 60 * 60 * 1000;
        const payload = JSON.parse(atob(parts[1]));
        if (typeof payload.exp === "number") return payload.exp * 1000;
    } catch { /* malformed JWT */ }
    return Date.now() + 60 * 60 * 1000;
}

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
            await sleep(delay);
        }
    }
    throw lastError;
}

async function acquireToken(): Promise<TokenCache> {
    const platformToken = await extractPlatformToken();
    if (platformToken) {
        console.info(`${LOG_PREFIX} Token acquired via Xrm platform strategy`);
        return platformToken;
    }
    const msalToken = await acquireTokenViaMsal();
    if (msalToken) {
        console.info(`${LOG_PREFIX} Token acquired via MSAL ssoSilent`);
        return msalToken;
    }
    throw new AuthError(
        "Could not acquire token. Xrm platform and MSAL both failed.",
        { isRetryable: false }
    );
}

let cachedToken: TokenCache | null = null;
let refreshIntervalId: ReturnType<typeof setInterval> | null = null;

export function isXrmAvailable(): boolean {
    return findXrm() !== null;
}

export function getClientUrl(): string | null {
    const xrm = findXrm();
    if (!xrm) return null;
    try {
        return xrm.Utility.getGlobalContext().getClientUrl() || null;
    } catch {
        return null;
    }
}

export async function getAccessToken(): Promise<string> {
    if (cachedToken) {
        if (Date.now() < cachedToken.expiresAt - TOKEN_EXPIRY_BUFFER_MS) {
            return cachedToken.token;
        }
        cachedToken = null;
    }
    const tokenCache = await retryWithBackoff(acquireToken, MAX_RETRY_ATTEMPTS, RETRY_BASE_DELAY_MS);
    cachedToken = tokenCache;
    return tokenCache.token;
}

export function clearTokenCache(): void {
    cachedToken = null;
}

export async function initializeAuth(): Promise<string> {
    console.info(`${LOG_PREFIX} Initializing authentication...`);
    const clientUrl = getClientUrl();
    if (clientUrl) {
        console.info(`${LOG_PREFIX} Dataverse org: ${clientUrl}`);
    } else {
        console.info(`${LOG_PREFIX} Xrm SDK not available, will use MSAL ssoSilent`);
    }
    const token = await getAccessToken();

    // Start proactive token refresh every 4 minutes
    if (!refreshIntervalId) {
        refreshIntervalId = setInterval(async () => {
            try {
                cachedToken = null;
                await getAccessToken();
                console.debug(`${LOG_PREFIX} Proactive token refresh completed`);
            } catch (err) {
                console.warn(`${LOG_PREFIX} Proactive token refresh failed`, err);
            }
        }, TOKEN_REFRESH_INTERVAL_MS);
    }

    return token;
}

export function stopTokenRefresh(): void {
    if (refreshIntervalId) {
        clearInterval(refreshIntervalId);
        refreshIntervalId = null;
    }
}
