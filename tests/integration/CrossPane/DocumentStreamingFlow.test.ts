/**
 * Document Streaming Flow Integration Tests
 *
 * Validates the end-to-end document streaming pipeline:
 *   SSE document_stream_start -> document_stream_token (multiple) -> document_stream_end
 *   flowing through SprkChatBridge from SprkChat pane to AnalysisWorkspace editor.
 *
 * Verifies:
 * - Token ordering preservation across the bridge
 * - Operation ID tracking through the full pipeline
 * - Content assembly from individual tokens
 * - Cancellation mid-stream with partial content preservation
 * - Document replace flow with undo stack
 * - Large document streaming without token drops
 *
 * @see SprkChatBridge (services/SprkChatBridge.ts)
 * @see StreamingInsertPlugin (RichTextEditor/plugins/StreamingInsertPlugin.tsx)
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - No real document content in tests
 */

import { MockBroadcastChannel } from "./helpers/MockBroadcastChannel";
import {
    SprkChatBridge,
    type DocumentStreamStartPayload,
    type DocumentStreamTokenPayload,
    type DocumentStreamEndPayload,
    type DocumentReplacedPayload,
} from "./helpers/SprkChatBridgeTestHarness";
import {
    makeStreamStart,
    makeStreamToken,
    makeStreamEnd,
    makeDocumentReplaced,
    emitStreamingSequence,
    createMockEditorRef,
    SAMPLE_TOKENS,
    LARGE_TOKEN_SET,
    DOCUMENT_CONTENT_TOKENS,
} from "./helpers/testFixtures";

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("Document Streaming Flow Integration Tests", () => {
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
    // 1. Happy path: full streaming write flow
    // -----------------------------------------------------------------------

    describe("happy path: SSE -> Bridge -> Editor", () => {
        let sprkChatBridge: SprkChatBridge;
        let awBridge: SprkChatBridge;

        beforeEach(() => {
            sprkChatBridge = new SprkChatBridge({ context: "doc-stream-e2e" });
            awBridge = new SprkChatBridge({ context: "doc-stream-e2e" });
        });

        afterEach(() => {
            sprkChatBridge.disconnect();
            awBridge.disconnect();
        });

        it("streamSequence_AllTokens_ReceivedInOrderByConsumer", () => {
            const receivedEvents: Array<{ type: string; payload: unknown }> = [];

            awBridge.subscribe("document_stream_start", (p) =>
                receivedEvents.push({ type: "start", payload: p })
            );
            awBridge.subscribe("document_stream_token", (p) =>
                receivedEvents.push({ type: "token", payload: p })
            );
            awBridge.subscribe("document_stream_end", (p) =>
                receivedEvents.push({ type: "end", payload: p })
            );

            emitStreamingSequence(sprkChatBridge, "op-happy-1", SAMPLE_TOKENS);

            // Total events: 1 start + N tokens + 1 end
            expect(receivedEvents).toHaveLength(SAMPLE_TOKENS.length + 2);

            // First event is start
            expect(receivedEvents[0].type).toBe("start");
            expect(
                (receivedEvents[0].payload as DocumentStreamStartPayload)
                    .operationId
            ).toBe("op-happy-1");

            // Middle events are tokens in order
            for (let i = 0; i < SAMPLE_TOKENS.length; i++) {
                const event = receivedEvents[i + 1];
                expect(event.type).toBe("token");
                const tokenPayload =
                    event.payload as DocumentStreamTokenPayload;
                expect(tokenPayload.token).toBe(SAMPLE_TOKENS[i]);
                expect(tokenPayload.index).toBe(i);
                expect(tokenPayload.operationId).toBe("op-happy-1");
            }

            // Last event is end
            const endPayload = receivedEvents[SAMPLE_TOKENS.length + 1]
                .payload as DocumentStreamEndPayload;
            expect(endPayload.cancelled).toBe(false);
            expect(endPayload.totalTokens).toBe(SAMPLE_TOKENS.length);
        });

        it("streamSequence_FullPipeline_ContentAssembledInEditor", () => {
            const mockEditor = createMockEditorRef();
            let streamActive = false;

            // Wire consumer (simulates useDocumentStreamConsumer)
            awBridge.subscribe("document_stream_start", (p) => {
                mockEditor.pushToUndoStack();
                mockEditor.editorRef.current.beginStreamingInsert(
                    p.targetPosition
                );
                streamActive = true;
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
                mockEditor.editorRef.current.endStreamingInsert(
                    mockEditor.getMockHandle()
                );
                streamActive = false;
                mockEditor.pushToUndoStack();
            });

            emitStreamingSequence(
                sprkChatBridge,
                "op-pipeline-1",
                DOCUMENT_CONTENT_TOKENS
            );

            // Content assembled correctly
            const expected = DOCUMENT_CONTENT_TOKENS.join("");
            expect(mockEditor.getHtmlContent()).toBe(expected);

            // Streaming calls in correct order
            const calls = mockEditor.getStreamingCalls();
            expect(calls[0].method).toBe("beginStreamingInsert");
            for (let i = 1; i <= DOCUMENT_CONTENT_TOKENS.length; i++) {
                expect(calls[i].method).toBe("appendStreamToken");
            }
            expect(calls[calls.length - 1].method).toBe(
                "endStreamingInsert"
            );

            // Undo stack has pre/post snapshots
            expect(mockEditor.getUndoStack()).toHaveLength(2);
        });

        it("streamSequence_OperationId_TrackedThroughEntirePipeline", () => {
            const opIds: string[] = [];

            awBridge.subscribe("document_stream_start", (p) =>
                opIds.push(p.operationId)
            );
            awBridge.subscribe("document_stream_token", (p) =>
                opIds.push(p.operationId)
            );
            awBridge.subscribe("document_stream_end", (p) =>
                opIds.push(p.operationId)
            );

            const testOpId = "op-tracking-xyz";
            emitStreamingSequence(sprkChatBridge, testOpId, [
                "tok1 ",
                "tok2 ",
            ]);

            // All events should carry the same operationId
            expect(opIds.every((id) => id === testOpId)).toBe(true);
            expect(opIds).toHaveLength(4); // start + 2 tokens + end
        });
    });

    // -----------------------------------------------------------------------
    // 2. Large document streaming
    // -----------------------------------------------------------------------

    describe("large document streaming", () => {
        it("stream_100Tokens_AllDeliveredWithoutDrops", () => {
            const producer = new SprkChatBridge({ context: "large-doc" });
            const consumer = new SprkChatBridge({ context: "large-doc" });
            const receivedTokens: DocumentStreamTokenPayload[] = [];

            consumer.subscribe("document_stream_token", (p) =>
                receivedTokens.push(p)
            );

            emitStreamingSequence(producer, "op-large", LARGE_TOKEN_SET);

            expect(receivedTokens).toHaveLength(LARGE_TOKEN_SET.length);
            for (let i = 0; i < LARGE_TOKEN_SET.length; i++) {
                expect(receivedTokens[i].token).toBe(LARGE_TOKEN_SET[i]);
                expect(receivedTokens[i].index).toBe(i);
            }

            producer.disconnect();
            consumer.disconnect();
        });

        it("stream_250Tokens_ContentAssembledCorrectly", () => {
            const producer = new SprkChatBridge({
                context: "large-assemble",
            });
            const consumer = new SprkChatBridge({
                context: "large-assemble",
            });
            const mockEditor = createMockEditorRef();
            let streamActive = false;

            consumer.subscribe("document_stream_start", (p) => {
                mockEditor.editorRef.current.beginStreamingInsert(
                    p.targetPosition
                );
                streamActive = true;
            });
            consumer.subscribe("document_stream_token", (p) => {
                if (streamActive) {
                    mockEditor.editorRef.current.appendStreamToken(
                        mockEditor.getMockHandle(),
                        p.token
                    );
                }
            });
            consumer.subscribe("document_stream_end", () => {
                mockEditor.editorRef.current.endStreamingInsert(
                    mockEditor.getMockHandle()
                );
                streamActive = false;
            });

            const bigTokens = Array.from(
                { length: 250 },
                (_, i) => `word${i} `
            );
            emitStreamingSequence(producer, "op-big", bigTokens);

            expect(mockEditor.getHtmlContent()).toBe(bigTokens.join(""));

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 3. Cancel mid-stream
    // -----------------------------------------------------------------------

    describe("cancel mid-stream", () => {
        it("cancelAfter3Tokens_PartialContentPreserved", () => {
            const producer = new SprkChatBridge({ context: "cancel-mid" });
            const consumer = new SprkChatBridge({ context: "cancel-mid" });
            const mockEditor = createMockEditorRef();
            let streamActive = false;

            consumer.subscribe("document_stream_start", (p) => {
                mockEditor.pushToUndoStack();
                mockEditor.editorRef.current.beginStreamingInsert(
                    p.targetPosition
                );
                streamActive = true;
            });
            consumer.subscribe("document_stream_token", (p) => {
                if (streamActive) {
                    mockEditor.editorRef.current.appendStreamToken(
                        mockEditor.getMockHandle(),
                        p.token
                    );
                }
            });
            consumer.subscribe("document_stream_end", () => {
                mockEditor.editorRef.current.endStreamingInsert(
                    mockEditor.getMockHandle()
                );
                streamActive = false;
                mockEditor.pushToUndoStack();
            });

            const allTokens = [
                "The ",
                "analysis ",
                "reveals ",
                "several ",
                "findings.",
            ];
            emitStreamingSequence(producer, "op-cancel", allTokens, {
                cancelAfter: 3,
            });

            expect(mockEditor.getHtmlContent()).toBe(
                "The analysis reveals "
            );
            expect(mockEditor.getStreamedTokens()).toEqual([
                "The ",
                "analysis ",
                "reveals ",
            ]);

            producer.disconnect();
            consumer.disconnect();
        });

        it("cancelMidStream_UndoRestoresPreStreamContent", () => {
            const producer = new SprkChatBridge({ context: "cancel-undo" });
            const consumer = new SprkChatBridge({ context: "cancel-undo" });
            const mockEditor = createMockEditorRef(
                "<p>Existing content</p>"
            );
            let streamActive = false;

            mockEditor.pushToUndoStack(); // Initial state

            consumer.subscribe("document_stream_start", (p) => {
                mockEditor.pushToUndoStack();
                mockEditor.editorRef.current.beginStreamingInsert(
                    p.targetPosition
                );
                streamActive = true;
            });
            consumer.subscribe("document_stream_token", (p) => {
                if (streamActive) {
                    mockEditor.editorRef.current.appendStreamToken(
                        mockEditor.getMockHandle(),
                        p.token
                    );
                }
            });
            consumer.subscribe("document_stream_end", () => {
                mockEditor.editorRef.current.endStreamingInsert(
                    mockEditor.getMockHandle()
                );
                streamActive = false;
                mockEditor.pushToUndoStack();
            });

            emitStreamingSequence(
                producer,
                "op-cancel-undo",
                ["New ", "streamed "],
                { cancelAfter: 2 }
            );

            // Partial content present
            expect(mockEditor.getHtmlContent()).toContain("New ");

            // Undo reverts to pre-stream state
            mockEditor.undo();
            expect(mockEditor.getHtmlContent()).toContain("Existing content");

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 4. Document replace flow
    // -----------------------------------------------------------------------

    describe("document replace flow (re-analysis)", () => {
        it("documentReplace_PreviousContentPushedToUndoStack", () => {
            const producer = new SprkChatBridge({ context: "replace-flow" });
            const consumer = new SprkChatBridge({ context: "replace-flow" });
            const mockEditor = createMockEditorRef(
                "<p>Original content</p>"
            );
            mockEditor.pushToUndoStack();

            consumer.subscribe(
                "document_replaced",
                (p: DocumentReplacedPayload) => {
                    mockEditor.pushToUndoStack();
                    mockEditor.editorRef.current.setHtml(p.html);
                    mockEditor.pushToUndoStack();
                }
            );

            producer.emit(
                "document_replaced",
                makeDocumentReplaced(
                    "op-replace",
                    "<p>New re-analysis result</p>",
                    "prev-ver-1"
                )
            );

            expect(mockEditor.getHtmlContent()).toBe(
                "<p>New re-analysis result</p>"
            );
            expect(mockEditor.getUndoStack().length).toBeGreaterThanOrEqual(
                3
            );

            // Undo restores original
            mockEditor.undo();
            expect(mockEditor.getHtmlContent()).toBe(
                "<p>Original content</p>"
            );

            producer.disconnect();
            consumer.disconnect();
        });

        it("documentReplace_DuringActiveStreaming_RejectedByConsumer", () => {
            const producer = new SprkChatBridge({
                context: "replace-conflict",
            });
            const consumer = new SprkChatBridge({
                context: "replace-conflict",
            });
            let streamingActive = false;
            let replaceAttempted = false;

            consumer.subscribe("document_stream_start", () => {
                streamingActive = true;
            });
            consumer.subscribe("document_stream_end", () => {
                streamingActive = false;
            });
            consumer.subscribe("document_replaced", () => {
                if (streamingActive) {
                    replaceAttempted = true;
                    return; // Reject replace during streaming
                }
            });

            // Start stream but don't end it
            producer.emit(
                "document_stream_start",
                makeStreamStart("op-conflict")
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-conflict", "token ", 0)
            );

            // Try replace during active stream
            producer.emit(
                "document_replaced",
                makeDocumentReplaced("op-replace-mid", "<p>New</p>")
            );

            expect(replaceAttempted).toBe(true);

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 5. Sequential streaming operations
    // -----------------------------------------------------------------------

    describe("sequential streaming operations", () => {
        it("twoConsecutiveStreams_BothSucceed_ContentFromSecondStream", () => {
            const producer = new SprkChatBridge({
                context: "sequential-ops",
            });
            const consumer = new SprkChatBridge({
                context: "sequential-ops",
            });
            const mockEditor = createMockEditorRef();
            let streamActive = false;

            consumer.subscribe("document_stream_start", (p) => {
                // Reset editor for new stream (simulates replace behavior)
                mockEditor.editorRef.current.clear();
                mockEditor.editorRef.current.beginStreamingInsert(
                    p.targetPosition
                );
                streamActive = true;
            });
            consumer.subscribe("document_stream_token", (p) => {
                if (streamActive) {
                    mockEditor.editorRef.current.appendStreamToken(
                        mockEditor.getMockHandle(),
                        p.token
                    );
                }
            });
            consumer.subscribe("document_stream_end", () => {
                mockEditor.editorRef.current.endStreamingInsert(
                    mockEditor.getMockHandle()
                );
                streamActive = false;
            });

            // First stream
            emitStreamingSequence(producer, "op-seq-1", [
                "First ",
                "stream.",
            ]);
            expect(mockEditor.getHtmlContent()).toBe("First stream.");

            // Second stream
            emitStreamingSequence(producer, "op-seq-2", [
                "Second ",
                "stream.",
            ]);
            expect(mockEditor.getHtmlContent()).toBe("Second stream.");

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 6. Concurrent operations (operationId discrimination)
    // -----------------------------------------------------------------------

    describe("concurrent operation ID discrimination", () => {
        it("interleavedTokens_OperationIdDiscriminates_Correctly", () => {
            const producer = new SprkChatBridge({
                context: "concurrent-ops",
            });
            const consumer = new SprkChatBridge({
                context: "concurrent-ops",
            });

            const op1Tokens: string[] = [];
            const op2Tokens: string[] = [];

            consumer.subscribe("document_stream_token", (p) => {
                if (p.operationId === "op-1") {
                    op1Tokens.push(p.token);
                } else if (p.operationId === "op-2") {
                    op2Tokens.push(p.token);
                }
            });

            // Interleave tokens from two operations
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-1", "alpha ", 0)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-2", "beta ", 0)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-1", "gamma ", 1)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-2", "delta ", 1)
            );

            expect(op1Tokens).toEqual(["alpha ", "gamma "]);
            expect(op2Tokens).toEqual(["beta ", "delta "]);

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 7. Auto-save trigger after stream end
    // -----------------------------------------------------------------------

    describe("auto-save triggering", () => {
        it("streamEnd_NotCancelled_TriggersAutoSaveCallback", () => {
            const producer = new SprkChatBridge({
                context: "autosave-trigger",
            });
            const consumer = new SprkChatBridge({
                context: "autosave-trigger",
            });
            let autoSaveCalled = false;

            consumer.subscribe("document_stream_end", (p) => {
                if (!p.cancelled) {
                    autoSaveCalled = true;
                }
            });

            emitStreamingSequence(producer, "op-autosave", [
                "content ",
                "here.",
            ]);

            expect(autoSaveCalled).toBe(true);

            producer.disconnect();
            consumer.disconnect();
        });

        it("streamEnd_Cancelled_DoesNotTriggerAutoSave", () => {
            const producer = new SprkChatBridge({
                context: "autosave-cancel",
            });
            const consumer = new SprkChatBridge({
                context: "autosave-cancel",
            });
            let autoSaveCalled = false;

            consumer.subscribe("document_stream_end", (p) => {
                if (!p.cancelled) {
                    autoSaveCalled = true;
                }
            });

            emitStreamingSequence(
                producer,
                "op-autosave-c",
                ["content ", "here."],
                { cancelAfter: 1 }
            );

            expect(autoSaveCalled).toBe(false);

            producer.disconnect();
            consumer.disconnect();
        });
    });
});
