/**
 * useSseStream — SSE connection hook using fetch with ReadableStream
 *
 * Canonical implementation. Single source of truth for SSE streaming in Spaarke.
 * Both SprkChat and AiSessionProvider import from this location.
 *
 * Connects to SSE endpoints via POST fetch (EventSource only supports GET).
 * Parses "data: {...}\n\n" events from the stream and accumulates token content.
 *
 * URL CONSTRUCTION: Callers MUST pass pre-built URLs from ChatApiClient.buildMessagesUrl()
 * or buildBffApiUrl() — never raw template literals. This hook receives fully-constructed
 * URLs and re-acquires a fresh access token for each streaming fetch call.
 *
 * Auth v2 (D-AUTH-7): `startStream` accepts an `AccessTokenGetter` (`() => Promise<string>`)
 * rather than a token string. The getter is invoked ONCE per stream open, immediately
 * before issuing the fetch — so the token is always fresh for THIS stream. The token
 * is NEVER snapshotted in React state and NEVER reused across stream opens. This
 * eliminates the class of bugs where a token was captured at mount time, expired
 * mid-session, and then attached to an SSE stream that consequently 401'd silently
 * (EventSource has no auto-401 retry).
 *
 * NOTE: `authenticatedFetch` cannot be used here because SSE requires streaming the
 * ReadableStream body, which the wrapper function does not expose. The hook
 * replicates the same token-attachment + X-Tenant-Id derivation pattern internally.
 *
 * CONSOLIDATION NOTE (AIPU2-082):
 * Two prior implementations were merged here:
 *   1. SprkChat/hooks/useSseStream.ts — mature, feature-complete, but had duplicated
 *      event dispatch logic in main loop AND remainder-buffer loop (copy-paste drift risk).
 *   2. Spaarke.AI.Context/src/hooks/useSseStream.ts — refactored with processEvent()
 *      dispatcher to eliminate the duplication, but lacked parsePaneEvent / setOnPaneEvent
 *      support needed by SprkChat/ChatPanel.
 * Decision: adopt the AI.Context processEvent() dispatcher structure (eliminates duplicate
 * event handling blocks) and extend it with the full SprkChat feature set
 * (parsePaneEvent, setOnPaneEvent, IAiPaneEvent). The AI.Context local copy is deleted;
 * its hooks/index.ts re-exports parseSseEvent from @spaarke/ui-components.
 *
 * @see ADR-013 — AI Architecture
 * @see ADR-022 — React 16 APIs only (useState, useEffect, useRef, useCallback)
 * @see ChatEndpoints.cs — SSE format: data: {"type":"token","content":"..."}\n\n
 * @see ChatApiClient.buildMessagesUrl() — MUST use to construct streaming URL
 */

import { useState, useRef, useCallback } from 'react';
import type {
  IChatSseEvent,
  IChatSseEventData,
  ICitation,
  IDocumentStreamSseEvent,
  IAiPaneEvent,
  IUseSseStreamResult,
  ICitationSseItem,
  IPlaybookOptionsPayload,
  AccessTokenGetter,
} from '../components/SprkChat/types';

// ─────────────────────────────────────────────────────────────────────────────
// SSE Parsing utilities (exported for testing)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parse a single SSE data line into a ChatSseEvent.
 * Expects format: data: {"type":"token","content":"..."}
 * Returns null for non-data lines or invalid JSON.
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
 * Parse a single SSE data line into an IAiPaneEvent.
 * Pane events use `event` as the discriminator (not `type`), so parseSseEvent()
 * returns null for them. This parser handles that alternate envelope shape.
 *
 * Expected formats:
 *   data: {"event":"output_pane","widgetType":"AnalysisEditor","payload":{...}}
 *   data: {"event":"source_pane","widgetType":"DocumentViewer","payload":{...}}
 *   data: {"event":"source_highlight","sourceRef":"doc-1","selectionRef":"cit-3"}
 */
