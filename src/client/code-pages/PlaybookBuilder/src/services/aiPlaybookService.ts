/**
 * AI Playbook Service - API client for AI playbook canvas building
 *
 * Handles communication with the /api/ai/playbook-builder/process endpoint.
 * Uses fetch with ReadableStream for SSE streaming (POST request).
 *
 * Migrated from R4 PCF: Token acquisition via AuthService.getAccessToken()
 * instead of PCF context.
 *
 * SSE Event Types:
 * - thinking: AI is processing
 * - dataverse_operation: Dataverse record created/updated
 * - canvas_patch: Canvas changes to apply
 * - message: AI response text
 * - clarification: AI needs more info
 * - plan_preview: Build plan for confirmation
 * - done: Stream complete
 * - error: Error occurred
 *
 * @version 2.0.0 (Code Page migration)
 */

import { getAccessToken } from "./authService";

// ============================================================================
// SSE Event Types
// ============================================================================

export type SseEventType =
    | "thinking"
    | "dataverse_operation"
    | "canvas_patch"
    | "message"
    | "done"
    | "error"
    | "clarification"
    | "plan_preview";

// ============================================================================
// Canvas Patch Types
// ============================================================================

export type CanvasPatchOperation =
    | "AddNode"
    | "RemoveNode"
    | "UpdateNode"
    | "AddEdge"
    | "RemoveEdge"
    | "ConfigureNode"
    | "LinkScope";

export interface NodePosition {
    x: number;
    y: number;
}

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

export interface CanvasPatchEdge {
    id: string;
    sourceId: string;
    targetId: string;
    sourceHandle?: string;
    targetHandle?: string;
    type?: string;
    animated?: boolean;
}

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

// ============================================================================
// API Request Types (matches BFF API's BuilderRequest model)
// ============================================================================

export interface ApiNodePosition {
    x: number;
    y: number;
}

export interface ApiCanvasNode {
    id: string;
    type?: string;
    position: ApiNodePosition;
    label?: string;
    config?: Record<string, unknown>;
}

export interface ApiCanvasEdge {
    id: string;
    sourceId: string;
    targetId: string;
    sourceHandle?: string | null;
    targetHandle?: string | null;
    edgeType?: string;
    animated?: boolean;
}

export interface CanvasState {
    nodes: ApiCanvasNode[];
    edges: ApiCanvasEdge[];
}

export interface ConversationMessage {
    role: "user" | "assistant" | "system";
    content: string;
}

/**
 * Request body for build-playbook-canvas endpoint.
 * Property names match API's BuilderRequest model (camelCase serialization).
 */
export interface BuildPlaybookCanvasRequest {
    message: string;
    canvasState: CanvasState;
    playbookId?: string;
    sessionId?: string;
    chatHistory?: ConversationMessage[];
    modelId?: string;
}

// ============================================================================
// Response Types (SSE Events)
// ============================================================================

export interface SseEvent<T = unknown> {
    type: SseEventType;
    data: T;
}

export interface ThinkingEventData {
    message: string;
    step?: string;
}

export interface DataverseOperationEventData {
    operation: "create" | "update" | "link";
    entity: string;
    record?: Record<string, unknown>;
    id?: string;
}

export interface CanvasPatchEventData {
    patch: CanvasPatch;
}

export interface MessageEventData {
    content: string;
    isPartial?: boolean;
}

export interface ClarificationEventData {
    question: string;
    options?: string[];
    context?: string;
}

export interface PlanPreviewEventData {
    summary: string;
    steps: Array<{
        step: number;
        operation: string;
        description: string;
    }>;
    estimatedNodes: number;
}

export interface ErrorEventData {
    message: string;
    code?: string;
    details?: string;
}

export interface DoneEventData {
    operationCount: number;
    summary?: string;
}

// ============================================================================
// Service Configuration
// ============================================================================

export interface AiPlaybookServiceConfig {
    /** Base URL for the BFF API */
    apiBaseUrl: string;
    /** Request timeout in ms (default: 120000) */
    timeout?: number;
}

// ============================================================================
// Event Handlers
// ============================================================================

export interface AiPlaybookEventHandlers {
    onThinking?: (data: ThinkingEventData) => void;
    onDataverseOperation?: (data: DataverseOperationEventData) => void;
    onCanvasPatch?: (data: CanvasPatchEventData) => void;
    onMessage?: (data: MessageEventData) => void;
    onClarification?: (data: ClarificationEventData) => void;
    onPlanPreview?: (data: PlanPreviewEventData) => void;
    onError?: (data: ErrorEventData) => void;
    onDone?: (data: DoneEventData) => void;
    onConnectionError?: (error: Error) => void;
}

// ============================================================================
// Service Class
// ============================================================================

/**
 * AI Playbook Service for canvas building via SSE streaming.
 *
 * Code Page migration: uses AuthService.getAccessToken() for Bearer token
 * instead of PCF context accessToken.
 */
export class AiPlaybookService {
    private config: Required<AiPlaybookServiceConfig>;
    private abortController: AbortController | null = null;

    constructor(config: AiPlaybookServiceConfig) {
        this.config = {
            ...config,
            timeout: config.timeout ?? 120000,
        };
    }

