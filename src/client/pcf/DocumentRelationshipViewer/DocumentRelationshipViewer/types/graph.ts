/**
 * Graph types for DocumentRelationshipViewer
 *
 * These types represent the document relationship graph data structure
 * used by React Flow and d3-force layout.
 *
 * Note: Using react-flow-renderer v10 for React 16 compatibility (ADR-022)
 */

import type { Node, Edge } from "react-flow-renderer";

/**
 * Document node data from the visualization API
 */
export interface DocumentNodeData {
    /** Document ID in Dataverse */
    documentId: string;
    /** Document display name */
    name: string;
    /** File extension (e.g., "pdf", "docx") */
    fileType: string;
    /** File size in bytes */
    size?: number;
    /** Similarity score to source document (0-1) */
    similarity?: number;
    /** Whether this is the source/center node */
    isSource?: boolean;
    /** Parent entity name (Matter/Project) */
    parentEntityName?: string;
    /** URL to open the document in SPE */
    fileUrl?: string;
    /** Shared keywords with connected documents */
    sharedKeywords?: string[];
    /** Compact mode - show icon only (set by DocumentGraph) */
    compactMode?: boolean;
}

/**
 * Edge data for document relationships
 */
export interface DocumentEdgeData {
    /** Similarity score between connected documents (0-1) */
    similarity: number;
    /** Shared keywords between documents */
    sharedKeywords?: string[];
}

/**
 * React Flow node with document data
 */
export type DocumentNode = Node<DocumentNodeData>;

/**
 * React Flow edge with relationship data
 */
export type DocumentEdge = Edge<DocumentEdgeData>;

/**
 * Graph data structure from API response
 */
export interface GraphData {
    nodes: DocumentNode[];
    edges: DocumentEdge[];
}

/**
 * Force layout options
 */
export interface ForceLayoutOptions {
    /** Distance multiplier for edges (default: 200) */
    distanceMultiplier?: number;
    /** Collision radius for nodes (default: 50) */
    collisionRadius?: number;
    /** Center X position */
    centerX?: number;
    /** Center Y position */
    centerY?: number;
    /** Simulation strength (default: -300) */
    chargeStrength?: number;
}

/**
 * D3 simulation node with position
 */
export interface SimulationNode {
    id: string;
    x?: number;
    y?: number;
    fx?: number | null;
    fy?: number | null;
}

/**
 * D3 simulation link
 */
export interface SimulationLink {
    source: string | SimulationNode;
    target: string | SimulationNode;
    distance: number;
}
