/**
 * useDocumentStreaming - AnalysisWorkspace integration hook for document streaming
 *
 * Combines SprkChatBridge, useDocumentStreamConsumer, and useDocumentHistory
 * into a single integration point for the AnalysisWorkspace Code Page.
 *
 * Responsibilities:
 * 1. Initialize SprkChatBridge with workspace context
 * 2. Subscribe to document_stream_start/token/end via useDocumentStreamConsumer
 * 3. Subscribe to document_replaced events for bulk content replacement
 * 4. Push undo snapshots before AI-initiated writes (FR-07)
 * 5. Provide cancel-stream support (keeps partial content, pushes to undo stack)
 * 6. Expose streaming state for UI (StreamingIndicator)
 *
 * Data flow:
 *   SprkChat side pane → BroadcastChannel → SprkChatBridge →
 *   useDocumentStreamConsumer → RichTextEditor streaming ref
 *
 * SECURITY: Auth tokens are NEVER transmitted via BroadcastChannel.
 * Each pane authenticates independently via Xrm.Utility.getGlobalContext().
 *
 * @see SprkChatBridge (services/SprkChatBridge.ts)
 * @see useDocumentStreamConsumer (RichTextEditor/hooks/)
 * @see useDocumentHistory (hooks/useDocumentHistory.ts)
 * @see ADR-012 - Shared Component Library
 */

import { useEffect, useMemo, useRef, useCallback, useState } from "react";
import { SprkChatBridge } from "@spaarke/ui-components/services/SprkChatBridge";
import type { DocumentReplacedPayload } from "@spaarke/ui-components/services/SprkChatBridge";
import { useDocumentStreamConsumer } from "@spaarke/ui-components/components/RichTextEditor/hooks/useDocumentStreamConsumer";
import { useDocumentHistory } from "@spaarke/ui-components/hooks/useDocumentHistory";
import type { RichTextEditorRef } from "@spaarke/ui-components/components/RichTextEditor";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface UseDocumentStreamingOptions {
    /**
     * Workspace context identifier used for BroadcastChannel naming.
     * Typically the analysis session ID. Channel becomes: sprk-workspace-{context}
     */
    context: string;

    /**
     * Ref to the RichTextEditor instance providing streaming + HTML APIs.
     * Must be wired via React.useRef<RichTextEditorRef>(null).
     */
    editorRef: React.RefObject<RichTextEditorRef | null>;

    /**
     * Whether the bridge should be active. Set to false to disable
     * bridge subscriptions (e.g., when editor is not mounted yet).
     */
    enabled?: boolean;
}

export interface UseDocumentStreamingResult {
    /** Whether a streaming operation is currently in progress */
    isStreaming: boolean;

    /** The operationId of the current (or last) streaming operation */
    operationId: string | null;

    /** Number of tokens received in the current streaming operation */
    tokenCount: number;

    /** Whether a document replacement is in progress */
    isReplacing: boolean;

    /** Cancel the current streaming operation (keeps partial content on undo stack) */
    cancelStream: () => void;

    /** The SprkChatBridge instance (for sending events back, e.g., cancel ack) */
    bridge: SprkChatBridge | null;

