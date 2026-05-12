/**
 * useSseStream - SSE connection hook using fetch with ReadableStream
 *
 * Connects to SSE endpoints via POST fetch (EventSource only supports GET).
 * Parses "data: {...}\n\n" events from the stream and accumulates token content.
 *
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useRef, useCallback)
 * @see ChatEndpoints.cs - SSE format: data: {"type":"token","content":"..."}\n\n
 */
import { useState, useRef, useCallback } from 'react';
/**
 * Parse a single SSE data line into a ChatSseEvent.
 * Expects format: data: {"type":"token","content":"..."}
 */
export function parseSseEvent(line) {
    const trimmed = line.trim();
    if (!trimmed.startsWith('data: ')) {
        return null;
    }
    const jsonStr = trimmed.substring(6); // Remove "data: " prefix
    if (!jsonStr) {
        return null;
    }
    try {
        const parsed = JSON.parse(jsonStr);
        if (parsed && typeof parsed.type === 'string') {
            return parsed;
        }
        return null;
    }
    catch {
        return null;
    }
}
/**
 * Extract suggestions array from a "suggestions" SSE event.
 * The backend sends suggestions in the `data.suggestions` property:
 * data: {"type":"suggestions","content":null,"data":{"suggestions":["s1","s2","s3"]}}
 *
 * Falls back to `event.suggestions` for backward compatibility.
 */
