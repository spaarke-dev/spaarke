/**
 * useSseStream - SSE connection hook using fetch with ReadableStream
 *
 * Connects to SSE endpoints via POST fetch (EventSource only supports GET).
 * Parses "data: {...}\n\n" events from the stream and accumulates token content.
 *
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useRef, useCallback)
 * @see ChatEndpoints.cs - SSE format: data: {"type":"token","content":"..."}\n\n
 */
import { IChatSseEvent, IUseSseStreamResult } from '../types';
/**
 * Parse a single SSE data line into a ChatSseEvent.
 * Expects format: data: {"type":"token","content":"..."}
 */
export declare function parseSseEvent(line: string): IChatSseEvent | null;
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
export declare function useSseStream(): IUseSseStreamResult;
//# sourceMappingURL=useSseStream.d.ts.map