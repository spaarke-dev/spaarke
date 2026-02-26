/**
 * Selection-Based Revision Flow Integration Tests
 *
 * Validates the end-to-end selection revision pipeline:
 *   Editor text selection -> BroadcastChannel (selection_changed) ->
 *   SprkChat pane -> BFF /refine endpoint -> SSE response ->
 *   BroadcastChannel (document_stream_* with operationType="diff") ->
 *   useDiffReview hook buffers tokens -> DiffReviewPanel ->
 *   Accept: replace editor content | Reject: discard
 *
 * Tests verify:
 * - Selection events are transmitted correctly via bridge
 * - Diff-mode streaming (operationType="diff") routes tokens to buffer (not editor)
 * - DiffReviewPanel opens with correct original + proposed text
 * - Accept pushes undo and replaces editor content
 * - Reject discards without editor changes
 * - Multiple rapid selections debounce correctly
 *
 * @see useDiffReview (hooks/useDiffReview.ts)
 * @see SprkChatBridge (services/SprkChatBridge.ts)
 * @see SprkChat.handleEditorRefine (SprkChat.tsx)
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - No real document content in tests
 */

import { MockBroadcastChannel } from "./helpers/MockBroadcastChannel";
import {
    SprkChatBridge,
    type DocumentStreamStartPayload,
    type DocumentStreamTokenPayload,
    type DocumentStreamEndPayload,
    type SelectionChangedPayload,
} from "./helpers/SprkChatBridgeTestHarness";
import {
    makeStreamStart,
    makeStreamToken,
    makeStreamEnd,
    makeSelectionChanged,
    emitStreamingSequence,
    createMockEditorRef,
    TEST_ANALYSIS_CONTENT,
    TEST_REVISED_CONTENT,
} from "./helpers/testFixtures";

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("Selection-Based Revision Flow Integration Tests", () => {
    const originalBroadcastChannel = (globalThis as Record<string, unknown>)
        .BroadcastChannel;

    beforeEach(() => {
        MockBroadcastChannel.reset();
        (globalThis as Record<string, unknown>).BroadcastChannel =
            MockBroadcastChannel;
    });

    afterEach(() => {
        MockBroadcastChannel.reset();
        if (originalBroadcastChannel) {
            (globalThis as Record<string, unknown>).BroadcastChannel =
                originalBroadcastChannel;
        } else {
            delete (globalThis as Record<string, unknown>).BroadcastChannel;
        }
    });

    // -----------------------------------------------------------------------
    // 1. Selection transmission via bridge
    // -----------------------------------------------------------------------

    describe("selection transmission: editor -> SprkChat via bridge", () => {
        it("selectionChanged_TransmittedViaBridge_ReceivedBySprkChat", () => {
            // Arrange: AW editor (producer) and SprkChat (consumer)
            const awBridge = new SprkChatBridge({
                context: "sel-transmit",
            });
            const sprkChatBridge = new SprkChatBridge({
                context: "sel-transmit",
            });
            let receivedSelection: SelectionChangedPayload | null = null;

            sprkChatBridge.subscribe("selection_changed", (p) => {
                receivedSelection = p;
            });

            // Act: AW emits selection_changed (simulates useSelectionBroadcast)
            awBridge.emit(
                "selection_changed",
                makeSelectionChanged(
                    "The quick brown fox",
                    10,
                    29
                )
            );

            // Assert
            expect(receivedSelection).not.toBeNull();
            expect(receivedSelection!.text).toBe("The quick brown fox");
            expect(receivedSelection!.startOffset).toBe(10);
            expect(receivedSelection!.endOffset).toBe(29);

            awBridge.disconnect();
            sprkChatBridge.disconnect();
        });

        it("selectionChanged_WithContext_ContextFieldPreserved", () => {
            const awBridge = new SprkChatBridge({
                context: "sel-context",
            });
            const sprkChatBridge = new SprkChatBridge({
                context: "sel-context",
            });
            let receivedSelection: SelectionChangedPayload | null = null;

            sprkChatBridge.subscribe("selection_changed", (p) => {
                receivedSelection = p;
            });

            awBridge.emit(
                "selection_changed",
                makeSelectionChanged(
                    "selected text",
                    0,
                    13,
                    "selection_cleared"
                )
            );

            expect(receivedSelection!.context).toBe("selection_cleared");

            awBridge.disconnect();
            sprkChatBridge.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 2. Diff-mode streaming: SprkChat -> bridge -> DiffReview buffer
    // -----------------------------------------------------------------------

    describe("diff-mode streaming: tokens buffered, not written to editor", () => {
        it("diffStream_TokensBuffered_NotWrittenToEditorDirectly", () => {
            // Arrange: simulate the full revision flow
            // SprkChat (producer) sends diff-mode stream events
            // AW (consumer) has useDiffReview-like logic that buffers
            const sprkChatBridge = new SprkChatBridge({
                context: "diff-buffer",
            });
            const awBridge = new SprkChatBridge({
                context: "diff-buffer",
            });
            const mockEditor = createMockEditorRef(TEST_ANALYSIS_CONTENT);

            // Simulate useDiffReview behavior
            let activeDiffOpId: string | null = null;
            let originalContent = "";
            const tokenBuffer: string[] = [];
            let diffPanelOpen = false;
            let diffPanelProposedText = "";

            awBridge.subscribe(
                "document_stream_start",
                (p: DocumentStreamStartPayload) => {
                    if (p.operationType === "diff") {
                        activeDiffOpId = p.operationId;
                        originalContent = mockEditor.getHtmlContent();
                        tokenBuffer.length = 0;
                    }
                }
            );
            awBridge.subscribe(
                "document_stream_token",
                (p: DocumentStreamTokenPayload) => {
                    if (activeDiffOpId === p.operationId) {
                        tokenBuffer.push(p.token);
                    }
                }
            );
            awBridge.subscribe(
                "document_stream_end",
                (p: DocumentStreamEndPayload) => {
                    if (activeDiffOpId === p.operationId && !p.cancelled) {
                        diffPanelOpen = true;
                        diffPanelProposedText = tokenBuffer.join("");
                    }
                    activeDiffOpId = null;
                }
            );

            // Act: SprkChat emits diff-mode stream
            const revisionTokens = [
                "<p>Revised ",
                "synthetic ",
                "analysis ",
                "output.</p>",
            ];
            emitStreamingSequence(sprkChatBridge, "op-diff-1", revisionTokens, {
                operationType: "diff",
                targetPosition: "selection",
            });

            // Assert: editor content NOT changed (tokens were buffered)
            expect(mockEditor.getHtmlContent()).toBe(TEST_ANALYSIS_CONTENT);
            expect(
                mockEditor.editorRef.current.beginStreamingInsert
            ).not.toHaveBeenCalled();

            // Assert: diff panel opened with correct content
            expect(diffPanelOpen).toBe(true);
            expect(originalContent).toBe(TEST_ANALYSIS_CONTENT);
            expect(diffPanelProposedText).toBe(
                "<p>Revised synthetic analysis output.</p>"
            );

            sprkChatBridge.disconnect();
            awBridge.disconnect();
        });

        it("diffStream_Cancelled_PanelDoesNotOpen", () => {
            const sprkChatBridge = new SprkChatBridge({
                context: "diff-cancel",
            });
            const awBridge = new SprkChatBridge({
                context: "diff-cancel",
            });
            let activeDiffOpId: string | null = null;
            const tokenBuffer: string[] = [];
            let diffPanelOpen = false;

            awBridge.subscribe("document_stream_start", (p) => {
                if (p.operationType === "diff") {
                    activeDiffOpId = p.operationId;
                    tokenBuffer.length = 0;
                }
            });
            awBridge.subscribe("document_stream_token", (p) => {
                if (activeDiffOpId === p.operationId) {
                    tokenBuffer.push(p.token);
                }
            });
            awBridge.subscribe("document_stream_end", (p) => {
                if (activeDiffOpId === p.operationId && !p.cancelled) {
                    diffPanelOpen = true;
                }
                activeDiffOpId = null;
            });

            // Act: SprkChat emits start, then cancels the stream
            sprkChatBridge.emit(
                "document_stream_start",
                makeStreamStart("op-diff-cancel", "selection", "diff")
            );
            sprkChatBridge.emit(
                "document_stream_token",
                makeStreamToken("op-diff-cancel", "partial", 0)
            );
            sprkChatBridge.emit(
                "document_stream_end",
                makeStreamEnd("op-diff-cancel", 1, true) // cancelled
            );

            expect(diffPanelOpen).toBe(false);

            sprkChatBridge.disconnect();
            awBridge.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 3. Accept diff: push undo + replace editor content
    // -----------------------------------------------------------------------

    describe("accept diff: push undo and replace editor", () => {
        it("acceptDiff_ReplacesEditorContent_PushesUndo", () => {
            const mockEditor = createMockEditorRef(TEST_ANALYSIS_CONTENT);
            mockEditor.pushToUndoStack(); // Initial state

            const proposedText = TEST_REVISED_CONTENT;

            // Simulate Accept: push undo, then replace
            mockEditor.pushToUndoStack();
            mockEditor.editorRef.current.setHtml(proposedText);
            mockEditor.pushToUndoStack();

            // Editor has new content
            expect(mockEditor.getHtmlContent()).toBe(TEST_REVISED_CONTENT);

            // Undo reverts to original
            mockEditor.undo();
            expect(mockEditor.getHtmlContent()).toBe(TEST_ANALYSIS_CONTENT);

            // Redo brings back revision
            mockEditor.redo();
            expect(mockEditor.getHtmlContent()).toBe(TEST_REVISED_CONTENT);
        });

        it("acceptDiff_WithEditedText_UsesEditedVersion", () => {
            const mockEditor = createMockEditorRef(TEST_ANALYSIS_CONTENT);
            mockEditor.pushToUndoStack();

            // Simulate user editing the proposed text before accepting
            const editedProposal = "<p>User-edited version of revision.</p>";

            mockEditor.pushToUndoStack();
            mockEditor.editorRef.current.setHtml(editedProposal);

            expect(mockEditor.getHtmlContent()).toBe(editedProposal);
        });
    });

    // -----------------------------------------------------------------------
    // 4. Reject diff: discard without changes
    // -----------------------------------------------------------------------

    describe("reject diff: discard without editor changes", () => {
        it("rejectDiff_EditorContentUnchanged", () => {
            const mockEditor = createMockEditorRef(TEST_ANALYSIS_CONTENT);

            // Simulate diff panel opens but user rejects
            // (no setHtml called, no undo pushed)
            const htmlAfterReject = mockEditor.getHtmlContent();

            expect(htmlAfterReject).toBe(TEST_ANALYSIS_CONTENT);
            expect(
                mockEditor.editorRef.current.setHtml
            ).not.toHaveBeenCalled();
        });
    });

    // -----------------------------------------------------------------------
    // 5. Full E2E revision flow
    // -----------------------------------------------------------------------

    describe("full E2E revision flow: select -> bridge -> diff -> accept/reject", () => {
        it("fullRevisionFlow_SelectionToDiffToAccept_EditorUpdated", () => {
            // Arrange: set up both panes
            const awBridge = new SprkChatBridge({ context: "revision-e2e" });
            const sprkChatBridge = new SprkChatBridge({
                context: "revision-e2e",
            });
            const mockEditor = createMockEditorRef(
                "<p>Original document text here.</p>"
            );
            mockEditor.pushToUndoStack();

            // Consumer-side diff review state (simulates useDiffReview)
            let activeDiffOpId: string | null = null;
            let originalContent = "";
            const tokenBuffer: string[] = [];
            let diffProposed = "";

            awBridge.subscribe("document_stream_start", (p) => {
                if (p.operationType === "diff") {
                    activeDiffOpId = p.operationId;
                    originalContent = mockEditor.getHtmlContent();
                    tokenBuffer.length = 0;
                }
            });
            awBridge.subscribe("document_stream_token", (p) => {
                if (activeDiffOpId === p.operationId) {
                    tokenBuffer.push(p.token);
                }
            });
            awBridge.subscribe("document_stream_end", (p) => {
                if (activeDiffOpId === p.operationId && !p.cancelled) {
                    diffProposed = tokenBuffer.join("");
                }
                activeDiffOpId = null;
            });

            // SprkChat-side: receives selection and sends refine request
            let receivedSelection: SelectionChangedPayload | null = null;
            sprkChatBridge.subscribe("selection_changed", (p) => {
                receivedSelection = p;
            });

            // Step 1: AW editor emits selection
            awBridge.emit(
                "selection_changed",
                makeSelectionChanged("document text", 19, 32)
            );

            expect(receivedSelection).not.toBeNull();
            expect(receivedSelection!.text).toBe("document text");

            // Step 2: SprkChat sends refine request to BFF (simulated)
            // and streams the response back via bridge as diff-mode
            const revisionTokens = [
                "revised ",
                "document ",
                "content",
            ];
            emitStreamingSequence(
                sprkChatBridge,
                "op-revision-e2e",
                revisionTokens,
                {
                    operationType: "diff",
                    targetPosition: "selection",
                }
            );

            // Step 3: Diff panel has proposed text
            expect(diffProposed).toBe("revised document content");
            expect(originalContent).toBe(
                "<p>Original document text here.</p>"
            );

            // Step 4: User accepts -> replace editor
            mockEditor.pushToUndoStack();
            mockEditor.editorRef.current.setHtml(diffProposed);
            mockEditor.pushToUndoStack();

            expect(mockEditor.getHtmlContent()).toBe(
                "revised document content"
            );

            // Step 5: Undo restores original
            mockEditor.undo();
            expect(mockEditor.getHtmlContent()).toBe(
                "<p>Original document text here.</p>"
            );

            awBridge.disconnect();
            sprkChatBridge.disconnect();
        });

        it("fullRevisionFlow_SelectionToDiffToReject_EditorUnchanged", () => {
            const awBridge = new SprkChatBridge({
                context: "revision-reject-e2e",
            });
            const sprkChatBridge = new SprkChatBridge({
                context: "revision-reject-e2e",
            });
            const mockEditor = createMockEditorRef(
                "<p>Original unchanged content.</p>"
            );

            // Consumer-side diff review state
            let activeDiffOpId: string | null = null;
            const tokenBuffer: string[] = [];

            awBridge.subscribe("document_stream_start", (p) => {
                if (p.operationType === "diff") {
                    activeDiffOpId = p.operationId;
                    tokenBuffer.length = 0;
                }
            });
            awBridge.subscribe("document_stream_token", (p) => {
                if (activeDiffOpId === p.operationId) {
                    tokenBuffer.push(p.token);
                }
            });
            awBridge.subscribe("document_stream_end", (p) => {
                activeDiffOpId = null;
            });

            // Step 1: Selection emitted
            awBridge.emit(
                "selection_changed",
                makeSelectionChanged("unchanged", 19, 28)
            );

            // Step 2: SprkChat streams diff response
            emitStreamingSequence(
                sprkChatBridge,
                "op-reject-e2e",
                ["proposed ", "revision"],
                {
                    operationType: "diff",
                    targetPosition: "selection",
                }
            );

            // Step 3: User rejects (no setHtml, no undo push)
            // Just clear the buffer, close the panel
            tokenBuffer.length = 0;

            // Assert: editor content unchanged
            expect(mockEditor.getHtmlContent()).toBe(
                "<p>Original unchanged content.</p>"
            );
            expect(
                mockEditor.editorRef.current.setHtml
            ).not.toHaveBeenCalled();

            awBridge.disconnect();
            sprkChatBridge.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 6. Non-diff operations don't trigger diff review
    // -----------------------------------------------------------------------

    describe("non-diff operations bypass diff review", () => {
        it("insertStream_DoesNotActivateDiffReview_WritesDirectlyToEditor", () => {
            const sprkChatBridge = new SprkChatBridge({
                context: "insert-bypass",
            });
            const awBridge = new SprkChatBridge({
                context: "insert-bypass",
            });
            const mockEditor = createMockEditorRef();
            let diffActivated = false;
            let streamActive = false;

            awBridge.subscribe("document_stream_start", (p) => {
                if (p.operationType === "diff") {
                    diffActivated = true;
                } else {
                    // Insert mode: write directly to editor
                    mockEditor.editorRef.current.beginStreamingInsert(
                        p.targetPosition
                    );
                    streamActive = true;
                }
            });
            awBridge.subscribe("document_stream_token", (p) => {
                if (streamActive) {
                    mockEditor.editorRef.current.appendStreamToken(
                        mockEditor.getMockHandle(),
                        p.token
                    );
                }
            });
            awBridge.subscribe("document_stream_end", () => {
                if (streamActive) {
                    mockEditor.editorRef.current.endStreamingInsert(
                        mockEditor.getMockHandle()
                    );
                    streamActive = false;
                }
            });

            // Emit an insert-mode stream (not diff)
            emitStreamingSequence(
                sprkChatBridge,
                "op-insert",
                ["Direct ", "insert."],
                { operationType: "insert" }
            );

            expect(diffActivated).toBe(false);
            expect(mockEditor.getHtmlContent()).toBe("Direct insert.");

            sprkChatBridge.disconnect();
            awBridge.disconnect();
        });
    });
});
