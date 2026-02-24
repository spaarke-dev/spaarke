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
 * Node type values matching NodeTypes in IVisualizationService.cs
 */
export type NodeType =
    | "source"   // The source document being queried (depth 0)
    | "related"  // A related document found via similarity or relationship
    | "orphan"   // An orphan file with no Dataverse record
    | "matter"   // A Matter entity acting as a hub node
    | "project"  // A Project entity acting as a hub node
    | "invoice"  // An Invoice entity acting as a hub node
    | "email";   // An Email document acting as a hub node (parent of attachments)

/**
 * Check if a node type represents a parent hub node.
 */
export const isParentHubNode = (type: NodeType): boolean =>
    type === "matter" || type === "project" || type === "invoice" || type === "email";

/**
 * A document node from the API response.
 */
export interface ApiDocumentNode {
    /** Entity GUID - document ID for document nodes, parent entity ID for hub nodes */
    id: string;
    /** Node type indicating the entity type */
    type: NodeType;
    /** Depth level in the graph (0 = source, 1 = hubs, 2 = related documents) */
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
    /** Document type (e.g., Contract, Invoice). For orphan files, derived from file type (e.g., "PDF Document") */
    documentType: string;
    /** File extension/type: pdf, docx, msg, xlsx, etc. Used for icon selection */
    fileType?: string;
    /** SharePoint Embedded file ID (always populated). Primary identifier for the actual file */
    speFileId?: string;
    /** True if this is an orphan file with no associated Dataverse record */
    isOrphanFile?: boolean;
    /** Similarity score to source document (0.0-1.0), null for source */
    similarity?: number;
    /** Keywords extracted by AI analysis */
    extractedKeywords: string[];
    /** Document creation timestamp */
    createdOn: string;
    /** Document modified timestamp */
    modifiedOn: string;
    /** Dataverse record URL for navigation. Empty for orphan files */
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
    /** Relationship type key: "same_email", "same_matter", "semantic", etc. */
    relationshipType: string;
    /** Human-readable label: "From same email", "Same matter", "Semantic", etc. */
    relationshipLabel: string;
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
    /** Filter by relationship types (e.g., "same_email", "same_matter", "semantic") */
    relationshipTypes?: string[];
}

/**
 * Available relationship types for filtering.
 * Matches RelationshipTypes in IVisualizationService.cs
 */
export const RELATIONSHIP_TYPES = {
    /** Documents from the same email (parent email + attachments) */
    same_email: "Attachment",
    /** Documents from the same email thread (ConversationIndex prefix match) */
    same_thread: "Email thread",
    /** Documents associated with the same Matter */
    same_matter: "Same matter",
    /** Documents associated with the same Project */
    same_project: "Same project",
    /** Documents associated with the same Invoice */
    same_invoice: "Same invoice",
    /** Documents related by vector similarity (content-based) */
    semantic: "Semantic",
} as const;

/**
 * Get display label for a relationship type.
 * Falls back to the API label if type is unknown.
 */
export function getRelationshipLabel(relationshipType: string, apiLabel?: string): string {
    const key = relationshipType as RelationshipTypeKey;
    return RELATIONSHIP_TYPES[key] ?? apiLabel ?? "Related";
}

/**
 * Relationship type key (e.g., "same_email", "semantic")
 */
export type RelationshipTypeKey = keyof typeof RELATIONSHIP_TYPES;