    /**
     * Build playbook canvas via SSE streaming.
     * Acquires Bearer token from AuthService before each request.
     */
    async buildPlaybookCanvas(
        request: BuildPlaybookCanvasRequest,
        handlers: AiPlaybookEventHandlers
    ): Promise<void> {
        // Abort any existing request
        this.abort();

        this.abortController = new AbortController();
        const { signal } = this.abortController;

        // Set up timeout
        const timeoutId = setTimeout(() => {
            this.abortController?.abort();
        }, this.config.timeout);

        try {
            // Acquire Bearer token from AuthService
            const accessToken = await getAccessToken();

            const url = `${this.config.apiBaseUrl}/api/ai/playbook-builder/process`;

            const response = await fetch(url, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Authorization": `Bearer ${accessToken}`,
                    "Accept": "text/event-stream",
                },
                body: JSON.stringify(request),
                signal,
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`HTTP ${response.status}: ${errorText || response.statusText}`);
            }

            if (!response.body) {
                throw new Error("Response body is null");
            }

            // Process the SSE stream
            await this.processStream(response.body, handlers);
        } catch (error) {
            if (error instanceof Error) {
                if (error.name === "AbortError") {
                    // Request was aborted, don't call error handler
                    return;
                }
                handlers.onConnectionError?.(error);
            } else {
                handlers.onConnectionError?.(new Error("Unknown error occurred"));
            }
        } finally {
            clearTimeout(timeoutId);
            this.abortController = null;
        }
    }

    /**
     * Abort the current request.
     */
    abort(): void {
        if (this.abortController) {
            this.abortController.abort();
            this.abortController = null;
        }
    }

    /**
     * Check if a request is currently in progress.
     */
    isStreaming(): boolean {
        return this.abortController !== null;
    }

    /**
     * Process the SSE stream.
     */
    private async processStream(
        body: ReadableStream<Uint8Array>,
        handlers: AiPlaybookEventHandlers
    ): Promise<void> {
        const reader = body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";

        try {
            while (true) {
                const { done, value } = await reader.read();

                if (done) {
                    break;
                }

                // Decode chunk and add to buffer
                const chunk = decoder.decode(value, { stream: true });
                buffer += chunk;

                // Process complete events from buffer
                const events = this.parseEventsFromBuffer(buffer);
                buffer = events.remaining;

                for (const event of events.parsed) {
                    this.dispatchEvent(event, handlers);

                    // Stop processing if done or error
                    if (event.type === "done" || event.type === "error") {
                        return;
                    }
                }
            }

            // Process any remaining data in buffer
            if (buffer.trim()) {
                const events = this.parseEventsFromBuffer(buffer + "\n\n");
                for (const event of events.parsed) {
                    this.dispatchEvent(event, handlers);
                }
            }
        } finally {
            reader.releaseLock();
        }
    }

    /**
     * Parse SSE events from buffer.
     * SSE format: event: {type}\ndata: {json}\n\n
     */
    private parseEventsFromBuffer(buffer: string): {
        parsed: SseEvent[];
        remaining: string;
    } {
        const parsed: SseEvent[] = [];
        const eventRegex = /event:\s*(\w+)\s*\ndata:\s*([^\n]+)(?=\n\n|\nevent:|\n?$)/g;

        let lastIndex = 0;
        let match: RegExpExecArray | null;

        while ((match = eventRegex.exec(buffer)) !== null) {
            const [fullMatch, eventType, dataStr] = match;
            lastIndex = match.index + fullMatch.length;

            try {
                // Check if this event is complete (followed by double newline or end)
                const afterMatch = buffer.slice(lastIndex);
                if (!afterMatch.startsWith("\n") && afterMatch.length > 0 && !afterMatch.startsWith("\nevent:")) {
                    // Event might be incomplete, stop here
                    break;
                }

                const data = JSON.parse(dataStr.trim());
                parsed.push({
                    type: eventType as SseEventType,
                    data,
                });
            } catch {
                console.warn("[AiPlaybookService] Failed to parse event data:", dataStr);
            }
        }

        // Return remaining unparsed buffer
        const remaining = lastIndex > 0 ? buffer.slice(lastIndex).replace(/^\n+/, "") : buffer;

        return { parsed, remaining };
    }

    /**
     * Dispatch event to appropriate handler.
     */
    private dispatchEvent(event: SseEvent, handlers: AiPlaybookEventHandlers): void {
        switch (event.type) {
            case "thinking":
                handlers.onThinking?.(event.data as ThinkingEventData);
                break;

            case "dataverse_operation":
                handlers.onDataverseOperation?.(event.data as DataverseOperationEventData);
                break;

            case "canvas_patch":
                handlers.onCanvasPatch?.(event.data as CanvasPatchEventData);
                break;

            case "message":
                handlers.onMessage?.(event.data as MessageEventData);
                break;

            case "clarification":
                handlers.onClarification?.(event.data as ClarificationEventData);
                break;

            case "plan_preview":
                handlers.onPlanPreview?.(event.data as PlanPreviewEventData);
                break;

            case "error":
                handlers.onError?.(event.data as ErrorEventData);
                break;

            case "done":
                handlers.onDone?.(event.data as DoneEventData);
                break;

            default:
                console.warn("[AiPlaybookService] Unknown event type:", event.type);
        }
    }
}

/**
 * Create an AiPlaybookService instance.
 */
export function createAiPlaybookService(
    config: AiPlaybookServiceConfig
): AiPlaybookService {
    return new AiPlaybookService(config);
}

export default AiPlaybookService;