export function parsePaneEvent(line: string): IAiPaneEvent | null {
  const trimmed = line.trim();
  if (!trimmed.startsWith('data: ')) {
    return null;
  }

  const jsonStr = trimmed.substring(6);
  if (!jsonStr) {
    return null;
  }

  try {
    const parsed = JSON.parse(jsonStr) as Record<string, unknown>;
    const event = parsed['event'];
    if (event === 'output_pane' || event === 'source_pane' || event === 'source_highlight') {
      return parsed as unknown as IAiPaneEvent;
    }
    return null;
  } catch {
    return null;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Extract suggestions array from a "suggestions" SSE event.
 * The backend sends suggestions in the `data.suggestions` property:
 * data: {"type":"suggestions","content":null,"data":{"suggestions":["s1","s2","s3"]}}
 * Falls back to `event.suggestions` for backward compatibility.
 */
function parseSuggestions(event: IChatSseEvent): string[] {
  // Primary: data.suggestions (ChatSseSuggestionsData from the backend)
  if (event.data?.suggestions && event.data.suggestions.length > 0) {
    return event.data.suggestions.filter(s => typeof s === 'string' && s.length > 0);
  }
  // Fallback: event.suggestions (if sent as a top-level property)
  if (Array.isArray(event.suggestions) && event.suggestions.length > 0) {
    return event.suggestions.filter(s => typeof s === 'string' && s.length > 0);
  }
  return [];
}

/**
 * Maps SSE citation items to the frontend ICitation format.
 * Converts the camelCase `sourceName` from the server to the `source` field
 * expected by CitationMarker/SprkChatCitationPopover components.
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
// SSE event processing dispatcher
// Shared between main read loop and remainder-buffer processing.
// Using a dispatcher eliminates the copy-paste duplication that existed in the
// original SprkChat implementation (same switch logic appeared twice).
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
  /**
   * chat-routing-redesign-r1 task 117a/117b. Receives the full SSE payload
   * verbatim — caller is responsible for ADR-015 logging discipline.
   */
  onPlaybookOptions: (payload: IPlaybookOptionsPayload) => void;
  onDone: () => void;
  onError: (message: string) => void;
}

function processEvent(event: IChatSseEvent, handlers: SseEventHandlers): void {
  if (event.type === 'typing_start') {
    handlers.onTypingStart();
  } else if (event.type === 'token' && event.content) {
    // First token arrives — hide typing indicator, show content
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
    // Phase 2F: store planId and full plan data so callers can:
    // 1. Set message metadata (responseType, planTitle, plan steps)
    // 2. Call /plan/approve on "Proceed" with the correct planId
    handlers.onPlanPreview(event.data.planId, event.data);
  } else if (
    event.type === 'action_confirmation' ||
    event.type === 'action_success' ||
    event.type === 'action_error' ||
    event.type === 'dialog_open' ||
    event.type === 'navigate'
  ) {
    // R2-039/R2-052: Store action/dialog/navigate event for caller to handle via useEffect.
    handlers.onActionEvent({
      type: event.type as PendingActionEvent['type'],
      data: event.data ?? {},
    });
  } else if (
    event.type === 'document_stream_start' ||
    event.type === 'document_stream_token' ||
    event.type === 'document_stream_end'
  ) {
    // R2-051: BFF write-back SSE → callback → SprkChatBridge → editor.
    // Invoked via callback (not React state) for zero-loss high-frequency token delivery.
    // ADR-015: Only content tokens and structural metadata — no auth tokens forwarded.
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
  } else if (event.type === 'playbook_options') {
    // chat-routing-redesign-r1 task 117a/117b — FR-49 / 50 / 51.
    // Locked payload shape (see PlaybookOptionsSseEvent.cs):
    //   { candidates: [...], libraryModalCta: true, sessionAttachmentIds: [...],
    //     rerankInvoked: bool, rerankReason?: string }
    // Forwarded verbatim to the consumer; tier-1 safe by BFF construction.
    const data = event.data ?? ({} as IChatSseEventData);
    handlers.onPlaybookOptions({
      candidates: Array.isArray(data.candidates) ? data.candidates : [],
      libraryModalCta: typeof data.libraryModalCta === 'boolean' ? data.libraryModalCta : true,
      sessionAttachmentIds: Array.isArray(data.sessionAttachmentIds) ? data.sessionAttachmentIds : [],
      rerankInvoked: typeof data.rerankInvoked === 'boolean' ? data.rerankInvoked : false,
      rerankReason: data.rerankReason ?? null,
    });
  } else if (event.type === 'done') {
    handlers.onDone();
  } else if (event.type === 'error') {
    handlers.onError(event.content || 'Stream error');
  }
  // plan_step_start / plan_step_complete: no state update needed here —
  // these events are emitted by the /plan/approve endpoint and handled via
  // the dedicated approval stream in handlePlanProceed (SprkChat.tsx).
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
 * Callers provide the full URL (built via ChatApiClient.buildMessagesUrl() or
 * buildBffApiUrl()) and a `getAccessToken` function (typically the `getAccessToken`
 * value from `useAuth()`). The function is invoked ONCE per stream open,
 * immediately before opening the fetch, so the token is always fresh for THIS
 * stream open (Auth v2 D-AUTH-7).
 *
 * @returns SSE stream state and control functions
 *
 * @example
 * ```tsx
 * const { content, isDone, isStreaming, error, startStream, cancelStream } = useSseStream();
 *
 * const handleSend = () => {
 *   const url = client.buildMessagesUrl(session.sessionId);
 *   // getAccessToken is re-invoked on every stream open — never snapshot it.
 *   startStream(url, { message: "Hello" }, getAccessToken);
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
  // Phase 2F: stores planId from plan_preview SSE event so SprkChat can call /plan/approve
  const [pendingPlanId, setPendingPlanId] = useState<string | null>(null);
  // Phase 2F: full plan_preview data (planTitle, steps) used to set message metadata
  const [pendingPlanData, setPendingPlanData] = useState<IChatSseEventData | null>(null);
  // R2-039/R2-052: stores the latest action/dialog/navigate event for caller to handle.
  const [pendingActionEvent, setPendingActionEvent] = useState<PendingActionEvent | null>(null);

  // R2-051: Callback ref for document stream event forwarding.
  // Uses a ref (not state) because document_stream_token events arrive at high
  // frequency (one per AI-generated token). React state batching would coalesce
  // multiple token events in a single render frame, causing lost tokens.
  // The callback ref is invoked synchronously from the fetch loop, ensuring
  // every token is forwarded to SprkChatBridge without loss.
  // SECURITY (ADR-015): Only content tokens and structural metadata are forwarded.
  const onDocumentStreamEventRef = useRef<((event: IDocumentStreamSseEvent) => void) | null>(null);

  // Task 041: Callback ref for AI pane-routing SSE event forwarding.
  // Handles output_pane / source_pane / source_highlight events from the BFF stream.
  // Uses a ref (not state) for zero-serialization delivery — pane events may carry
  // large payloads (widget data) and must not trigger SprkChat re-renders.
  // OutputPanel and SourcePanel subscribe via StandaloneAiContext to receive these.
  const onPaneEventRef = useRef<((event: IAiPaneEvent) => void) | null>(null);

  // chat-routing-redesign-r1 task 117a/117b — callback ref for `playbook_options`
  // SSE events. Same synchronous callback-ref pattern as setOnPaneEvent. SprkChat
  // wires this to the `onPlaybookOptions` prop so the host (ConversationPane) can
  // append a structured playbook_options chat message to its in-memory thread.
  const onPlaybookOptionsRef = useRef<((payload: IPlaybookOptionsPayload) => void) | null>(null);

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

  // R2-039: clear the pending action event after caller has handled it
  const clearPendingActionEvent = useCallback(() => {
    setPendingActionEvent(null);
  }, []);

  // R2-051: Register/unregister the document stream event callback.
  // SprkChat.tsx calls this once during setup to wire bridge forwarding.
  const setOnDocumentStreamEvent = useCallback((handler: ((event: IDocumentStreamSseEvent) => void) | null) => {
    onDocumentStreamEventRef.current = handler;
  }, []);

  // Task 041: Register/unregister the AI pane-routing SSE event callback.
  // ChatPanel.tsx wires this to the StandaloneAiContext onPaneEvent callback so
  // OutputPanel and SourcePanel can react to output_pane / source_pane / source_highlight events.
  const setOnPaneEvent = useCallback((handler: ((event: IAiPaneEvent) => void) | null) => {
    onPaneEventRef.current = handler;
  }, []);

  // chat-routing-redesign-r1 task 117b: register/unregister the playbook_options callback.
  // SprkChat wires this to the host (typically ConversationPane in SpaarkeAi)
  // via the `onPlaybookOptions` prop so the host can synthesize a structured
  // chat message + handle click → playbook execute.
  const setOnPlaybookOptions = useCallback((handler: ((payload: IPlaybookOptionsPayload) => void) | null) => {
    onPlaybookOptionsRef.current = handler;
  }, []);

  const startStream = useCallback(
    (url: string, body: Record<string, unknown>, getAccessToken: AccessTokenGetter) => {
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
          // Auth v2 (D-AUTH-7): re-acquire a fresh token for THIS stream open.
          // Never snapshot the token; never reuse across stream opens.
          const token = await getAccessToken();
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

          // Build event handlers that capture accumulated content via closure.
          // This dispatcher pattern (from the AI.Context refactor) eliminates the
          // duplication of having identical event-handling blocks in both the main
          // read loop and the remainder-buffer processing that existed in the original
          // SprkChat implementation.
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
            onPlaybookOptions: (payload: IPlaybookOptionsPayload) => {
              // Synchronous callback (same pattern as onDocumentStreamEvent and onPaneEvent).
              // No React state on the hook for this event — caller manages its own state.
              const handler = onPlaybookOptionsRef.current;
              if (handler) {
                handler(payload);
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
                // Check for pane-routing events first (output_pane / source_pane / source_highlight).
                // These use `event` as discriminator rather than `type`, so parseSseEvent returns null.
                const paneEvent = parsePaneEvent(line);
                if (paneEvent) {
                  const paneHandler = onPaneEventRef.current;
                  if (paneHandler) {
                    paneHandler(paneEvent);
                  }
                  continue;
                }

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
              // Check for pane-routing events in the trailing buffer too.
              const paneEvent = parsePaneEvent(line);
              if (paneEvent) {
                const paneHandler = onPaneEventRef.current;
                if (paneHandler) {
                  paneHandler(paneEvent);
                }
                continue;
              }

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
    clearPendingDocumentStreamEvent: () => {}, // No-op: callback pattern doesn't need clearing
    setOnDocumentStreamEvent,
    setOnPaneEvent,
    setOnPlaybookOptions,
  };
}
