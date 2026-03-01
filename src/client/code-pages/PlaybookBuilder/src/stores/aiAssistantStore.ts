/**
 * AI Assistant Store - Zustand v5 state management for AI chat modal
 *
 * Manages the AI assistant modal state, chat history, streaming state, and test execution.
 * Integrates with canvasStore for applying canvas patches from AI responses.
 * Wires SSE streaming from AiPlaybookService to update store state.
 * Supports test execution modes: Mock, Quick, and Production.
 *
 * Migrated from R4 PCF: uses AuthService.getAccessToken() instead of PCF context.
 *
 * @version 2.0.0 (Code Page migration)
 */

import { create } from "zustand";
import { useCanvasStore } from "./canvasStore";
import type { PlaybookNode, PlaybookNodeData } from "../types/canvas";
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
    type CanvasPatch,
    type CanvasPatchNode,
    type CanvasPatchOperation,
    type SseEventType,
} from "../services/aiPlaybookService";

// ============================================================================
// Re-export types used by UI components
// ============================================================================

export type { CanvasPatch, CanvasPatchNode, CanvasPatchOperation, SseEventType };

// ============================================================================
// Types
// ============================================================================

/** Message role in conversation. */
export type ChatMessageRole = "user" | "assistant" | "system";

/**
 * Canvas operation performed by the AI assistant.
 * Used for displaying feedback about what changes were made.
 */
export interface CanvasOperation {
    type: "add_node" | "remove_node" | "update_node" | "add_edge" | "remove_edge";
    nodeId?: string;
    edgeId?: string;
    description?: string;
}

/**
 * Clarification data for messages that need user input.
 * Used to display interactive option selection UI.
 */
export interface ClarificationData {
    question: string;
    options?: string[];
    context?: string;
    sessionId?: string;
    responded?: boolean;
    selectedOption?: number | "other";
    freeTextResponse?: string;
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
    clarification?: ClarificationData;
}

/** Streaming state for tracking ongoing operations. */
export interface StreamingState {
    isActive: boolean;
    currentStep?: string;
    operationCount: number;
}

/** Test execution mode (matches server TestMode enum). */
export type TestMode = "mock" | "quick" | "production";

/** Test options selected by the user. */
export interface TestOptions {
    mode: TestMode;
    documentFile?: File;
    documentId?: string;
    driveId?: string;
    itemId?: string;
}

/** Test node execution progress. */
export interface TestNodeProgress {
    nodeId: string;
    label: string;
    status: "pending" | "running" | "completed" | "failed" | "skipped";
    output?: Record<string, unknown>;
    durationMs?: number;
    error?: string;
}

/** Test execution state for tracking test progress. */
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

/** AI model selection options. */
export type AiModelSelection = "gpt-4o" | "gpt-4o-mini";

/** Model option with display information. */
export interface AiModelOption {
    id: AiModelSelection;
    name: string;
    description: string;
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
    serviceConfig: AiPlaybookServiceConfig | null;

    // Model selection state
    modelSelection: AiModelSelection;
    showAdvancedOptions: boolean;

    // Test execution state
    isTestDialogOpen: boolean;
    testExecution: TestExecutionState;

    // Modal actions
    openModal: () => void;
    closeModal: () => void;
    toggleModal: () => void;

    // Session actions
    setPlaybookId: (playbookId: string | null) => void;
    setServiceConfig: (config: AiPlaybookServiceConfig) => void;
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
        serviceConfig?: AiPlaybookServiceConfig
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

    // Model selection actions
    setModelSelection: (model: AiModelSelection) => void;
    toggleAdvancedOptions: () => void;
    setShowAdvancedOptions: (show: boolean) => void;

    // Clarification actions
    respondToClarification: (
        messageId: string,
        response: { selectedOption: number | "other"; freeText?: string }
    ) => void;

    // Reset
    reset: () => void;
}

// ============================================================================
// Service Instance (Module-level singleton)
// ============================================================================

let serviceInstance: AiPlaybookService | null = null;

const getService = (config: AiPlaybookServiceConfig): AiPlaybookService => {
    if (!serviceInstance) {
        serviceInstance = new AiPlaybookService(config);
    }
    return serviceInstance;
};

// ============================================================================
// Helpers
// ============================================================================

