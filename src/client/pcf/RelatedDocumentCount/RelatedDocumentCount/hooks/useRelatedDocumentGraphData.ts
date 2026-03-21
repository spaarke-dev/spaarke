/**
 * useRelatedDocumentGraphData Hook
 *
 * Single API call that returns both the related document count (from metadata.totalResults)
 * and the mini graph preview data (nodes + edges). Replaces the previous two-phase
 * pattern (countOnly fetch → graph fetch) to eliminate one network round-trip.
 *
 * Transforms the API response into slim MiniGraphNode/MiniGraphEdge types,
 * limiting to MAX_PREVIEW_NODES for readability in the compact preview.
 *
 * React 16 compatible (useState + useEffect + useCallback + useRef only, per ADR-022).
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { authenticatedFetch } from '@spaarke/auth';
import type { MiniGraphNode, MiniGraphEdge } from '@spaarke/ui-components/dist/types/MiniGraphTypes';

/** Maximum nodes to include in the mini preview (source + related). */
const MAX_PREVIEW_NODES = 15;

// No hardcoded default — apiBaseUrl is resolved from Dataverse environment variables at runtime.

// ─── API Response Types (minimal, matching BFF response shape) ───────

interface ApiNodeData {
  label: string;
  similarity?: number;
  [key: string]: unknown;
}

interface ApiNode {
  id: string;
  type: string;
  depth: number;
  data: ApiNodeData;
}

interface ApiEdgeData {
  similarity: number;
  relationshipType: string;
  [key: string]: unknown;
}

interface ApiEdge {
  id: string;
  source: string;
  target: string;
  data: ApiEdgeData;
}

interface ApiGraphResponse {
  nodes: ApiNode[];
  edges: ApiEdge[];
  metadata: {
    totalResults: number;
    [key: string]: unknown;
  };
}

// ─── Return Type ─────────────────────────────────────────────────────

export interface UseRelatedDocumentGraphDataResult {
  /** Total count of related documents (from metadata.totalResults). */
  count: number;
  /** Nodes for the mini graph preview. */
  nodes: MiniGraphNode[];
  /** Edges for the mini graph preview. */
  edges: MiniGraphEdge[];
  /** Whether data is currently being loaded. */
  isLoading: boolean;
  /** Error message, or null if no error. */
  error: string | null;
  /** Timestamp of the last successful fetch. */
  lastUpdated: Date | null;
  /** Manually trigger a re-fetch. */
  refetch: () => void;
}

// ─── Hook ────────────────────────────────────────────────────────────

/**
 * Fetch related document data in a single API call.
 * Returns count (from metadata) and graph preview data (nodes + edges).
 *
 * @param documentId - Source document GUID
 * @param tenantId - Azure AD tenant ID
 * @param apiBaseUrl - BFF API base URL
 * @param enabled - Only fetch when true (typically after auth is ready)
 */
export function useRelatedDocumentGraphData(
  documentId: string,
  tenantId: string | undefined,
  apiBaseUrl: string | undefined,
  enabled: boolean
): UseRelatedDocumentGraphDataResult {
  const [count, setCount] = useState(0);
  const [nodes, setNodes] = useState<MiniGraphNode[]>([]);
  const [edges, setEdges] = useState<MiniGraphEdge[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  const mountedRef = useRef(true);
  const fetchIdRef = useRef(0);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const fetchData = useCallback(async () => {
    if (!enabled || !documentId || documentId.trim() === '') {
      setCount(0);
      setNodes([]);
      setEdges([]);
      setError(null);
      setIsLoading(false);
      return;
    }

    const currentFetchId = ++fetchIdRef.current;
    setIsLoading(true);
    setError(null);

    try {
      if (!apiBaseUrl) {
        setError('BFF API URL not configured. Check Dataverse environment variables.');
        setIsLoading(false);
        return;
      }
      const baseUrl = apiBaseUrl.replace(/\/$/, '');
      const url = `${baseUrl}/ai/visualization/related/${documentId}?${tenantId ? `tenantId=${encodeURIComponent(tenantId)}&` : ''}limit=20`;

      console.log('[useRelatedDocumentGraphData] Fetching count + graph:', {
        documentId,
        url,
      });

      const response = await authenticatedFetch(url, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      });

      if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
        return;
      }

      if (!response.ok) {
        if (response.status === 404) {
          setCount(0);
          setNodes([]);
          setEdges([]);
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

      const data = (await response.json()) as ApiGraphResponse;

      if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
        return;
      }

      // Extract count from metadata
      const total = data.metadata?.totalResults ?? 0;
      console.log('[useRelatedDocumentGraphData] Got count:', total, 'nodes:', data.nodes.length);
      setCount(total);
      setLastUpdated(new Date());

      // Transform and limit nodes for mini graph preview
      if (data.nodes.length === 0) {
        setNodes([]);
        setEdges([]);
        return;
      }

      const sourceNode = data.nodes.find(n => n.type === 'source');
      const others = data.nodes
        .filter(n => n.type !== 'source')
        .sort((a, b) => (b.data.similarity ?? 0) - (a.data.similarity ?? 0))
        .slice(0, MAX_PREVIEW_NODES - 1);

      const limitedNodes = sourceNode ? [sourceNode, ...others] : others.slice(0, MAX_PREVIEW_NODES);

      const nodeIds = new Set(limitedNodes.map(n => n.id));

      // Filter edges to only include retained nodes
      const limitedEdges = data.edges.filter(e => nodeIds.has(e.source) && nodeIds.has(e.target));

      // Map to MiniGraph types
      const miniNodes: MiniGraphNode[] = limitedNodes.map(n => ({
        id: n.id,
        type: n.type,
        label: n.data.label,
        similarity: n.data.similarity,
      }));

      const miniEdges: MiniGraphEdge[] = limitedEdges.map(e => ({
        source: e.source,
        target: e.target,
        relationshipType: e.data.relationshipType,
        similarity: e.data.similarity,
      }));

      setNodes(miniNodes);
      setEdges(miniEdges);
    } catch (err) {
      if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
        return;
      }

      console.error('[useRelatedDocumentGraphData] Error:', err);

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
  }, [documentId, tenantId, apiBaseUrl, enabled]);

  useEffect(() => {
    if (enabled) {
      void fetchData();
    }
  }, [fetchData, enabled]);

  return {
    count,
    nodes,
    edges,
    isLoading,
    error,
    lastUpdated,
    refetch: fetchData,
  };
}
