/**
 * useDiffReview Hook Tests
 *
 * Tests the diff-mode streaming pipeline: bridge events trigger token buffering,
 * DiffReviewPanel opens on stream_end, Accept replaces editor content with undo,
 * Reject discards changes, and new streams auto-reject existing diffs.
 *
 * @see useDiffReview (hooks/useDiffReview.ts)
 * @see SprkChatBridge (services/SprkChatBridge.ts)
 * @see DiffReviewPanel (components/DiffReviewPanel.tsx)
 * @see ADR-012 - Shared Component Library
 */

import { renderHook, act } from "@testing-library/react";
import { useDiffReview, type UseDiffReviewOptions } from "../hooks/useDiffReview";
import { SprkChatBridge } from "../__tests__/mocks/MockSprkChatBridge";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Create a mock editor ref with getHtml / setHtml */
function createMockEditorRef(initialHtml = "<p>Original content</p>") {
    let html = initialHtml;
    const editorRef = {
        current: {
            focus: jest.fn(),
            getHtml: jest.fn(() => html),
            setHtml: jest.fn((newHtml: string) => {
                html = newHtml;
            }),
            clear: jest.fn(),
            beginStreamingInsert: jest.fn(),
            appendStreamToken: jest.fn(),
            endStreamingInsert: jest.fn(),
        },
    };

    return {
        editorRef: editorRef as unknown as React.RefObject<any>,
        getHtml: () => html,
        setHtml: (newHtml: string) => {
            html = newHtml;
        },
    };
}

/** Emit a complete diff-mode streaming sequence through the bridge */
function emitDiffStream(
    bridge: SprkChatBridge,
    operationId: string,
    tokens: string[],
    options?: { cancelled?: boolean }
) {
    bridge.emit("document_stream_start", {
        operationId,
        targetPosition: "selection",
        operationType: "diff",
    });

    for (let i = 0; i < tokens.length; i++) {
        bridge.emit("document_stream_token", {
            operationId,
            token: tokens[i],
            index: i,
        });
    }

    bridge.emit("document_stream_end", {
        operationId,
        cancelled: options?.cancelled ?? false,
        totalTokens: tokens.length,
    });
}

