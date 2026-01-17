/**
 * AI Assistant Store - Zustand state management for AI chat modal
 *
 * Manages the AI assistant modal state, chat history, streaming state, and test execution.
 * Integrates with canvasStore for applying canvas patches from AI responses.
 * Wires SSE streaming from AiPlaybookService to update store state.
 * Supports test execution modes: Mock, Quick, and Production.
 *
 * @version 2.1.0
 */

import { create } from 'zustand';
import { useCanvasStore, type PlaybookNode, type PlaybookNodeData } from './canvasStore';
import {
  AiPlaybookService,
  type AiPlaybookServiceConfig,
  type BuildPlaybookCanvasRequest,
  type ThinkingEventData,
  type MessageEventData,
  type CanvasPatchEventData,
  type ClarificationEventData,
  type PlanPreviewEventData,
  type ErrorEventData,
  type DoneEventData,
  type DataverseOperationEventData,
} from '../services/AiPlaybookService';

// ============================================================================
// Types
// ============================================================================

/**
 * Message role in conversation.
 */
export type ChatMessageRole = 'user' | 'assistant' | 'system';

/**
 * Canvas operation performed by the AI assistant.
 * Used for displaying feedback about what changes were made.
 */
export interface CanvasOperation {
  type: 'add_node' | 'remove_node' | 'update_node' | 'add_edge' | 'remove_edge';
  nodeId?: string;
  edgeId?: string;
  description?: string;
}

/**
 * A chat message in the conversation history.
 */
export interface ChatMessage {
  id: string;
  role: ChatMessageRole;
  content: string;
  timestamp: Date;
  canvasOperations?: CanvasOperation[];
  isStreaming?: boolean;
}

/**
 * Canvas patch operation type (matches server CanvasPatchOperation enum).
 */
export type CanvasPatchOperation =
  | 'AddNode'
  | 'RemoveNode'
  | 'UpdateNode'
  | 'AddEdge'
  | 'RemoveEdge'
  | 'ConfigureNode'
  | 'LinkScope';

/**
 * Position on the canvas.
 */
export interface NodePosition {
  x: number;
  y: number;
}

/**
 * Canvas node for patches (simplified from server model).
 */
export interface CanvasPatchNode {
  id: string;
  type: string;
  label?: string;
  position?: NodePosition;
  config?: Record<string, unknown>;
  actionId?: string;
  outputVariable?: string;
  skillIds?: string[];
  knowledgeIds?: string[];
  toolId?: string;
  modelDeploymentId?: string;
}

/**
 * Canvas edge for patches.
 */
export interface CanvasPatchEdge {
  id: string;
  sourceId: string;
  targetId: string;
  sourceHandle?: string;
  targetHandle?: string;
  type?: string;
  animated?: boolean;
}

/**
 * A patch to apply to the canvas from AI assistant responses.
 * Supports both individual operations (for streaming) and batch operations.
 */
export interface CanvasPatch {
  // Individual operation mode (for SSE streaming)
  operation?: CanvasPatchOperation;
  nodeId?: string;
  edgeId?: string;
  node?: CanvasPatchNode;
  edge?: CanvasPatchEdge;
  config?: Record<string, unknown>;

  // Batch operation mode
  addNodes?: CanvasPatchNode[];
  removeNodeIds?: string[];
  updateNodes?: CanvasPatchNode[];
  addEdges?: CanvasPatchEdge[];
  removeEdgeIds?: string[];
}

/**
 * SSE event types from the server.
 */
export type SseEventType =
  | 'thinking'
  | 'dataverse_operation'
  | 'canvas_patch'
  | 'message'
  | 'done'
  | 'error'
  | 'clarification'
  | 'plan_preview';

/**
 * Streaming state for tracking ongoing operations.
 */
export interface StreamingState {
  isActive: boolean;
  currentStep?: string;
  operationCount: number;
}

/**
 * Test execution mode (matches server TestMode enum).
 */
export type TestMode = 'mock' | 'quick' | 'production';

/**
 * Test options selected by the user.
 */
