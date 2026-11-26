/**
 * useVisualizationApi — fetches document relationship graph data from the BFF, with loading,
 * error and refetch state. React 16-safe (useState / useMemo / useCallback / useEffect only).
 *
 * Shared by the DocumentRelationshipViewer code page and PCF (#3093).
 */

import { useState, useCallback, useEffect, useMemo } from 'react';
import { VisualizationApiService, VisualizationApiError } from './VisualizationApiService';
import type { DocumentGraphNode, DocumentGraphEdge, VisualizationQueryParams, GraphMetadata } from './types';

export interface UseVisualizationApiOptions {
  /** BFF API base URL (host only) */
  apiBaseUrl: string;
  /** Source document GUID */
  documentId: string;
  /** Configured tenant — only gates the fetch; it is NOT sent to the BFF (the BFF uses the token's `tid`). */
  tenantId: string;
  /** Minimum similarity threshold (default: 0.65) */
  threshold?: number;
  /** Maximum documents per level (default: 25) */
  limit?: number;
  /** Relationship depth (default: 1) */
  depth?: number;
  /** Document type filters */
  documentTypes?: string[];
  /** Relationship type filters (e.g., ["same_email", "semantic"]) */
  relationshipTypes?: string[];
  /** Whether the API should be called (skip if false) */
  enabled?: boolean;
}

export interface VisualizationApiState {
  nodes: DocumentGraphNode[];
  edges: DocumentGraphEdge[];
  metadata: GraphMetadata | null;
  isLoading: boolean;
  error: VisualizationApiError | Error | null;
  refetch: () => Promise<void>;
}

export function useVisualizationApi(options: UseVisualizationApiOptions): VisualizationApiState {
  const {
    apiBaseUrl,
    documentId,
    tenantId,
    threshold = 0.65,
    limit = 25,
    depth = 1,
    documentTypes,
    relationshipTypes,
    enabled = true,
  } = options;

  const [nodes, setNodes] = useState<DocumentGraphNode[]>([]);
  const [edges, setEdges] = useState<DocumentGraphEdge[]>([]);
  const [metadata, setMetadata] = useState<GraphMetadata | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<VisualizationApiError | Error | null>(null);

  const service = useMemo(() => new VisualizationApiService(apiBaseUrl), [apiBaseUrl]);

  const queryParams = useMemo(
    (): VisualizationQueryParams => ({
      threshold,
      limit,
      depth,
      includeKeywords: true,
      includeParentEntity: true,
      documentTypes,
      relationshipTypes,
    }),
    [threshold, limit, depth, documentTypes, relationshipTypes]
  );

  const fetchData = useCallback(async () => {
    if (!enabled || !documentId || !tenantId || documentId.trim() === '') {
      setNodes([]);
      setEdges([]);
      setMetadata(null);
      setError(null);
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const result = await service.getRelatedDocuments(documentId, queryParams);
      setNodes(result.nodes);
      setEdges(result.edges);
      setMetadata(result.metadata);
    } catch (err) {
      if (err instanceof VisualizationApiError) {
        setError(err);
        // 404: the document is not indexed yet — show an empty graph rather than stale data.
        if (err.isNotFound()) {
          setNodes([]);
          setEdges([]);
          setMetadata(null);
        }
      } else if (err instanceof Error) {
        setError(err);
      } else {
        setError(new Error('Unknown error occurred'));
      }
    } finally {
      setIsLoading(false);
    }
  }, [enabled, documentId, tenantId, service, queryParams]);

  useEffect(() => {
    void fetchData();
  }, [fetchData]);

  return { nodes, edges, metadata, isLoading, error, refetch: fetchData };
}

/** User-facing text for a visualization error. */
export function formatVisualizationError(error: VisualizationApiError | Error | null): string {
  if (!error) return '';
  if (error instanceof VisualizationApiError) {
    if (error.isNotFound()) {
      return 'Document not found or has no relationship data yet. The document may still be processing.';
    }
    if (error.isUnauthorized()) return "You don't have permission to view relationships for this document.";
    return error.message || 'Failed to load document relationships.';
  }
  return error.message || 'An unexpected error occurred.';
}
