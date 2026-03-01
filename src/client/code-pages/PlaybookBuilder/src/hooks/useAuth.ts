/**
 * useAuth -- React hook for auth state management
 *
 * Initializes AuthService on mount, provides token status and
 * Dataverse URL to the component tree.
 */

import { useState, useEffect } from "react";
import { initializeAuth, getClientUrl, stopTokenRefresh, AuthError } from "../services/authService";

export interface AuthState {
    isAuthenticated: boolean;
    isLoading: boolean;
    error: string | null;
    dataverseUrl: string | null;
}

export function useAuth(): AuthState {
    const [state, setState] = useState<AuthState>({
        isAuthenticated: false,
        isLoading: true,
        error: null,
        dataverseUrl: null,
    });

    useEffect(() => {
        let cancelled = false;

        (async () => {
            try {
                await initializeAuth();
                if (!cancelled) {
                    setState({
                        isAuthenticated: true,
                        isLoading: false,
                        error: null,
                        dataverseUrl: getClientUrl(),
                    });
                }
            } catch (err) {
                if (!cancelled) {
                    const message = err instanceof AuthError
                        ? err.message
                        : "Authentication failed";
                    setState({
                        isAuthenticated: false,
                        isLoading: false,
                        error: message,
                        dataverseUrl: null,
                    });
                }
            }
        })();

        return () => {
            cancelled = true;
            stopTokenRefresh();
        };
    }, []);

    return state;
}
