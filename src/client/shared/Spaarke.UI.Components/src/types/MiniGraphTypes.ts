/**
 * Lightweight types for the MiniGraph preview component.
 *
 * Intentionally minimal — only what the SVG preview needs.
 * The full DocumentRelationshipViewer has its own richer API types.
 */

/** A node in the mini graph preview. */
export interface MiniGraphNode {
    /** Node identifier (document GUID or hub entity ID). */
    id: string;
    /** Node type: "source", "related", "orphan", "matter", "project", "invoice", "email". */
    type: string;
    /** Display label (may be truncated for preview). */
    label?: string;
    /** Cosine similarity to source document (0-1). Undefined for source node. */
    similarity?: number;
}

/** An edge in the mini graph preview. */
export interface MiniGraphEdge {
    /** Source node ID. */
    source: string;
    /** Target node ID. */
    target: string;
    /** Relationship type: "semantic", "same_matter", "same_email", "same_thread", "same_project", "same_invoice". */
    relationshipType: string;
    /** Cosine similarity score (0-1). */
    similarity?: number;
}
