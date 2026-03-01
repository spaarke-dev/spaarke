/**
 * Canvas Store — Zustand v5 state management for @xyflow/react v12 canvas
 *
 * Migrated from react-flow-renderer v10 (PCF R4) to @xyflow/react v12 (Code Page R5).
 *
 * Key v12 migration changes:
 * - Node, Edge, Connection, NodeChange, EdgeChange imported from '@xyflow/react'
 * - applyNodeChanges / applyEdgeChanges from '@xyflow/react'
 * - addEdge from '@xyflow/react'
 * - XYPosition from '@xyflow/react'
 * - Typed generics: Node<PlaybookNodeData>, Edge<ConditionEdgeData>
 */

import { create } from "zustand";
import {
    type Connection,
    type NodeChange,
    type EdgeChange,
    type XYPosition,
    applyNodeChanges,
    applyEdgeChanges,
    addEdge,
} from "@xyflow/react";
import type {
    PlaybookNodeType,
    PlaybookNodeData,
    PlaybookNode,
    PlaybookEdge,
    CanvasJson,
} from "../types/canvas";
import { CANVAS_JSON_VERSION } from "../types/canvas";

/** Generate a unique ID for new nodes */
const generateNodeId = (): string =>
    `node_${Date.now()}_${Math.random().toString(36).substring(2, 11)}`;

// ---------------------------------------------------------------------------
// Store interface
// ---------------------------------------------------------------------------

interface CanvasState {
    // State
    nodes: PlaybookNode[];
    edges: PlaybookEdge[];
    selectedNodeId: string | null;
    isDirty: boolean;
    lastSavedJson: string | null;

    // Node actions
    setNodes: (nodes: PlaybookNode[]) => void;
    onNodesChange: (changes: NodeChange<PlaybookNode>[]) => void;
    addNode: (node: PlaybookNode) => void;
    updateNodeData: (nodeId: string, data: Partial<PlaybookNodeData>) => void;
    removeNode: (nodeId: string) => void;

    // Edge actions
    setEdges: (edges: PlaybookEdge[]) => void;
    onEdgesChange: (changes: EdgeChange<PlaybookEdge>[]) => void;
    onConnect: (connection: Connection) => void;
    removeEdge: (edgeId: string) => void;

    // Selection
    selectNode: (nodeId: string | null) => void;

    // Drag and drop
    onDrop: (position: XYPosition, nodeType: PlaybookNodeType, label: string) => void;

    // Persistence
    loadFromCanvasJson: (json: string) => void;
    exportToCanvasJson: () => string;
    markSaved: () => void;
    markDirty: () => void;

    // Reset
    reset: () => void;
}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const initialState = {
    nodes: [] as PlaybookNode[],
    edges: [] as PlaybookEdge[],
    selectedNodeId: null as string | null,
    isDirty: false,
    lastSavedJson: null as string | null,
};

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

/**
 * Zustand v5 store for @xyflow/react v12 canvas state management.
 * Handles nodes, edges, selections, drag-drop, and JSON persistence.
 */
export const useCanvasStore = create<CanvasState>((set, get) => ({
    ...initialState,

    // -----------------------------------------------------------------------
    // Node actions
    // -----------------------------------------------------------------------

    setNodes: (nodes) => set({ nodes, isDirty: true }),

    onNodesChange: (changes) =>
        set((state) => ({
            nodes: applyNodeChanges(changes, state.nodes),
            isDirty: true,
        })),

    addNode: (node) =>
        set((state) => ({
            nodes: [...state.nodes, node],
            isDirty: true,
        })),

    updateNodeData: (nodeId, data) =>
        set((state) => ({
            nodes: state.nodes.map((node) =>
                node.id === nodeId
                    ? { ...node, data: { ...node.data, ...data } }
                    : node
            ),
            isDirty: true,
        })),

    removeNode: (nodeId) =>
        set((state) => ({
            nodes: state.nodes.filter((node) => node.id !== nodeId),
            edges: state.edges.filter(
                (edge) => edge.source !== nodeId && edge.target !== nodeId
            ),
            selectedNodeId:
                state.selectedNodeId === nodeId ? null : state.selectedNodeId,
            isDirty: true,
        })),

    // -----------------------------------------------------------------------
    // Edge actions
    // -----------------------------------------------------------------------

    setEdges: (edges) => set({ edges, isDirty: true }),

    onEdgesChange: (changes) =>
        set((state) => ({
            edges: applyEdgeChanges(changes, state.edges),
            isDirty: true,
        })),

    onConnect: (connection) =>
        set((state) => {
            // Determine edge type based on source node and handle
            let edgeType = "smoothstep";
            let animated = true;
            let edgeData: PlaybookEdge["data"] | undefined;

            // Check if source is a condition node
            const sourceNode = state.nodes.find((n) => n.id === connection.source);
            if (sourceNode?.data.type === "condition" && connection.sourceHandle) {
                if (connection.sourceHandle === "true") {
                    edgeType = "trueBranch";
                    animated = false;
                    edgeData = { branch: "true" as const };
                } else if (connection.sourceHandle === "false") {
                    edgeType = "falseBranch";
                    animated = false;
                    edgeData = { branch: "false" as const };
                }
            }

            return {
                edges: addEdge(
                    {
                        ...connection,
                        type: edgeType,
                        animated,
                        data: edgeData,
                    },
                    state.edges
                ),
                isDirty: true,
            };
        }),

    removeEdge: (edgeId) =>
        set((state) => ({
            edges: state.edges.filter((edge) => edge.id !== edgeId),
            isDirty: true,
        })),

    // -----------------------------------------------------------------------
    // Selection
    // -----------------------------------------------------------------------

    selectNode: (nodeId) => set({ selectedNodeId: nodeId }),

    // -----------------------------------------------------------------------
    // Drag and drop
    // -----------------------------------------------------------------------

    onDrop: (position, nodeType, label) => {
        const newNode: PlaybookNode = {
            id: generateNodeId(),
            type: nodeType,
            position,
            data: {
                label,
                type: nodeType,
                outputVariable: nodeType === "start" ? undefined : `output_${nodeType}`,
                isConfigured: false,
                validationErrors: [],
            },
        };
        get().addNode(newNode);
        get().selectNode(newNode.id);
    },

    // -----------------------------------------------------------------------
    // Persistence
    // -----------------------------------------------------------------------

    loadFromCanvasJson: (json: string) => {
        try {
            const data: CanvasJson = JSON.parse(json);
            set({
                nodes: data.nodes || [],
                edges: data.edges || [],
                selectedNodeId: null,
                isDirty: false,
                lastSavedJson: json,
            });
            console.info("[CanvasStore] Loaded canvas from JSON");
        } catch (error) {
            console.error("[CanvasStore] Failed to parse canvas JSON:", error);
        }
    },

    exportToCanvasJson: (): string => {
        const { nodes, edges } = get();
        const data: CanvasJson = {
            nodes,
            edges,
            version: CANVAS_JSON_VERSION,
        };
        return JSON.stringify(data);
    },

    markSaved: () => {
        const json = get().exportToCanvasJson();
        set({ isDirty: false, lastSavedJson: json });
    },

    markDirty: () => set({ isDirty: true }),

    // -----------------------------------------------------------------------
    // Reset
    // -----------------------------------------------------------------------

    reset: () => set(initialState),
}));
