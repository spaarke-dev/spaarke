/**
 * useExternalContext — React hook for loading the external user's context.
 *
 * Calls GET /api/v1/external/me via the BFF API client and provides the user's
 * profile and accessible project list to the Workspace Home Page and other consumers.
 *
 * Authentication is handled entirely by the Power Pages portal (Entra External ID).
 * If the user is not authenticated, the portal redirects them to login before the
 * SPA loads — this hook never needs to show a login prompt.
 *
 * Token acquisition and 401-retry logic are handled transparently by
 * `bffApiCall` in bff-client.ts (via portal-auth.ts).
 *
 * See: docs/architecture/power-pages-spa-guide.md — Authentication section
 */

import { useState, useEffect, useCallback } from "react";
import { getExternalUserContext } from "../auth/bff-client";
import type { ExternalUserContextResponse } from "../auth/bff-client";
import { ApiError } from "../types";

// ---------------------------------------------------------------------------
// Public hook state interface
// ---------------------------------------------------------------------------

export interface UseExternalContextState {
  /** The authenticated external user's context (null until loaded). */
  context: ExternalUserContextResponse | null;
  /** True while the initial fetch (or a manual refresh) is in progress. */
  isLoading: boolean;
  /** Non-null when the fetch failed; null on success or while loading. */
  error: string | null;
  /**
   * Call this function to manually re-fetch the user context.
   * Useful after an access change (e.g., the user just accepted an invitation).
   */
  refresh: () => void;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

/**
 * Load the authenticated external user's context from GET /api/v1/external/me.
 *
 * Returns the user's Dataverse Contact ID, email, and the list of projects
 * they are granted access to with their current access level for each.
 *
 * Loading and error states are exposed so callers can render appropriate UI.
 *
 * @example
 * ```tsx
 * const { context, isLoading, error } = useExternalContext();
 * if (isLoading) return <Spinner />;
 * if (error) return <MessageBar intent="error">{error}</MessageBar>;
 * return <div>Hello {context?.email}</div>;
 * ```
 */
export function useExternalContext(): UseExternalContextState {
  const [context, setContext] = useState<ExternalUserContextResponse | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  // Incrementing this counter triggers a re-fetch without exposing internal state
  const [fetchTrigger, setFetchTrigger] = useState<number>(0);

  const refresh = useCallback(() => {
    setFetchTrigger((n) => n + 1);
  }, []);

  useEffect(() => {
    let cancelled = false;

    const fetchContext = async () => {
      setIsLoading(true);
      setError(null);

      try {
        const data = await getExternalUserContext();
        if (!cancelled) {
          setContext(data);
        }
      } catch (err) {
        if (!cancelled) {
          if (err instanceof ApiError) {
            setError(`Failed to load your workspace context (${err.statusCode}). Please try refreshing the page.`);
          } else if (err instanceof Error && err.message.includes("redirecting to login")) {
            // Portal session expired — portal-auth.ts is handling the redirect.
            // No need to display an error; the page will navigate away.
          } else {
            console.error("[ExternalContext] Unexpected error loading workspace context:", err);
            setError("An unexpected error occurred while loading your workspace. Please try refreshing the page.");
          }
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    void fetchContext();

    return () => {
      cancelled = true;
    };
  }, [fetchTrigger]);

  return { context, isLoading, error, refresh };
}
