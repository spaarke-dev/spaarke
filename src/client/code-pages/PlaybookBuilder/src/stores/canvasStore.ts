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

import { create } from 'zustand';
import {
  type Connection,
  type NodeChange,
  type EdgeChange,
  type XYPosition,
  applyNodeChanges,
  applyEdgeChanges,
  addEdge,
} from '@xyflow/react';
import type { PlaybookNodeType, PlaybookNodeData, PlaybookNode, PlaybookEdge, CanvasJson } from '../types/canvas';
import { CANVAS_JSON_VERSION } from '../types/canvas';

/** Generate a unique ID for new nodes */
const generateNodeId = (): string => `node_${Date.now()}_${Math.random().toString(36).substring(2, 11)}`;

// ---------------------------------------------------------------------------
// Store interface
// ---------------------------------------------------------------------------

/** Snapshot of N:N scopes at load time, keyed by canvas node ID. */
type InitialScopeMap = Map<string, { skillIds: string[]; knowledgeIds: string[]; toolIds: string[] }>;

/**
 * Branch choice for a Condition→downstream edge. 'both' creates TWO edges
 * (one trueBranch + one falseBranch) — we never invent a third edge type.
 * See R3-092 (FR-3H2.2) and notes/playbookbuilder-pattern-research.md §6.
 */
export type BranchChoice = 'true' | 'false' | 'both';

interface CanvasState {
  // State
  nodes: PlaybookNode[];
  edges: PlaybookEdge[];
  selectedNodeId: string | null;
  isDirty: boolean;
  lastSavedJson: string | null;
  /** N:N scopes as they were when the canvas loaded — used to detect external changes on save */
  initialNodeScopes: InitialScopeMap;
  /**
   * Pending Condition→downstream connection awaiting author branch choice.
   * Populated by onConnect when source is a condition node and sourceHandle
   * is unset. Consumed by BranchPickerDialog (R3-092 / FR-3H2.2 / AC-H2.2).
   */
  pendingBranchConnection: Connection | null;

  // Node actions
  setNodes: (nodes: PlaybookNode[]) => void;
  onNodesChange: (changes: NodeChange<PlaybookNode>[]) => void;
  addNode: (node: PlaybookNode) => void;
  updateNodeData: (nodeId: string, data: Partial<PlaybookNodeData>) => void;
  removeNode: (nodeId: string) => void;
  /**
   * R3 P9 H2 (task 091): Auto-rename all `{{oldName.output.*}}` template
   * references across the canvas. Mutates every node whose serialized config
   * (configJson.fieldMappings[].value, configJson.fields, configJson template
   * fields, node.data.template / emailBody / emailSubject / conditionJson)
   * contains the pattern. Returns the number of nodes mutated.
   *
   * Invoked by the rename-guard dialog when the user picks "Auto-rename
   * references". No-ops when oldName / newName are empty or identical.
   */
  renameOutputVariableReferences: (oldName: string, newName: string) => number;

  // Edge actions
  setEdges: (edges: PlaybookEdge[]) => void;
  onEdgesChange: (changes: EdgeChange<PlaybookEdge>[]) => void;
  onConnect: (connection: Connection) => void;
  removeEdge: (edgeId: string) => void;
  /** Resolve a pending Condition→downstream branch picker dialog (R3-092). */
  confirmBranchSelection: (choice: BranchChoice) => void;
  /** Cancel the pending branch picker dialog without creating an edge (R3-092). */
  cancelBranchSelection: () => void;

  // Selection
  selectNode: (nodeId: string | null) => void;

  // Drag and drop
  onDrop: (position: XYPosition, nodeType: PlaybookNodeType, label: string) => void;

  // Persistence
  loadFromCanvasJson: (json: string) => void;
  mergeNodeScopes: (scopeMap: InitialScopeMap) => void;
  getInitialNodeScopes: () => InitialScopeMap;
  exportToCanvasJson: () => string;
  markSaved: () => void;
  markDirty: () => void;

  // Initialization
  initializeNewCanvas: () => void;

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
  initialNodeScopes: new Map() as InitialScopeMap,
  pendingBranchConnection: null as Connection | null,
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

  setNodes: nodes => set({ nodes, isDirty: true }),

  onNodesChange: changes =>
    set(state => ({
      nodes: applyNodeChanges(changes, state.nodes),
      isDirty: true,
    })),

  addNode: node =>
    set(state => ({
      nodes: [...state.nodes, node],
      isDirty: true,
    })),

  updateNodeData: (nodeId, data) =>
    set(state => ({
      nodes: state.nodes.map(node => (node.id === nodeId ? { ...node, data: { ...node.data, ...data } } : node)),
      isDirty: true,
    })),

  removeNode: nodeId =>
    set(state => ({
      nodes: state.nodes.filter(node => node.id !== nodeId),
      edges: state.edges.filter(edge => edge.source !== nodeId && edge.target !== nodeId),
      selectedNodeId: state.selectedNodeId === nodeId ? null : state.selectedNodeId,
      isDirty: true,
    })),

