/**
 * SSE Stream Hook (useSseStream)
 *
 * React hook for consuming Server-Sent Events from the streaming endpoint.
 * Handles connection, parsing, errors, and cleanup.
 *
 * @version 1.0.0.0
 */
import { useState, useCallback, useRef, useEffect } from 'react';
/**
 * Parse SSE line to extract data
 * SSE format: data: {"content": "token", "done": false}
 */
const parseSseLine = (line) => {
    const trimmed = line.trim();
    // Skip empty lines and comments
    if (!trimmed || trimmed.startsWith(':')) {
        return null;
    }
    // Parse data: prefix
    if (trimmed.startsWith('data:')) {
        const jsonStr = trimmed.slice(5).trim();
        if (!jsonStr || jsonStr === '[DONE]') {
            return { done: true };
        }
        try {
            return JSON.parse(jsonStr);
        }
        catch {
            // If not JSON, treat as plain text content
            return { content: jsonStr };
        }
    }
    return null;
};
/**
 * useSseStream Hook
 *
 * Provides real-time data streaming with status tracking, error handling,
 * and proper cleanup on component unmount.
 *
 * @example
 * ```tsx
 * const { data, status, error, start, abort } = useSseStream({
 *     url: '/api/ai/document-intelligence/analyze',
 *     body: { documentId, driveId, itemId },
 *     token: accessToken,
 *     onComplete: (summary) => console.log('Done:', summary)
 * });
 *
 * // Start streaming
 * start();
 *
 * // Abort if needed
 * abort();
 * ```
 */
export const useSseStream = (options) => {
    const { url, body, token, onChunk, onComplete, onError } = options;
    const [data, setData] = useState('');
    const [status, setStatus] = useState('idle');
    const [error, setError] = useState(null);
    const abortControllerRef = useRef(null);
    const dataRef = useRef('');
    /**
     * Reset to initial state
     */
    const reset = useCallback(() => {
        // Abort any ongoing request
        if (abortControllerRef.current) {
            abortControllerRef.current.abort();
            abortControllerRef.current = null;
        }
        setData('');
        setStatus('idle');
        setError(null);
        dataRef.current = '';
    }, []);
    /**
     * Abort the current stream
     */
    const abort = useCallback(() => {
        if (abortControllerRef.current) {
            abortControllerRef.current.abort();
            abortControllerRef.current = null;
            setStatus('aborted');
        }
    }, []);
    /**
     * Start streaming from the endpoint
     */
    const start = useCallback(async () => {
        // Reset state
        setData('');
        setError(null);
        dataRef.current = '';
        // Create new AbortController
        abortControllerRef.current = new AbortController();
        const signal = abortControllerRef.current.signal;
        setStatus('connecting');
        try {
            const headers = {
                'Content-Type': 'application/json',
                Accept: 'text/event-stream',
            };
            if (token) {
                headers['Authorization'] = `Bearer ${token}`;
            }
            const response = await fetch(url, {
                method: 'POST',
                headers,
                body: body ? JSON.stringify(body) : undefined,
                signal,
            });
            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(errorText || `HTTP ${response.status}: ${response.statusText}`);
            }
            if (!response.body) {
                throw new Error('Response body is not readable');
            }
            setStatus('streaming');
            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';
            while (true) {
                const { done, value } = await reader.read();
                if (done) {
                    // Process any remaining buffer
                    if (buffer.trim()) {
                        const lines = buffer.split('\n');
                        for (const line of lines) {
                            const chunk = parseSseLine(line);
                            if (chunk) {
                                if (chunk.content) {
                                    dataRef.current += chunk.content;
                                    setData(dataRef.current);
                                }
                                onChunk?.(chunk);
                            }
                        }
                    }
                    setStatus('complete');
                    onComplete?.(dataRef.current);
                    break;
                }
                // Decode chunk and add to buffer
                buffer += decoder.decode(value, { stream: true });
                // Process complete lines (SSE events are separated by double newlines)
                const events = buffer.split('\n\n');
                buffer = events.pop() || ''; // Keep incomplete event in buffer
                for (const event of events) {
                    const lines = event.split('\n');
                    for (const line of lines) {
                        const chunk = parseSseLine(line);
                        if (chunk) {
                            if (chunk.done) {
                                setStatus('complete');
                                onComplete?.(dataRef.current);
                                return;
                            }
                            if (chunk.error) {
                                throw new Error(chunk.error);
                            }
                            if (chunk.content) {
                                dataRef.current += chunk.content;
                                setData(dataRef.current);
                            }
                            onChunk?.(chunk);
                        }
                    }
                }
            }
        }
        catch (err) {
            // Handle abort
            if (err instanceof Error && err.name === 'AbortError') {
                setStatus('aborted');
                return;
            }
            // Handle other errors
            const errorMessage = err instanceof Error ? err.message : 'Unknown error occurred';
            setError(errorMessage);
            setStatus('error');
            onError?.(errorMessage);
        }
    }, [url, body, token, onChunk, onComplete, onError]);
    // Cleanup on unmount
    useEffect(() => {
        return () => {
            if (abortControllerRef.current) {
                abortControllerRef.current.abort();
            }
        };
    }, []);
    return {
        data,
        status,
        error,
        start,
        abort,
        reset,
    };
};
export default useSseStream;
//# sourceMappingURL=useSseStream.js.map