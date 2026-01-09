import { create } from 'zustand';
import type { PlaybookDefinition, PlaybookNode } from '../types';

interface PlaybookState {
  // State
  playbook: PlaybookDefinition | null;
  selectedNodeId: string | null;
  isDirty: boolean;

  // Actions
  setPlaybook: (playbook: PlaybookDefinition) => void;
  selectNode: (nodeId: string | null) => void;
  addNode: (node: PlaybookNode) => void;
  updateNode: (nodeId: string, updates: Partial<PlaybookNode>) => void;
  removeNode: (nodeId: string) => void;
  setDirty: (isDirty: boolean) => void;
}

/**
 * Zustand store for playbook builder state management.
 * Handles playbook definition, node selection, and dirty state.
 */
export const usePlaybookStore = create<PlaybookState>((set) => ({
  // Initial state
  playbook: null,
  selectedNodeId: null,
  isDirty: false,

  // Actions
  setPlaybook: (playbook) => set({ playbook, isDirty: false }),

  selectNode: (nodeId) => set({ selectedNodeId: nodeId }),

  addNode: (node) =>
    set((state) => {
      if (!state.playbook) return state;
      return {
        playbook: {
          ...state.playbook,
          nodes: [...state.playbook.nodes, node],
        },
        isDirty: true,
      };
    }),

  updateNode: (nodeId, updates) =>
    set((state) => {
      if (!state.playbook) return state;
      return {
        playbook: {
          ...state.playbook,
          nodes: state.playbook.nodes.map((node) =>
            node.id === nodeId ? { ...node, ...updates } : node
          ),
        },
        isDirty: true,
      };
    }),

  removeNode: (nodeId) =>
    set((state) => {
      if (!state.playbook) return state;
      return {
        playbook: {
          ...state.playbook,
          nodes: state.playbook.nodes.filter((node) => node.id !== nodeId),
        },
        selectedNodeId:
          state.selectedNodeId === nodeId ? null : state.selectedNodeId,
        isDirty: true,
      };
    }),

  setDirty: (isDirty) => set({ isDirty }),
}));
