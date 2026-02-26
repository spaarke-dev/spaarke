/**
 * useAuth -- convenience hook for authentication state in AnalysisWorkspace
 *
 * Wraps useAuthContext() with a simplified return shape matching the task
 * specification. Components can use either useAuth() (simple) or
 * useAuthContext() (full API) depending on their needs.
 *
 * Return shape:
 *   - token: string | null -- the Bearer access token
 *   - isAuthenticated: boolean -- whether the user has a valid token
 *   - isAuthenticating: boolean -- whether token acquisition is in progress
 *   - authError: Error | null -- auth error details (if any)
 *   - refreshToken: () => Promise<string | null> -- force-refresh the token
 *   - retryAuth: () => void -- retry from scratch after an error
 *
 * @example
 * ```tsx
 * const { token, isAuthenticated, isAuthenticating, authError, retryAuth } = useAuth();
 *
 * if (isAuthenticating) return <Spinner label="Authenticating..." />;
 * if (authError) return <ErrorState error={authError} onRetry={retryAuth} />;
 * if (isAuthenticated && token) return <AuthenticatedContent token={token} />;
 * ```
 *
 * @see context/AuthContext.tsx -- the provider that manages auth state
 * @see services/authService.ts -- the token acquisition service
 */

import { useAuthContext } from "../context/AuthContext";

export interface UseAuthResult {
    /** The Bearer access token (null when not authenticated) */
    token: string | null;
    /** Whether the user is fully authenticated with a valid token */
    isAuthenticated: boolean;
    /** Whether authentication is in progress (initial or retry) */
    isAuthenticating: boolean;
    /** Auth error details, or null if no error */
    authError: Error | null;
    /** Whether the error is because Xrm SDK is not available (outside Dataverse) */
    isXrmUnavailable: boolean;
    /** Force-refresh the token (e.g., after a 401 from BFF API) */
    refreshToken: () => Promise<string | null>;
    /** Retry authentication from scratch after an error */
    retryAuth: () => void;
}

/**
 * useAuth -- simplified authentication hook for AnalysisWorkspace components.
 *
 * Must be used within an AuthProvider.
 *
 * @returns UseAuthResult with token, status booleans, error, and actions
 */
export function useAuth(): UseAuthResult {
    const ctx = useAuthContext();

    return {
        token: ctx.token,
        isAuthenticated: ctx.isAuthenticated,
        isAuthenticating: ctx.isAuthenticating,
        authError: ctx.error,
        isXrmUnavailable: ctx.isXrmUnavailable,
        refreshToken: ctx.refreshToken,
        retryAuth: ctx.retryAuth,
    };
}
