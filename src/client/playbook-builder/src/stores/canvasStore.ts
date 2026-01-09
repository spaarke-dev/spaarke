import { create } from 'zustand';
import {
  Node,
  Edge,
  Connection,
  NodeChange,
  EdgeChange,
  applyNodeChanges,
  applyEdgeChanges,
  addEdge,
  XYPosition,
} from '@xyflow/react';

// Node type constants
export type PlaybookNodeType = 'aiAnalysis' | 'aiCompletion' | 'condition' | 'deliverOutput' | 'createTask' | 'sendEmail' | 'wait';

// Node data type for playbook nodes - index signature required by React Flow
export interface PlaybookNodeData extends Record<string, unknown> {
  label: string;
  type: PlaybookNodeType;
  actionId?: string;
  outputVariable?: string;
  config?: Record<string, unknown>;
  // Node configuration (maps to Dataverse fields)
  timeoutSeconds?: number;
  retryCount?: number;
  conditionJson?: string; // For condition nodes
  // Scope selections (skills, knowledge, tool)
  skillIds?: string[];
  knowledgeIds?: string[];
  toolId?: string;
}

// Typed node for our application
export type PlaybookNode = Node<PlaybookNodeData, string>;

interface CanvasState {
  // State
  nodes: PlaybookNode[];
  edges: Edge[];
  selectedNodeId: string | null;
  isDirty: boolean;
  lastSavedJson: string | null;

  // Node actions
  setNodes: (nodes: PlaybookNode[]) => void;
  onNodesChange: (changes: NodeChange<PlaybookNode>[]) => void;
  addNode: (node: PlaybookNode) => void;
  updateNode: (nodeId: string, data: Partial<PlaybookNodeData>) => void;
  removeNode: (nodeId: string) => void;

  // Edge actions
  setEdges: (edges: Edge[]) => void;
  onEdgesChange: (changes: EdgeChange[]) => void;
  onConnect: (connection: Connection) => void;

  // Selection
  selectNode: (nodeId: string | null) => void;

  // Drag and drop
  onDrop: (position: XYPosition, nodeType: PlaybookNodeData['type'], label: string) => void;

  // Persistence
  loadFromJson: (json: string) => void;
  toJson: () => string;
  markSaved: () => void;
  markDirty: () => void;

  // Reset
  reset: () => void;
}

// Generate unique ID for new nodes
const generateNodeId = () => `node_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

// Initial state
const initialState = {
  nodes: [] as PlaybookNode[],
  edges: [] as Edge[],
  selectedNodeId: null as string | null,
  isDirty: false,
  lastSavedJson: null as string | null,
};

// Canvas JSON structure for persistence
interface CanvasJson {
  nodes: PlaybookNode[];
  edges: Edge[];
  version: number;
}

const CANVAS_JSON_VERSION = 1;

/**
 * Zustand store for React Flow canvas state management.
 * Handles nodes, edges, selections, and drag-drop operations.
 */
export const useCanvasStore = create<CanvasState>((set, get) => ({
  ...initialState,

  // Node state setters
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

  updateNode: (nodeId, data) =>
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
      selectedNodeId: state.selectedNodeId === nodeId ? null : state.selectedNodeId,
      isDirty: true,
    })),

  // Edge state setters
  setEdges: (edges) => set({ edges, isDirty: true }),

  onEdgesChange: (changes) =>
    set((state) => ({
      edges: applyEdgeChanges(changes, state.edges),
      isDirty: true,
    })),

  onConnect: (connection) =>
    set((state) => ({
      edges: addEdge(
        {
          ...connection,
          type: 'smoothstep',
          animated: true,
        },
        state.edges
      ),
      isDirty: true,
    })),

  // Selection
  selectNode: (nodeId) => set({ selectedNodeId: nodeId }),

  // Drag and drop handler
  onDrop: (position, nodeType, label) => {
    const newNode: PlaybookNode = {
      id: generateNodeId(),
      type: nodeType, // Uses custom node component from nodeTypes registry
      position,
      data: {
        label,
        type: nodeType,
        outputVariable: `output_${nodeType}`,
      },
    };
    get().addNode(newNode);
    get().selectNode(newNode.id);
  },

  // Persistence
  loadFromJson: (json: string) => {
    try {
      const data: CanvasJson = JSON.parse(json);
      set({
        nodes: data.nodes || [],
        edges: data.edges || [],
        selectedNodeId: null,
        isDirty: false,
        lastSavedJson: json,
      });
      console.info('[CanvasStore] Loaded canvas from JSON');
    } catch (error) {
      console.error('[CanvasStore] Failed to parse canvas JSON:', error);
    }
  },

  toJson: () => {
    const { nodes, edges } = get();
    const data: CanvasJson = {
      nodes,
      edges,
      version: CANVAS_JSON_VERSION,
    };
    return JSON.stringify(data);
  },

  markSaved: () => {
    const json = get().toJson();
    set({ isDirty: false, lastSavedJson: json });
  },

  markDirty: () => set({ isDirty: true }),

  // Reset canvas
  reset: () => set(initialState),
}));
