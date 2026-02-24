/**
 * useVisualizationApi Hook — Code Page version
 * Identical logic to PCF version; framework-agnostic data fetching.
 */

import { useState, useCallback, useEffect, useMemo } from "react";
import { VisualizationApiService, VisualizationApiError } from "../services/VisualizationApiService";
import type { DocumentNode, DocumentEdge } from "../types/graph";
import type { VisualizationQueryParams, GraphMetadata } from "../types/api";

export interface UseVisualizationApiOptions {
    apiBaseUrl: string;
    documentId: string;
    tenantId: string;
    accessToken?: string;
    threshold?: number;
    limit?: number;
    depth?: number;
    documentTypes?: string[];
    relationshipTypes?: string[];
    enabled?: boolean;
}

export interface VisualizationApiState {
    nodes: DocumentNode[];
    edges: DocumentEdge[];
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
        accessToken,
        threshold = 0.65,
        limit = 25,
        depth = 1,
        documentTypes,
        relationshipTypes,
        enabled = true,
    } = options;

    const [nodes, setNodes] = useState<DocumentNode[]>([]);
    const [edges, setEdges] = useState<DocumentEdge[]>([]);
    const [metadata, setMetadata] = useState<GraphMetadata | null>(null);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<VisualizationApiError | Error | null>(null);

    const service = useMemo(() => new VisualizationApiService(apiBaseUrl), [apiBaseUrl]);

    const queryParams = useMemo((): VisualizationQueryParams => ({
        tenantId,
        threshold,
        limit,
        depth,
        documentTypes,
        relationshipTypes,
        includeKeywords: true,
        includeParentEntity: true,
    }), [tenantId, threshold, limit, depth, documentTypes, relationshipTypes]);

    const fetchData = useCallback(async () => {
        if (!enabled || !documentId || !tenantId || documentId.trim() === "") {
            setNodes([]);
            setEdges([]);
            setMetadata(null);
            setError(null);
            return;
        }

        setIsLoading(true);
        setError(null);

        try {
            const result = await service.getRelatedDocuments(documentId, queryParams, accessToken);
            setNodes(result.nodes);
            setEdges(result.edges);
            setMetadata(result.metadata);
        } catch (err) {
            if (err instanceof VisualizationApiError) {
                setError(err);
                if (err.isNotFound()) {
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

    useEffect(() => {
        void fetchData();
    }, [fetchData]);

    return { nodes, edges, metadata, isLoading, error, refetch: fetchData };
}

export function formatVisualizationError(error: VisualizationApiError | Error | null): string {
    if (!error) return "";
    if (error instanceof VisualizationApiError) {
        if (error.isNotFound()) return "Document not found or has no relationship data yet.";
        if (error.isUnauthorized()) return "You don't have permission to view relationships for this document.";
        return error.message || "Failed to load document relationships.";
    }
    return error.message || "An unexpected error occurred.";
}