  /**
   * R3 P9 H2 (task 091): Walk every node and rewrite
   * `{{<oldName>.output.<field>}}` → `{{<newName>.output.<field>}}` across all
   * fields the rename-guard scanner inspects:
   *   - node.data.template, emailBody, emailSubject, conditionJson
   *   - configJson.fieldMappings[].value
   *   - configJson.fields (legacy dict)
   *   - configJson.{template, body, subject, description}
   *
   * Returns the count of nodes mutated. No-ops when oldName / newName are
   * empty or identical (returns 0).
   */
  renameOutputVariableReferences: (oldName, newName) => {
    const trimmedOld = oldName.trim();
    const trimmedNew = newName.trim();
    if (trimmedOld === '' || trimmedNew === '' || trimmedOld === trimmedNew) return 0;

    // Pattern: {{oldName.output.<field>}} — escape oldName for regex safety.
    const escapedOld = trimmedOld.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const refPattern = new RegExp(`\\{\\{${escapedOld}\\.output\\.(\\w+)\\}\\}`, 'g');
    const replacement = `{{${trimmedNew}.output.$1}}`;

    const rewriteString = (value: unknown): { changed: boolean; result: unknown } => {
      if (typeof value !== 'string' || value.length === 0) return { changed: false, result: value };
      refPattern.lastIndex = 0;
      if (!refPattern.test(value)) return { changed: false, result: value };
      refPattern.lastIndex = 0;
      return { changed: true, result: value.replace(refPattern, replacement) };
    };

    let mutatedCount = 0;

    set(state => ({
      nodes: state.nodes.map(node => {
        let nodeChanged = false;
        const nextData: Record<string, unknown> = { ...node.data };

        // Direct string fields on node.data
        for (const key of ['template', 'emailBody', 'emailSubject', 'conditionJson']) {
          const { changed, result } = rewriteString(nextData[key]);
          if (changed) {
            nextData[key] = result;
            nodeChanged = true;
          }
        }

        // configJson: parse → rewrite known string fields → re-serialize
        const configJsonStr = nextData.configJson;
        if (typeof configJsonStr === 'string' && configJsonStr.length > 0) {
          try {
            const parsed = JSON.parse(configJsonStr) as Record<string, unknown>;
            let configChanged = false;

            // fieldMappings[].value
            if (Array.isArray(parsed.fieldMappings)) {
              const updated = parsed.fieldMappings.map(entry => {
                if (!entry || typeof entry !== 'object') return entry;
                const e = entry as Record<string, unknown>;
                const r = rewriteString(e.value);
                if (r.changed) {
                  configChanged = true;
                  return { ...e, value: r.result };
                }
                return entry;
              });
              if (configChanged) parsed.fieldMappings = updated;
            }

            // legacy fields dict
            if (parsed.fields && typeof parsed.fields === 'object' && !Array.isArray(parsed.fields)) {
              const fields = parsed.fields as Record<string, unknown>;
              const nextFields: Record<string, unknown> = { ...fields };
              let fieldsChanged = false;
              for (const [k, v] of Object.entries(fields)) {
                const r = rewriteString(v);
                if (r.changed) {
                  nextFields[k] = r.result;
                  fieldsChanged = true;
                }
              }
              if (fieldsChanged) {
                parsed.fields = nextFields;
                configChanged = true;
              }
            }

            // Top-level template fields
            for (const key of ['template', 'body', 'subject', 'description']) {
              const r = rewriteString(parsed[key]);
              if (r.changed) {
                parsed[key] = r.result;
                configChanged = true;
              }
            }

            if (configChanged) {
              nextData.configJson = JSON.stringify(parsed);
              nodeChanged = true;
            }
          } catch {
            // Malformed configJson — leave untouched (matches scanner's graceful handling).
          }
        }

        if (nodeChanged) {
          mutatedCount++;
          return { ...node, data: nextData as PlaybookNodeData };
        }
        return node;
      }),
      isDirty: state.nodes.length > 0 ? true : state.isDirty,
    }));

    return mutatedCount;
  },

  // -----------------------------------------------------------------------
  // Edge actions
  // -----------------------------------------------------------------------

  setEdges: edges => set({ edges, isDirty: true }),

  onEdgesChange: changes =>
    set(state => ({
      edges: applyEdgeChanges(changes, state.edges),
      isDirty: true,
    })),

