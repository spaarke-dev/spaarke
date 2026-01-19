/**
 * useVisualizationApi Hook
 *
 * React hook for fetching document relationship data from the BFF API.
 * Handles loading states, errors, and automatic refetching when parameters change.
 */

import * as React from "react";
import { VisualizationApiService, VisualizationApiError } from "../services/VisualizationApiService";
import type { DocumentNode, DocumentEdge } from "../types/graph";
import type { VisualizationQueryParams, GraphMetadata } from "../types/api";

/**
 * Configuration options for the visualization API hook.
 */
export interface UseVisualizationApiOptions {
    /** BFF API base URL */
    apiBaseUrl: string;
    /** Source document GUID */
    documentId: string;
    /** Tenant ID for multi-tenant routing */
    tenantId: string;
    /** Optional access token for authenticated requests */
    accessToken?: string;
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
    /** Whether API should be called (skip if false) */
    enabled?: boolean;
}

/**
 * State returned by the useVisualizationApi hook.
 */
export interface VisualizationApiState {
    /** Document nodes for the graph */
    nodes: DocumentNode[];
    /** Relationship edges for the graph */
    edges: DocumentEdge[];
    /** Metadata about the query results */
    metadata: GraphMetadata | null;
    /** Loading state */
    isLoading: boolean;
    /** Error state */
    error: VisualizationApiError | Error | null;
    /** Refetch function to manually reload data */
    refetch: () => Promise<void>;
}

/**
 * Hook for fetching and managing document relationship visualization data.
 *
 * @param options - Configuration options including API URL, document ID, and filters
 * @returns State object with nodes, edges, loading, error, and refetch
 *
 * @example
 * ```tsx
 * const { nodes, edges, isLoading, error } = useVisualizationApi({
 *   apiBaseUrl: "https://api.example.com",
 *   documentId: "abc-123",
 *   tenantId: "tenant-1",
 *   threshold: 0.65,
 * });
 * ```
 */
export function useVisualizationApi(options: UseVisualizationApiOptions): VisualizationApiState {
    const {
        apiBaseUrl,
        documentId,
        tenantId,
        accessToken,
        threshold = 0.65,
        limit = 25,
        depth = 1,
        documentTypes,
        relationshipTypes,
        enabled = true,
    } = options;

    const [nodes, setNodes] = React.useState<DocumentNode[]>([]);
    const [edges, setEdges] = React.useState<DocumentEdge[]>([]);
    const [metadata, setMetadata] = React.useState<GraphMetadata | null>(null);
    const [isLoading, setIsLoading] = React.useState(false);
    const [error, setError] = React.useState<VisualizationApiError | Error | null>(null);

    // Create service instance (memoized by apiBaseUrl)
    const service = React.useMemo(() => {
        return new VisualizationApiService(apiBaseUrl);
    }, [apiBaseUrl]);

    // Build query params
    const queryParams = React.useMemo((): VisualizationQueryParams => ({
        tenantId,
        threshold,
        limit,
        depth,
        documentTypes,
        relationshipTypes,
        includeKeywords: true,
        includeParentEntity: true,
    }), [tenantId, threshold, limit, depth, documentTypes, relationshipTypes]);

    // Fetch function
    const fetchData = React.useCallback(async () => {
        if (!enabled || !documentId || !tenantId || documentId.trim() === "") {
            // Clear data if not enabled or missing required params
            setNodes([]);
            setEdges([]);
            setMetadata(null);
            setError(null);
            return;
        }

        setIsLoading(true);
        setError(null);

        try {
            console.log("[VisualizationApi] Fetching related documents:", {
                documentId,
                tenantId,
                threshold,
                limit,
                depth,
            });

            const result = await service.getRelatedDocuments(documentId, queryParams, accessToken);

            console.log("[VisualizationApi] Received data:", {
                nodeCount: result.nodes.length,
                edgeCount: result.edges.length,
                searchLatencyMs: result.metadata.searchLatencyMs,
            });

            setNodes(result.nodes);
            setEdges(result.edges);
            setMetadata(result.metadata);
        } catch (err) {
            console.error("[VisualizationApi] Error fetching data:", err);

            if (err instanceof VisualizationApiError) {
                setError(err);

                // For 404, show empty graph instead of error
                if (err.isNotFound()) {
                    console.log("[VisualizationApi] Document not found or has no embedding, showing empty graph");
                    setNodes([]);
                    setEdges([]);
                    setMetadata(null);
                }
            } else if (err instanceof Error) {
                setError(err);
            } else {
                setError(new Error("Unknown error occurred"));
            }
        } finally {
            setIsLoading(false);
        }
    }, [enabled, documentId, tenantId, service, queryParams, accessToken, threshold, limit, depth]);

    // Fetch on mount and when dependencies change
    React.useEffect(() => {
        void fetchData();
    }, [fetchData]);

    return {
        nodes,
        edges,
        metadata,
        isLoading,
        error,
        refetch: fetchData,
    };
}

/**
 * Format error message for display to user.
 *
 * @param error - The error object
 * @returns User-friendly error message
 */
export function formatVisualizationError(error: VisualizationApiError | Error | null): string {
    if (!error) return "";

    if (error instanceof VisualizationApiError) {
        if (error.isNotFound()) {
            return "Document not found or has no relationship data yet. The document may still be processing.";
        }
        if (error.isUnauthorized()) {
            return "You don't have permission to view relationships for this document.";
        }
        return error.message || "Failed to load document relationships.";
    }

    return error.message || "An unexpected error occurred.";
}
