/**
 * useSseStream — SSE connection hook using fetch with ReadableStream
 *
 * Connects to SSE endpoints via POST fetch (EventSource only supports GET).
 * Parses "data: {...}\n\n" events and accumulates token content.
 *
 * Standalone — does not depend on SprkChat internals.
 *
 * URL CONSTRUCTION: Callers MUST pass pre-built URLs from ChatApiClient.buildMessagesUrl()
 * or buildBffApiUrl() — never raw template literals. This hook receives fully-constructed
 * URLs and forwards auth tokens for the streaming fetch call.
 *
 * NOTE: authenticatedFetch() cannot be used here because SSE requires streaming the
 * ReadableStream body. Instead, the caller provides the access token and this hook
 * attaches the Authorization header directly (matching the authenticatedFetch pattern
 * for token extraction and X-Tenant-Id header).
 *
 * @see ADR-013 — AI Architecture
 * @see ChatApiClient.buildMessagesUrl() — MUST use to construct streaming URL
 * @see ChatEndpoints.cs — SSE format: data: {"type":"token","content":"..."}\n\n
 */

import { useState, useRef, useCallback } from 'react';
import type {
  IChatSseEvent,
  IChatSseEventData,
  ICitation,
  IDocumentStreamSseEvent,
  IUseSseStreamResult,
  ICitationSseItem,
} from '../types/chat';

// ─────────────────────────────────────────────────────────────────────────────
// SSE Parsing utilities (exported for testing)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parse a single SSE data line into a ChatSseEvent.
 * Expects format: data: {"type":"token","content":"..."}
 */
export function parseSseEvent(line: string): IChatSseEvent | null {
  const trimmed = line.trim();
  if (!trimmed.startsWith('data: ')) {
    return null;
  }

  const jsonStr = trimmed.substring(6); // Remove "data: " prefix
  if (!jsonStr) {
    return null;
  }

  try {
    const parsed = JSON.parse(jsonStr) as IChatSseEvent;
    if (parsed && typeof parsed.type === 'string') {
      return parsed;
    }
    return null;
  } catch {
    return null;
  }
}

/**
 * Extract suggestions array from a "suggestions" SSE event.
 */
function parseSuggestions(event: IChatSseEvent): string[] {
  if (event.data?.suggestions && event.data.suggestions.length > 0) {
    return event.data.suggestions.filter(s => typeof s === 'string' && s.length > 0);
  }
  if (Array.isArray(event.suggestions) && event.suggestions.length > 0) {
    return event.suggestions.filter(s => typeof s === 'string' && s.length > 0);
  }
  return [];
}

/**
 * Maps SSE citation items to the frontend ICitation format.
 */
function mapSseCitations(items: NonNullable<IChatSseEventData['citations']>): ICitation[] {
  if (!items || items.length === 0) {
    return [];
  }
  return items.map((item: ICitationSseItem) => ({
    id: item.id,
    source: item.sourceName,
    page: item.page ?? undefined,
    excerpt: item.excerpt,
    chunkId: item.chunkId,
    sourceType: item.sourceType,
    url: item.url,
    snippet: item.snippet,
  }));
}

/**
 * Extract tenant ID from JWT access token for X-Tenant-Id header.
 * Azure AD tokens include 'tid' claim with the tenant GUID.
 */
