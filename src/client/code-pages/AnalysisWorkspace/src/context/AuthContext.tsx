/**
 * AuthContext — Authentication lifecycle (initializeAuth) + UI state machine.
 *
 * Spaarke Auth v2 rewrite (project: spaarke-auth-v2-and-hardening, task 026):
 *
 *   - NO `token: string` in context value. Components requiring a token call
 *     `getAccessToken()` (escape hatch for SSE) or use `authenticatedFetch`
 *     (canonical path). Token strings never cross a component boundary.
 *
 *   - This context owns the BOOTSTRAP lifecycle (initializeAuth() promise:
 *     "authenticating" → "authenticated" / "error"). The provider singleton
 *     created by @spaarke/auth handles token caching + proactive refresh
 *     internally — we no longer poll on a setInterval.
 *
 *   - `retryAuth()` re-runs initializeAuth() after an error. There is no
 *     longer a `refreshToken()` method on the context — call sites that need
 *     a fresh token call `getAccessToken()` directly (which always returns a
 *     fresh value via the provider's cache + JWT exp validation).
 *
 * @see services/authInit.ts — initializeAuth() + library re-exports
 * @see .claude/AUDIT-FINDINGS-AUTH-SYSTEM.md — function-based auth contract
 */

import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from 'react';
import { initializeAuth, AuthError } from '../services/authInit';

// ---------------------------------------------------------------------------
// Auth State
// ---------------------------------------------------------------------------

export type AuthStatus = 'authenticating' | 'authenticated' | 'error';

export interface AuthContextValue {
  /** Current auth status — drives the UI state machine in App.tsx. */
  status: AuthStatus;
  /** Auth error details (available when status === "error"). */
  error: AuthError | Error | null;
  /** Whether the error is due to Xrm SDK being unavailable. */
  isXrmUnavailable: boolean;
  /** Whether the user is fully authenticated. */
  isAuthenticated: boolean;
  /** Whether authentication is in progress. */
  isAuthenticating: boolean;
  /** Retry authentication after an error. */
  retryAuth: () => void;
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
 * AuthProvider — runs initializeAuth() once on mount and tracks the bootstrap
 * state (authenticating / authenticated / error). Children gate on
 * `isAuthenticated` before calling `useAuth()` from @spaarke/auth, which
 * throws if `initAuth()` hasn't run.
 */
export function AuthProvider({ children }: AuthProviderProps): JSX.Element {
  const [status, setStatus] = useState<AuthStatus>('authenticating');
  const [error, setError] = useState<AuthError | Error | null>(null);
  const [isXrmUnavailable, setIsXrmUnavailable] = useState(false);

  const runInit = useCallback(async () => {
    setStatus('authenticating');
    setError(null);
    setIsXrmUnavailable(false);

    try {
      await initializeAuth();
      setStatus('authenticated');
    } catch (err) {
      const authErr: AuthError | Error =
        err instanceof AuthError
          ? err
          : err instanceof Error
            ? err
            : new Error(typeof err === 'string' ? err : 'Authentication failed');

      setError(authErr);

      // The library's AuthError uses string codes; "xrm_unavailable" indicates
      // we are running outside Dataverse (Xrm context could not be resolved).
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const code = (authErr as any).code;
      setIsXrmUnavailable(code === 'xrm_unavailable' || code === 'no_xrm_context');

      setStatus('error');
      console.error('[AnalysisWorkspace:Auth] Authentication failed:', authErr.message);
    }
  }, []);

  // Initialize on mount (once)
  useEffect(() => {
    runInit();
  }, [runInit]);

  const retryAuth = useCallback(() => {
    runInit();
  }, [runInit]);

  const value: AuthContextValue = {
    status,
    error,
    isXrmUnavailable,
    isAuthenticated: status === 'authenticated',
    isAuthenticating: status === 'authenticating',
    retryAuth,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useAuthContext — access the auth bootstrap state from any component.
 *
 * NOTE: For token / authenticatedFetch / getAccessToken, use the library's
 * `useAuth()` hook from `@spaarke/auth` (re-exported via `../services/authInit`).
 * This context is only for the bootstrap state machine.
 *
 * @throws Error if used outside of an AuthProvider
 */
export function useAuthContext(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error(
      'useAuthContext must be used within an AuthProvider. Wrap your component tree with <AuthProvider>.'
    );
  }
  return context;
}
