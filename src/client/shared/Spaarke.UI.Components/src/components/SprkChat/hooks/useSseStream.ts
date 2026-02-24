/**
 * useSseStream - SSE connection hook using fetch with ReadableStream
 *
 * Connects to SSE endpoints via POST fetch (EventSource only supports GET).
 * Parses "data: {...}\n\n" events from the stream and accumulates token content.
 *
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useRef, useCallback)
 * @see ChatEndpoints.cs - SSE format: data: {"type":"token","content":"..."}\n\n
 */

import { useState, useRef, useCallback } from "react";
import { IChatSseEvent, IUseSseStreamResult } from "../types";

/**
 * Parse a single SSE data line into a ChatSseEvent.
 * Expects format: data: {"type":"token","content":"..."}
 */
export function parseSseEvent(line: string): IChatSseEvent | null {
    const trimmed = line.trim();
    if (!trimmed.startsWith("data: ")) {
        return null;
    }

    const jsonStr = trimmed.substring(6); // Remove "data: " prefix
    if (!jsonStr) {
        return null;
    }

    try {
        const parsed = JSON.parse(jsonStr) as IChatSseEvent;
        if (parsed && typeof parsed.type === "string") {
            return parsed;
        }
        return null;
    } catch {
        return null;
    }
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
export function useSseStream(): IUseSseStreamResult {
    const [content, setContent] = useState<string>("");
    const [isDone, setIsDone] = useState<boolean>(false);
    const [error, setError] = useState<Error | null>(null);
    const [isStreaming, setIsStreaming] = useState<boolean>(false);

    const abortControllerRef = useRef<AbortController | null>(null);

    const cancelStream = useCallback(() => {
        if (abortControllerRef.current) {
            abortControllerRef.current.abort();
            abortControllerRef.current = null;
        }
        setIsStreaming(false);
    }, []);

    /**
     * Extract tenant ID from JWT access token for X-Tenant-Id header.
     */
    const extractTenantId = (token: string): string | null => {
        try {
            const parts = token.split(".");
            if (parts.length !== 3) return null;
            const payload = JSON.parse(atob(parts[1]));
            return payload.tid || null;
        } catch {
            return null;
        }
    };

    const startStream = useCallback(
        (url: string, body: Record<string, unknown>, token: string) => {
            // Cancel any existing stream
            if (abortControllerRef.current) {
                abortControllerRef.current.abort();
            }

            // Reset state
            setContent("");
            setIsDone(false);
            setError(null);
            setIsStreaming(true);

            const controller = new AbortController();
            abortControllerRef.current = controller;

            const fetchStream = async () => {
                try {
                    const tenantId = extractTenantId(token);
                    const response = await fetch(url, {
                        method: "POST",
                        headers: {
                            "Content-Type": "application/json",
                            Authorization: `Bearer ${token}`,
                            ...(tenantId ? { "X-Tenant-Id": tenantId } : {}),
                        },
                        body: JSON.stringify(body),
                        signal: controller.signal,
                    });

                    if (!response.ok) {
                        const errorText = await response.text();
                        throw new Error(
                            `Chat request failed (${response.status}): ${errorText}`
                        );
                    }

                    if (!response.body) {
                        throw new Error("Response body is empty");
                    }

                    const reader = response.body.getReader();
                    const decoder = new TextDecoder();
                    let buffer = "";
                    let accumulated = "";

                    while (true) {
                        const { done, value } = await reader.read();
                        if (done) {
                            break;
                        }

                        buffer += decoder.decode(value, { stream: true });

                        // SSE events are separated by double newlines
                        const parts = buffer.split("\n\n");
                        // Keep the last incomplete part in the buffer
                        buffer = parts.pop() || "";

                        for (const part of parts) {
                            const lines = part.split("\n");
                            for (const line of lines) {
                                const event = parseSseEvent(line);
                                if (!event) {
                                    continue;
                                }

                                if (event.type === "token" && event.content) {
                                    accumulated += event.content;
                                    // Use functional update to ensure correct state
                                    setContent(accumulated);
                                } else if (event.type === "done") {
                                    setIsDone(true);
                                    setIsStreaming(false);
                                } else if (event.type === "error") {
                                    throw new Error(
                                        event.content || "Stream error"
                                    );
                                }
                            }
                        }
                    }

                    // Process any remaining buffer
                    if (buffer.trim()) {
                        const lines = buffer.split("\n");
                        for (const line of lines) {
                            const event = parseSseEvent(line);
                            if (!event) {
                                continue;
                            }
                            if (event.type === "token" && event.content) {
                                accumulated += event.content;
                                setContent(accumulated);
                            } else if (event.type === "done") {
                                setIsDone(true);
                            } else if (event.type === "error") {
                                setError(
                                    new Error(event.content || "Stream error")
                                );
                            }
                        }
                    }

                    setIsStreaming(false);
                } catch (err: unknown) {
                    if (err instanceof DOMException && err.name === "AbortError") {
                        // Stream was cancelled by user, not an error
                        setIsStreaming(false);
                        return;
                    }

                    const errorObj =
                        err instanceof Error
                            ? err
                            : new Error("Unknown stream error");
                    setError(errorObj);
                    setIsStreaming(false);
                }
            };

            fetchStream();
        },
        []
    );

    return {
        content,
        isDone,
        error,
        isStreaming,
        startStream,
        cancelStream,
    };
}