function extractTenantId(token: string): string | null {
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return null;
    const payload = JSON.parse(atob(parts[1]));
    return (payload as { tid?: string }).tid ?? null;
  } catch {
    return null;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// SSE event processing (shared between main loop and remainder buffer)
// ─────────────────────────────────────────────────────────────────────────────

type PendingActionEvent = {
  type: 'action_confirmation' | 'action_success' | 'action_error' | 'dialog_open' | 'navigate';
  data: IChatSseEventData;
};

interface SseEventHandlers {
  onTypingStart: () => void;
  onToken: (token: string) => void;
  onTypingEnd: () => void;
  onSuggestions: (suggestions: string[]) => void;
  onCitations: (citations: ICitation[]) => void;
  onPlanPreview: (planId: string, planData: IChatSseEventData) => void;
  onActionEvent: (event: PendingActionEvent) => void;
  onDocumentStreamEvent: (event: IDocumentStreamSseEvent) => void;
  onDone: () => void;
  onError: (message: string) => void;
}

function processEvent(event: IChatSseEvent, handlers: SseEventHandlers): void {
  if (event.type === 'typing_start') {
    handlers.onTypingStart();
  } else if (event.type === 'token' && event.content) {
    handlers.onToken(event.content);
  } else if (event.type === 'typing_end') {
    handlers.onTypingEnd();
  } else if (event.type === 'suggestions') {
    const suggestions = parseSuggestions(event);
    if (suggestions.length > 0) {
      handlers.onSuggestions(suggestions);
    }
  } else if (event.type === 'citations' && event.data?.citations) {
    handlers.onCitations(mapSseCitations(event.data.citations));
  } else if (event.type === 'plan_preview' && event.data?.planId) {
    handlers.onPlanPreview(event.data.planId, event.data);
  } else if (
    event.type === 'action_confirmation' ||
    event.type === 'action_success' ||
    event.type === 'action_error' ||
    event.type === 'dialog_open' ||
    event.type === 'navigate'
  ) {
    handlers.onActionEvent({
      type: event.type as PendingActionEvent['type'],
      data: event.data ?? {},
    });
  } else if (
    event.type === 'document_stream_start' ||
    event.type === 'document_stream_token' ||
    event.type === 'document_stream_end'
  ) {
    const raw = event as unknown as Record<string, unknown>;
    if (event.type === 'document_stream_start') {
      handlers.onDocumentStreamEvent({
        type: 'document_stream_start',
        operationId: (raw.operationId as string) || '',
        targetPosition: (raw.targetPosition as string) || 'cursor',
        operationType: (raw.operationType as 'insert' | 'replace' | 'diff') || 'insert',
      });
    } else if (event.type === 'document_stream_token') {
      handlers.onDocumentStreamEvent({
        type: 'document_stream_token',
        operationId: (raw.operationId as string) || '',
        token: (raw.content as string) || (raw.token as string) || '',
        index: (raw.index as number) || 0,
      });
    } else if (event.type === 'document_stream_end') {
      handlers.onDocumentStreamEvent({
        type: 'document_stream_end',
        operationId: (raw.operationId as string) || '',
        cancelled: (raw.cancelled as boolean) || false,
        totalTokens: (raw.totalTokens as number) || 0,
      });
    }
  } else if (event.type === 'done') {
    handlers.onDone();
  } else if (event.type === 'error') {
    handlers.onError(event.content || 'Stream error');
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook for consuming SSE streams from POST endpoints.
 *
 * Uses fetch() with ReadableStream to read server-sent events.
 * Handles cancellation via AbortController.
 *
 * Callers provide the full URL (built via ChatApiClient.buildMessagesUrl() or buildBffApiUrl())
 * and a valid access token (from getAccessToken() or authenticatedFetch provider).
 *
 * @example
 * ```tsx
 * const { content, isDone, isStreaming, error, startStream, cancelStream } = useSseStream();
 *
 * const handleSend = () => {
 *   const url = client.buildMessagesUrl(session.sessionId);
 *   startStream(url, { message: "Hello" }, accessToken);
 * };
 * ```
 */
export function useSseStream(): IUseSseStreamResult {
  const [content, setContent] = useState<string>('');
  const [isDone, setIsDone] = useState<boolean>(false);
  const [error, setError] = useState<Error | null>(null);
  const [isStreaming, setIsStreaming] = useState<boolean>(false);
  const [isTyping, setIsTyping] = useState<boolean>(false);
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const [citations, setCitations] = useState<ICitation[]>([]);
  const [pendingPlanId, setPendingPlanId] = useState<string | null>(null);
  const [pendingPlanData, setPendingPlanData] = useState<IChatSseEventData | null>(null);
  const [pendingActionEvent, setPendingActionEvent] = useState<PendingActionEvent | null>(null);

  // R2-051: stores the latest document stream event for forwarding to the editor.
  // Uses a ref (not state) because document_stream_token events arrive at high
  // frequency. The callback ref is invoked synchronously from the fetch loop,
  // ensuring every token is forwarded without loss from React batch coalescing.
  // SECURITY (ADR-015): Only content tokens and structural metadata are forwarded.
  const onDocumentStreamEventRef = useRef<((event: IDocumentStreamSseEvent) => void) | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  const cancelStream = useCallback(() => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
      abortControllerRef.current = null;
    }
    setIsStreaming(false);
    setIsTyping(false);
  }, []);

  const clearSuggestions = useCallback(() => {
    setSuggestions([]);
  }, []);

  const clearPendingActionEvent = useCallback(() => {
    setPendingActionEvent(null);
  }, []);

  const clearPendingDocumentStreamEvent = useCallback(() => {
    // No-op: callback pattern doesn't need clearing
  }, []);

  const setOnDocumentStreamEvent = useCallback((handler: ((event: IDocumentStreamSseEvent) => void) | null) => {
    onDocumentStreamEventRef.current = handler;
  }, []);

  const startStream = useCallback(
    (url: string, body: Record<string, unknown>, token: string) => {
      // Cancel any existing stream
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }

      // Reset state
      setContent('');
      setIsDone(false);
      setError(null);
      setIsStreaming(true);
      setIsTyping(false);
      setSuggestions([]);
      setCitations([]);
      setPendingPlanId(null);
      setPendingPlanData(null);
      setPendingActionEvent(null);

      const controller = new AbortController();
      abortControllerRef.current = controller;

      const fetchStream = async () => {
        try {
          const tenantId = extractTenantId(token);

          const response = await fetch(url, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              Authorization: `Bearer ${token}`,
              ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
            },
            body: JSON.stringify(body),
            signal: controller.signal,
          });

          if (!response.ok) {
            const errorText = await response.text();
            // ADR-016: Show user-friendly message for rate limiting (429)
            if (response.status === 429) {
              throw new Error('You are sending messages too quickly. Please wait a moment and try again.');
            }
            throw new Error(`Chat request failed (${response.status}): ${errorText}`);
          }

          if (!response.body) {
            throw new Error('Response body is empty');
          }

          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = '';
          let accumulated = '';

          // Build event handlers that capture accumulated content via closure
          const handlers: SseEventHandlers = {
            onTypingStart: () => setIsTyping(true),
            onToken: (tokenContent: string) => {
              setIsTyping(false);
              accumulated += tokenContent;
              setContent(accumulated);
            },
            onTypingEnd: () => setIsTyping(false),
            onSuggestions: (s: string[]) => setSuggestions(s),
            onCitations: (c: ICitation[]) => setCitations(c),
            onPlanPreview: (planId: string, planData: IChatSseEventData) => {
              setPendingPlanId(planId);
              setPendingPlanData(planData);
            },
            onActionEvent: (evt: PendingActionEvent) => setPendingActionEvent(evt),
            onDocumentStreamEvent: (evt: IDocumentStreamSseEvent) => {
              // Synchronous callback — bypasses React state batching for high-frequency events
              const handler = onDocumentStreamEventRef.current;
              if (handler) {
                handler(evt);
              }
            },
            onDone: () => {
              setIsDone(true);
              setIsStreaming(false);
              setIsTyping(false);
            },
            onError: (message: string) => {
              setIsTyping(false);
              throw new Error(message);
            },
          };

          // Main read loop
          while (true) {
            const { done, value } = await reader.read();
            if (done) {
              break;
            }

            buffer += decoder.decode(value, { stream: true });

            // SSE events are separated by double newlines
            const parts = buffer.split('\n\n');
            // Keep the last incomplete part in the buffer
            buffer = parts.pop() || '';

            for (const part of parts) {
              const lines = part.split('\n');
              for (const line of lines) {
                const event = parseSseEvent(line);
                if (event) {
                  processEvent(event, handlers);
                }
              }
            }
          }

          // Process any remaining buffer content
          if (buffer.trim()) {
            const lines = buffer.split('\n');
            for (const line of lines) {
              const event = parseSseEvent(line);
              if (event) {
                processEvent(event, handlers);
              }
            }
          }

          setIsStreaming(false);
          setIsTyping(false);
        } catch (err: unknown) {
          if (err instanceof DOMException && err.name === 'AbortError') {
            // Stream was cancelled by user — not an error
            setIsStreaming(false);
            setIsTyping(false);
            return;
          }

          const errorObj = err instanceof Error ? err : new Error('Unknown stream error');
          setError(errorObj);
          setIsStreaming(false);
          setIsTyping(false);
        }
      };

      fetchStream();
    },
    [] // No dependencies — all state setters are stable
  );

  return {
    content,
    isDone,
    error,
    isStreaming,
    isTyping,
    suggestions,
    citations,
    pendingPlanId,
    pendingPlanData,
    pendingActionEvent,
    pendingDocumentStreamEvent: null, // Deprecated: use setOnDocumentStreamEvent callback instead
    startStream,
    cancelStream,
    clearSuggestions,
    clearPendingActionEvent,
    clearPendingDocumentStreamEvent,
    setOnDocumentStreamEvent,
  };
}
