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
import type { SprkChatBridge } from '../../../services/SprkChatBridge';
import type { RichTextEditorRef } from '../RichTextEditor';
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
export declare function useDocumentStreamConsumer(options: UseDocumentStreamConsumerOptions): UseDocumentStreamConsumerResult;
//# sourceMappingURL=useDocumentStreamConsumer.d.ts.map