const generateMessageId = () =>
    `msg_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

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
        type: patchNode.type as PlaybookNodeData["type"],
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
// Constants
// ============================================================================

/** Available AI model options for selection. */
export const AI_MODEL_OPTIONS: AiModelOption[] = [
    {
        id: "gpt-4o",
        name: "GPT-4o",
        description: "Powerful",
    },
    {
        id: "gpt-4o-mini",
        name: "GPT-4o-mini",
        description: "Fast",
    },
];

/** Default AI model selection. */
export const DEFAULT_MODEL_SELECTION: AiModelSelection = "gpt-4o-mini";

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
    serviceConfig: null as AiPlaybookServiceConfig | null,
    modelSelection: DEFAULT_MODEL_SELECTION as AiModelSelection,
    showAdvancedOptions: false,
    isTestDialogOpen: false,
    testExecution: initialTestExecutionState,
};

// ============================================================================
// Store
// ============================================================================

/**
 * Zustand v5 store for AI assistant modal state management.
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

    setServiceConfig: (config) => set({ serviceConfig: config }),

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
            role: "user",
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
            role: "assistant",
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
            role: "system",
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
                case "AddNode":
                    if (patch.node) {
                        canvasStore.addNode(patchNodeToPlaybookNode(patch.node));
                        operations.push({
                            type: "add_node",
                            nodeId: patch.node.id,
                            description: `Added ${patch.node.type} node`,
                        });
                    }
                    break;

                case "RemoveNode":
                    if (patch.nodeId) {
                        canvasStore.removeNode(patch.nodeId);
                        operations.push({
                            type: "remove_node",
                            nodeId: patch.nodeId,
                            description: `Removed node ${patch.nodeId}`,
                        });
                    }
                    break;

                case "UpdateNode":
                case "ConfigureNode":
                    if (patch.nodeId && (patch.node || patch.config)) {
                        const updates: Partial<PlaybookNodeData> = {
                            ...patch.node?.config,
                            ...patch.config,
                        };
                        if (patch.node?.label) {
                            updates.label = patch.node.label;
                        }
                        canvasStore.updateNodeData(patch.nodeId, updates);
                        operations.push({
                            type: "update_node",
                            nodeId: patch.nodeId,
                            description: `Updated node ${patch.nodeId}`,
                        });
                    }
                    break;

                case "AddEdge":
                    if (patch.edge) {
                        const edgeToAdd = {
                            id: patch.edge.id,
                            source: patch.edge.sourceId,
                            target: patch.edge.targetId,
                            sourceHandle: patch.edge.sourceHandle ?? undefined,
                            targetHandle: patch.edge.targetHandle ?? undefined,
                            type: patch.edge.type ?? "smoothstep",
                            animated: patch.edge.animated ?? true,
                        };
                        canvasStore.setEdges([...canvasStore.edges, edgeToAdd]);
                        operations.push({
                            type: "add_edge",
                            edgeId: patch.edge.id,
                            description: `Connected ${patch.edge.sourceId} to ${patch.edge.targetId}`,
                        });
                    }
                    break;

                case "RemoveEdge":
                    if (patch.edgeId) {
                        canvasStore.setEdges(
                            canvasStore.edges.filter((e) => e.id !== patch.edgeId)
                        );
                        operations.push({
                            type: "remove_edge",
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
                    type: "add_node",
                    nodeId: node.id,
                    description: `Added ${node.type} node`,
                });
            }
        }

        if (patch.removeNodeIds) {
            for (const nodeId of patch.removeNodeIds) {
                canvasStore.removeNode(nodeId);
                operations.push({
                    type: "remove_node",
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
                canvasStore.updateNodeData(node.id, updates);
                operations.push({
                    type: "update_node",
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
                type: edge.type ?? "smoothstep",
                animated: edge.animated ?? true,
            }));
            canvasStore.setEdges([...canvasStore.edges, ...newEdges]);
            for (const edge of patch.addEdges) {
                operations.push({
                    type: "add_edge",
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
                    type: "remove_edge",
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

        // Use provided config or stored config
        const config = serviceConfig ?? state.serviceConfig;

        // Validate playbook is set
        if (!state.currentPlaybookId) {
            set({ error: "No playbook selected. Please open a playbook first." });
            return;
        }

        // Validate service config is available
        if (!config || !config.apiBaseUrl) {
            set({ error: "AI service not configured. Set the API Base URL." });
            return;
        }

        // Add user message to chat
        get().addUserMessage(message);

        // Create streaming assistant message placeholder
        const assistantMessageId = generateMessageId();
        const assistantMessage: ChatMessage = {
            id: assistantMessageId,
            role: "assistant",
            content: "",
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
            message,
            canvasState: {
                nodes: canvasStore.nodes.map((node) => ({
                    id: node.id,
                    type: node.type,
                    position: node.position,
                    label: node.data?.label,
                    config: node.data,
                })),
                edges: canvasStore.edges.map((edge) => ({
                    id: edge.id,
                    sourceId: edge.source,
                    targetId: edge.target,
                    sourceHandle: edge.sourceHandle ?? undefined,
                    targetHandle: edge.targetHandle ?? undefined,
                    edgeType: edge.type,
                    animated: edge.animated,
                })),
            },
            playbookId: state.currentPlaybookId ?? undefined,
            sessionId: state.sessionId ?? undefined,
            chatHistory: state.messages.map((m) => ({
                role: m.role,
                content: m.content,
            })),
            modelId: state.modelSelection,
        };

        // Get service instance
        const service = getService(config);

        try {
            await service.buildPlaybookCanvas(request, {
                onThinking: (data: ThinkingEventData) => {
                    set((s) => ({
                        streamingState: {
                            ...s.streamingState,
                            currentStep: data.step ?? data.message,
                        },
                    }));
                },

                onMessage: (data: MessageEventData) => {
                    if (data.isPartial) {
                        get().appendToMessage(assistantMessageId, data.content);
                    } else {
                        get().updateMessage(assistantMessageId, { content: data.content });
                    }
                },

                onCanvasPatch: (data: CanvasPatchEventData) => {
                    const operations = get().applyCanvasPatch(data.patch);

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

                onDataverseOperation: (data: DataverseOperationEventData) => {
                    set((s) => ({
                        streamingState: {
                            ...s.streamingState,
                            currentStep: `${data.operation} ${data.entity}${data.id ? ` (${data.id})` : ""}`,
                        },
                    }));
                },

                onClarification: (data: ClarificationEventData) => {
                    get().updateMessage(assistantMessageId, {
                        content: data.question,
                        clarification: {
                            question: data.question,
                            options: data.options,
                            context: data.context,
                            sessionId: state.sessionId ?? undefined,
                            responded: false,
                        },
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

                onPlanPreview: (data: PlanPreviewEventData) => {
                    let planContent = `**Plan Preview**: ${data.summary}\n\n`;
                    planContent += data.steps
                        .map((s) => `${s.step}. **${s.operation}**: ${s.description}`)
                        .join("\n");
                    planContent += `\n\nEstimated nodes: ${data.estimatedNodes}`;
                    get().updateMessage(assistantMessageId, { content: planContent });
                },

                onError: (data: ErrorEventData) => {
                    get().updateMessage(assistantMessageId, {
                        content: `Error: ${data.message}${data.details ? "\n\n" + data.details : ""}`,
                        isStreaming: false,
                    });
                    set({
                        isStreaming: false,
                        streamingState: { isActive: false, operationCount: get().streamingState.operationCount },
                        error: data.message,
                    });
                },

                onDone: (data: DoneEventData) => {
                    const finalContent = get().messages.find((m) => m.id === assistantMessageId)?.content ?? "";
                    const summaryAddition = data.summary ? (finalContent ? "\n\n" : "") + data.summary : "";

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
            const errorMessage = error instanceof Error ? error.message : "Unknown error occurred";
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
                updatedProgress = state.testExecution.nodesProgress.map((n, i) =>
                    i === existingIndex ? { ...n, ...progress } : n
                );
            } else {
                updatedProgress = [
                    ...state.testExecution.nodesProgress,
                    {
                        nodeId,
                        label: progress.label ?? nodeId,
                        status: progress.status ?? "pending",
                        ...progress,
                    },
                ];
            }

            return {
                testExecution: {
                    ...state.testExecution,
                    currentNodeId: progress.status === "running" ? nodeId : state.testExecution.currentNodeId,
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

    // Model selection actions
    setModelSelection: (model: AiModelSelection) =>
        set({ modelSelection: model }),

    toggleAdvancedOptions: () =>
        set((state) => ({ showAdvancedOptions: !state.showAdvancedOptions })),

    setShowAdvancedOptions: (show: boolean) =>
        set({ showAdvancedOptions: show }),

    // Clarification actions
    respondToClarification: (messageId, response) => {
        const state = get();

        const message = state.messages.find((m) => m.id === messageId);
        if (!message?.clarification) {
            return;
        }

        let responseText: string;
        if (response.selectedOption === "other") {
            responseText = response.freeText ?? "";
        } else {
            const options = message.clarification.options ?? [];
            responseText = options[response.selectedOption] ?? `Option ${response.selectedOption + 1}`;
        }

        // Update the clarification as responded
        set((s) => ({
            messages: s.messages.map((m) =>
                m.id === messageId
                    ? {
                          ...m,
                          clarification: {
                              ...m.clarification!,
                              responded: true,
                              selectedOption: response.selectedOption,
                              freeTextResponse: response.freeText,
                          },
                      }
                    : m
            ),
        }));

        // Send the response as a new user message
        get().sendMessage(responseText);
    },

    // Reset
    reset: () => set(initialState),
}));
