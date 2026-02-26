/**
 * useDiffReview - Hook for managing AI-proposed revision review via diff panel
 *
 * Subscribes to SprkChatBridge events for diff-mode streaming. When the AI
 * sends a stream with operationType "diff" (via document_stream_start), this
 * hook collects tokens into a buffer instead of writing them directly to the
 * editor. On document_stream_end, it opens the DiffReviewPanel with the
 * buffered content for user review.
 *
 * Responsibilities:
 * 1. Subscribe to document_stream_start with operationType="diff"
 * 2. Buffer tokens during diff-mode streaming
 * 3. Open DiffReviewPanel on stream_end with original + proposed content
 * 4. Handle Accept (push undo, replace editor content)
 * 5. Handle Reject (close panel, discard buffer)
 * 6. Auto-reject on new stream start (prevents stale diffs)
 *
 * Task 103: Wire DiffCompareView into Analysis Workspace
 *
 * @see DiffReviewPanel
 * @see SprkChatBridge
 * @see useDocumentHistory
 * @see ADR-012 - Shared component library
 */

import { useCallback, useEffect, useRef, useState } from "react";
import type { RichTextEditorRef } from "@spaarke/ui-components";
import type { SprkChatBridge } from "@spaarke/ui-components/services/SprkChatBridge";
import type {
    DocumentStreamStartPayload,
    DocumentStreamTokenPayload,
    DocumentStreamEndPayload,
} from "@spaarke/ui-components/services/SprkChatBridge";
import type { DiffReviewState } from "../types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseDiffReviewOptions {
    /** SprkChatBridge instance for subscribing to stream events */
    bridge: SprkChatBridge | null;
    /** Ref to the RichTextEditor for reading current content */
    editorRef: React.RefObject<RichTextEditorRef | null>;
    /** Whether the hook should be active */
    enabled: boolean;
    /**
     * Push a snapshot to the undo stack. Called before replacing editor
     * content on Accept so the user can undo back.
     */
    pushUndoVersion: () => void;
}

export interface UseDiffReviewResult {
    /** Current diff review state (for DiffReviewPanel props) */
    diffState: DiffReviewState;
    /** Accept the proposed text: pushes undo, replaces editor, closes panel */
    acceptDiff: (acceptedText: string) => void;
    /** Reject the proposed changes: closes panel, discards buffer */
    rejectDiff: () => void;
    /** Whether a diff-mode stream is currently in progress (buffering) */
    isDiffStreaming: boolean;
}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const INITIAL_DIFF_STATE: DiffReviewState = {
    isOpen: false,
    originalText: "",
    proposedText: "",
    operationId: null,
};

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useDiffReview(options: UseDiffReviewOptions): UseDiffReviewResult {
    const { bridge, editorRef, enabled, pushUndoVersion } = options;

    const [diffState, setDiffState] = useState<DiffReviewState>(INITIAL_DIFF_STATE);
    const [isDiffStreaming, setIsDiffStreaming] = useState(false);

    // Mutable refs for token buffering (avoids re-renders per token)
    const tokenBufferRef = useRef<string[]>([]);
    const activeDiffOpRef = useRef<string | null>(null);
    const originalContentRef = useRef<string>("");
    // Track isOpen via ref to avoid stale closures in bridge event handlers
    const isOpenRef = useRef(false);
    isOpenRef.current = diffState.isOpen;

    // ---- Accept handler ----
    const acceptDiff = useCallback(
        (acceptedText: string) => {
            const editor = editorRef.current;
            if (!editor) {
                console.warn("[useDiffReview] Editor ref is null; cannot apply diff.");
                setDiffState(INITIAL_DIFF_STATE);
                return;
            }

            // Push current content to undo stack before replacing
            pushUndoVersion();

            // Replace editor content with accepted text
            editor.setHtml(acceptedText);

            // Push new content to undo stack after replacement
            pushUndoVersion();

            // Close panel
            setDiffState(INITIAL_DIFF_STATE);
        },
        [editorRef, pushUndoVersion],
    );

    // ---- Reject handler ----
    const rejectDiff = useCallback(() => {
        // Close panel, discard everything
        setDiffState(INITIAL_DIFF_STATE);
        tokenBufferRef.current = [];
        activeDiffOpRef.current = null;
    }, []);

    // ---- Subscribe to bridge events ----
    useEffect(() => {
        if (!bridge || !enabled) {
            return;
        }

        // Handler: document_stream_start
        // When operationType is "diff", capture the current editor content
        // and start buffering tokens instead of writing to the editor.
        const unsubStart = bridge.subscribe(
            "document_stream_start",
            (payload: DocumentStreamStartPayload) => {
                // Auto-reject any existing open diff review when a new stream starts
                if (isOpenRef.current) {
                    setDiffState(INITIAL_DIFF_STATE);
                }

                // Only handle diff-mode streams
                if (payload.operationType !== "diff") {
                    activeDiffOpRef.current = null;
                    return;
                }

                // Capture current editor content as the "original" for comparison
                const currentContent = editorRef.current?.getHtml() ?? "";
                originalContentRef.current = currentContent;

                // Reset token buffer
                tokenBufferRef.current = [];
                activeDiffOpRef.current = payload.operationId;
                setIsDiffStreaming(true);
            },
        );

        // Handler: document_stream_token
        // If this is a diff-mode stream, buffer the token instead of writing.
        const unsubToken = bridge.subscribe(
            "document_stream_token",
            (payload: DocumentStreamTokenPayload) => {
                if (activeDiffOpRef.current !== payload.operationId) {
                    return; // Not a diff-mode stream, let DocumentStreamBridge handle it
                }
                tokenBufferRef.current.push(payload.token);
            },
        );

        // Handler: document_stream_end
        // If this is a diff-mode stream, join the buffer and open DiffReviewPanel.
        const unsubEnd = bridge.subscribe(
            "document_stream_end",
            (payload: DocumentStreamEndPayload) => {
                if (activeDiffOpRef.current !== payload.operationId) {
                    return; // Not a diff-mode stream
                }

                const proposedText = tokenBufferRef.current.join("");
                setIsDiffStreaming(false);

                // Only open panel if we have proposed content and it wasn't cancelled
                if (!payload.cancelled && proposedText.length > 0) {
                    setDiffState({
                        isOpen: true,
                        originalText: originalContentRef.current,
                        proposedText,
                        operationId: payload.operationId,
                    });
                }

                // Clean up buffer
                tokenBufferRef.current = [];
                activeDiffOpRef.current = null;
            },
        );

        return () => {
            unsubStart();
            unsubToken();
            unsubEnd();
        };
    }, [bridge, enabled, editorRef]);

    return {
        diffState,
        acceptDiff,
        rejectDiff,
        isDiffStreaming,
    };
}
