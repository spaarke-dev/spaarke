/**
 * Visualization API Service
 *
 * Handles fetching document relationship data from the BFF API
 * and mapping it to the PCF graph types.
 */

import type {
    DocumentGraphResponse,
    ApiDocumentNode,
    ApiDocumentEdge,
    VisualizationQueryParams,
    GraphMetadata,
} from "../types/api";
import { getRelationshipLabel } from "../types/api";
import type { DocumentNode, DocumentEdge, DocumentNodeData, DocumentEdgeData } from "../types/graph";

/**
 * Service for interacting with the visualization API.
 */
export class VisualizationApiService {
    private apiBaseUrl: string;

    constructor(apiBaseUrl: string) {
        // Remove trailing slash if present
        this.apiBaseUrl = apiBaseUrl.replace(/\/$/, "");
    }

    /**
     * Fetch related documents for a given source document.
     *
     * @param documentId - Source document GUID
     * @param params - Query parameters including tenantId
     * @param accessToken - Optional Bearer token for authentication
     * @returns Graph data with nodes and edges for visualization
     */
    async getRelatedDocuments(
        documentId: string,
        params: VisualizationQueryParams,
        accessToken?: string
    ): Promise<{ nodes: DocumentNode[]; edges: DocumentEdge[]; metadata: GraphMetadata }> {
        const url = this.buildUrl(documentId, params);

        const headers: Record<string, string> = {
            "Content-Type": "application/json",
        };

        if (accessToken) {
            headers.Authorization = `Bearer ${accessToken}`;
        }

        const response = await fetch(url, {
            method: "GET",
            headers,
            credentials: "include", // Include cookies for same-origin requests
        });

        if (!response.ok) {
            const errorBody = await this.parseErrorResponse(response);
            throw new VisualizationApiError(
                errorBody.detail ?? `API error: ${response.status}`,
                response.status,
                errorBody
            );
        }

        const apiResponse = (await response.json()) as DocumentGraphResponse;
        return this.mapToGraphData(apiResponse);
    }

    /**
     * Build the API URL with query parameters.
     */
    private buildUrl(documentId: string, params: VisualizationQueryParams): string {
        const url = new URL(`${this.apiBaseUrl}/api/ai/visualization/related/${documentId}`);

        // Required parameter
        url.searchParams.set("tenantId", params.tenantId);

        // Optional parameters
        if (params.threshold !== undefined) {
            url.searchParams.set("threshold", params.threshold.toString());
        }
        if (params.limit !== undefined) {
            url.searchParams.set("limit", params.limit.toString());
        }
        if (params.depth !== undefined) {
            url.searchParams.set("depth", params.depth.toString());
        }
        if (params.includeKeywords !== undefined) {
            url.searchParams.set("includeKeywords", params.includeKeywords.toString());
        }
        if (params.includeParentEntity !== undefined) {
            url.searchParams.set("includeParentEntity", params.includeParentEntity.toString());
        }
        if (params.documentTypes && params.documentTypes.length > 0) {
            params.documentTypes.forEach((type) => {
                url.searchParams.append("documentTypes", type);
            });
        }
        if (params.relationshipTypes && params.relationshipTypes.length > 0) {
            params.relationshipTypes.forEach((type) => {
                url.searchParams.append("relationshipTypes", type);
            });
        }

        return url.toString();
    }

    /**
     * Parse error response from API.
     */
    private async parseErrorResponse(response: Response): Promise<{ title?: string; detail?: string; status?: number }> {
        try {
            return (await response.json()) as { title?: string; detail?: string; status?: number };
        } catch {
            return {
                title: "Unknown Error",
                detail: `HTTP ${response.status}: ${response.statusText}`,
                status: response.status,
            };
        }
    }

    /**
     * Map API response to PCF graph types.
     * Derives primary relationship type for each node from connected edges.
     */
    private mapToGraphData(apiResponse: DocumentGraphResponse): {
        nodes: DocumentNode[];
        edges: DocumentEdge[];
        metadata: GraphMetadata;
    } {
        // First map edges to get relationship data
        const edges = apiResponse.edges.map((apiEdge) => this.mapApiEdgeToDocumentEdge(apiEdge));

        // Build a map of node ID → primary relationship (from edges targeting this node)
        // Priority: same_email > same_thread > same_matter > same_project > same_invoice > semantic
        const nodeRelationships = this.buildNodeRelationshipMap(apiResponse.edges);

        // Map nodes with relationship data from edges
        const nodes = apiResponse.nodes.map((apiNode) =>
            this.mapApiNodeToDocumentNode(apiNode, nodeRelationships.get(apiNode.id))
        );

        return {
            nodes,
            edges,
            metadata: apiResponse.metadata,
        };
    }

