/**
 * useDocumentStreamConsumer - Bridge-to-editor streaming write consumer
 *
 * Subscribes to document_stream_* events from the SprkChatBridge and drives
 * the RichTextEditor's streaming ref API (beginStreamingInsert, appendStreamToken,
 * endStreamingInsert). This hook is the consumer-side counterpart to the
 * producer-side useSseStream + bridge.emit() flow.
 *
 * Data flow:
 *   BFF SSE → useSseStream → SprkChatBridge.emit() → [BroadcastChannel] →
 *   SprkChatBridge.subscribe() → useDocumentStreamConsumer → RichTextEditor ref
 *
 * SECURITY: This hook never handles auth tokens. The bridge only transmits
 * document/selection events (ADR-015).
 *
 * NFR-01: Per-token latency target < 100ms from SSE event receipt to DOM update.
 * NFR-03: BroadcastChannel delivery within 10ms.
 *
 * @see SprkChatBridge (services/SprkChatBridge.ts)
 * @see RichTextEditor (RichTextEditor.tsx) - streaming ref API
 * @see StreamingInsertPlugin - underlying Lexical plugin
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - No auth tokens via BroadcastChannel
 */

import { useEffect, useRef, useCallback, useState } from "react";
import type { SprkChatBridge } from "../../../services/SprkChatBridge";
import type {
    DocumentStreamStartPayload,
    DocumentStreamTokenPayload,
    DocumentStreamEndPayload,
} from "../../../services/SprkChatBridge";
import type { RichTextEditorRef } from "../RichTextEditor";
import type { StreamingInsertHandle } from "../plugins/StreamingInsertPlugin";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Options for the useDocumentStreamConsumer hook.
 */
export interface UseDocumentStreamConsumerOptions {
    /** The SprkChatBridge instance to subscribe to */
    bridge: SprkChatBridge | null;

    /** Ref to the RichTextEditor providing the streaming API */
    editorRef: React.RefObject<RichTextEditorRef | null>;

    /**
     * Optional callback invoked BEFORE a streaming operation begins.
     * Called before beginStreamingInsert() to allow snapshotting the current
     * editor state (e.g., via useDocumentHistory.pushVersion()).
     *
     * Per FR-07: Every AI-initiated modification MUST snapshot before writing.
     */
    onBeforeStreamStart?: () => void;

    /**
     * Optional callback invoked when a streaming operation starts.
     * Receives the operationId for tracking.
     */
    onStreamStart?: (operationId: string) => void;

    /**
     * Optional callback invoked when a streaming operation ends.
     * Receives the operationId and whether it was cancelled.
     */
    onStreamEnd?: (operationId: string, cancelled: boolean) => void;

    /**
     * Optional callback invoked on each token for latency measurement.
     * Receives the token index and a high-resolution timestamp.
     */
    onTokenReceived?: (index: number, timestamp: number) => void;
}

/**
 * Return type for the useDocumentStreamConsumer hook.
 */
export interface UseDocumentStreamConsumerResult {
    /** Whether a streaming operation is currently active */
    isStreaming: boolean;

    /** The operationId of the current (or last) streaming operation */
    operationId: string | null;

