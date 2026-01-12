/**
 * Execution Store - Zustand state management for playbook execution
 *
 * Manages real-time execution state for visualization overlay.
 * Receives updates from SSE stream and tracks node execution status.
 *
 * @version 1.0.0
 */

import { create } from 'zustand';

// Node execution status
export type NodeExecutionStatus =
  | 'pending'
  | 'running'
  | 'completed'
  | 'failed'
  | 'skipped';

// Execution event types from SSE stream
export type ExecutionEventType =
  | 'execution_started'
  | 'node_started'
  | 'node_progress'
  | 'node_completed'
  | 'node_failed'
  | 'execution_completed'
  | 'execution_failed';

// Node execution state
export interface NodeExecutionState {
  nodeId: string;
  status: NodeExecutionStatus;
  startedAt?: string;
  completedAt?: string;
  progress?: number; // 0-100 for progress indicator
  error?: string;
  outputPreview?: string; // Brief text preview of output
  tokensUsed?: number;
}

// Overall execution state
export interface ExecutionState {
  executionId: string | null;
  status: 'idle' | 'running' | 'completed' | 'failed';
  startedAt: string | null;
  completedAt: string | null;
  nodeStates: Map<string, NodeExecutionState>;
  totalTokensUsed: number;
  error: string | null;
}

// SSE event payload structure (matches backend PlaybookExecutionEvent)
export interface ExecutionEvent {
  eventType: ExecutionEventType;
  executionId: string;
  nodeId?: string;
  nodeName?: string;
  status?: NodeExecutionStatus;
  progress?: number;
  outputPreview?: string;
  tokensUsed?: number;
  error?: string;
  timestamp: string;
}

interface ExecutionStoreState extends ExecutionState {
  // Actions
  startExecution: (executionId: string) => void;
  handleEvent: (event: ExecutionEvent) => void;
  stopExecution: () => void;
  resetExecution: () => void;

  // Getters
  getNodeState: (nodeId: string) => NodeExecutionState | undefined;
  isNodeRunning: (nodeId: string) => boolean;
  isExecuting: () => boolean;
}

const initialState: ExecutionState = {
  executionId: null,
  status: 'idle',
  startedAt: null,
  completedAt: null,
  nodeStates: new Map(),
  totalTokensUsed: 0,
  error: null,
};

/**
 * Zustand store for playbook execution state.
 * Handles SSE events and maintains node execution states for visualization.
 */
export const useExecutionStore = create<ExecutionStoreState>((set, get) => ({
  ...initialState,

  startExecution: (executionId: string) => {
    set({
      executionId,
      status: 'running',
      startedAt: new Date().toISOString(),
      completedAt: null,
      nodeStates: new Map(),
      totalTokensUsed: 0,
      error: null,
    });
    console.info('[ExecutionStore] Execution started:', executionId);
  },

  handleEvent: (event: ExecutionEvent) => {
    const { eventType, nodeId, status, progress, outputPreview, tokensUsed, error } = event;

    console.debug('[ExecutionStore] Handling event:', eventType, nodeId);

    set((state) => {
      const newNodeStates = new Map(state.nodeStates);

      switch (eventType) {
        case 'node_started':
          if (nodeId) {
            newNodeStates.set(nodeId, {
              nodeId,
              status: 'running',
              startedAt: event.timestamp,
              progress: 0,
            });
          }
          return { nodeStates: newNodeStates };

        case 'node_progress':
          if (nodeId) {
            const existing = newNodeStates.get(nodeId);
            newNodeStates.set(nodeId, {
              ...existing,
              nodeId,
              status: 'running',
              progress: progress ?? existing?.progress,
            });
          }
          return { nodeStates: newNodeStates };

        case 'node_completed':
          if (nodeId) {
            const existing = newNodeStates.get(nodeId);
            newNodeStates.set(nodeId, {
              ...existing,
              nodeId,
              status: 'completed',
              completedAt: event.timestamp,
              progress: 100,
              outputPreview,
              tokensUsed,
            });
          }
          return {
            nodeStates: newNodeStates,
            totalTokensUsed: state.totalTokensUsed + (tokensUsed ?? 0),
          };

        case 'node_failed':
          if (nodeId) {
            const existing = newNodeStates.get(nodeId);
            newNodeStates.set(nodeId, {
              ...existing,
              nodeId,
              status: 'failed',
              completedAt: event.timestamp,
              error,
            });
          }
          return { nodeStates: newNodeStates };

        case 'execution_completed':
          return {
            status: 'completed',
            completedAt: event.timestamp,
          };

        case 'execution_failed':
          return {
            status: 'failed',
            completedAt: event.timestamp,
            error: error ?? 'Execution failed',
          };

        default:
          return {};
      }
    });
  },

  stopExecution: () => {
    set({
      status: 'idle',
      completedAt: new Date().toISOString(),
    });
    console.info('[ExecutionStore] Execution stopped');
  },

  resetExecution: () => {
    set(initialState);
    console.info('[ExecutionStore] Execution reset');
  },

  getNodeState: (nodeId: string) => {
    return get().nodeStates.get(nodeId);
  },

  isNodeRunning: (nodeId: string) => {
    const nodeState = get().nodeStates.get(nodeId);
    return nodeState?.status === 'running';
  },

  isExecuting: () => {
    return get().status === 'running';
  },
}));
