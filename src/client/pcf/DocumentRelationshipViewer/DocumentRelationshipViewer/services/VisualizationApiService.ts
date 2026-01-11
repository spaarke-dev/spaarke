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
     */
    private mapToGraphData(apiResponse: DocumentGraphResponse): {
        nodes: DocumentNode[];
        edges: DocumentEdge[];
        metadata: GraphMetadata;
    } {
        const nodes = apiResponse.nodes.map((apiNode) => this.mapApiNodeToDocumentNode(apiNode));
        const edges = apiResponse.edges.map((apiEdge) => this.mapApiEdgeToDocumentEdge(apiEdge));

        return {
            nodes,
            edges,
            metadata: apiResponse.metadata,
        };
    }

    /**
     * Map an API document node to PCF DocumentNode type.
     */
    private mapApiNodeToDocumentNode(apiNode: ApiDocumentNode): DocumentNode {
        // For orphan files, the node ID is the speFileId
        const isOrphanFile = apiNode.data.isOrphanFile ?? apiNode.type === "orphan";

        // Use fileType from API if available, otherwise extract from name
        const fileType = apiNode.data.fileType ?? this.extractFileType(apiNode.data.label, apiNode.data.documentType);

        const data: DocumentNodeData = {
            // For orphan files, documentId is undefined; use speFileId instead
            documentId: isOrphanFile ? undefined : apiNode.id,
            speFileId: apiNode.data.speFileId,
            name: apiNode.data.label,
            fileType,
            similarity: apiNode.data.similarity,
            isSource: apiNode.type === "source",
            isOrphanFile,
            parentEntityName: apiNode.data.parentEntityName,
            fileUrl: apiNode.data.fileUrl,
            recordUrl: apiNode.data.recordUrl,
            sharedKeywords: apiNode.data.extractedKeywords,
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