function parseSuggestions(event) {
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
 * Hook for consuming SSE streams from POST endpoints.
 *
 * Uses fetch() with ReadableStream to read server-sent events.
 * Handles cancellation via AbortController.
 *
 * @returns SSE stream state and control functions
 *
 * @example
 * ```tsx
 * const { content, isDone, isStreaming, error, startStream, cancelStream } = useSseStream();
 *
 * const handleSend = () => {
 *   startStream(
 *     "https://api.example.com/api/ai/chat/sessions/123/messages",
 *     { message: "Hello" },
 *     "bearer-token"
 *   );
 * };
 * ```
 */
export function useSseStream() {
    const [content, setContent] = useState('');
    const [isDone, setIsDone] = useState(false);
    const [error, setError] = useState(null);
    const [isStreaming, setIsStreaming] = useState(false);
    const [isTyping, setIsTyping] = useState(false);
    const [suggestions, setSuggestions] = useState([]);
    const [citations, setCitations] = useState([]);
    // Phase 2F: stores planId from plan_preview SSE event so SprkChat can call /plan/approve
    const [pendingPlanId, setPendingPlanId] = useState(null);
    // Phase 2F: full plan_preview data (planTitle, steps) used to set message metadata
    const [pendingPlanData, setPendingPlanData] = useState(null);
    // Task R2-039/R2-052: stores the latest action/dialog/navigate event data for SprkChat to handle.
    // Follows the same pattern as pendingPlanId — SprkChat watches via useEffect.
    const [pendingActionEvent, setPendingActionEvent] = useState(null);
    // Task R2-051: Callback ref for document stream event forwarding.
    // Uses a ref (not state) because document_stream_token events arrive at high
    // frequency (one per AI-generated token). React state batching would coalesce
    // multiple token events in a single render frame, causing lost tokens.
    // The callback ref is invoked synchronously from the fetch loop, ensuring
    // every token is forwarded to SprkChatBridge without loss.
    // SECURITY (ADR-015): Only content tokens and structural metadata are forwarded.
    const onDocumentStreamEventRef = useRef(null);
    const abortControllerRef = useRef(null);
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
    // Task R2-039: clear the pending action event after SprkChat has handled it
    const clearPendingActionEvent = useCallback(() => {
        setPendingActionEvent(null);
    }, []);
    // Task R2-051: Register/unregister the document stream event callback.
    // SprkChat.tsx calls this once during setup to wire bridge forwarding.
    const setOnDocumentStreamEvent = useCallback((handler) => {
        onDocumentStreamEventRef.current = handler;
    }, []);
    /**
     * Extract tenant ID from JWT access token for X-Tenant-Id header.
     */
    const extractTenantId = (token) => {
        try {
            const parts = token.split('.');
            if (parts.length !== 3)
                return null;
            const payload = JSON.parse(atob(parts[1]));
            return payload.tid || null;
        }
        catch {
            return null;
        }
    };
    const startStream = useCallback((url, body, token) => {
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
                            if (!event) {
                                continue;
                            }
                            if (event.type === 'typing_start') {
                                setIsTyping(true);
                            }
                            else if (event.type === 'token' && event.content) {
                                // First token arrives — hide typing indicator, show content
                                setIsTyping(false);
                                accumulated += event.content;
                                // Use functional update to ensure correct state
                                setContent(accumulated);
                            }
                            else if (event.type === 'typing_end') {
                                setIsTyping(false);
                            }
                            else if (event.type === 'suggestions') {
                                const suggestionsData = parseSuggestions(event);
                                if (suggestionsData.length > 0) {
                                    setSuggestions(suggestionsData);
                                }
                            }
                            else if (event.type === 'citations' && event.data?.citations) {
                                setCitations(mapSseCitations(event.data.citations));
                            }
                            else if (event.type === 'plan_preview' && event.data?.planId) {
                                // Phase 2F: store planId and full plan data so SprkChat can:
                                // 1. Set message metadata (responseType, planTitle, plan steps)
                                // 2. Call /plan/approve on "Proceed" with the correct planId
                                setPendingPlanId(event.data.planId);
                                setPendingPlanData(event.data);
                            }
                            else if (event.type === 'action_confirmation' ||
                                event.type === 'action_success' ||
                                event.type === 'action_error' ||
                                event.type === 'dialog_open' ||
                                event.type === 'navigate') {
                                // Task R2-039/R2-052: Store action/dialog/navigate event for SprkChat to handle via useEffect.
                                // SprkChat watches pendingActionEvent and dispatches to the appropriate handler
                                // (confirmation dialog, toast, Code Page navigateTo, or Xrm navigation).
                                setPendingActionEvent({
                                    type: event.type,
                                    data: event.data || {},
                                });
                            }
                            else if (event.type === 'document_stream_start' ||
                                event.type === 'document_stream_token' ||
                                event.type === 'document_stream_end') {
                                // Task R2-051: BFF write-back SSE → callback → SprkChatBridge → editor.
                                // Invokes callback synchronously (no React state) to ensure every token
                                // is forwarded without loss from React batch coalescing.
                                // ADR-015: Only content tokens and structural metadata — no auth tokens.
                                const handler = onDocumentStreamEventRef.current;
                                if (handler) {
                                    const raw = event;
                                    if (event.type === 'document_stream_start') {
                                        handler({
                                            type: 'document_stream_start',
                                            operationId: raw.operationId || '',
                                            targetPosition: raw.targetPosition || 'cursor',
                                            operationType: raw.operationType || 'insert',
                                        });
                                    }
                                    else if (event.type === 'document_stream_token') {
                                        handler({
                                            type: 'document_stream_token',
                                            operationId: raw.operationId || '',
                                            token: raw.content || raw.token || '',
                                            index: raw.index || 0,
                                        });
                                    }
                                    else if (event.type === 'document_stream_end') {
                                        handler({
                                            type: 'document_stream_end',
                                            operationId: raw.operationId || '',
                                            cancelled: raw.cancelled || false,
                                            totalTokens: raw.totalTokens || 0,
                                        });
                                    }
                                }
                            }
                            else if (event.type === 'done') {
                                setIsDone(true);
                                setIsStreaming(false);
                                setIsTyping(false);
                            }
                            else if (event.type === 'error') {
                                setIsTyping(false);
                                throw new Error(event.content || 'Stream error');
                            }
                            // plan_step_start / plan_step_complete: no state update needed here —
                            // these events are emitted by the /plan/approve endpoint and handled via
                            // the dedicated approval stream in handlePlanProceed (SprkChat.tsx).
                        }
                    }
                }
                // Process any remaining buffer
                if (buffer.trim()) {
                    const lines = buffer.split('\n');
                    for (const line of lines) {
                        const event = parseSseEvent(line);
                        if (!event) {
                            continue;
                        }
                        if (event.type === 'typing_start') {
                            setIsTyping(true);
                        }
                        else if (event.type === 'token' && event.content) {
                            setIsTyping(false);
                            accumulated += event.content;
                            setContent(accumulated);
                        }
                        else if (event.type === 'typing_end') {
                            setIsTyping(false);
                        }
                        else if (event.type === 'suggestions') {
                            const suggestionsData = parseSuggestions(event);
                            if (suggestionsData.length > 0) {
                                setSuggestions(suggestionsData);
                            }
                        }
                        else if (event.type === 'citations' && event.data?.citations) {
                            setCitations(mapSseCitations(event.data.citations));
                        }
                        else if (event.type === 'plan_preview' && event.data?.planId) {
                            setPendingPlanId(event.data.planId);
                            setPendingPlanData(event.data);
                        }
                        else if (event.type === 'action_confirmation' ||
                            event.type === 'action_success' ||
                            event.type === 'action_error' ||
                            event.type === 'dialog_open' ||
                            event.type === 'navigate') {
                            // Task R2-052: Store action/dialog/navigate event for SprkChat to handle via useEffect.
                            // Mirrors the main SSE parser block (R2-039/R2-052) — must include 'navigate'.
                            setPendingActionEvent({
                                type: event.type,
                                data: event.data || {},
                            });
                        }
                        else if (event.type === 'document_stream_start' ||
                            event.type === 'document_stream_token' ||
                            event.type === 'document_stream_end') {
                            // Task R2-051: Same callback forwarding as in the main loop.
                            const handler = onDocumentStreamEventRef.current;
                            if (handler) {
                                const raw = event;
                                if (event.type === 'document_stream_start') {
                                    handler({
                                        type: 'document_stream_start',
                                        operationId: raw.operationId || '',
                                        targetPosition: raw.targetPosition || 'cursor',
                                        operationType: raw.operationType || 'insert',
                                    });
                                }
                                else if (event.type === 'document_stream_token') {
                                    handler({
                                        type: 'document_stream_token',
                                        operationId: raw.operationId || '',
                                        token: raw.content || raw.token || '',
                                        index: raw.index || 0,
                                    });
                                }
                                else if (event.type === 'document_stream_end') {
                                    handler({
                                        type: 'document_stream_end',
                                        operationId: raw.operationId || '',
                                        cancelled: raw.cancelled || false,
                                        totalTokens: raw.totalTokens || 0,
                                    });
                                }
                            }
                        }
                        else if (event.type === 'done') {
                            setIsDone(true);
                            setIsTyping(false);
                        }
                        else if (event.type === 'error') {
                            setIsTyping(false);
                            setError(new Error(event.content || 'Stream error'));
                        }
                    }
                }
                setIsStreaming(false);
                setIsTyping(false);
            }
            catch (err) {
                if (err instanceof DOMException && err.name === 'AbortError') {
                    // Stream was cancelled by user, not an error
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
    }, []);
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
        clearPendingDocumentStreamEvent: () => { }, // No-op: callback pattern doesn't need clearing
        setOnDocumentStreamEvent,
    };
}
/**
 * Maps SSE citation items to the frontend ICitation format.
 * Converts the camelCase `sourceName` from the server to the `source` field
 * expected by CitationMarker/SprkChatCitationPopover components.
 */
function mapSseCitations(items) {
    if (!items || items.length === 0) {
        return [];
    }
    return items.map(item => ({
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
//# sourceMappingURL=useSseStream.js.map