  onConnect: connection =>
    set(state => {
      // Determine edge type based on source node and handle
      let edgeType = 'smoothstep';
      let animated = true;
      let edgeData: PlaybookEdge['data'] | undefined;

      // Check if source is a condition node
      const sourceNode = state.nodes.find(n => n.id === connection.source);
      if (sourceNode?.data.type === 'condition') {
        // R3-092 (FR-3H2.2 / AC-H2.2): Condition→downstream connections
        // MUST specify a branch. If sourceHandle is already set (drag from
        // 'true'/'false' handle), honor it. Otherwise, defer the edge and
        // prompt the author via BranchPickerDialog.
        if (connection.sourceHandle === 'true') {
          edgeType = 'trueBranch';
          animated = false;
          edgeData = { branch: 'true' as const };
        } else if (connection.sourceHandle === 'false') {
          edgeType = 'falseBranch';
          animated = false;
          edgeData = { branch: 'false' as const };
        } else {
          // Defer edge creation; surface the picker dialog.
          return { pendingBranchConnection: connection };
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

  removeEdge: edgeId =>
    set(state => ({
      edges: state.edges.filter(edge => edge.id !== edgeId),
      isDirty: true,
    })),

  // -----------------------------------------------------------------------
  // Branch picker (R3-092 / FR-3H2.2 / AC-H2.2)
  // -----------------------------------------------------------------------

  /**
   * Confirm the author's branch choice for a pending Condition→downstream
   * connection. `'both'` creates TWO edges (one trueBranch + one falseBranch)
   * — we never invent a third 'bothBranch' edge type (research §7 anti-pattern).
   */
  confirmBranchSelection: choice =>
    set(state => {
      const pending = state.pendingBranchConnection;
      if (!pending) return {};

      let nextEdges = state.edges;
      if (choice === 'true' || choice === 'both') {
        nextEdges = addEdge(
          {
            ...pending,
            sourceHandle: 'true',
            type: 'trueBranch',
            animated: false,
            data: { branch: 'true' as const },
          },
          nextEdges
        );
      }
      if (choice === 'false' || choice === 'both') {
        nextEdges = addEdge(
          {
            ...pending,
            sourceHandle: 'false',
            type: 'falseBranch',
            animated: false,
            data: { branch: 'false' as const },
          },
          nextEdges
        );
      }

      return {
        edges: nextEdges,
        pendingBranchConnection: null,
        isDirty: true,
      };
    }),

  /** Cancel the pending branch picker — discard the connection. */
  cancelBranchSelection: () => set({ pendingBranchConnection: null }),

  // -----------------------------------------------------------------------
  // Selection
  // -----------------------------------------------------------------------

  selectNode: nodeId => set({ selectedNodeId: nodeId }),

  // -----------------------------------------------------------------------
  // Drag and drop
  // -----------------------------------------------------------------------

  onDrop: (position, nodeType, label) => {
    const baseData: Record<string, unknown> = {
      label,
      type: nodeType,
      outputVariable: nodeType === 'start' ? undefined : `output_${nodeType}`,
      isConfigured: false,
      validationErrors: [],
    };

    // Set type-specific defaults for structural nodes
    if (nodeType === 'deliverOutput') {
      baseData.deliveryType = 'markdown';
    } else if (nodeType === 'deliverToIndex') {
      baseData.indexName = 'knowledge';
      baseData.indexSource = 'document';
    }

    const newNode: PlaybookNode = {
      id: generateNodeId(),
      type: nodeType,
      position,
      data: baseData,
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
      console.info('[CanvasStore] Loaded canvas from JSON');
    } catch (error) {
      console.error('[CanvasStore] Failed to parse canvas JSON:', error);
    }
  },

  mergeNodeScopes: scopeMap => {
    const { nodes } = get();
    let merged = 0;
    const updatedNodes = nodes.map(node => {
      const scopes = scopeMap.get(node.id);
      if (!scopes) return node;
      merged++;
      return {
        ...node,
        data: {
          ...node.data,
          skillIds: scopes.skillIds,
          knowledgeIds: scopes.knowledgeIds,
          toolIds: scopes.toolIds,
        },
      };
    });
    // Store both the merged nodes and the initial scope snapshot.
    // The snapshot is used at save time to distinguish "user removed a scope"
    // from "scope was added externally after canvas loaded".
    set({ nodes: updatedNodes, initialNodeScopes: new Map(scopeMap) });
    console.info(`[CanvasStore] Merged N:N scopes into ${merged} nodes`);
  },

  getInitialNodeScopes: () => get().initialNodeScopes,

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
  // Initialization
  // -----------------------------------------------------------------------

  initializeNewCanvas: () => {
    const startNode: PlaybookNode = {
      id: generateNodeId(),
      type: 'start',
      position: { x: 100, y: 200 },
      data: {
        label: 'Start',
        type: 'start',
        isConfigured: true,
        validationErrors: [],
      },
    };
    set({
      nodes: [startNode],
      edges: [],
      selectedNodeId: null,
      isDirty: false,
      lastSavedJson: null,
    });
  },

  // -----------------------------------------------------------------------
  // Reset
  // -----------------------------------------------------------------------

  reset: () => set(initialState),
}));