/** Emit a non-diff (insert mode) streaming sequence */
function emitInsertStream(
    bridge: SprkChatBridge,
    operationId: string,
    tokens: string[]
) {
    bridge.emit("document_stream_start", {
        operationId,
        targetPosition: "end",
        operationType: "insert",
    });

    for (let i = 0; i < tokens.length; i++) {
        bridge.emit("document_stream_token", {
            operationId,
            token: tokens[i],
            index: i,
        });
    }

    bridge.emit("document_stream_end", {
        operationId,
        cancelled: false,
        totalTokens: tokens.length,
    });
}

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("useDiffReview", () => {
    let bridge: SprkChatBridge;
    let mockPushUndoVersion: jest.Mock;

    beforeEach(() => {
        bridge = new SprkChatBridge({ context: "diff-test" });
        mockPushUndoVersion = jest.fn();
    });

    afterEach(() => {
        bridge.disconnect();
    });

    // Helper to render the hook with standard options
    function renderDiffReviewHook(overrides?: Partial<UseDiffReviewOptions>) {
        const { editorRef, getHtml, setHtml } = createMockEditorRef();

        const defaultOptions: UseDiffReviewOptions = {
            bridge,
            editorRef,
            enabled: true,
            pushUndoVersion: mockPushUndoVersion,
        };

        const result = renderHook(() =>
            useDiffReview({ ...defaultOptions, ...overrides })
        );

        return { ...result, editorRef, getHtml, setHtml };
    }

    // -----------------------------------------------------------------------
    // 1. Diff stream start with operationType="diff" starts buffering
    // -----------------------------------------------------------------------

    describe("document_stream_start with operationType='diff'", () => {
        it("streamStart_DiffOperationType_SetsIsDiffStreamingTrue", () => {
            const { result } = renderDiffReviewHook();

            expect(result.current.isDiffStreaming).toBe(false);

            act(() => {
                bridge.emit("document_stream_start", {
                    operationId: "op-1",
                    targetPosition: "selection",
                    operationType: "diff",
                });
            });

            expect(result.current.isDiffStreaming).toBe(true);
        });

        it("streamStart_InsertOperationType_DoesNotActivateDiffMode", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                bridge.emit("document_stream_start", {
                    operationId: "op-insert",
                    targetPosition: "end",
                    operationType: "insert",
                });
            });

            expect(result.current.isDiffStreaming).toBe(false);
        });

        it("streamStart_ReplaceOperationType_DoesNotActivateDiffMode", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                bridge.emit("document_stream_start", {
                    operationId: "op-replace",
                    targetPosition: "end",
                    operationType: "replace",
                });
            });

            expect(result.current.isDiffStreaming).toBe(false);
        });
    });

    // -----------------------------------------------------------------------
    // 2. Token accumulation in buffer
    // -----------------------------------------------------------------------

    describe("document_stream_token accumulation", () => {
        it("streamToken_DiffMode_TokensBufferedNotWrittenToEditor", () => {
            const { result, editorRef } = renderDiffReviewHook();

            act(() => {
                bridge.emit("document_stream_start", {
                    operationId: "op-buf",
                    targetPosition: "selection",
                    operationType: "diff",
                });

                bridge.emit("document_stream_token", {
                    operationId: "op-buf",
                    token: "Revised ",
                    index: 0,
                });

                bridge.emit("document_stream_token", {
                    operationId: "op-buf",
                    token: "content",
                    index: 1,
                });
            });

            // Editor should NOT have been written to (tokens are buffered)
            expect(editorRef.current!.setHtml).not.toHaveBeenCalled();
            // Panel should not be open yet (stream not ended)
            expect(result.current.diffState.isOpen).toBe(false);
        });

        it("streamToken_NonDiffOperation_TokensIgnoredByHook", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                bridge.emit("document_stream_start", {
                    operationId: "op-insert",
                    targetPosition: "end",
                    operationType: "insert",
                });

                bridge.emit("document_stream_token", {
                    operationId: "op-insert",
                    token: "ignored token",
                    index: 0,
                });
            });

            // No diff state change for non-diff operations
            expect(result.current.diffState.isOpen).toBe(false);
            expect(result.current.isDiffStreaming).toBe(false);
        });
    });

    // -----------------------------------------------------------------------
    // 3. Stream end opens DiffReviewPanel with buffered content
    // -----------------------------------------------------------------------

    describe("document_stream_end opens DiffReviewPanel", () => {
        it("streamEnd_DiffComplete_OpensPanelWithOriginalAndProposed", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                emitDiffStream(bridge, "op-end-1", [
                    "Revised ",
                    "content ",
                    "here.",
                ]);
            });

            expect(result.current.diffState.isOpen).toBe(true);
            expect(result.current.diffState.originalText).toBe(
                "<p>Original content</p>"
            );
            expect(result.current.diffState.proposedText).toBe(
                "Revised content here."
            );
            expect(result.current.diffState.operationId).toBe("op-end-1");
            expect(result.current.isDiffStreaming).toBe(false);
        });

        it("streamEnd_CancelledStream_DoesNotOpenPanel", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                emitDiffStream(bridge, "op-cancel", ["partial"], {
                    cancelled: true,
                });
            });

            expect(result.current.diffState.isOpen).toBe(false);
            expect(result.current.isDiffStreaming).toBe(false);
        });

        it("streamEnd_EmptyBuffer_DoesNotOpenPanel", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                // Start diff stream but emit no tokens
                bridge.emit("document_stream_start", {
                    operationId: "op-empty",
                    targetPosition: "selection",
                    operationType: "diff",
                });
                bridge.emit("document_stream_end", {
                    operationId: "op-empty",
                    cancelled: false,
                    totalTokens: 0,
                });
            });

            expect(result.current.diffState.isOpen).toBe(false);
        });

        it("streamEnd_NonDiffOperation_PanelRemainsClosedNoStateChange", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                emitInsertStream(bridge, "op-insert-end", ["some", " tokens"]);
            });

            expect(result.current.diffState.isOpen).toBe(false);
            expect(result.current.diffState.proposedText).toBe("");
        });
    });

    // -----------------------------------------------------------------------
    // 4. Accept handler pushes undo and replaces editor content
    // -----------------------------------------------------------------------

    describe("acceptDiff handler", () => {
        it("acceptDiff_PushesUndoAndReplacesEditorContent", () => {
            const { result, editorRef, getHtml } = renderDiffReviewHook();

            // Open the diff panel
            act(() => {
                emitDiffStream(bridge, "op-accept", ["New content"]);
            });

            expect(result.current.diffState.isOpen).toBe(true);

            // Accept the proposed text
            act(() => {
                result.current.acceptDiff("New content");
            });

            // Undo should have been called twice (before and after replacement)
            expect(mockPushUndoVersion).toHaveBeenCalledTimes(2);

            // Editor content should be updated
            expect(editorRef.current!.setHtml).toHaveBeenCalledWith(
                "New content"
            );

            // Panel should close after accept
            expect(result.current.diffState.isOpen).toBe(false);
            expect(result.current.diffState.proposedText).toBe("");
        });

        it("acceptDiff_EditedText_ReplacesWithEditedVersion", () => {
            const { result, editorRef } = renderDiffReviewHook();

            // Open diff panel
            act(() => {
                emitDiffStream(bridge, "op-edit-accept", [
                    "AI proposed content",
                ]);
            });

            // Accept with user-edited version
            act(() => {
                result.current.acceptDiff(
                    "User-edited version of AI content"
                );
            });

            expect(editorRef.current!.setHtml).toHaveBeenCalledWith(
                "User-edited version of AI content"
            );
        });

        it("acceptDiff_NullEditorRef_ClosePanelGracefully", () => {
            const { result } = renderDiffReviewHook({
                editorRef: { current: null } as unknown as React.RefObject<any>,
            });

            // Open diff panel
            act(() => {
                emitDiffStream(bridge, "op-null-editor", ["content"]);
            });

            // Accept should not crash with null editor
            act(() => {
                result.current.acceptDiff("content");
            });

            // Panel should still close
            expect(result.current.diffState.isOpen).toBe(false);
        });
    });

    // -----------------------------------------------------------------------
    // 5. Reject handler closes panel without changes
    // -----------------------------------------------------------------------

    describe("rejectDiff handler", () => {
        it("rejectDiff_ClosesPanelWithoutChangingEditorContent", () => {
            const { result, editorRef, getHtml } = renderDiffReviewHook();

            // Open diff panel
            act(() => {
                emitDiffStream(bridge, "op-reject", ["Rejected content"]);
            });

            expect(result.current.diffState.isOpen).toBe(true);

            // Reject the diff
            act(() => {
                result.current.rejectDiff();
            });

            // Panel should close
            expect(result.current.diffState.isOpen).toBe(false);

            // Editor content should NOT be changed (no setHtml calls)
            expect(editorRef.current!.setHtml).not.toHaveBeenCalled();

            // Undo version should NOT be pushed for reject
            expect(mockPushUndoVersion).not.toHaveBeenCalled();

            // Editor still has original content
            expect(getHtml()).toBe("<p>Original content</p>");
        });

        it("rejectDiff_ResetsAllDiffState", () => {
            const { result } = renderDiffReviewHook();

            // Open diff panel
            act(() => {
                emitDiffStream(bridge, "op-reject-state", [
                    "Some proposed text",
                ]);
            });

            expect(result.current.diffState.operationId).toBe(
                "op-reject-state"
            );

            // Reject
            act(() => {
                result.current.rejectDiff();
            });

            // All state should be reset
            expect(result.current.diffState.isOpen).toBe(false);
            expect(result.current.diffState.originalText).toBe("");
            expect(result.current.diffState.proposedText).toBe("");
            expect(result.current.diffState.operationId).toBeNull();
        });
    });

    // -----------------------------------------------------------------------
    // 6. New stream while diff is open auto-rejects existing
    // -----------------------------------------------------------------------

    describe("auto-reject on new stream", () => {
        it("newDiffStream_WhilePanelOpen_AutoRejectsExistingDiff", () => {
            const { result } = renderDiffReviewHook();

            // Open first diff
            act(() => {
                emitDiffStream(bridge, "op-first", ["First proposed"]);
            });

            expect(result.current.diffState.isOpen).toBe(true);
            expect(result.current.diffState.operationId).toBe("op-first");

            // Start a new diff stream while the panel is open
            act(() => {
                emitDiffStream(bridge, "op-second", ["Second proposed"]);
            });

            // Panel should show the new diff, not the old one
            expect(result.current.diffState.isOpen).toBe(true);
            expect(result.current.diffState.operationId).toBe("op-second");
            expect(result.current.diffState.proposedText).toBe(
                "Second proposed"
            );
        });

        it("newInsertStream_WhileDiffOpen_AutoRejectsExistingDiff", () => {
            const { result } = renderDiffReviewHook();

            // Open a diff
            act(() => {
                emitDiffStream(bridge, "op-diff-open", ["Diff content"]);
            });

            expect(result.current.diffState.isOpen).toBe(true);

            // Start a non-diff stream (insert mode)
            act(() => {
                bridge.emit("document_stream_start", {
                    operationId: "op-insert-new",
                    targetPosition: "end",
                    operationType: "insert",
                });
            });

            // The existing diff should be auto-rejected (panel closed)
            expect(result.current.diffState.isOpen).toBe(false);
        });
    });

    // -----------------------------------------------------------------------
    // 7. Disabled state
    // -----------------------------------------------------------------------

    describe("disabled state", () => {
        it("enabled_False_DoesNotSubscribeToBridgeEvents", () => {
            const { result } = renderDiffReviewHook({ enabled: false });

            act(() => {
                emitDiffStream(bridge, "op-disabled", ["Should not appear"]);
            });

            expect(result.current.diffState.isOpen).toBe(false);
            expect(result.current.isDiffStreaming).toBe(false);
        });

        it("bridge_Null_DoesNotCrash", () => {
            const { result } = renderDiffReviewHook({ bridge: null as any });

            // Should not crash with null bridge
            expect(result.current.diffState.isOpen).toBe(false);
            expect(result.current.isDiffStreaming).toBe(false);
        });
    });

    // -----------------------------------------------------------------------
    // 8. Edge cases
    // -----------------------------------------------------------------------

    describe("edge cases", () => {
        it("multipleTokens_BufferedCorrectly_JoinedInOrder", () => {
            const { result } = renderDiffReviewHook();

            const tokens = [
                "<p>",
                "This ",
                "is ",
                "a ",
                "multi-token ",
                "stream.",
                "</p>",
            ];

            act(() => {
                emitDiffStream(bridge, "op-multi", tokens);
            });

            expect(result.current.diffState.proposedText).toBe(
                "<p>This is a multi-token stream.</p>"
            );
        });

        it("rapidConsecutiveStreams_OnlyLastStreamOpensPanel", () => {
            const { result } = renderDiffReviewHook();

            // Emit three rapid diff streams
            act(() => {
                emitDiffStream(bridge, "op-rapid-1", ["First"]);
            });

            act(() => {
                emitDiffStream(bridge, "op-rapid-2", ["Second"]);
            });

            act(() => {
                emitDiffStream(bridge, "op-rapid-3", ["Third"]);
            });

            // Only the last stream's content should be shown
            expect(result.current.diffState.isOpen).toBe(true);
            expect(result.current.diffState.proposedText).toBe("Third");
            expect(result.current.diffState.operationId).toBe("op-rapid-3");
        });

        it("streamEnd_NonMatchingOperationId_Ignored", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                bridge.emit("document_stream_start", {
                    operationId: "op-match",
                    targetPosition: "selection",
                    operationType: "diff",
                });

                bridge.emit("document_stream_token", {
                    operationId: "op-match",
                    token: "Matching token",
                    index: 0,
                });

                // End event for a different operation ID
                bridge.emit("document_stream_end", {
                    operationId: "op-different",
                    cancelled: false,
                    totalTokens: 1,
                });
            });

            // Panel should NOT open (the end event was for a different op)
            expect(result.current.diffState.isOpen).toBe(false);
            // Still streaming (the matching op hasn't ended)
            expect(result.current.isDiffStreaming).toBe(true);
        });

        it("tokenForDifferentOperation_IgnoredByBuffer", () => {
            const { result } = renderDiffReviewHook();

            act(() => {
                bridge.emit("document_stream_start", {
                    operationId: "op-active",
                    targetPosition: "selection",
                    operationType: "diff",
                });

                // Token for the active operation
                bridge.emit("document_stream_token", {
                    operationId: "op-active",
                    token: "Active ",
                    index: 0,
                });

                // Token for a different operation (should be ignored)
                bridge.emit("document_stream_token", {
                    operationId: "op-other",
                    token: "Other ",
                    index: 0,
                });

                // Another token for the active operation
                bridge.emit("document_stream_token", {
                    operationId: "op-active",
                    token: "content",
                    index: 1,
                });

                bridge.emit("document_stream_end", {
                    operationId: "op-active",
                    cancelled: false,
                    totalTokens: 2,
                });
            });

            // Only active operation tokens should appear
            expect(result.current.diffState.proposedText).toBe(
                "Active content"
            );
        });
    });
});
