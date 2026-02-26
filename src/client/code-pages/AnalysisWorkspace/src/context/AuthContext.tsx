/**
 * AuthContext -- React Context for AnalysisWorkspace Authentication
 *
 * Provides the auth token and auth state throughout the AnalysisWorkspace
 * component tree. Components use `useAuthContext()` to access the current
 * token, auth status, and retry/refresh capabilities.
 *
 * Authentication lifecycle:
 *   1. AuthProvider mounts -> calls initializeAuth() from authService
 *   2. Token acquired via Xrm.Utility.getGlobalContext() (multi-strategy)
 *   3. Proactive token refresh runs on interval (every 4 minutes)
 *   4. Token available to all children via useAuthContext()
 *   5. 401 responses trigger token refresh via refreshToken()
 *
 * Constraints:
 *   - Auth tokens MUST NOT be transmitted via BroadcastChannel or postMessage
 *   - Each Code Page acquires its own token independently
 *   - Token is stored in React state only (in-memory), never in localStorage/sessionStorage
 *
 * @see ADR-008 - Endpoint filters for auth
 * @see services/authService.ts - Token acquisition and caching
 */

import {
    createContext,
    useContext,
    useEffect,
    useState,
    useCallback,
    type ReactNode,
} from "react";
import {
    getAccessToken,
    initializeAuth,
    clearTokenCache,
    AuthError,
} from "../services/authService";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Interval for proactive token refresh (4 minutes).
 * Tokens typically live 1 hour; refreshing every 4 minutes ensures
 * the token is always fresh when API calls are made.
 */
const TOKEN_REFRESH_INTERVAL_MS = 4 * 60 * 1000;

// ---------------------------------------------------------------------------
// Auth State
// ---------------------------------------------------------------------------

export type AuthStatus = "authenticating" | "authenticated" | "error";

export interface AuthContextValue {
    /** Current auth status */
    status: AuthStatus;
    /** The Bearer access token (available when status is "authenticated") */
    token: string | null;
    /** Auth error details (available when status is "error") */
    error: AuthError | Error | null;
    /** Whether the error is due to Xrm SDK being unavailable */
    isXrmUnavailable: boolean;
    /** Whether the user is fully authenticated */
    isAuthenticated: boolean;
    /** Whether authentication is in progress */
    isAuthenticating: boolean;
    /** Retry authentication after an error */
    retryAuth: () => void;
    /** Force refresh the token (e.g., after a 401 response) */
    refreshToken: () => Promise<string | null>;
}

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

const AuthContext = createContext<AuthContextValue | null>(null);

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

export interface AuthProviderProps {
    children: ReactNode;
}

/**
 * AuthProvider -- wraps children with authentication context.
 *
 * On mount:
 *   1. Calls initializeAuth() to acquire the first token
 *   2. Sets up a proactive refresh interval
 *   3. Provides token + status to all children via context
 */
export function AuthProvider({ children }: AuthProviderProps): JSX.Element {
    const [status, setStatus] = useState<AuthStatus>("authenticating");
    const [token, setToken] = useState<string | null>(null);
    const [error, setError] = useState<AuthError | Error | null>(null);
    const [isXrmUnavailable, setIsXrmUnavailable] = useState(false);

    /**
     * Initialize authentication -- acquire the first token.
     */
    const initAuth = useCallback(async () => {
        setStatus("authenticating");
        setError(null);
        setIsXrmUnavailable(false);

        try {
            const initialToken = await initializeAuth();
            setToken(initialToken);
            setStatus("authenticated");
        } catch (err) {
            const authErr = err instanceof AuthError ? err : new AuthError(
                err instanceof Error ? err.message : "Authentication failed",
                { isRetryable: true, cause: err }
            );
            setError(authErr);
            setIsXrmUnavailable(authErr instanceof AuthError && authErr.isXrmUnavailable);
            setStatus("error");
            // Log without exposing the token itself
            console.error("[AnalysisWorkspace:Auth] Authentication failed:", authErr.message);
        }
    }, []);

    // Initialize on mount
    useEffect(() => {
        initAuth();
    }, [initAuth]);

    /**
     * Proactive token refresh interval.
     * Silently refreshes the token before it expires. On failure, keeps
     * the existing token (it may still be valid until actual expiration).
     */
    useEffect(() => {
        if (status !== "authenticated") return;

        const intervalId = setInterval(async () => {
            try {
                const freshToken = await getAccessToken();
                setToken(freshToken);
            } catch (err) {
                console.warn("[AnalysisWorkspace:Auth] Token refresh failed, will retry:", err);
                // Don't set error state -- existing token may still work.
                // The next API call will surface the real error if needed.
            }
        }, TOKEN_REFRESH_INTERVAL_MS);

        return () => clearInterval(intervalId);
    }, [status]);

    /**
     * Retry auth after an error. Clears the cache and re-initializes.
     */
    const retryAuth = useCallback(() => {
        clearTokenCache();
        initAuth();
    }, [initAuth]);

    /**
     * Force-refresh the token (e.g., after receiving a 401 from the BFF API).
     * Returns the new token or null on failure.
     */
    const refreshToken = useCallback(async (): Promise<string | null> => {
        try {
            clearTokenCache();
            const freshToken = await getAccessToken();
            setToken(freshToken);
            setStatus("authenticated");
            return freshToken;
        } catch (err) {
            console.warn("[AnalysisWorkspace:Auth] Manual token refresh failed:", err);
            // On refresh failure mid-session, don't immediately switch to error state.
            // The user can continue with the existing token until it truly expires.
            // If it's a hard failure (Xrm unavailable), switch to error.
            if (err instanceof AuthError && err.isXrmUnavailable) {
                setError(err);
                setIsXrmUnavailable(true);
                setStatus("error");
            }
            return null;
        }
    }, []);

    const value: AuthContextValue = {
        status,
        token,
        error,
        isXrmUnavailable,
        isAuthenticated: status === "authenticated",
        isAuthenticating: status === "authenticating",
        retryAuth,
        refreshToken,
    };

    return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useAuthContext -- access the auth token and status from any component.
 *
 * @throws Error if used outside of an AuthProvider
 * @returns AuthContextValue with token, status, retryAuth, refreshToken
 */
export function useAuthContext(): AuthContextValue {
    const context = useContext(AuthContext);
    if (!context) {
        throw new Error(
            "useAuthContext must be used within an AuthProvider. " +
            "Wrap your component tree with <AuthProvider>."
        );
    }
    return context;
}
