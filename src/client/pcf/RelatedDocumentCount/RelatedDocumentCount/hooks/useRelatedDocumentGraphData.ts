/**
 * useRelatedDocumentGraphData Hook
 *
 * Fetches the full graph data (nodes + edges) for the mini graph preview.
 * This is the "phase 2" fetch — runs after the fast countOnly fetch completes
 * and only when count > 0.
 *
 * Transforms the full API response into slim MiniGraphNode/MiniGraphEdge types,
 * limiting to MAX_PREVIEW_NODES for readability in the compact preview.
 *
 * React 16 compatible (useState + useEffect + useCallback + useRef only, per ADR-022).
 */

import { useState, useEffect, useCallback, useRef } from "react";
import { authenticatedFetch } from "@spaarke/auth";
import type { MiniGraphNode, MiniGraphEdge } from "@spaarke/ui-components/dist/types/MiniGraphTypes";

/** Maximum nodes to include in the mini preview (source + related). */
const MAX_PREVIEW_NODES = 15;

/** Default BFF API URL when not configured via PCF properties. */
const DEFAULT_API_BASE_URL = "https://spe-api-dev-67e2xz.azurewebsites.net";

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
    /** Nodes for the mini graph preview. */
    nodes: MiniGraphNode[];
    /** Edges for the mini graph preview. */
    edges: MiniGraphEdge[];
    /** Whether graph data is currently being loaded. */
    isLoading: boolean;
    /** Error message, or null if no error. */
    error: string | null;
}

// ─── Hook ────────────────────────────────────────────────────────────

/**
 * Fetch full graph data and transform to mini graph format.
 *
 * @param documentId - Source document GUID
 * @param tenantId - Azure AD tenant ID
 * @param apiBaseUrl - BFF API base URL
 * @param enabled - Only fetch when true (after count loaded and count > 0)
 */
export function useRelatedDocumentGraphData(
    documentId: string,
    tenantId: string | undefined,
    apiBaseUrl: string | undefined,
    enabled: boolean
): UseRelatedDocumentGraphDataResult {
    const [nodes, setNodes] = useState<MiniGraphNode[]>([]);
    const [edges, setEdges] = useState<MiniGraphEdge[]>([]);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const mountedRef = useRef(true);
    const fetchIdRef = useRef(0);

    useEffect(() => {
        mountedRef.current = true;
        return () => {
            mountedRef.current = false;
        };
    }, []);

    const fetchGraphData = useCallback(async () => {
        if (!enabled || !documentId || documentId.trim() === "") {
            return;
        }

        const currentFetchId = ++fetchIdRef.current;
        setIsLoading(true);
        setError(null);

        try {
            const baseUrl = (apiBaseUrl || DEFAULT_API_BASE_URL).replace(/\/$/, "");
            const url = `${baseUrl}/api/ai/visualization/related/${documentId}?${tenantId ? `tenantId=${encodeURIComponent(tenantId)}&` : ""}limit=20`;

            const response = await authenticatedFetch(url, {
                method: "GET",
                headers: { "Content-Type": "application/json" },
            });

            if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
                return;
            }

            if (!response.ok) {
                // Non-critical — the count card still works, just no preview
                console.warn("[useRelatedDocumentGraphData] API returned", response.status);
                setError(null); // Don't show error for preview — card still has count
                return;
            }

            const data = (await response.json()) as ApiGraphResponse;

            if (!mountedRef.current || currentFetchId !== fetchIdRef.current) {
                return;
            }

            // Transform and limit nodes
            const sourceNode = data.nodes.find((n) => n.type === "source");
            const others = data.nodes
                .filter((n) => n.type !== "source")
                .sort((a, b) => (b.data.similarity ?? 0) - (a.data.similarity ?? 0))
                .slice(0, MAX_PREVIEW_NODES - 1);

            const limitedNodes = sourceNode
                ? [sourceNode, ...others]
                : others.slice(0, MAX_PREVIEW_NODES);

            const nodeIds = new Set(limitedNodes.map((n) => n.id));

            // Filter edges to only include retained nodes
            const limitedEdges = data.edges.filter(
                (e) => nodeIds.has(e.source) && nodeIds.has(e.target)
            );

            // Map to MiniGraph types
            const miniNodes: MiniGraphNode[] = limitedNodes.map((n) => ({
                id: n.id,
                type: n.type,
                label: n.data.label,
                similarity: n.data.similarity,
            }));

            const miniEdges: MiniGraphEdge[] = limitedEdges.map((e) => ({
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
            // Non-critical — log but don't show error in UI
            console.warn("[useRelatedDocumentGraphData] Failed to fetch graph data:", err);
        } finally {
            if (mountedRef.current && currentFetchId === fetchIdRef.current) {
                setIsLoading(false);
            }
        }
    }, [documentId, tenantId, apiBaseUrl, enabled]);

    useEffect(() => {
        if (enabled) {
            void fetchGraphData();
        }
    }, [fetchGraphData, enabled]);

    return { nodes, edges, isLoading, error };
}
