/**
 * AI Playbook Service - API client for AI playbook canvas building
 *
 * Handles communication with the /api/ai/playbook-builder/process endpoint.
 * Uses fetch with ReadableStream for SSE streaming (POST request).
 *
 * Auth v2 (D-AUTH-7): Acquires a fresh Bearer token from @spaarke/auth
 * (via authInit re-export → SpaarkeAuthProvider.getAccessToken()) once per
 * stream open. Token is NEVER snapshotted in React state and NEVER reused
 * across streams. This eliminates the class of bugs where a token was
 * captured at mount time, expired mid-session, and silently 401'd the SSE
 * stream (no auto-retry on streaming responses).
 *
 * NOTE: `authenticatedFetch` from @spaarke/auth CANNOT be used here because
 * SSE requires streaming the ReadableStream body, which the wrapper does not
 * expose. The same constraint applies to the canonical useSseStream hook in
 * @spaarke/ui-components — see its file header for the full rationale.
 *
 * task 045 (FR-P3-06 / NFR-08): the SSE READ loop (fetch + getReader +
 * TextDecoder + buffer split) is the canonical `readSseStream` from
 * @spaarke/ui-components — exactly ONE SSE parse path client-wide. This
 * service's wire format uses `event: {name}\ndata: {json}` pairs, so the
 * event/data pairing is a small stateful line handler in `onLine`; the
 * reader/buffer machinery itself is shared.
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
 * @version 3.0.0 (Auth v2 — function-based contract, task 024)
 */

import { readSseStream } from '@spaarke/ui-components';
import { getAccessToken } from './authInit';

// ============================================================================
// SSE Event Types
// ============================================================================

export type SseEventType =
  | 'thinking'
  | 'dataverse_operation'
  | 'canvas_patch'
  | 'message'
  | 'done'
  | 'error'
  | 'clarification'
  | 'plan_preview';

// ============================================================================
// Canvas Patch Types
// ============================================================================

export type CanvasPatchOperation =
  | 'AddNode'
  | 'RemoveNode'
  | 'UpdateNode'
  | 'AddEdge'
  | 'RemoveEdge'
  | 'ConfigureNode'
  | 'LinkScope';

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
  toolIds?: string[];
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
  role: 'user' | 'assistant' | 'system';
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
  operation: 'create' | 'update' | 'link';
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
 * Auth v2: acquires a fresh Bearer token from the @spaarke/auth provider
 * once per stream open. No React state snapshot of the token.
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
   * Acquires a fresh Bearer token from the @spaarke/auth provider before
   * opening this stream (D-AUTH-7 — never snapshotted, never reused;
   * `readSseStream` invokes the getter ONCE immediately before the fetch).
   *
   * task 045 (FR-P3-06 / NFR-08): the reader loop is the canonical
   * `readSseStream`. The `event:`/`data:` pairing for this endpoint's wire
   * format is a stateful line handler in `onLine` below.
   */
  async buildPlaybookCanvas(request: BuildPlaybookCanvasRequest, handlers: AiPlaybookEventHandlers): Promise<void> {
    // Abort any existing request
    this.abort();

    this.abortController = new AbortController();
    const { signal } = this.abortController;

    // Set up timeout
    const timeoutId = setTimeout(() => {
      this.abortController?.abort();
    }, this.config.timeout);

    // Stateful `event:`/`data:` pairing — tracks the last seen `event:` name;
    // the following `data:` line completes the pair and is dispatched.
    let pendingEventType: SseEventType | null = null;
    // Mirrors the retired hand-rolled loop: stop consuming after done/error.
    let stopped = false;

    try {
      const url = `${this.config.apiBaseUrl}/api/ai/playbook-builder/process`;

      await readSseStream({
        url,
        body: request as unknown as Record<string, unknown>,
        getAccessToken,
        signal,
        mapHttpError: async response => {
          const errorText = await response.text();
          return new Error(`HTTP ${response.status}: ${errorText || response.statusText}`);
        },
        onLine: (line: string) => {
          if (stopped) {
            return;
          }
          const trimmed = line.trim();

          if (trimmed.startsWith('event:')) {
            pendingEventType = trimmed.substring(6).trim() as SseEventType;
            return;
          }

          if (trimmed.startsWith('data:') && pendingEventType !== null) {
            const eventType = pendingEventType;
            pendingEventType = null;

            const dataStr = trimmed.substring(5).trim();
            let data: unknown;
            try {
              data = JSON.parse(dataStr);
            } catch {
              console.warn('[AiPlaybookService] Failed to parse event data:', dataStr);
              return;
            }

            this.dispatchEvent({ type: eventType, data }, handlers);

            // The retired hand-rolled loop stopped reading after done/error;
            // abort the stream to preserve that termination behavior (the
            // AbortError is swallowed below, matching the original early return).
            if (eventType === 'done' || eventType === 'error') {
              stopped = true;
              this.abortController?.abort();
            }
          }
        },
      });
    } catch (error) {
      if (error instanceof Error) {
        if (error.name === 'AbortError') {
          // Request was aborted (user abort, timeout, or post-done/error stop)
          // — don't call error handler
          return;
        }
        handlers.onConnectionError?.(error);
      } else {
        handlers.onConnectionError?.(new Error('Unknown error occurred'));
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
   * Dispatch event to appropriate handler.
   */
  private dispatchEvent(event: SseEvent, handlers: AiPlaybookEventHandlers): void {
    switch (event.type) {
      case 'thinking':
        handlers.onThinking?.(event.data as ThinkingEventData);
        break;

      case 'dataverse_operation':
        handlers.onDataverseOperation?.(event.data as DataverseOperationEventData);
        break;

      case 'canvas_patch':
        handlers.onCanvasPatch?.(event.data as CanvasPatchEventData);
        break;

      case 'message':
        handlers.onMessage?.(event.data as MessageEventData);
        break;

      case 'clarification':
        handlers.onClarification?.(event.data as ClarificationEventData);
        break;

      case 'plan_preview':
        handlers.onPlanPreview?.(event.data as PlanPreviewEventData);
        break;

      case 'error':
        handlers.onError?.(event.data as ErrorEventData);
        break;

      case 'done':
        handlers.onDone?.(event.data as DoneEventData);
        break;

      default:
        console.warn('[AiPlaybookService] Unknown event type:', event.type);
    }
  }
}

/**
 * Create an AiPlaybookService instance.
 */
export function createAiPlaybookService(config: AiPlaybookServiceConfig): AiPlaybookService {
  return new AiPlaybookService(config);
}

export default AiPlaybookService;