export interface TestOptions {
  mode: TestMode;
  documentFile?: File;
  documentId?: string;
  driveId?: string;
  itemId?: string;
}

/**
 * Test node execution progress.
 */
export interface TestNodeProgress {
  nodeId: string;
  label: string;
  status: 'pending' | 'running' | 'completed' | 'failed' | 'skipped';
  output?: Record<string, unknown>;
  durationMs?: number;
  error?: string;
}

/**
 * Test execution state for tracking test progress.
 */
export interface TestExecutionState {
  isActive: boolean;
  mode: TestMode | null;
  currentNodeId: string | null;
  nodesProgress: TestNodeProgress[];
  totalDurationMs: number;
  analysisId: string | null;
  reportUrl: string | null;
  error: string | null;
}

// ============================================================================
// Store State Interface
// ============================================================================

interface AiAssistantState {
  // State
  isOpen: boolean;
  messages: ChatMessage[];
  isStreaming: boolean;
  streamingState: StreamingState;
  currentPlaybookId: string | null;
  sessionId: string | null;
  error: string | null;

  // Test execution state
  isTestDialogOpen: boolean;
  testExecution: TestExecutionState;

  // Modal actions
  openModal: () => void;
  closeModal: () => void;
  toggleModal: () => void;

  // Session actions
  setPlaybookId: (playbookId: string | null) => void;
  startSession: (playbookId: string) => void;
  endSession: () => void;

  // Message actions
  addUserMessage: (content: string) => string;
  addAssistantMessage: (content: string, canvasOperations?: CanvasOperation[]) => string;
  addSystemMessage: (content: string) => string;
  updateMessage: (messageId: string, updates: Partial<ChatMessage>) => void;
  appendToMessage: (messageId: string, content: string) => void;
  clearHistory: () => void;

  // Streaming actions
  setStreaming: (isStreaming: boolean) => void;
  updateStreamingState: (state: Partial<StreamingState>) => void;

  // Canvas patch actions
  applyCanvasPatch: (patch: CanvasPatch) => CanvasOperation[];

  // Error handling
  setError: (error: string | null) => void;

  // API Integration
  sendMessage: (
    message: string,
    serviceConfig: AiPlaybookServiceConfig
  ) => Promise<void>;
  abortStream: () => void;

  // Test execution actions
  openTestDialog: () => void;
  closeTestDialog: () => void;
  startTestExecution: (options: TestOptions) => void;
  updateTestNodeProgress: (nodeId: string, progress: Partial<TestNodeProgress>) => void;
  completeTestExecution: (result: { analysisId?: string; reportUrl?: string; totalDurationMs: number }) => void;
  failTestExecution: (error: string) => void;
  resetTestExecution: () => void;

  // Reset
  reset: () => void;
}

// ============================================================================
// Service Instance (Module-level singleton)
// ============================================================================

let serviceInstance: AiPlaybookService | null = null;

/**
 * Get or create the AiPlaybookService instance.
 */
const getService = (config: AiPlaybookServiceConfig): AiPlaybookService => {
  if (!serviceInstance) {
    serviceInstance = new AiPlaybookService(config);
  } else {
    // Update access token if it changed
    serviceInstance.setAccessToken(config.accessToken);
  }
  return serviceInstance;
};

// ============================================================================
// Helpers
// ============================================================================

/**
 * Generate a unique ID for messages.
 */
const generateMessageId = () =>
  `msg_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

/**
 * Generate a unique session ID.
 */
const generateSessionId = () =>
  `session_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

/**
 * Convert a CanvasPatchNode to a PlaybookNode for the canvas store.
 */
const patchNodeToPlaybookNode = (
  patchNode: CanvasPatchNode
): PlaybookNode => ({
  id: patchNode.id,
  type: patchNode.type,
  position: patchNode.position ?? { x: 100, y: 100 },
  data: {
    label: patchNode.label ?? patchNode.type,
    type: patchNode.type as PlaybookNodeData['type'],
    actionId: patchNode.actionId,
    outputVariable: patchNode.outputVariable,
    skillIds: patchNode.skillIds,
    knowledgeIds: patchNode.knowledgeIds,
    toolId: patchNode.toolId,
    modelDeploymentId: patchNode.modelDeploymentId,
    ...patchNode.config,
  } as PlaybookNodeData,
});

