/**
 * useRelatedDocumentCount Hook
 *
 * Fetches the count of semantically related documents for a given document ID
 * from the BFF visualization API using the countOnly=true parameter.
 *
 * Uses @spaarke/auth authenticatedFetch for proper MSAL token management,
 * matching the pattern used by SemanticSearchControl and DocumentRelationshipViewer.
 *
 * React 16 compatible (useState + useEffect + useCallback only, per ADR-022).
 *
 * @see ADR-022 - React 16 APIs only in PCF controls
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { authenticatedFetch } from '@spaarke/auth';

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

/** Default BFF API URL when not configured via PCF properties */
const DEFAULT_API_BASE_URL = 'https://spe-api-dev-67e2xz.azurewebsites.net';

/**
 * Hook to fetch the count of semantically related documents.
 *
 * @param documentId - Source document GUID
 * @param tenantId - Azure AD tenant ID for multi-tenant routing
 * @param apiBaseUrl - BFF API base URL (defaults to dev endpoint)
 * @returns State object with count, loading, error, lastUpdated, and refetch
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
    // Skip if missing document ID
    if (!documentId || documentId.trim() === '') {
      console.warn('[useRelatedDocumentCount] No documentId available — skipping fetch.');
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
      const baseUrl = (apiBaseUrl || DEFAULT_API_BASE_URL).replace(/\/$/, '');
      const url = `${baseUrl}/api/ai/visualization/related/${documentId}?countOnly=true${tenantId ? `&tenantId=${encodeURIComponent(tenantId)}` : ''}`;

      console.log('[useRelatedDocumentCount] Fetching count:', {
        documentId,
        baseUrl,
        url,
      });

      // Use authenticatedFetch from @spaarke/auth — handles MSAL token acquisition
      const response = await authenticatedFetch(url, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      });

      // Guard against stale responses (documentId changed while fetching)
      if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
        return;
      }

      if (!response.ok) {
        if (response.status === 404) {
          setCount(0);
          setLastUpdated(new Date());
          return;
        }
        if (response.status === 401 || response.status === 403) {
          setError("You don't have permission to view related documents.");
          return;
        }
        setError('Failed to load related document count.');
        return;
      }

      const data = (await response.json()) as CountOnlyResponse;

      if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
        return;
      }

      const total = data.metadata?.totalResults ?? 0;
      console.log('[useRelatedDocumentCount] Got count:', total);
      setCount(total);
      setLastUpdated(new Date());
    } catch (err) {
      if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
        return;
      }

      console.error('[useRelatedDocumentCount] Error fetching count:', err);

      if (err instanceof Error && err.message.includes('auth')) {
        setError('Authentication error. Please refresh the page.');
      } else {
        setError('Unable to load related documents. Please try again.');
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
