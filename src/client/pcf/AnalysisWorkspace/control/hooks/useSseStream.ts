/**
 * SSE Stream Hook
 *
 * Custom React hook for consuming Server-Sent Events (SSE) streams
 * from the BFF API for AI chat responses.
 *
 * Features:
 * - Automatic reconnection on failure
 * - Token-by-token streaming for real-time display
 * - Error handling and retry logic
 * - Cleanup on unmount
 *
 * Task 056: Implement SSE Client for Chat Streaming
 */

import * as React from "react";
import { logInfo, logError, logWarn } from "../utils/logger";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface ISseStreamOptions {
    /** Base URL for the BFF API */
    apiBaseUrl: string;
    /** Analysis ID for context */
    analysisId: string;
    /** Callback for each token received */
    onToken: (token: string) => void;
    /** Callback when stream completes */
    onComplete: (fullResponse: string) => void;
    /** Callback on error */
    onError: (error: Error) => void;
    /** Optional headers (e.g., auth token) */
    headers?: Record<string, string>;
}

export interface ISseStreamState {
    /** Whether the stream is currently active */
    isStreaming: boolean;
    /** Accumulated response text */
    responseText: string;
    /** Any error that occurred */
    error: Error | null;
}

export interface ISseStreamActions {
    /** Start streaming a chat message */
    sendMessage: (message: string, chatHistory?: Array<{ role: string; content: string }>) => Promise<void>;
    /** Abort the current stream */
    abort: () => void;
    /** Reset state */
    reset: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const MAX_RETRIES = 3;
const RETRY_DELAY_MS = 1000;

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

export function useSseStream(options: ISseStreamOptions): [ISseStreamState, ISseStreamActions] {
    const { apiBaseUrl, analysisId, onToken, onComplete, onError, headers } = options;

    // State
    const [isStreaming, setIsStreaming] = React.useState(false);
    const [responseText, setResponseText] = React.useState("");
    const [error, setError] = React.useState<Error | null>(null);

    // Refs for cleanup
    const abortControllerRef = React.useRef<AbortController | null>(null);
    const retryCountRef = React.useRef(0);

    // Cleanup on unmount
    React.useEffect(() => {
        return () => {
            if (abortControllerRef.current) {
                abortControllerRef.current.abort();
            }
        };
    }, []);

    /**
     * Send a message and stream the response
     */
    const sendMessage = React.useCallback(async (
        message: string,
        chatHistory?: Array<{ role: string; content: string }>
    ): Promise<void> => {
        // Abort any existing stream
        if (abortControllerRef.current) {
            abortControllerRef.current.abort();
        }

        // Create new abort controller
        abortControllerRef.current = new AbortController();
        const signal = abortControllerRef.current.signal;

        // Reset state
        setIsStreaming(true);
        setResponseText("");
        setError(null);
        retryCountRef.current = 0;

        logInfo("useSseStream", `Starting stream for analysis: ${analysisId}`);

        try {
            // Build request body - only message, analysisId is in URL path
            const requestBody = {
                message
            };

            // Make fetch request to BFF API continue endpoint
            const response = await fetch(`${apiBaseUrl}/api/ai/analysis/${analysisId}/continue`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "text/event-stream",
                    ...headers
                },
                body: JSON.stringify(requestBody),
                signal
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            // Get reader for streaming
            const reader = response.body?.getReader();
            if (!reader) {
                throw new Error("Response body is not readable");
            }

            const decoder = new TextDecoder();
            let accumulatedText = "";
            let buffer = "";

            // Read stream
            while (true) {
                const { done, value } = await reader.read();

                if (done) {
                    logInfo("useSseStream", "Stream completed");
                    break;
                }

                // Decode chunk
                buffer += decoder.decode(value, { stream: true });

                // Process SSE events (format: "data: {json}\n\n")
                const lines = buffer.split("\n");
                buffer = lines.pop() || ""; // Keep incomplete line in buffer

                for (const line of lines) {
                    if (line.startsWith("data: ")) {
                        const data = line.slice(6).trim();

                        // Check for stream end marker (legacy format)
                        if (data === "[DONE]") {
                            continue;
                        }

                        try {
                            const parsed = JSON.parse(data);

                            // BFF API AnalysisStreamChunk format:
                            // { type: "chunk"|"done"|"error"|"metadata", content, done, error }
                            if (parsed.type === "chunk" && parsed.content) {
                                accumulatedText += parsed.content;
                                setResponseText(accumulatedText);
                                onToken(parsed.content);
                            }

                            // Handle legacy format with "token" field
                            if (parsed.token) {
                                accumulatedText += parsed.token;
                                setResponseText(accumulatedText);
                                onToken(parsed.token);
                            }

                            if (parsed.type === "error" || parsed.error) {
                                throw new Error(parsed.error || "Unknown error");
                            }

                            // Check for stream completion
                            if (parsed.done === true || parsed.type === "done") {
                                logInfo("useSseStream", "Received done signal from server");
                            }
                        } catch (parseError) {
                            // If parse error is from our thrown error, re-throw it
                            if (parseError instanceof Error && parseError.message !== "Unexpected token") {
                                throw parseError;
                            }
                            // If it's not JSON, treat as raw token
                            if (data && data !== "[DONE]") {
                                accumulatedText += data;
                                setResponseText(accumulatedText);
                                onToken(data);
                            }
                        }
                    }
                }
            }

            // Stream completed successfully
            setIsStreaming(false);
            onComplete(accumulatedText);
            logInfo("useSseStream", `Stream completed with ${accumulatedText.length} chars`);

        } catch (err) {
            // Handle abort
            if (err instanceof Error && err.name === "AbortError") {
                logInfo("useSseStream", "Stream aborted by user");
                setIsStreaming(false);
                return;
            }

            // Handle error
            const streamError = err instanceof Error ? err : new Error(String(err));
            logError("useSseStream", "Stream error", streamError);

            // Retry logic
            if (retryCountRef.current < MAX_RETRIES) {
                retryCountRef.current++;
                logWarn("useSseStream", `Retrying (${retryCountRef.current}/${MAX_RETRIES})...`);

                await new Promise(resolve => setTimeout(resolve, RETRY_DELAY_MS));

                // Retry
                return sendMessage(message, chatHistory);
            }

            // Max retries exceeded
            setError(streamError);
            setIsStreaming(false);
            onError(streamError);
        }
    }, [apiBaseUrl, analysisId, headers, onToken, onComplete, onError]);

    /**
     * Abort the current stream
     */
    const abort = React.useCallback(() => {
        if (abortControllerRef.current) {
            abortControllerRef.current.abort();
            abortControllerRef.current = null;
        }
        setIsStreaming(false);
        logInfo("useSseStream", "Stream aborted");
    }, []);

    /**
     * Reset state
     */
    const reset = React.useCallback(() => {
        abort();
        setResponseText("");
        setError(null);
    }, [abort]);

    // Return state and actions
    const state: ISseStreamState = { isStreaming, responseText, error };
    const actions: ISseStreamActions = { sendMessage, abort, reset };

    return [state, actions];
}

export default useSseStream;
