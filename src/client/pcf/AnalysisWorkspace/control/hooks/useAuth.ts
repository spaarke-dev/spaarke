/**
 * useAuth Hook
 *
 * Manages authentication state for the AnalysisWorkspace control.
 * Handles @spaarke/auth initialization check, access token acquisition,
 * and SprkChat token refresh lifecycle.
 *
 * Follows UseXxxOptions/UseXxxResult convention from shared library hooks.
 * React 16/17 compatible (ADR-022).
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { getAuthProvider } from '@spaarke/auth';
import { logInfo, logError } from '../utils/logger';

/**
 * Options for the useAuth hook
 */
export interface UseAuthOptions {
  /** Whether the parent (index.ts) signals auth is ready */
  isAuthReady: boolean;

  /** Whether to use legacy chat (skips SprkChat token refresh) */
  useLegacyChat: boolean;
}

/**
 * Result returned by the useAuth hook
 */
export interface UseAuthResult {
  /** Whether @spaarke/auth provider is initialized and available */
  isAuthInitialized: boolean;

  /** Access token for SprkChat component (refreshed every 45 min) */
  accessToken: string;

  /** SprkChat session ID (set when session is created) */
  sessionId: string | undefined;

  /** Callback to acquire an access token from @spaarke/auth */
  getAccessToken: () => Promise<string>;

  /** Callback to set the SprkChat session ID */
  setSessionId: (sessionId: string | undefined) => void;
}

/**
 * useAuth Hook
 *
 * Encapsulates authentication initialization, token acquisition,
 * and SprkChat token refresh interval.
 *
 * @example
 * ```tsx
 * const { isAuthInitialized, accessToken, sessionId, getAccessToken, setSessionId } = useAuth({
 *   isAuthReady: props.isAuthReady,
 *   useLegacyChat: props.useLegacyChat,
 * });
 * ```
 */
export const useAuth = (options: UseAuthOptions): UseAuthResult => {
  const { isAuthReady, useLegacyChat } = options;

  const [isAuthInitialized, setIsAuthInitialized] = React.useState(false);
  const [sprkChatAccessToken, setSprkChatAccessToken] = React.useState<string>('');
  const [sprkChatSessionId, setSprkChatSessionId] = React.useState<string | undefined>(undefined);

  // Check if @spaarke/auth is already initialized (by index.ts initializeAuth)
  React.useEffect(() => {
    try {
      getAuthProvider(); // Will throw if not initialized
      setIsAuthInitialized(true);
      logInfo('useAuth', '@spaarke/auth provider available');
    } catch {
      logError('useAuth', '@spaarke/auth not yet initialized, waiting...');
      // Auth may not be ready yet (async init in index.ts).
      // The parent will re-render with isAuthReady=true once initializeAuth completes.
      // Check props.isAuthReady on re-render.
    }
  }, [isAuthReady]);

  // Function to get access token for API calls (via @spaarke/auth)
  const getAccessToken = React.useCallback(async (): Promise<string> => {
    const provider = getAuthProvider();
    return provider.getAccessToken();
  }, []);

  // Acquire and refresh access token for SprkChat component (new chat system)
  React.useEffect(() => {
    if (!isAuthInitialized || useLegacyChat) return;
    let isMounted = true;
    const acquireToken = async () => {
      try {
        const token = await getAccessToken();
        if (isMounted) {
          setSprkChatAccessToken(token);
          logInfo('useAuth', 'SprkChat access token acquired');
        }
      } catch (err) {
        logError('useAuth', 'Failed to acquire SprkChat access token', err);
      }
    };
    acquireToken();
    // Refresh token every 45 minutes (tokens typically expire in 60min)
    const refreshInterval = setInterval(acquireToken, 45 * 60 * 1000);
    return () => {
      isMounted = false;
      clearInterval(refreshInterval);
    };
  }, [isAuthInitialized, useLegacyChat, getAccessToken]);

  return {
    isAuthInitialized,
    accessToken: sprkChatAccessToken,
    sessionId: sprkChatSessionId,
    getAccessToken,
    setSessionId: setSprkChatSessionId,
  };
};

export default useAuth;
