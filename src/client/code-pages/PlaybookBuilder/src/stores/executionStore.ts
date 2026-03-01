/**
 * Execution Store - Zustand v5 state management for playbook execution
 *
 * Manages real-time execution state for visualization overlay.
 * Receives updates from SSE stream and tracks node execution status.
 *
 * Migrated from R4 PCF: framework-agnostic, no PCF dependencies.
 *
 * @version 2.0.0 (Code Page migration)
 */

import { create } from "zustand";

// ============================================================================
// Types
// ============================================================================

/** Node execution status */
export type NodeExecutionStatus =
    | "pending"
    | "running"
    | "completed"
    | "failed"
    | "skipped";

/** Execution event types from SSE stream */
export type ExecutionEventType =
    | "execution_started"
    | "node_started"
    | "node_progress"
    | "node_completed"
    | "node_failed"
    | "execution_completed"
    | "execution_failed";

/** Node execution state */
export interface NodeExecutionState {
    nodeId: string;
    status: NodeExecutionStatus;
    startedAt?: string;
    completedAt?: string;
    progress?: number;
    error?: string;
    outputPreview?: string;
    tokensUsed?: number;
    confidence?: number;
}

/** Overall execution state */
export interface ExecutionState {
    executionId: string | null;
    status: "idle" | "running" | "completed" | "failed";
    startedAt: string | null;
    completedAt: string | null;
    nodeStates: Map<string, NodeExecutionState>;
    totalTokensUsed: number;
    error: string | null;
    overallConfidence: number | null;
}

/** SSE event payload structure (matches backend PlaybookExecutionEvent) */
export interface ExecutionEvent {
    eventType: ExecutionEventType;
    executionId: string;
    nodeId?: string;
    nodeName?: string;
    status?: NodeExecutionStatus;
    progress?: number;
    outputPreview?: string;
    tokensUsed?: number;
    confidence?: number;
    overallConfidence?: number;
    error?: string;
    timestamp: string;
}

// ============================================================================
// Store Interface
// ============================================================================

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

// ============================================================================
// Initial State
// ============================================================================

const initialState: ExecutionState = {
    executionId: null,
    status: "idle",
    startedAt: null,
    completedAt: null,
    nodeStates: new Map(),
    totalTokensUsed: 0,
    error: null,
    overallConfidence: null,
};

// ============================================================================
// Store
// ============================================================================

/**
 * Zustand v5 store for playbook execution state.
 * Handles SSE events and maintains node execution states for visualization.
 */
export const useExecutionStore = create<ExecutionStoreState>((set, get) => ({
    ...initialState,

    startExecution: (executionId: string) => {
        set({
            executionId,
            status: "running",
            startedAt: new Date().toISOString(),
            completedAt: null,
            nodeStates: new Map(),
            totalTokensUsed: 0,
            error: null,
            overallConfidence: null,
        });
    },

    handleEvent: (event: ExecutionEvent) => {
        const { eventType, nodeId, progress, outputPreview, tokensUsed, confidence, overallConfidence, error } = event;

        set((state) => {
            const newNodeStates = new Map(state.nodeStates);

            switch (eventType) {
                case "node_started":
                    if (nodeId) {
                        newNodeStates.set(nodeId, {
                            nodeId,
                            status: "running",
                            startedAt: event.timestamp,
                            progress: 0,
                        });
                    }
                    return { nodeStates: newNodeStates };

                case "node_progress":
                    if (nodeId) {
                        const existing = newNodeStates.get(nodeId);
                        newNodeStates.set(nodeId, {
                            ...existing,
                            nodeId,
                            status: "running",
                            progress: progress ?? existing?.progress,
                        });
                    }
                    return { nodeStates: newNodeStates };

                case "node_completed":
                    if (nodeId) {
                        const existing = newNodeStates.get(nodeId);
                        newNodeStates.set(nodeId, {
                            ...existing,
                            nodeId,
                            status: "completed",
                            completedAt: event.timestamp,
                            progress: 100,
                            outputPreview,
                            tokensUsed,
                            confidence,
                        });
                    }
                    return {
                        nodeStates: newNodeStates,
                        totalTokensUsed: state.totalTokensUsed + (tokensUsed ?? 0),
                    };

                case "node_failed":
                    if (nodeId) {
                        const existing = newNodeStates.get(nodeId);
                        newNodeStates.set(nodeId, {
                            ...existing,
                            nodeId,
                            status: "failed",
                            completedAt: event.timestamp,
                            error,
                        });
                    }
                    return { nodeStates: newNodeStates };

                case "execution_completed":
                    return {
                        status: "completed" as const,
                        completedAt: event.timestamp,
                        overallConfidence: overallConfidence ?? null,
                    };

                case "execution_failed":
                    return {
                        status: "failed" as const,
                        completedAt: event.timestamp,
                        error: error ?? "Execution failed",
                    };

                default:
                    return {};
            }
        });
    },

    stopExecution: () => {
        set({
            status: "idle",
            completedAt: new Date().toISOString(),
        });
    },

    resetExecution: () => {
        set(initialState);
    },

    getNodeState: (nodeId: string) => {
        return get().nodeStates.get(nodeId);
    },

    isNodeRunning: (nodeId: string) => {
        const nodeState = get().nodeStates.get(nodeId);
        return nodeState?.status === "running";
    },

    isExecuting: () => {
        return get().status === "running";
    },
}));