    /**
     * Build a map of node ID to primary relationship data.
     * For each node, finds the highest-priority relationship from connected edges.
     * Assigns relationships to BOTH source and target of each edge since edge direction
     * varies by relationship type (semantic edges go from center outward, while
     * same_matter edges go from document to parent hub).
     */
    private buildNodeRelationshipMap(
        edges: ApiDocumentEdge[]
    ): Map<string, { type: string; label: string; similarity: number }> {
        const relationshipPriority: Record<string, number> = {
            same_email: 1,
            same_thread: 2,
            same_matter: 3,
            same_project: 4,
            same_invoice: 5,
            semantic: 6,
        };

        const nodeRelationships = new Map<string, { type: string; label: string; similarity: number }>();

        for (const edge of edges) {
            const relType = edge.data.relationshipType;
            const relLabel = getRelationshipLabel(relType, edge.data.relationshipLabel);
            const similarity = edge.data.similarity;

            // Assign relationship to BOTH nodes in the edge
            // The source document will be filtered out later (it has isSource=true and doesn't show relationship label)
            for (const nodeId of [edge.source, edge.target]) {
                const existing = nodeRelationships.get(nodeId);
                const existingPriority = existing ? (relationshipPriority[existing.type] ?? 99) : 99;
                const newPriority = relationshipPriority[relType] ?? 99;

                // Update if this relationship has higher priority (lower number)
                if (newPriority < existingPriority) {
                    nodeRelationships.set(nodeId, { type: relType, label: relLabel, similarity });
                }
            }
        }

        return nodeRelationships;
    }

    /**
     * Map an API document node to PCF DocumentNode type.
     * @param apiNode - The API node data
     * @param relationshipData - Optional relationship data derived from edges
     */
    private mapApiNodeToDocumentNode(
        apiNode: ApiDocumentNode,
        relationshipData?: { type: string; label: string; similarity: number }
    ): DocumentNode {
        // For orphan files, the node ID is the speFileId
        const isOrphanFile = apiNode.data.isOrphanFile ?? apiNode.type === "orphan";

        // Use fileType from API if available, otherwise extract from name
        // For parent hub nodes, use the document type directly (Matter, Project, etc.)
        const isParentHub = apiNode.type === "matter" || apiNode.type === "project" ||
                           apiNode.type === "invoice" || apiNode.type === "email";
        const fileType = isParentHub
            ? apiNode.data.documentType?.toLowerCase() ?? apiNode.type
            : apiNode.data.fileType ?? this.extractFileType(apiNode.data.label, apiNode.data.documentType);

        const data: DocumentNodeData = {
            // For orphan files, documentId is undefined; use speFileId instead
            // For parent hub nodes, use the node id (which is formatted as "matter-{guid}")
            documentId: isOrphanFile ? undefined : (isParentHub ? undefined : apiNode.id),
            speFileId: apiNode.data.speFileId,
            name: apiNode.data.label,
            fileType,
            documentType: apiNode.data.documentType,
            // Use similarity from relationship data if available (for direct relationships),
            // otherwise use the API node similarity (for semantic)
            similarity: relationshipData?.similarity ?? apiNode.data.similarity,
            isSource: apiNode.type === "source",
            isOrphanFile,
            nodeType: apiNode.type, // Pass the API node type for hub node styling
            // Relationship data derived from edges
            relationshipType: relationshipData?.type,
            relationshipLabel: relationshipData?.label,
            parentEntityName: apiNode.data.parentEntityName,
            fileUrl: apiNode.data.fileUrl,
            recordUrl: apiNode.data.recordUrl,
            sharedKeywords: apiNode.data.extractedKeywords,
            createdOn: apiNode.data.createdOn,
            modifiedOn: apiNode.data.modifiedOn,
        };

        return {
            id: apiNode.id,
            type: "document",
            position: apiNode.position ?? { x: 0, y: 0 },
            data,
        };
    }

    /**
     * Map an API edge to PCF DocumentEdge type.
     */
    private mapApiEdgeToDocumentEdge(apiEdge: ApiDocumentEdge): DocumentEdge {
        const data: DocumentEdgeData = {
            similarity: apiEdge.data.similarity,
            sharedKeywords: apiEdge.data.sharedKeywords,
            relationshipType: apiEdge.data.relationshipType,
            relationshipLabel: getRelationshipLabel(apiEdge.data.relationshipType, apiEdge.data.relationshipLabel),
        };

        return {
            id: apiEdge.id,
            source: apiEdge.source,
            target: apiEdge.target,
            data,
        };
    }

    /**
     * Extract file type (extension) from document name or type.
     *
     * @param documentName - The document display name (e.g., "Contract.pdf")
     * @param documentType - The document type from API (e.g., "Contract")
     * @returns File extension without dot (e.g., "pdf")
     */
    private extractFileType(documentName: string, documentType: string): string {
        // Try to extract extension from document name
        const extensionRegex = /\.([a-zA-Z0-9]+)$/;
        const extensionMatch = extensionRegex.exec(documentName);
        if (extensionMatch) {
            return extensionMatch[1].toLowerCase();
        }

        // Map common document types to extensions
        const typeMapping: Record<string, string> = {
            "Contract": "pdf",
            "Invoice": "pdf",
            "Agreement": "pdf",
            "Report": "pdf",
            "Spreadsheet": "xlsx",
            "Presentation": "pptx",
            "Document": "docx",
            "Email": "msg",
            "Image": "png",
        };

        return typeMapping[documentType] || "pdf";
    }
}

/**
 * Custom error class for API errors.
 */
export class VisualizationApiError extends Error {
    constructor(
        message: string,
        public readonly statusCode: number,
        public readonly errorBody?: { title?: string; detail?: string; status?: number }
    ) {
        super(message);
        this.name = "VisualizationApiError";
    }

    /**
     * Check if error is due to document not found.
     */
    isNotFound(): boolean {
        return this.statusCode === 404;
    }

    /**
     * Check if error is due to unauthorized access.
     */
    isUnauthorized(): boolean {
        return this.statusCode === 401 || this.statusCode === 403;
    }
}
