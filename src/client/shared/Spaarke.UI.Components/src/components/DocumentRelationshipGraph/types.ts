/**
 * Document relationship graph types — shared by the DocumentRelationshipViewer
 * code page (React 19, `@xyflow/react` v12) and PCF (React 16, `react-flow-renderer` v10).
 *
 * The API types mirror the C# models in `IVisualizationService.cs` (BFF). The graph
 * types are deliberately independent of either flow library: `DocumentGraphNode` and
 * `DocumentGraphEdge` carry only the fields both libraries' `Node` / `Edge` types need,
 * so each host passes them straight to its own canvas (#3093).
 */

// ─── API response (GET /api/ai/visualization/related/{documentId}) ─────────────

/** Response from GET /api/ai/visualization/related/{documentId}. */
export interface DocumentGraphResponse {
  nodes: ApiDocumentNode[];
  edges: ApiDocumentEdge[];
  metadata: GraphMetadata;
}

/** Node type values matching NodeTypes in IVisualizationService.cs. */
export type NodeType =
  | 'source' // The source document being queried (depth 0)
  | 'related' // A related document found via similarity or relationship
  | 'orphan' // An orphan file with no Dataverse record
  | 'matter' // A Matter entity acting as a hub node
  | 'project' // A Project entity acting as a hub node
  | 'invoice' // An Invoice entity acting as a hub node
  | 'email'; // An Email document acting as a hub node (parent of attachments)

/** Check if a node type represents a parent hub node. */
export const isParentHubNode = (type: NodeType): boolean =>
  type === 'matter' || type === 'project' || type === 'invoice' || type === 'email';

/** A document node from the API response. */
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

/** Data associated with a document node from the API. */
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

/** A relationship edge from the API response. */
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

/** Data associated with a relationship edge from the API. */
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

/** Metadata about the graph query and results. */
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

/** Query parameters for the visualization API. */
export interface VisualizationQueryParams {
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
  same_email: 'Attachment',
  /** Documents from the same email thread (ConversationIndex prefix match) */
  same_thread: 'Email thread',
  /** Documents associated with the same Matter */
  same_matter: 'Same matter',
  /** Documents associated with the same Project */
  same_project: 'Same project',
  /** Documents associated with the same Invoice */
  same_invoice: 'Same invoice',
  /** Documents related by vector similarity (content-based) */
  semantic: 'Semantic',
} as const;

/** Relationship type key (e.g., "same_email", "semantic"). */
export type RelationshipTypeKey = keyof typeof RELATIONSHIP_TYPES;

/**
 * Get display label for a relationship type.
 * Falls back to the API label if type is unknown.
 */
export function getRelationshipLabel(relationshipType: string, apiLabel?: string): string {
  // The API can send a type this client does not know, so look it up as an open record.
  const labels: Readonly<Record<string, string | undefined>> = RELATIONSHIP_TYPES;
  return labels[relationshipType] ?? apiLabel ?? 'Related';
}

// ─── Graph (canvas) types ──────────────────────────────────────────────────────

/** One relationship a node has to the graph (a node can have several). */
export interface DocumentRelationship {
  /** Relationship type key ("semantic", "same_matter", ...) */
  type: string;
  /** Display label */
  label: string;
  /** Similarity carried by the edge (0-1) */
  similarity: number;
}

/**
 * Document node data from the visualization API.
 *
 * Extends `Record<string, unknown>` because `@xyflow/react` v12 requires node data to;
 * `react-flow-renderer` v10 accepts any data, so the one type serves both hosts.
 */
export interface DocumentNodeData extends Record<string, unknown> {
  /** Document ID in Dataverse. Undefined for orphan files (use speFileId instead) and hub nodes */
  documentId?: string;
  /** SharePoint Embedded file ID. Primary identifier for file operations */
  speFileId?: string;
  /** Document display name */
  name: string;
  /** File extension (e.g., "pdf", "docx") */
  fileType: string;
  /** Document type/classification (e.g., "Contract", "Invoice", "Email") */
  documentType?: string;
  /** File size in bytes */
  size?: number;
  /** Similarity score to source document (0-1) */
  similarity?: number;
  /** Whether this is the source/center node */
  isSource?: boolean;
  /** Whether this is an orphan file (no Dataverse record) */
  isOrphanFile?: boolean;
  /** Node type from API (source, related, orphan, matter, project, invoice, email) */
  nodeType?: NodeType;
  /** Primary relationship type key — "semantic" when the node has one, else its first relationship */
  relationshipType?: string;
  /** Primary relationship label for display (e.g., "Semantic", "Same matter") */
  relationshipLabel?: string;
  /** Every relationship type connecting this node, one entry per type */
  relationshipTypes?: DocumentRelationship[];
  /** Parent entity name (Matter/Project) */
  parentEntityName?: string;
  /** URL to open the document in SPE */
  fileUrl?: string;
  /** Dataverse record URL. Empty for orphan files */
  recordUrl?: string;
  /** Shared keywords with connected documents */
  sharedKeywords?: string[];
  /** Document creation timestamp (ISO string) */
  createdOn?: string;
  /** Document last modified timestamp (ISO string) */
  modifiedOn?: string;
  /** Compact mode - show icon only (set by the host's graph) */
  compactMode?: boolean;
}

/** Edge data for document relationships. */
export interface DocumentEdgeData extends Record<string, unknown> {
  /** Similarity score between connected documents (0-1) */
  similarity: number;
  /** Shared keywords between documents */
  sharedKeywords?: string[];
  /** Relationship type key: "same_email", "same_matter", "semantic", etc. */
  relationshipType?: string;
  /** Human-readable label for display */
  relationshipLabel?: string;
}

/** A graph node — assignable to both `@xyflow/react` and `react-flow-renderer` `Node<DocumentNodeData>`. */
export interface DocumentGraphNode {
  id: string;
  type?: string;
  position: { x: number; y: number };
  data: DocumentNodeData;
}

/** A graph edge — assignable to both flow libraries' `Edge<DocumentEdgeData>`. */
export interface DocumentGraphEdge {
  id: string;
  source: string;
  target: string;
  type?: string;
  data?: DocumentEdgeData;
}

/** Force layout options the hosts' graphs accept (mapped onto `useForceSimulation`). */
export interface ForceLayoutOptions {
  /** Distance multiplier for edges (default: 400) */
  distanceMultiplier?: number;
  /** Collision radius for nodes (default: 100) */
  collisionRadius?: number;
  /** Center X position */
  centerX?: number;
  /** Center Y position */
  centerY?: number;
  /** Charge (repulsion) strength (default: -1000) */
  chargeStrength?: number;
}
