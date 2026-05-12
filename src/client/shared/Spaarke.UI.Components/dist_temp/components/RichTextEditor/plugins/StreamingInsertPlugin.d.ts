/**
 * StreamingInsertPlugin - Lexical plugin for streaming document insertion
 *
 * Receives individual tokens via SSE events and inserts them into the Lexical editor
 * at a target position with cursor tracking and smooth typewriter-like UX.
 *
 * Key design decisions:
 * - Uses requestAnimationFrame-based batching to prevent UI jank at 50-100 tokens/sec
 * - Maintains editor selection/cursor state during streaming
 * - Sets editor to read-only during active streaming to prevent user conflicts
 * - Supports cancellation with optional partial content removal
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9 compatible (no hard-coded colors)
 * @see IDocumentStreamStartEvent, IDocumentStreamTokenEvent, IDocumentStreamEndEvent
 */
import * as React from 'react';
/**
 * Imperative handle exposed by the StreamingInsertPlugin via ref.
 * Consumers use this to drive the streaming lifecycle:
 *   1. startStream() - begin a streaming insertion operation
 *   2. insertToken() - called for each token received from SSE
 *   3. endStream()   - finalize or cancel the operation
 */
export interface StreamingInsertHandle {
    /**
     * Begin a streaming insertion at the current cursor position or a specified target.
     * Sets the editor to read-only and initializes the insertion point.
     *
     * @param targetPosition - Optional Lexical node key to position the cursor at before streaming.
     *                         If omitted, streaming begins at the current selection.
     */
    startStream(targetPosition?: string): void;
    /**
     * Insert a single token (text fragment) at the current streaming position.
     * Tokens are buffered and flushed in batches via requestAnimationFrame for smooth UX.
     *
     * @param token - The text token to insert (may contain whitespace, newlines, etc.)
     */
    insertToken(token: string): void;
    /**
     * End the streaming operation.
     *
     * @param cancelled - If true, removes all content inserted during this streaming operation.
     *                    If false (default), content is kept and the editor returns to editable state.
     */
    endStream(cancelled?: boolean): void;
}
/**
 * Props for the StreamingInsertPlugin component.
 */
export interface StreamingInsertPluginProps {
    /** Whether a streaming operation is currently active (controls visual state) */
    isStreaming: boolean;
    /** Callback invoked when the streaming operation completes (either normally or via cancellation) */
    onStreamingComplete?: (cancelled: boolean) => void;
}
/**
 * StreamingInsertPlugin - A Lexical plugin for handling streaming document insertion.
 *
 * This plugin must be placed inside a <LexicalComposer> tree. It exposes an imperative
 * handle via React.forwardRef that the parent component uses to drive streaming:
 *
 * @example
 * ```tsx
 * const streamRef = useRef<StreamingInsertHandle>(null);
 *
 * // In SSE event handler:
 * streamRef.current?.startStream();
 * streamRef.current?.insertToken("Hello ");
 * streamRef.current?.insertToken("world!");
 * streamRef.current?.endStream();
 *
 * // In the editor tree:
 * <LexicalComposer initialConfig={config}>
 *   <StreamingInsertPlugin
 *     ref={streamRef}
 *     isStreaming={isStreaming}
 *     onStreamingComplete={handleComplete}
 *   />
 * </LexicalComposer>
 * ```
 */
export declare const StreamingInsertPlugin: React.ForwardRefExoticComponent<StreamingInsertPluginProps & React.RefAttributes<StreamingInsertHandle>>;
export default StreamingInsertPlugin;
//# sourceMappingURL=StreamingInsertPlugin.d.ts.map