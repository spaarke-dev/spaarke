/**
 * useAuth — combined auth hook for AnalysisWorkspace components.
 *
 * Spaarke Auth v2: merges the bootstrap state from `AuthContext` (status
 * machine: authenticating / authenticated / error) with the function-based
 * surface from `@spaarke/auth` (`authenticatedFetch`, `getAccessToken`,
 * `tenantId`, `logout`).
 *
 * Critically, NO `token: string` field is returned. Token strings never
 * cross a component boundary — see CLAUDE.md §D-AUTH-1, §D-AUTH-7.
 *
 *   - Use `authenticatedFetch(url, init)` for one-shot BFF API calls.
 *   - Use `await getAccessToken()` ONLY for SSE / WebSocket setup where
 *     authenticatedFetch can't wrap the lifecycle. Never snapshot the result.
 *
 * @example
 * ```tsx
 * const {
 *   isAuthenticated, isAuthenticating, authError, retryAuth,
 *   authenticatedFetch, getAccessToken,
 * } = useAuth();
 *
 * if (isAuthenticating) return <Spinner label="Authenticating..." />;
 * if (authError) return <ErrorState error={authError} onRetry={retryAuth} />;
 *
 * // Call site:
 * const r = await authenticatedFetch(`${bff}/api/v1/foo`);
 * ```
 */

import type { AuthenticatedFetchFn } from '@spaarke/auth';
import { useAuthContext } from '../context/AuthContext';
import { useAuth as sharedUseAuth } from '../services/authInit';

export interface UseAuthResult {
  /** Whether the user is fully authenticated (bootstrap completed + library has a token). */
  isAuthenticated: boolean;
  /** Whether authentication is in progress (initial or retry). */
  isAuthenticating: boolean;
  /** Auth error details, or null if no error. */
  authError: Error | null;
  /** Whether the error is because Xrm SDK is not available (outside Dataverse). */
  isXrmUnavailable: boolean;
  /** Retry authentication from scratch after an error. */
  retryAuth: () => void;

  // ── Function-based auth surface (v2 contract) ─────────────────────────────

  /** Authenticated fetch — attaches Bearer header, retries 401 with backoff. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Acquire a fresh BFF token. Use ONLY for SSE/WebSocket paths. */
  getAccessToken: () => Promise<string>;
  /** Azure AD tenant ID from the cached JWT `tid` claim (empty string until first token). */
  tenantId: string;
  /** Log out: clears MSAL refresh token + broadcasts to other tabs. */
  logout: () => Promise<void>;
}

/**
 * useAuth — bootstrap state + function-based auth surface.
 *
 * MUST be used inside `<AuthProvider>`. Reading the library surface throws if
 * `initAuth()` hasn't run, so we only read it once `isAuthenticated === true`.
 */
export function useAuth(): UseAuthResult {
  const ctx = useAuthContext();

  // Only consult the library after bootstrap completed — sharedUseAuth() throws
  // before initAuth() resolves. While bootstrap is in flight, return safe
  // no-op stand-ins so components rendering during the spinner phase don't
  // explode if they read these fields defensively.
  if (ctx.isAuthenticated) {
    const lib = sharedUseAuth();
    return {
      isAuthenticated: true,
      isAuthenticating: false,
      authError: null,
      isXrmUnavailable: false,
      retryAuth: ctx.retryAuth,
      authenticatedFetch: lib.authenticatedFetch,
      getAccessToken: lib.getAccessToken,
      tenantId: lib.tenantId,
      logout: lib.logout,
    };
  }

  // Pre-auth or error state — provide functions that reject so call sites
  // can't accidentally fire requests without auth.
  const rejectNotReady = (): Promise<never> =>
    Promise.reject(new Error('Authentication not initialized. Wait for isAuthenticated.'));

  return {
    isAuthenticated: false,
    isAuthenticating: ctx.isAuthenticating,
    authError: ctx.error,
    isXrmUnavailable: ctx.isXrmUnavailable,
    retryAuth: ctx.retryAuth,
    authenticatedFetch: rejectNotReady as unknown as AuthenticatedFetchFn,
    getAccessToken: rejectNotReady,
    tenantId: '',
    logout: rejectNotReady,
  };
}
