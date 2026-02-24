/**
 * Graph types for DocumentRelationshipViewer Code Page
 *
 * Uses @xyflow/react v12 (React 19 compatible) instead of
 * the deprecated react-flow-renderer v10 used in the PCF version.
 */

import type { Node, Edge } from "@xyflow/react";
import type { NodeType } from "./api";

/**
 * Supported file types for icon selection and display.
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
    file: "File",
} as const;

export type FileType = keyof typeof FILE_TYPES;

export function getFileTypeDisplayName(fileType: string | undefined): string {
    if (!fileType) return FILE_TYPES.file;
    const normalizedType = fileType.toLowerCase().replace(/^\./, "") as FileType;
    return FILE_TYPES[normalizedType] ?? FILE_TYPES.file;
}

/**
 * Document node data from the visualization API
 */
export interface DocumentNodeData extends Record<string, unknown> {
    documentId?: string;
    speFileId?: string;
    name: string;
    fileType: string;
    documentType?: string;
    size?: number;
    similarity?: number;
    isSource?: boolean;
    isOrphanFile?: boolean;
    nodeType?: NodeType;
    relationshipType?: string;
    relationshipLabel?: string;
    relationshipTypes?: Array<{ type: string; label: string; similarity: number }>;
    parentEntityName?: string;
    fileUrl?: string;
    recordUrl?: string;
    sharedKeywords?: string[];
    createdOn?: string;
    modifiedOn?: string;
    compactMode?: boolean;
}

/**
 * Edge data for document relationships
 */
export interface DocumentEdgeData extends Record<string, unknown> {
    similarity: number;
    sharedKeywords?: string[];
    relationshipType?: string;
    relationshipLabel?: string;
}

/**
 * @xyflow/react v12 node and edge types
 */
export type DocumentNode = Node<DocumentNodeData>;
export type DocumentEdge = Edge<DocumentEdgeData>;

export interface GraphData {
    nodes: DocumentNode[];
    edges: DocumentEdge[];
}

export interface ForceLayoutOptions {
    distanceMultiplier?: number;
    collisionRadius?: number;
    centerX?: number;
    centerY?: number;
    chargeStrength?: number;
}

export interface SimulationNode {
    id: string;
    x?: number;
    y?: number;
    fx?: number | null;
    fy?: number | null;
}

export interface SimulationLink {
    source: string | SimulationNode;
    target: string | SimulationNode;
    distance: number;
}
