/**
 * Visualization API Service
 *
 * Handles fetching document relationship data from the BFF API.
 * Identical logic to PCF version — framework-agnostic service.
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

export class VisualizationApiService {
    private apiBaseUrl: string;

    constructor(apiBaseUrl: string) {
        this.apiBaseUrl = apiBaseUrl.replace(/\/$/, "");
    }

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
            credentials: "include",
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

    private buildUrl(documentId: string, params: VisualizationQueryParams): string {
        const url = new URL(`${this.apiBaseUrl}/api/ai/visualization/related/${documentId}`);
        url.searchParams.set("tenantId", params.tenantId);
        if (params.threshold !== undefined) url.searchParams.set("threshold", params.threshold.toString());
        if (params.limit !== undefined) url.searchParams.set("limit", params.limit.toString());
        if (params.depth !== undefined) url.searchParams.set("depth", params.depth.toString());
        if (params.includeKeywords !== undefined) url.searchParams.set("includeKeywords", params.includeKeywords.toString());
        if (params.includeParentEntity !== undefined) url.searchParams.set("includeParentEntity", params.includeParentEntity.toString());
        if (params.documentTypes && params.documentTypes.length > 0) {
            params.documentTypes.forEach((type) => url.searchParams.append("documentTypes", type));
        }
        if (params.relationshipTypes && params.relationshipTypes.length > 0) {
            params.relationshipTypes.forEach((type) => url.searchParams.append("relationshipTypes", type));
        }
        return url.toString();
    }

    private async parseErrorResponse(response: Response): Promise<{ title?: string; detail?: string; status?: number }> {
        try {
            return (await response.json()) as { title?: string; detail?: string; status?: number };
        } catch {
            return { title: "Unknown Error", detail: `HTTP ${response.status}: ${response.statusText}`, status: response.status };
        }
    }

    private mapToGraphData(apiResponse: DocumentGraphResponse): {
        nodes: DocumentNode[];
        edges: DocumentEdge[];
        metadata: GraphMetadata;
    } {
        const edges = apiResponse.edges.map((apiEdge) => this.mapApiEdgeToDocumentEdge(apiEdge));
        const nodeRelationships = this.buildNodeRelationshipMap(apiResponse.edges);
        const nodes = apiResponse.nodes.map((apiNode) =>
            this.mapApiNodeToDocumentNode(apiNode, nodeRelationships.get(apiNode.id))
        );
        return { nodes, edges, metadata: apiResponse.metadata };
    }

    private buildNodeRelationshipMap(
        edges: ApiDocumentEdge[]
    ): Map<string, Array<{ type: string; label: string; similarity: number }>> {
        const nodeRelationships = new Map<string, Array<{ type: string; label: string; similarity: number }>>();

        for (const edge of edges) {
            const relType = edge.data.relationshipType;
            const relLabel = getRelationshipLabel(relType, edge.data.relationshipLabel);
            const similarity = edge.data.similarity;
            const entry = { type: relType, label: relLabel, similarity };

            for (const nodeId of [edge.source, edge.target]) {
                const existing = nodeRelationships.get(nodeId);
                if (existing) {
                    if (!existing.some((r) => r.type === relType)) {
                        existing.push(entry);
                    }
                } else {
                    nodeRelationships.set(nodeId, [entry]);
                }
            }
        }

        return nodeRelationships;
    }

    private mapApiNodeToDocumentNode(
        apiNode: ApiDocumentNode,
        relationships?: Array<{ type: string; label: string; similarity: number }>
    ): DocumentNode {
        const isOrphanFile = apiNode.data.isOrphanFile ?? apiNode.type === "orphan";
        const isParentHub = apiNode.type === "matter" || apiNode.type === "project" ||
            apiNode.type === "invoice" || apiNode.type === "email";
        const fileType = isParentHub
            ? apiNode.data.documentType?.toLowerCase() ?? apiNode.type
            : apiNode.data.fileType ?? this.extractFileType(apiNode.data.label, apiNode.data.documentType);

        // Prefer semantic as the primary relationship for this viewer
        const primary = relationships?.find((r) => r.type === "semantic") ?? relationships?.[0];

        const data: DocumentNodeData = {
            documentId: isOrphanFile ? undefined : (isParentHub ? undefined : apiNode.id),
            speFileId: apiNode.data.speFileId,
            name: apiNode.data.label,
            fileType,
            documentType: apiNode.data.documentType,
            similarity: primary?.similarity ?? apiNode.data.similarity,
            isSource: apiNode.type === "source",
            isOrphanFile,
            nodeType: apiNode.type,
            relationshipType: primary?.type,
            relationshipLabel: primary?.label,
            relationshipTypes: relationships,
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

    private extractFileType(documentName: string, documentType: string): string {
        const extensionMatch = /\.([a-zA-Z0-9]+)$/.exec(documentName);
        if (extensionMatch) return extensionMatch[1].toLowerCase();

        const typeMapping: Record<string, string> = {
            Contract: "pdf", Invoice: "pdf", Agreement: "pdf", Report: "pdf",
            Spreadsheet: "xlsx", Presentation: "pptx", Document: "docx",
            Email: "msg", Image: "png",
        };
        return typeMapping[documentType] || "pdf";
    }
}

export class VisualizationApiError extends Error {
    constructor(
        message: string,
        public readonly statusCode: number,
        public readonly errorBody?: { title?: string; detail?: string; status?: number }
    ) {
        super(message);
        this.name = "VisualizationApiError";
    }

    isNotFound(): boolean { return this.statusCode === 404; }
    isUnauthorized(): boolean { return this.statusCode === 401 || this.statusCode === 403; }
}
