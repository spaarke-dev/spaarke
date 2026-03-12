/**
 * useRelatedDocumentCount Hook
 *
 * Fetches the count of semantically related documents for a given document ID
 * from the BFF visualization API using the countOnly=true parameter.
 *
 * Uses a simple fetch pattern with token acquisition from the PCF context.
 * React 16 compatible (useState + useEffect + useCallback only, per ADR-022).
 *
 * @see ADR-022 - React 16 APIs only in PCF controls
 */

import { useState, useEffect, useCallback, useRef } from "react";

/**
 * API response shape for countOnly=true calls.
 */
interface CountOnlyResponse {
    nodes: unknown[];
    edges: unknown[];
    metadata: {
        totalResults: number;
        [key: string]: unknown;
    };
}

/**
 * Return type for useRelatedDocumentCount hook.
 */
export interface UseRelatedDocumentCountResult {
    /** Number of related documents found. */
    count: number;
    /** Whether the count is currently being loaded. */
    isLoading: boolean;
    /** Error message, or null if no error. */
    error: string | null;
    /** Timestamp of the last successful fetch. */
    lastUpdated: Date | null;
    /** Manually trigger a re-fetch. */
    refetch: () => void;
}

/**
 * Acquire a bearer token using the Xrm.Utility.getGlobalContext() token provider.
 * Falls back to null if Xrm is not available (e.g., test harness).
 *
 * @param apiBaseUrl - The BFF API base URL (used to construct the resource scope)
 * @returns Bearer token string or null
 */
async function acquireToken(apiBaseUrl: string): Promise<string | null> {
    try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm;
        if (xrm?.Utility?.getGlobalContext) {
            const context = xrm.Utility.getGlobalContext();
            // Attempt to get token from the Xrm auth context
            if (context.getCurrentAppUrl && typeof context.getCurrentAppUrl === "function") {
                // In Dataverse, use the WebApi token which is already available
                const token = await xrm.Utility.getGlobalContext().authenticateToken?.();
                if (token) return token;
            }
        }
    } catch {
        // Xrm not available or auth failed
    }

    // TODO: Migrate to @spaarke/auth (authenticatedFetch) once the auth library
    // dist is built and available. See DocumentRelationshipViewer for the pattern.
    // For now, fetch without auth - the BFF API may accept Dataverse session cookies.
    return null;
}

/**
 * Hook to fetch the count of semantically related documents.
 *
 * @param documentId - Source document GUID
 * @param tenantId - Azure AD tenant ID for multi-tenant routing
 * @param apiBaseUrl - BFF API base URL
 * @returns State object with count, loading, error, lastUpdated, and refetch
 *
 * @example
 * ```tsx
 * const { count, isLoading, error, lastUpdated, refetch } = useRelatedDocumentCount(
 *   "abc-123",
 *   "tenant-1",
 *   "https://spe-api-dev-67e2xz.azurewebsites.net"
 * );
 * ```
 */
export function useRelatedDocumentCount(
    documentId: string,
    tenantId: string | undefined,
    apiBaseUrl: string | undefined
): UseRelatedDocumentCountResult {
    const [count, setCount] = useState(0);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

    // Track mounted state to avoid state updates after unmount
    const mountedRef = useRef(true);

    // Track current fetch to avoid race conditions on rapid documentId changes
    const fetchIdRef = useRef(0);

    useEffect(() => {
        mountedRef.current = true;
        return () => {
            mountedRef.current = false;
        };
    }, []);

    const fetchCount = useCallback(async () => {
        // Skip if missing required parameters
        if (!documentId || documentId.trim() === "" || !apiBaseUrl) {
            setCount(0);
            setError(null);
            setIsLoading(false);
            return;
        }

        const currentFetchId = ++fetchIdRef.current;
        setIsLoading(true);
        setError(null);

        try {
            // Build URL with countOnly=true for lightweight response
            const baseUrl = apiBaseUrl.replace(/\/$/, "");
            const url = new URL(`${baseUrl}/api/ai/visualization/related/${documentId}`);
            url.searchParams.set("countOnly", "true");
            if (tenantId) {
                url.searchParams.set("tenantId", tenantId);
            }

            // Acquire auth token (best-effort)
            const token = await acquireToken(apiBaseUrl);
            const headers: Record<string, string> = {
                "Content-Type": "application/json",
            };
            if (token) {
                headers["Authorization"] = `Bearer ${token}`;
            }

            const response = await fetch(url.toString(), {
                method: "GET",
                headers,
            });

            // Guard against stale responses (documentId changed while fetching)
            if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
                return;
            }

            if (!response.ok) {
                // Handle specific HTTP errors with user-friendly messages
                if (response.status === 404) {
                    setCount(0);
                    setLastUpdated(new Date());
                    return;
                }
                if (response.status === 401 || response.status === 403) {
                    setError("You don't have permission to view related documents.");
                    return;
                }
                setError("Failed to load related document count.");
                return;
            }

            const data = (await response.json()) as CountOnlyResponse;

            if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
                return;
            }

            setCount(data.metadata?.totalResults ?? 0);
            setLastUpdated(new Date());
        } catch (err) {
            if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
                return;
            }

            console.error("[useRelatedDocumentCount] Error fetching count:", err);

            if (err instanceof Error && err.message.includes("auth")) {
                setError("Authentication error. Please refresh the page.");
            } else {
                setError("Unable to load related documents. Please try again.");
            }
        } finally {
            if (mountedRef.current && currentFetchId === fetchIdRef.current) {
                setIsLoading(false);
            }
        }
    }, [documentId, tenantId, apiBaseUrl]);

    // Fetch on mount and when documentId changes (record navigation)
    useEffect(() => {
        void fetchCount();
    }, [fetchCount]);

    return {
        count,
        isLoading,
        error,
        lastUpdated,
        refetch: fetchCount,
    };
}
