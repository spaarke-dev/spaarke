/**
 * useStreamingInsert - Hook for managing streaming document insertion in Lexical
 *
 * Provides a convenience wrapper around StreamingInsertPlugin that manages:
 * - A ref for the StreamingInsertHandle
 * - Streaming state (isStreaming, tokenCount, operationId)
 * - Integration with IDocumentStreamEvent SSE event types
 *
 * Usage pattern:
 *   1. Spread `pluginProps` onto <StreamingInsertPlugin>
 *   2. Pass `pluginRef` as the ref to <StreamingInsertPlugin>
 *   3. Call `handleStreamEvent(event)` for each IDocumentStreamEvent from SSE
 *   — OR use the manual API: `startStream()`, `insertToken()`, `endStream()`
 *
 * @see StreamingInsertPlugin
 * @see IDocumentStreamEvent (SprkChat/types.ts)
 * @see ADR-012 - Shared Component Library
 */
import * as React from 'react';
import type { StreamingInsertHandle, StreamingInsertPluginProps } from './StreamingInsertPlugin';
import type { IDocumentStreamEvent } from '../../SprkChat/types';
/**
 * Return type for the useStreamingInsert hook.
 */
export interface UseStreamingInsertResult {
    /** Ref to pass to <StreamingInsertPlugin ref={pluginRef}> */
    pluginRef: React.RefObject<StreamingInsertHandle | null>;
    /** Props to spread onto <StreamingInsertPlugin {...pluginProps}> */
    pluginProps: StreamingInsertPluginProps;
    /** Whether a streaming operation is currently active */
    isStreaming: boolean;
    /** Number of tokens inserted so far in the current operation */
    tokenCount: number;
    /** The operationId of the current (or last) streaming operation */
    operationId: string | null;
    /**
     * Handle an IDocumentStreamEvent from SSE.
     * Dispatches to startStream/insertToken/endStream based on event.type.
     *
     * @param event - A discriminated union event from the document streaming SSE
     */
    handleStreamEvent: (event: IDocumentStreamEvent) => void;
    /**
     * Manually start a streaming operation.
     * Prefer handleStreamEvent() when consuming SSE events directly.
     *
     * @param operationId - Unique ID for this operation
     * @param targetPosition - Optional Lexical node key for insertion point
     */
    startStream: (operationId: string, targetPosition?: string) => void;
    /**
     * Manually insert a single token.
     *
     * @param token - The text token to insert
     */
    insertToken: (token: string) => void;
    /**
     * Manually end the streaming operation.
     *
     * @param cancelled - If true, removes content inserted during this operation
     */
    endStream: (cancelled?: boolean) => void;
}
/**
 * Hook for managing streaming document insertion with the StreamingInsertPlugin.
 *
 * @param onComplete - Optional callback invoked when a streaming operation finishes
 * @returns UseStreamingInsertResult with ref, props, state, and control functions
 *
 * @example
 * ```tsx
 * function MyEditor() {
 *     const {
 *         pluginRef, pluginProps, isStreaming,
 *         tokenCount, handleStreamEvent,
 *     } = useStreamingInsert();
 *
 *     // In SSE event handler:
 *     const onSseEvent = (event: IDocumentStreamEvent) => {
 *         handleStreamEvent(event);
 *     };
 *
 *     return (
 *         <LexicalComposer initialConfig={config}>
 *             <RichTextPlugin ... />
 *             <StreamingInsertPlugin ref={pluginRef} {...pluginProps} />
 *             {isStreaming && <div>Streaming... ({tokenCount} tokens)</div>}
 *         </LexicalComposer>
 *     );
 * }
 * ```
 */
export declare function useStreamingInsert(onComplete?: (operationId: string | null, cancelled: boolean) => void): UseStreamingInsertResult;
//# sourceMappingURL=useStreamingInsert.d.ts.map