    /** Document history controls (undo/redo) */
    history: {
        undo: () => void;
        redo: () => void;
        canUndo: boolean;
        canRedo: boolean;
        historyLength: number;
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

export function useDocumentStreaming(
    options: UseDocumentStreamingOptions
): UseDocumentStreamingResult {
    const { context, editorRef, enabled = true } = options;

    const [isReplacing, setIsReplacing] = useState(false);

    // Track whether a cancel was requested to coordinate with stream end
    const cancelRequestedRef = useRef(false);
    // Track active operation for cancel acknowledgment
    const activeOperationRef = useRef<string | null>(null);

    // ─────────────────────────────────────────────────────────────────────
    // 1. Initialize SprkChatBridge
    // ─────────────────────────────────────────────────────────────────────

    const bridge = useMemo(() => {
        if (!enabled || !context) {
            return null;
        }
        try {
            return new SprkChatBridge({ context, transport: "auto" });
        } catch (err) {
            console.error(
                "[useDocumentStreaming] Failed to create SprkChatBridge:",
                err
            );
            return null;
        }
    }, [context, enabled]);

    // Disconnect bridge on unmount or when context changes
    useEffect(() => {
        return () => {
            if (bridge && !bridge.isDisconnected) {
                bridge.disconnect();
            }
        };
    }, [bridge]);

    // ─────────────────────────────────────────────────────────────────────
    // 2. Initialize document history for undo/redo (FR-07)
    // ─────────────────────────────────────────────────────────────────────

    const documentHistory = useDocumentHistory(editorRef);

    // ─────────────────────────────────────────────────────────────────────
    // 3. Wire useDocumentStreamConsumer with FR-07 snapshot callback
    // ─────────────────────────────────────────────────────────────────────

    const handleBeforeStreamStart = useCallback(() => {
        // FR-07: Push current editor content to undo stack BEFORE
        // the streaming write begins. This ensures the user can undo
        // back to the exact pre-stream state.
        documentHistory.pushVersion();
    }, [documentHistory]);

    const handleStreamStart = useCallback((opId: string) => {
        activeOperationRef.current = opId;
        cancelRequestedRef.current = false;
    }, []);

    const handleStreamEnd = useCallback(
        (opId: string, cancelled: boolean) => {
            activeOperationRef.current = null;

            // After stream ends (normal or cancelled), push the final state
            // to the undo stack so the user has a snapshot of the completed write.
            // For cancelled streams, the partial content is already in the editor
            // (useDocumentStreamConsumer keeps it) — we snapshot that too.
            documentHistory.pushVersion();

            if (cancelRequestedRef.current && bridge) {
                // Send cancel acknowledgment back to SprkChat side pane
                // so it knows to stop sending tokens
                bridge.emit("document_stream_end", {
                    operationId: opId,
                    cancelled: true,
                    totalTokens: 0,
                });
            }
            cancelRequestedRef.current = false;
        },
        [documentHistory, bridge]
    );

    const streamConsumer = useDocumentStreamConsumer({
        bridge,
        editorRef,
        onBeforeStreamStart: handleBeforeStreamStart,
        onStreamStart: handleStreamStart,
        onStreamEnd: handleStreamEnd,
    });

    // ─────────────────────────────────────────────────────────────────────
    // 4. Subscribe to document_replaced for bulk content replacement
    // ─────────────────────────────────────────────────────────────────────

    useEffect(() => {
        if (!bridge) {
            return;
        }

        const unsubReplace = bridge.subscribe(
            "document_replaced",
            (payload: DocumentReplacedPayload) => {
                const editor = editorRef.current;
                if (!editor) {
                    console.warn(
                        "[useDocumentStreaming] Editor ref is null; cannot handle document_replaced."
                    );
                    return;
                }

                // Reject replacement if streaming is active
                if (streamConsumer.isStreaming) {
                    console.warn(
                        "[useDocumentStreaming] Rejecting document_replaced during active stream."
                    );
                    return;
                }

                setIsReplacing(true);

                try {
                    // FR-07: Push current content to undo stack BEFORE replacing
                    documentHistory.pushVersion();

                    // Replace entire editor content
                    editor.setHtml(payload.html);

                    // Push the new content to undo stack after replacement
                    documentHistory.pushVersion();
                } catch (err) {
                    console.error(
                        "[useDocumentStreaming] Error handling document_replaced:",
                        err
                    );
                } finally {
                    setIsReplacing(false);
                }
            }
        );

        return () => {
            unsubReplace();
        };
    }, [bridge, editorRef, documentHistory, streamConsumer.isStreaming]);

    // ─────────────────────────────────────────────────────────────────────
    // 5. Cancel stream handler
    // ─────────────────────────────────────────────────────────────────────

    const cancelStream = useCallback(() => {
        if (!streamConsumer.isStreaming) {
            return;
        }

        cancelRequestedRef.current = true;

        // The useDocumentStreamConsumer does not expose a direct cancel method.
        // Instead, we emit a document_stream_end with cancelled: true to the bridge.
        // The producer (SprkChat) will stop sending tokens, and when the actual
        // stream_end event arrives, handleStreamEnd will finalize.
        //
        // If the bridge is available, send a cancel signal to the producer side.
        if (bridge && activeOperationRef.current) {
            bridge.emit("document_stream_end", {
                operationId: activeOperationRef.current,
                cancelled: true,
                totalTokens: streamConsumer.tokenCount,
            });
        }
    }, [streamConsumer.isStreaming, streamConsumer.tokenCount, bridge]);

    // ─────────────────────────────────────────────────────────────────────
    // 6. Return combined result
    // ─────────────────────────────────────────────────────────────────────

    return {
        isStreaming: streamConsumer.isStreaming,
        operationId: streamConsumer.operationId,
        tokenCount: streamConsumer.tokenCount,
        isReplacing,
        cancelStream,
        bridge,
        history: {
            undo: documentHistory.undo,
            redo: documentHistory.redo,
            canUndo: documentHistory.canUndo,
            canRedo: documentHistory.canRedo,
            historyLength: documentHistory.historyLength,
        },
    };
}
