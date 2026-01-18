/**
 * Graph types for DocumentRelationshipViewer
 *
 * These types represent the document relationship graph data structure
 * used by React Flow and d3-force layout.
 *
 * Note: Using react-flow-renderer v10 for React 16 compatibility (ADR-022)
 */

import type { Node, Edge } from "react-flow-renderer";
import type { NodeType } from "./api";

/**
 * Supported file types for icon selection and display.
 * Maps file extensions to display names.
 */
export const FILE_TYPES = {
    pdf: "PDF Document",
    docx: "Word Document",
    doc: "Word Document",
    xlsx: "Excel Spreadsheet",
    xls: "Excel Spreadsheet",
    pptx: "PowerPoint",
    ppt: "PowerPoint",
    msg: "Email",
    eml: "Email",
    txt: "Text File",
    csv: "CSV File",
    png: "Image",
    jpg: "Image",
    jpeg: "Image",
    gif: "Image",
    bmp: "Image",
    tiff: "Image",
    html: "Web Page",
    htm: "Web Page",
    json: "JSON File",
    xml: "XML File",
    zip: "Archive",
    file: "File", // Fallback for orphan files without extension
} as const;

/**
 * File type enum values
 */
export type FileType = keyof typeof FILE_TYPES;

/**
 * Get display name for a file type.
 * @param fileType - File extension (e.g., "pdf", "docx")
 * @returns Human-readable display name
 */
export function getFileTypeDisplayName(fileType: string | undefined): string {
    if (!fileType) return FILE_TYPES.file;
    const normalizedType = fileType.toLowerCase().replace(/^\./, "") as FileType;
    return FILE_TYPES[normalizedType] ?? FILE_TYPES.file;
}

/**
 * Document node data from the visualization API
 */
export interface DocumentNodeData {
    /** Document ID in Dataverse. Undefined for orphan files (use speFileId instead) */
    documentId?: string;
    /** SharePoint Embedded file ID (always present). Primary identifier for file operations */
    speFileId?: string;
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
    /** Whether this is an orphan file (no Dataverse record) */
    isOrphanFile?: boolean;
    /** Node type from API (source, related, orphan, matter, project, invoice, email) */
    nodeType?: NodeType;
    /** Primary relationship type key (derived from edge connecting to source/parent) */
    relationshipType?: string;
    /** Primary relationship label for display (e.g., "From email", "Same matter", "Semantic") */
    relationshipLabel?: string;
    /** Parent entity name (Matter/Project) */
    parentEntityName?: string;
    /** URL to open the document in SPE */
    fileUrl?: string;
    /** Dataverse record URL. Empty for orphan files */
    recordUrl?: string;
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
    /** Relationship type key: "same_email", "same_matter", "semantic", etc. */
    relationshipType?: string;
    /** Human-readable label for display */
    relationshipLabel?: string;
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
