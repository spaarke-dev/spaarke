/**
 * SSE Stream Hook (useSseStream)
 *
 * React hook for consuming Server-Sent Events from the streaming endpoint.
 * Handles connection, parsing, errors, and cleanup.
 *
 * @version 1.0.0.0
 */
/**
 * SSE stream status
 */
export type SseStreamStatus = 'idle' | 'connecting' | 'streaming' | 'complete' | 'error' | 'aborted';
/**
 * SSE data chunk format from server
 */
export interface SseDataChunk {
    /** Content token/text chunk */
    content?: string;
    /** Whether streaming is complete */
    done?: boolean;
    /** Error message if any */
    error?: string;
    /** Document ID for tracking */
    documentId?: string;
}
/**
 * Hook return type
 */
export interface UseSseStreamResult {
    /** Accumulated data string */
    data: string;
    /** Current connection status */
    status: SseStreamStatus;
    /** Error message if status is 'error' */
    error: string | null;
    /** Start streaming from the endpoint */
    start: () => void;
    /** Abort the current stream */
    abort: () => void;
    /** Reset to initial state */
    reset: () => void;
}
/**
 * Hook options
 */
export interface UseSseStreamOptions {
    /** URL to stream from */
    url: string;
    /** Request body (will be JSON stringified) */
    body?: object;
    /** Authorization token */
    token?: string;
    /** Callback when data chunk received */
    onChunk?: (chunk: SseDataChunk) => void;
    /** Callback when stream completes */
    onComplete?: (data: string) => void;
    /** Callback on error */
    onError?: (error: string) => void;
}
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
export declare const useSseStream: (options: UseSseStreamOptions) => UseSseStreamResult;
export default useSseStream;
//# sourceMappingURL=useSseStream.d.ts.map