    /** Number of tokens received in the current operation */
    tokenCount: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook that subscribes to document_stream_* events from a SprkChatBridge
 * and calls the RichTextEditor's streaming ref methods.
 *
 * Place this hook in the Analysis Workspace (editor pane) component.
 * The SprkChat side pane is the producer; this hook is the consumer.
 *
 * @param options - Bridge, editor ref, and optional callbacks
 * @returns Streaming state for UI rendering
 *
 * @example
 * ```tsx
 * function AnalysisWorkspace() {
 *     const bridge = useMemo(() => new SprkChatBridge({ context: sessionId }), [sessionId]);
 *     const editorRef = useRef<RichTextEditorRef>(null);
 *
 *     const { isStreaming, tokenCount } = useDocumentStreamConsumer({
 *         bridge,
 *         editorRef,
 *     });
 *
 *     return (
 *         <>
 *             <RichTextEditor ref={editorRef} value="" onChange={() => {}} />
 *             {isStreaming && <div>Streaming... ({tokenCount} tokens)</div>}
 *         </>
 *     );
 * }
 * ```
 */
export function useDocumentStreamConsumer(
    options: UseDocumentStreamConsumerOptions
): UseDocumentStreamConsumerResult {
    const { bridge, editorRef, onBeforeStreamStart, onStreamStart, onStreamEnd, onTokenReceived } = options;

    const [isStreaming, setIsStreaming] = useState(false);
    const [operationId, setOperationId] = useState<string | null>(null);
    const [tokenCount, setTokenCount] = useState(0);

    // Track the active streaming handle so we can call appendStreamToken/endStreamingInsert
    const streamHandleRef = useRef<StreamingInsertHandle | null>(null);
    // Track the active operationId in a ref (avoids stale closures)
    const activeOperationIdRef = useRef<string | null>(null);

    // Stable refs for callbacks to avoid re-subscribing on every render
    const onBeforeStreamStartRef = useRef(onBeforeStreamStart);
    onBeforeStreamStartRef.current = onBeforeStreamStart;
    const onStreamStartRef = useRef(onStreamStart);
    onStreamStartRef.current = onStreamStart;
    const onStreamEndRef = useRef(onStreamEnd);
    onStreamEndRef.current = onStreamEnd;
    const onTokenReceivedRef = useRef(onTokenReceived);
    onTokenReceivedRef.current = onTokenReceived;

    // ─────────────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────────────

    const handleStreamStart = useCallback(
        (payload: DocumentStreamStartPayload) => {
            const editor = editorRef.current;
            if (!editor) {
                console.warn(
                    "[useDocumentStreamConsumer] Editor ref is null; cannot start streaming."
                );
                return;
            }

            // Map bridge targetPosition to editor position parameter
            const position: "end" | "cursor" =
                payload.targetPosition === "cursor" ? "cursor" : "end";

            try {
                // FR-07: Snapshot pre-stream state BEFORE beginning the streaming insert.
                // This enables useDocumentHistory.undo() to restore the exact document
                // state before the AI write began — critical for cancel + undo flow.
                onBeforeStreamStartRef.current?.();

                const handle = editor.beginStreamingInsert(position);
                streamHandleRef.current = handle;
                activeOperationIdRef.current = payload.operationId;

                setIsStreaming(true);
                setOperationId(payload.operationId);
                setTokenCount(0);

                onStreamStartRef.current?.(payload.operationId);
            } catch (err) {
                console.error(
                    "[useDocumentStreamConsumer] Failed to begin streaming insert:",
                    err
                );
            }
        },
        [editorRef]
    );

    const handleStreamToken = useCallback(
        (payload: DocumentStreamTokenPayload) => {
            // Verify this token belongs to the active operation
            if (payload.operationId !== activeOperationIdRef.current) {
                return;
            }

            const handle = streamHandleRef.current;
            const editor = editorRef.current;
            if (!handle || !editor) {
                return;
            }

            editor.appendStreamToken(handle, payload.token);
            setTokenCount((prev) => prev + 1);

            // Latency measurement callback
            onTokenReceivedRef.current?.(payload.index, performance.now());
        },
        [editorRef]
    );

    const handleStreamEnd = useCallback(
        (payload: DocumentStreamEndPayload) => {
            // Verify this end belongs to the active operation
            if (payload.operationId !== activeOperationIdRef.current) {
                return;
            }

            const handle = streamHandleRef.current;
            const editor = editorRef.current;
            if (handle && editor) {
                // Per FR-06: cancelled writes PRESERVE partial content in the editor.
                // Pass cancelled=false to endStreamingInsert so content is kept.
                // The user can then use useDocumentHistory.undo() to revert
                // to the pre-stream state captured by pushVersion().
                //
                // Note: StreamingInsertPlugin.endStream(true) would REMOVE content,
                // which is NOT what we want for cancelled AI writes.
                editor.endStreamingInsert(handle);
            }

            streamHandleRef.current = null;
            activeOperationIdRef.current = null;

            setIsStreaming(false);

            onStreamEndRef.current?.(payload.operationId, payload.cancelled);
        },
        [editorRef]
    );

    // ─────────────────────────────────────────────────────────────────────
    // Bridge subscriptions
    // ─────────────────────────────────────────────────────────────────────

    useEffect(() => {
        if (!bridge) {
            return;
        }

        const unsubStart = bridge.subscribe(
            "document_stream_start",
            handleStreamStart
        );
        const unsubToken = bridge.subscribe(
            "document_stream_token",
            handleStreamToken
        );
        const unsubEnd = bridge.subscribe(
            "document_stream_end",
            handleStreamEnd
        );

        return () => {
            unsubStart();
            unsubToken();
            unsubEnd();
        };
    }, [bridge, handleStreamStart, handleStreamToken, handleStreamEnd]);

    // ─────────────────────────────────────────────────────────────────────
    // Cleanup on unmount: end any active streaming operation
    // ─────────────────────────────────────────────────────────────────────

    useEffect(() => {
        return () => {
            if (streamHandleRef.current && editorRef.current) {
                try {
                    editorRef.current.endStreamingInsert(streamHandleRef.current);
                } catch {
                    // Editor may already be unmounted; ignore
                }
            }
            streamHandleRef.current = null;
            activeOperationIdRef.current = null;
        };
    }, [editorRef]);

    return {
        isStreaming,
        operationId,
        tokenCount,
    };
}