// ============================================================================
// Initial State
// ============================================================================

const initialTestExecutionState: TestExecutionState = {
  isActive: false,
  mode: null,
  currentNodeId: null,
  nodesProgress: [],
  totalDurationMs: 0,
  analysisId: null,
  reportUrl: null,
  error: null,
};

const initialState = {
  isOpen: false,
  messages: [] as ChatMessage[],
  isStreaming: false,
  streamingState: {
    isActive: false,
    operationCount: 0,
  } as StreamingState,
  currentPlaybookId: null as string | null,
  sessionId: null as string | null,
  error: null as string | null,
  isTestDialogOpen: false,
  testExecution: initialTestExecutionState,
};

// ============================================================================
// Store
// ============================================================================

/**
 * Zustand store for AI assistant modal state management.
 * Handles chat history, streaming state, and canvas patch application.
 */
export const useAiAssistantStore = create<AiAssistantState>((set, get) => ({
  ...initialState,

  // Modal actions
  openModal: () => set({ isOpen: true }),
  closeModal: () => set({ isOpen: false }),
  toggleModal: () => set((state) => ({ isOpen: !state.isOpen })),

  // Session actions
  setPlaybookId: (playbookId) => set({ currentPlaybookId: playbookId }),

  startSession: (playbookId) =>
    set({
      currentPlaybookId: playbookId,
      sessionId: generateSessionId(),
      messages: [],
      error: null,
    }),

  endSession: () =>
    set({
      sessionId: null,
      isStreaming: false,
      streamingState: { isActive: false, operationCount: 0 },
    }),

  // Message actions
  addUserMessage: (content) => {
    const id = generateMessageId();
    const message: ChatMessage = {
      id,
      role: 'user',
      content,
      timestamp: new Date(),
    };
    set((state) => ({ messages: [...state.messages, message] }));
    return id;
  },

  addAssistantMessage: (content, canvasOperations) => {
    const id = generateMessageId();
    const message: ChatMessage = {
      id,
      role: 'assistant',
      content,
      timestamp: new Date(),
      canvasOperations,
    };
    set((state) => ({ messages: [...state.messages, message] }));
    return id;
  },

  addSystemMessage: (content) => {
    const id = generateMessageId();
    const message: ChatMessage = {
      id,
      role: 'system',
      content,
      timestamp: new Date(),
    };
    set((state) => ({ messages: [...state.messages, message] }));
    return id;
  },

  updateMessage: (messageId, updates) =>
    set((state) => ({
      messages: state.messages.map((msg) =>
        msg.id === messageId ? { ...msg, ...updates } : msg
      ),
    })),

  appendToMessage: (messageId, content) =>
    set((state) => ({
      messages: state.messages.map((msg) =>
        msg.id === messageId ? { ...msg, content: msg.content + content } : msg
      ),
    })),

  clearHistory: () =>
    set({
      messages: [],
      error: null,
    }),

  // Streaming actions
  setStreaming: (isStreaming) =>
    set({
      isStreaming,
      streamingState: isStreaming
        ? { isActive: true, operationCount: 0 }
        : { isActive: false, operationCount: get().streamingState.operationCount },
    }),

  updateStreamingState: (state) =>
    set((current) => ({
      streamingState: { ...current.streamingState, ...state },
    })),

  // Canvas patch actions
  applyCanvasPatch: (patch) => {
    const operations: CanvasOperation[] = [];
    const canvasStore = useCanvasStore.getState();

    // Handle individual operations (streaming mode)
    if (patch.operation) {
      switch (patch.operation) {
        case 'AddNode':
          if (patch.node) {
            canvasStore.addNode(patchNodeToPlaybookNode(patch.node));
            operations.push({
              type: 'add_node',
              nodeId: patch.node.id,
              description: `Added ${patch.node.type} node`,
            });
          }
          break;

        case 'RemoveNode':
          if (patch.nodeId) {
            canvasStore.removeNode(patch.nodeId);
            operations.push({
              type: 'remove_node',
              nodeId: patch.nodeId,
              description: `Removed node ${patch.nodeId}`,
            });
          }
          break;

        case 'UpdateNode':
        case 'ConfigureNode':
          if (patch.nodeId && (patch.node || patch.config)) {
            const updates: Partial<PlaybookNodeData> = {
              ...patch.node?.config,
              ...patch.config,
            };
            if (patch.node?.label) {
              updates.label = patch.node.label;
            }
            canvasStore.updateNode(patch.nodeId, updates);
            operations.push({
              type: 'update_node',
              nodeId: patch.nodeId,
              description: `Updated node ${patch.nodeId}`,
            });
          }
          break;

        case 'AddEdge':
          if (patch.edge) {
            const edgeToAdd = {
              id: patch.edge.id,
              source: patch.edge.sourceId,
              target: patch.edge.targetId,
              sourceHandle: patch.edge.sourceHandle ?? undefined,
              targetHandle: patch.edge.targetHandle ?? undefined,
              type: patch.edge.type ?? 'smoothstep',
              animated: patch.edge.animated ?? true,
            };
            canvasStore.setEdges([...canvasStore.edges, edgeToAdd]);
            operations.push({
              type: 'add_edge',
              edgeId: patch.edge.id,
              description: `Connected ${patch.edge.sourceId} to ${patch.edge.targetId}`,
            });
          }
          break;

        case 'RemoveEdge':
          if (patch.edgeId) {
            canvasStore.setEdges(
              canvasStore.edges.filter((e) => e.id !== patch.edgeId)
            );
            operations.push({
              type: 'remove_edge',
              edgeId: patch.edgeId,
              description: `Removed edge ${patch.edgeId}`,
            });
          }
          break;
      }
    }

    // Handle batch operations
    if (patch.addNodes) {
      for (const node of patch.addNodes) {
        canvasStore.addNode(patchNodeToPlaybookNode(node));
        operations.push({
          type: 'add_node',
          nodeId: node.id,
          description: `Added ${node.type} node`,
        });
      }
    }

    if (patch.removeNodeIds) {
      for (const nodeId of patch.removeNodeIds) {
        canvasStore.removeNode(nodeId);
        operations.push({
          type: 'remove_node',
          nodeId,
          description: `Removed node ${nodeId}`,
        });
      }
    }

    if (patch.updateNodes) {
      for (const node of patch.updateNodes) {
        const updates: Partial<PlaybookNodeData> = {
          label: node.label,
          ...node.config,
        };
        canvasStore.updateNode(node.id, updates);
        operations.push({
          type: 'update_node',
          nodeId: node.id,
          description: `Updated node ${node.id}`,
        });
      }
    }

    if (patch.addEdges) {
      const newEdges = patch.addEdges.map((edge) => ({
        id: edge.id,
        source: edge.sourceId,
        target: edge.targetId,
        sourceHandle: edge.sourceHandle ?? undefined,
        targetHandle: edge.targetHandle ?? undefined,
        type: edge.type ?? 'smoothstep',
        animated: edge.animated ?? true,
      }));
      canvasStore.setEdges([...canvasStore.edges, ...newEdges]);
      for (const edge of patch.addEdges) {
        operations.push({
          type: 'add_edge',
          edgeId: edge.id,
          description: `Connected ${edge.sourceId} to ${edge.targetId}`,
        });
      }
    }

    if (patch.removeEdgeIds) {
      canvasStore.setEdges(
        canvasStore.edges.filter((e) => !patch.removeEdgeIds!.includes(e.id))
      );
      for (const edgeId of patch.removeEdgeIds) {
        operations.push({
          type: 'remove_edge',
          edgeId,
          description: `Removed edge ${edgeId}`,
        });
      }
    }

    // Update streaming operation count
    if (operations.length > 0) {
      set((state) => ({
        streamingState: {
          ...state.streamingState,
          operationCount: state.streamingState.operationCount + operations.length,
        },
      }));
    }

    return operations;
  },

  // Error handling
  setError: (error) => set({ error }),

  // API Integration
  sendMessage: async (message, serviceConfig) => {
    const state = get();

    // Validate playbook is set
    if (!state.currentPlaybookId) {
      set({ error: 'No playbook selected. Please open a playbook first.' });
      return;
    }

    // Add user message to chat
    const userMessageId = get().addUserMessage(message);

    // Create streaming assistant message placeholder
    const assistantMessageId = generateMessageId();
    const assistantMessage: ChatMessage = {
      id: assistantMessageId,
      role: 'assistant',
      content: '',
      timestamp: new Date(),
      isStreaming: true,
      canvasOperations: [],
    };
    set((s) => ({
      messages: [...s.messages, assistantMessage],
      isStreaming: true,
      streamingState: { isActive: true, operationCount: 0 },
      error: null,
    }));

    // Build request from current state
    const canvasStore = useCanvasStore.getState();
    const request: BuildPlaybookCanvasRequest = {
      playbookId: state.currentPlaybookId,
      currentCanvas: {
        nodes: canvasStore.nodes,
        edges: canvasStore.edges,
      },
      message,
      conversationHistory: state.messages.map((m) => ({
        role: m.role,
        content: m.content,
      })),
      sessionId: state.sessionId ?? undefined,
    };

    // Get service instance
    const service = getService(serviceConfig);

    try {
      await service.buildPlaybookCanvas(request, {
        // Handle thinking events - update streaming state
        onThinking: (data: ThinkingEventData) => {
          set((s) => ({
            streamingState: {
              ...s.streamingState,
              currentStep: data.step ?? data.message,
            },
          }));
        },

        // Handle message events - append to assistant message
        onMessage: (data: MessageEventData) => {
          if (data.isPartial) {
            // Append to existing message for partial content
            get().appendToMessage(assistantMessageId, data.content);
          } else {
            // Replace content for complete messages
            get().updateMessage(assistantMessageId, { content: data.content });
          }
        },

        // Handle canvas patch events - apply to canvas and track operations
        onCanvasPatch: (data: CanvasPatchEventData) => {
          const operations = get().applyCanvasPatch(data);

          // Add operations to assistant message
          set((s) => ({
            messages: s.messages.map((m) =>
              m.id === assistantMessageId
                ? {
                    ...m,
                    canvasOperations: [
                      ...(m.canvasOperations ?? []),
                      ...operations,
                    ],
                  }
                : m
            ),
          }));
        },

        // Handle dataverse operation events - update streaming state
        onDataverseOperation: (data: DataverseOperationEventData) => {
          set((s) => ({
            streamingState: {
              ...s.streamingState,
              currentStep: `${data.operation} ${data.entity}${data.id ? ` (${data.id})` : ''}`,
            },
          }));
        },

        // Handle clarification events - add as system message
        onClarification: (data: ClarificationEventData) => {
          let clarificationContent = data.question;
          if (data.options && data.options.length > 0) {
            clarificationContent += '\n\nOptions:\n' + data.options.map((o, i) => `${i + 1}. ${o}`).join('\n');
          }
          if (data.context) {
            clarificationContent += '\n\n' + data.context;
          }
          get().updateMessage(assistantMessageId, {
            content: clarificationContent,
          });
        },

        // Handle plan preview events - show as formatted message
        onPlanPreview: (data: PlanPreviewEventData) => {
          let planContent = `**Plan Preview**: ${data.summary}\n\n`;
          planContent += data.steps
            .map((s) => `${s.step}. **${s.operation}**: ${s.description}`)
            .join('\n');
          planContent += `\n\nEstimated nodes: ${data.estimatedNodes}`;
          get().updateMessage(assistantMessageId, { content: planContent });
        },

        // Handle error events - show error in message and set error state
        onError: (data: ErrorEventData) => {
          get().updateMessage(assistantMessageId, {
            content: `Error: ${data.message}${data.details ? '\n\n' + data.details : ''}`,
            isStreaming: false,
          });
          set({
            isStreaming: false,
            streamingState: { isActive: false, operationCount: get().streamingState.operationCount },
            error: data.message,
          });
        },

        // Handle done events - mark streaming complete
        onDone: (data: DoneEventData) => {
          const finalContent = get().messages.find((m) => m.id === assistantMessageId)?.content ?? '';
          const summaryAddition = data.summary ? (finalContent ? '\n\n' : '') + data.summary : '';

          get().updateMessage(assistantMessageId, {
            content: finalContent + summaryAddition,
            isStreaming: false,
          });
          set({
            isStreaming: false,
            streamingState: {
              isActive: false,
              operationCount: get().streamingState.operationCount,
            },
          });
        },

        // Handle connection errors
        onConnectionError: (error: Error) => {
          get().updateMessage(assistantMessageId, {
            content: `Connection error: ${error.message}`,
            isStreaming: false,
          });
          set({
            isStreaming: false,
            streamingState: { isActive: false, operationCount: 0 },
            error: `Connection error: ${error.message}`,
          });
        },
      });
    } catch (error) {
      // Handle unexpected errors
      const errorMessage = error instanceof Error ? error.message : 'Unknown error occurred';
      get().updateMessage(assistantMessageId, {
        content: `Error: ${errorMessage}`,
        isStreaming: false,
      });
      set({
        isStreaming: false,
        streamingState: { isActive: false, operationCount: 0 },
        error: errorMessage,
      });
    }
  },

  abortStream: () => {
    if (serviceInstance) {
      serviceInstance.abort();
    }
    set({
      isStreaming: false,
      streamingState: { isActive: false, operationCount: get().streamingState.operationCount },
    });
  },

  // Test execution actions
  openTestDialog: () => set({ isTestDialogOpen: true }),

  closeTestDialog: () => set({ isTestDialogOpen: false }),

  startTestExecution: (options: TestOptions) =>
    set({
      isTestDialogOpen: false,
      testExecution: {
        isActive: true,
        mode: options.mode,
        currentNodeId: null,
        nodesProgress: [],
        totalDurationMs: 0,
        analysisId: null,
        reportUrl: null,
        error: null,
      },
    }),

  updateTestNodeProgress: (nodeId: string, progress: Partial<TestNodeProgress>) =>
    set((state) => {
      const existingIndex = state.testExecution.nodesProgress.findIndex(
        (n) => n.nodeId === nodeId
      );

      let updatedProgress: TestNodeProgress[];
      if (existingIndex >= 0) {
        // Update existing node progress
        updatedProgress = state.testExecution.nodesProgress.map((n, i) =>
          i === existingIndex ? { ...n, ...progress } : n
        );
      } else {
        // Add new node progress
        updatedProgress = [
          ...state.testExecution.nodesProgress,
          {
            nodeId,
            label: progress.label ?? nodeId,
            status: progress.status ?? 'pending',
            ...progress,
          },
        ];
      }

      return {
        testExecution: {
          ...state.testExecution,
          currentNodeId: progress.status === 'running' ? nodeId : state.testExecution.currentNodeId,
          nodesProgress: updatedProgress,
        },
      };
    }),

  completeTestExecution: (result) =>
    set((state) => ({
      testExecution: {
        ...state.testExecution,
        isActive: false,
        currentNodeId: null,
        analysisId: result.analysisId ?? null,
        reportUrl: result.reportUrl ?? null,
        totalDurationMs: result.totalDurationMs,
      },
    })),

  failTestExecution: (error: string) =>
    set((state) => ({
      testExecution: {
        ...state.testExecution,
        isActive: false,
        currentNodeId: null,
        error,
      },
    })),

  resetTestExecution: () =>
    set({
      testExecution: initialTestExecutionState,
    }),

  // Reset
  reset: () => set(initialState),
}));
