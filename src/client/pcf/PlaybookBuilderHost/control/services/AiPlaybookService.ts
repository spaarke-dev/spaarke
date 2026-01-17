/**
 * AI Playbook Service - API client for AI playbook canvas building
 *
 * Handles communication with the /api/ai/build-playbook-canvas endpoint.
 * Uses fetch with ReadableStream for SSE streaming (POST request).
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
 * @version 1.0.0
 */

import type {
  SseEventType,
  CanvasPatch,
  ChatMessage,
} from '../stores/aiAssistantStore';
import type { PlaybookNode, PlaybookEdge } from '../stores/canvasStore';

// ============================================================================
// Request Types
// ============================================================================

/**
 * Current canvas state for context.
 */
export interface CanvasState {
  nodes: PlaybookNode[];
  edges: PlaybookEdge[];
}

/**
 * Conversation message for history.
 */
export interface ConversationMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
}

/**
 * Request body for build-playbook-canvas endpoint.
 */
export interface BuildPlaybookCanvasRequest {
  /** Playbook record ID */
  playbookId: string;
  /** Current canvas state */
  currentCanvas: CanvasState;
  /** User's message/instruction */
  message: string;
  /** Conversation history */
  conversationHistory: ConversationMessage[];
  /** Session ID for continuity */
  sessionId?: string;
}

// ============================================================================
// Response Types (SSE Events)
// ============================================================================

/**
 * Base SSE event structure.
 */
export interface SseEvent<T = unknown> {
  type: SseEventType;
  data: T;
}

/**
 * Thinking event - AI is processing.
 */
export interface ThinkingEventData {
  message: string;
  step?: string;
}

/**
 * Dataverse operation event - record created/updated.
 */
export interface DataverseOperationEventData {
  operation: 'create' | 'update' | 'link';
  entity: string;
  record?: Record<string, unknown>;
  id?: string;
}

/**
 * Canvas patch event - changes to apply to canvas.
 */
export interface CanvasPatchEventData extends CanvasPatch {}

/**
 * Message event - AI response text.
 */
export interface MessageEventData {
  content: string;
  isPartial?: boolean;
}

/**
 * Clarification event - AI needs more information.
 */
export interface ClarificationEventData {
  question: string;
  options?: string[];
  context?: string;
}

/**
 * Plan preview event - build plan for confirmation.
 */
export interface PlanPreviewEventData {
  summary: string;
  steps: Array<{
    step: number;
    operation: string;
    description: string;
  }>;
  estimatedNodes: number;
}

/**
 * Error event data.
 */
export interface ErrorEventData {
  message: string;
  code?: string;
  details?: string;
}

/**
 * Done event data.
 */
export interface DoneEventData {
  success: boolean;
  summary?: string;
}

// ============================================================================
// Service Configuration
// ============================================================================

export interface AiPlaybookServiceConfig {
  /** Base URL for the API */
  apiBaseUrl: string;
  /** Access token for authentication */
  accessToken: string;
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
   * Update access token (e.g., after token refresh).
   */
  setAccessToken(token: string): void {
    this.config.accessToken = token;
  }

  /**
   * Build playbook canvas via SSE streaming.
   * Returns a promise that resolves when the stream is complete.
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
      const url = `${this.config.apiBaseUrl}/api/ai/build-playbook-canvas`;

      const response = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${this.config.accessToken}`,
          Accept: 'text/event-stream',
        },
        body: JSON.stringify(request),
        signal,
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`HTTP ${response.status}: ${errorText || response.statusText}`);
      }

      if (!response.body) {
        throw new Error('Response body is null');
      }

      // Process the SSE stream
      await this.processStream(response.body, handlers);
    } catch (error) {
      if (error instanceof Error) {
        if (error.name === 'AbortError') {
          // Request was aborted, don't call error handler
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
   * Process the SSE stream.
   */
  private async processStream(
    body: ReadableStream<Uint8Array>,
    handlers: AiPlaybookEventHandlers
  ): Promise<void> {
    const reader = body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    try {
      while (true) {
        const { done, value } = await reader.read();

        if (done) {
          break;
        }

        // Decode chunk and add to buffer
        buffer += decoder.decode(value, { stream: true });

        // Process complete events from buffer
        const events = this.parseEventsFromBuffer(buffer);
        buffer = events.remaining;

        for (const event of events.parsed) {
          this.dispatchEvent(event, handlers);

          // Stop processing if done or error
          if (event.type === 'done' || event.type === 'error') {
            return;
          }
        }
      }

      // Process any remaining data in buffer
      if (buffer.trim()) {
        const events = this.parseEventsFromBuffer(buffer + '\n\n');
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
    const eventRegex = /event:\s*(\w+)\s*\ndata:\s*(.+?)(?=\n\n|\nevent:|\n?$)/gs;

    let lastIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = eventRegex.exec(buffer)) !== null) {
      const [fullMatch, eventType, dataStr] = match;
      lastIndex = match.index + fullMatch.length;

      try {
        // Check if this event is complete (followed by double newline or end)
        const afterMatch = buffer.slice(lastIndex);
        if (!afterMatch.startsWith('\n') && afterMatch.length > 0 && !afterMatch.startsWith('\nevent:')) {
          // Event might be incomplete, stop here
          break;
        }

        const data = JSON.parse(dataStr.trim());
        parsed.push({
          type: eventType as SseEventType,
          data,
        });
      } catch (parseError) {
        console.warn('[AiPlaybookService] Failed to parse event data:', dataStr, parseError);
      }
    }

    // Return remaining unparsed buffer
    const remaining = lastIndex > 0 ? buffer.slice(lastIndex).replace(/^\n+/, '') : buffer;

    return { parsed, remaining };
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

// ============================================================================
// Factory Function
// ============================================================================

/**
 * Create an AiPlaybookService instance.
 */
export function createAiPlaybookService(
  config: AiPlaybookServiceConfig
): AiPlaybookService {
  return new AiPlaybookService(config);
}

export default AiPlaybookService;
