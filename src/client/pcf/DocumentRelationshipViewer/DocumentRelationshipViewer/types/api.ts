/**
 * API response types for the Visualization endpoints.
 *
 * These types mirror the C# models in IVisualizationService.cs
 * from the BFF API.
 */

/**
 * Response from GET /api/ai/visualization/related/{documentId}
 */
export interface DocumentGraphResponse {
    nodes: ApiDocumentNode[];
    edges: ApiDocumentEdge[];
    metadata: GraphMetadata;
}

/**
 * A document node from the API response.
 */
export interface ApiDocumentNode {
    /** Document GUID (sprk_document ID) */
    id: string;
    /** Node type: "source" for the queried document, "related" for similar documents */
    type: "source" | "related";
    /** Depth level in the graph (0 = source, 1+ = related) */
    depth: number;
    /** Document data for display */
    data: ApiDocumentNodeData;
    /** Optional pre-calculated position */
    position?: { x: number; y: number };
}

/**
 * Data associated with a document node from API.
 */
export interface ApiDocumentNodeData {
    /** Document display name */
    label: string;
    /** Document type (e.g., Contract, Invoice) */
    documentType: string;
    /** Similarity score to source document (0.0-1.0), null for source */
    similarity?: number;
    /** Keywords extracted by AI analysis */
    extractedKeywords: string[];
    /** Document creation timestamp */
    createdOn: string;
    /** Document modified timestamp */
    modifiedOn: string;
    /** Dataverse record URL for navigation */
    recordUrl: string;
    /** SharePoint file URL */
    fileUrl: string;
    /** Optional inline preview URL */
    filePreviewUrl?: string;
    /** Parent entity type (e.g., "sprk_matter") */
    parentEntityType?: string;
    /** Parent entity GUID */
    parentEntityId?: string;
    /** Parent entity display name */
    parentEntityName?: string;
}

/**
 * A relationship edge from the API response.
 */
export interface ApiDocumentEdge {
    /** Unique edge identifier */
    id: string;
    /** Source node ID */
    source: string;
    /** Target node ID */
    target: string;
    /** Edge relationship data */
    data: ApiDocumentEdgeData;
}

/**
 * Data associated with a relationship edge from API.
 */
export interface ApiDocumentEdgeData {
    /** Cosine similarity score (0.0-1.0) */
    similarity: number;
    /** Keywords shared between documents */
    sharedKeywords: string[];
    /** Relationship type: "semantic", "keyword", or "metadata" */
    relationshipType: string;
}

/**
 * Metadata about the graph query and results.
 */
export interface GraphMetadata {
    /** Source document ID that was queried */
    sourceDocumentId: string;
    /** Tenant ID for the query */
    tenantId: string;
    /** Total related documents found */
    totalResults: number;
    /** Similarity threshold used */
    threshold: number;
    /** Requested depth */
    depth: number;
    /** Actual max depth reached */
    maxDepthReached: number;
    /** Count of nodes at each depth level */
    nodesPerLevel: number[];
    /** Search latency in milliseconds */
    searchLatencyMs: number;
    /** Whether source embedding was cached */
    cacheHit: boolean;
}

/**
 * Query parameters for the visualization API.
 */
export interface VisualizationQueryParams {
    /** Tenant identifier (required) */
    tenantId: string;
    /** Minimum similarity threshold (0.0-1.0), default 0.65 */
    threshold?: number;
    /** Maximum documents per level (1-50), default 25 */
    limit?: number;
    /** Relationship depth (1-3), default 1 */
    depth?: number;
    /** Include shared keywords in edge data */
    includeKeywords?: boolean;
    /** Filter by document types */
    documentTypes?: string[];
    /** Include parent entity information */
    includeParentEntity?: boolean;
}
