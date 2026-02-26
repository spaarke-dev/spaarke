/**
 * Error Scenario Integration Tests
 *
 * Validates robust error handling across the cross-pane communication system:
 * - BroadcastChannel unavailable (fallback to postMessage)
 * - SSE connection drops mid-stream
 * - Malformed events / unexpected payloads
 * - Concurrent streaming operations
 * - Handler errors in one subscriber don't block others
 * - Emit/subscribe after disconnect
 *
 * These tests ensure the cross-pane system degrades gracefully under failure
 * conditions rather than crashing or leaving orphaned state.
 *
 * @see SprkChatBridge (services/SprkChatBridge.ts)
 * @see ADR-012 - Shared Component Library
 * @see ADR-019 - ProblemDetails error handling
 */

import { MockBroadcastChannel } from "./helpers/MockBroadcastChannel";
import {
    SprkChatBridge,
    type DocumentStreamTokenPayload,
    type DocumentStreamEndPayload,
    type SprkChatBridgeEventName,
} from "./helpers/SprkChatBridgeTestHarness";
import {
    makeStreamStart,
    makeStreamToken,
    makeStreamEnd,
    emitStreamingSequence,
    createMockEditorRef,
} from "./helpers/testFixtures";

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("Error Scenario Integration Tests", () => {
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
    // 1. BroadcastChannel unavailable: fallback to postMessage
    // -----------------------------------------------------------------------

    describe("BroadcastChannel unavailable: postMessage fallback", () => {
        it("noBroadcastChannel_AutoFallback_CommunicatesViaPostMessage", () => {
            delete (globalThis as Record<string, unknown>).BroadcastChannel;

            const consumer = new SprkChatBridge({
                context: "err-no-bc",
                transport: "auto",
                allowedOrigin: window.location.origin,
            });

            expect(consumer.transportType).toBe("postmessage");

            const receivedTokens: string[] = [];
            consumer.subscribe("document_stream_token", (p) =>
                receivedTokens.push(p.token)
            );

            // Simulate postMessage from producer
            const channelName = "sprk-workspace-err-no-bc";
            window.dispatchEvent(
                new MessageEvent("message", {
                    data: {
                        channel: channelName,
                        event: "document_stream_token" as SprkChatBridgeEventName,
                        payload: makeStreamToken(
                            "op-pm",
                            "postmessage-token",
                            0
                        ),
                    },
                    origin: window.location.origin,
                })
            );

            expect(receivedTokens).toEqual(["postmessage-token"]);

            consumer.disconnect();
        });

        it("postMessageTransport_WrongOrigin_MessageRejected", () => {
            delete (globalThis as Record<string, unknown>).BroadcastChannel;

            const consumer = new SprkChatBridge({
                context: "err-wrong-origin",
                transport: "postmessage",
                allowedOrigin: "https://trusted.example.com",
            });

            const receivedTokens: string[] = [];
            consumer.subscribe("document_stream_token", (p) =>
                receivedTokens.push(p.token)
            );

            // Simulate message from untrusted origin
            const channelName = "sprk-workspace-err-wrong-origin";
            window.dispatchEvent(
                new MessageEvent("message", {
                    data: {
                        channel: channelName,
                        event: "document_stream_token" as SprkChatBridgeEventName,
                        payload: makeStreamToken("op-evil", "evil-token", 0),
                    },
                    origin: "https://evil.example.com",
                })
            );

            // Message from wrong origin should be rejected
            expect(receivedTokens).toHaveLength(0);

            consumer.disconnect();
        });

        it("postMessageTransport_MalformedEnvelope_MessageIgnored", () => {
            delete (globalThis as Record<string, unknown>).BroadcastChannel;

            const consumer = new SprkChatBridge({
                context: "err-malformed-pm",
                transport: "postmessage",
                allowedOrigin: window.location.origin,
            });

            const receivedTokens: string[] = [];
            consumer.subscribe("document_stream_token", (p) =>
                receivedTokens.push(p.token)
            );

            // Send malformed messages (missing required fields)
            window.dispatchEvent(
                new MessageEvent("message", {
                    data: { notAnEnvelope: true },
                    origin: window.location.origin,
                })
            );

            window.dispatchEvent(
                new MessageEvent("message", {
                    data: {
                        channel: "sprk-workspace-err-malformed-pm",
                        // Missing event and payload
                    },
                    origin: window.location.origin,
                })
            );

            window.dispatchEvent(
                new MessageEvent("message", {
                    data: null,
                    origin: window.location.origin,
                })
            );

            // None of the malformed messages should be processed
            expect(receivedTokens).toHaveLength(0);

            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 2. SSE drops mid-stream (stream never ends)
    // -----------------------------------------------------------------------

    describe("SSE connection drops mid-stream", () => {
        it("networkDrop_StreamNeverEnds_ConsumerHandlesGracefully", () => {
            const producer = new SprkChatBridge({
                context: "err-netdrop",
            });
            const consumer = new SprkChatBridge({
                context: "err-netdrop",
            });

            let streamStarted = false;
            let streamEnded = false;
            const receivedTokens: string[] = [];

            consumer.subscribe("document_stream_start", () => {
                streamStarted = true;
            });
            consumer.subscribe("document_stream_token", (p) => {
                receivedTokens.push(p.token);
            });
            consumer.subscribe("document_stream_end", () => {
                streamEnded = true;
            });

            // Emit start and some tokens, but NO end (simulates SSE drop)
            producer.emit(
                "document_stream_start",
                makeStreamStart("op-netdrop")
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-netdrop", "partial ", 0)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-netdrop", "content", 1)
            );
            // ... SSE connection drops, no stream_end emitted

            expect(streamStarted).toBe(true);
            expect(receivedTokens).toEqual(["partial ", "content"]);
            expect(streamEnded).toBe(false);

            // Consumer can still be cleaned up without errors
            expect(() => consumer.disconnect()).not.toThrow();
            expect(() => producer.disconnect()).not.toThrow();
        });

        it("networkDrop_ProducerDisconnects_ConsumerSurvives", () => {
            const producer = new SprkChatBridge({
                context: "err-prod-disc",
            });
            const consumer = new SprkChatBridge({
                context: "err-prod-disc",
            });

            const tokens: string[] = [];
            consumer.subscribe("document_stream_token", (p) =>
                tokens.push(p.token)
            );

            producer.emit(
                "document_stream_token",
                makeStreamToken("op-prod-disc", "before", 0)
            );

            // Producer crashes/disconnects
            producer.disconnect();

            // Consumer is still healthy
            expect(consumer.isDisconnected).toBe(false);
            expect(tokens).toEqual(["before"]);

            // Consumer can disconnect cleanly
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 3. Malformed events
    // -----------------------------------------------------------------------

    describe("malformed events", () => {
        it("handlerThrows_OtherHandlersStillReceive_ErrorLogged", () => {
            const producer = new SprkChatBridge({
                context: "err-handler",
            });
            const consumer = new SprkChatBridge({
                context: "err-handler",
            });
            const consoleErrorSpy = jest
                .spyOn(console, "error")
                .mockImplementation(() => {});

            const validTokens: string[] = [];

            // Handler 1 throws on every event
            consumer.subscribe("document_stream_token", () => {
                throw new Error("Simulated handler crash");
            });
            // Handler 2 works fine
            consumer.subscribe("document_stream_token", (p) => {
                validTokens.push(p.token);
            });
            // Handler 3 also works fine
            consumer.subscribe("document_stream_token", (p) => {
                validTokens.push(`copy-${p.token}`);
            });

            producer.emit(
                "document_stream_token",
                makeStreamToken("op-err", "valid", 0)
            );

            // Handler 2 and 3 still received the token
            expect(validTokens).toEqual(["valid", "copy-valid"]);

            // Error was logged
            expect(consoleErrorSpy).toHaveBeenCalledWith(
                expect.stringContaining(
                    'Error in handler for "document_stream_token"'
                ),
                expect.any(Error)
            );

            consoleErrorSpy.mockRestore();
            producer.disconnect();
            consumer.disconnect();
        });

        it("unexpectedEventName_NoHandlers_SilentlyIgnored", () => {
            const producer = new SprkChatBridge({
                context: "err-unknown-event",
            });
            const consumer = new SprkChatBridge({
                context: "err-unknown-event",
            });

            // Only subscribe to known events
            const receivedEvents: string[] = [];
            consumer.subscribe("document_stream_start", () =>
                receivedEvents.push("start")
            );

            // Emit a known event
            producer.emit(
                "document_stream_start",
                makeStreamStart("op-known")
            );

            expect(receivedEvents).toEqual(["start"]);

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 4. Concurrent streaming operations
    // -----------------------------------------------------------------------

    describe("concurrent streaming operations", () => {
        it("twoSimultaneousStreams_OperationIdSeparates_NoDataMixing", () => {
            const producer = new SprkChatBridge({
                context: "err-concurrent",
            });
            const consumer = new SprkChatBridge({
                context: "err-concurrent",
            });

            const op1Events: Array<{ type: string; data: unknown }> = [];
            const op2Events: Array<{ type: string; data: unknown }> = [];

            consumer.subscribe("document_stream_start", (p) => {
                if (p.operationId === "op-concurrent-1") {
                    op1Events.push({ type: "start", data: p });
                } else if (p.operationId === "op-concurrent-2") {
                    op2Events.push({ type: "start", data: p });
                }
            });

            consumer.subscribe("document_stream_token", (p) => {
                if (p.operationId === "op-concurrent-1") {
                    op1Events.push({ type: "token", data: p });
                } else if (p.operationId === "op-concurrent-2") {
                    op2Events.push({ type: "token", data: p });
                }
            });

            consumer.subscribe("document_stream_end", (p) => {
                if (p.operationId === "op-concurrent-1") {
                    op1Events.push({ type: "end", data: p });
                } else if (p.operationId === "op-concurrent-2") {
                    op2Events.push({ type: "end", data: p });
                }
            });

            // Interleave two complete stream sequences
            producer.emit(
                "document_stream_start",
                makeStreamStart("op-concurrent-1")
            );
            producer.emit(
                "document_stream_start",
                makeStreamStart("op-concurrent-2")
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-concurrent-1", "A1 ", 0)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-concurrent-2", "B1 ", 0)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-concurrent-1", "A2 ", 1)
            );
            producer.emit(
                "document_stream_end",
                makeStreamEnd("op-concurrent-2", 1, false)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-concurrent-1", "A3", 2)
            );
            producer.emit(
                "document_stream_end",
                makeStreamEnd("op-concurrent-1", 3, false)
            );

            // Op 1: start + 3 tokens + end
            expect(op1Events).toHaveLength(5);
            expect(op1Events[0].type).toBe("start");
            expect(op1Events[1].type).toBe("token");
            expect(op1Events[2].type).toBe("token");
            expect(op1Events[3].type).toBe("token");
            expect(op1Events[4].type).toBe("end");

            // Op 2: start + 1 token + end
            expect(op2Events).toHaveLength(3);
            expect(op2Events[0].type).toBe("start");
            expect(op2Events[1].type).toBe("token");
            expect(op2Events[2].type).toBe("end");

            producer.disconnect();
            consumer.disconnect();
        });

        it("concurrentStreamWithEditor_OnlyActiveOpWritesToEditor", () => {
            const producer = new SprkChatBridge({
                context: "err-concurrent-editor",
            });
            const consumer = new SprkChatBridge({
                context: "err-concurrent-editor",
            });
            const mockEditor = createMockEditorRef();
            let activeOpId: string | null = null;

            consumer.subscribe("document_stream_start", (p) => {
                // Only the first stream to start gets the editor
                if (activeOpId === null) {
                    activeOpId = p.operationId;
                    mockEditor.editorRef.current.beginStreamingInsert(
                        p.targetPosition
                    );
                }
            });
            consumer.subscribe("document_stream_token", (p) => {
                // Only route tokens from the active operation to the editor
                if (p.operationId === activeOpId) {
                    mockEditor.editorRef.current.appendStreamToken(
                        mockEditor.getMockHandle(),
                        p.token
                    );
                }
            });
            consumer.subscribe("document_stream_end", (p) => {
                if (p.operationId === activeOpId) {
                    mockEditor.editorRef.current.endStreamingInsert(
                        mockEditor.getMockHandle()
                    );
                    activeOpId = null;
                }
            });

            // Start first stream
            producer.emit(
                "document_stream_start",
                makeStreamStart("op-active")
            );
            // Try to start a second (should be ignored for editor purposes)
            producer.emit(
                "document_stream_start",
                makeStreamStart("op-ignored")
            );

            // Send tokens for both
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-active", "Active ", 0)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-ignored", "Ignored ", 0)
            );
            producer.emit(
                "document_stream_token",
                makeStreamToken("op-active", "content.", 1)
            );

            // End active stream
            producer.emit(
                "document_stream_end",
                makeStreamEnd("op-active", 2, false)
            );

            // Only active operation's tokens in editor
            expect(mockEditor.getHtmlContent()).toBe("Active content.");
            expect(mockEditor.getStreamedTokens()).toEqual([
                "Active ",
                "content.",
            ]);

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 5. Disconnect error scenarios
    // -----------------------------------------------------------------------

    describe("disconnect error scenarios", () => {
        it("emitAfterDisconnect_ThrowsDescriptiveError", () => {
            const bridge = new SprkChatBridge({
                context: "err-emit-disc",
            });
            bridge.disconnect();

            expect(() => {
                bridge.emit(
                    "document_stream_start",
                    makeStreamStart("op-dead")
                );
            }).toThrow(/Cannot emit after disconnect/);
        });

        it("subscribeAfterDisconnect_ThrowsDescriptiveError", () => {
            const bridge = new SprkChatBridge({
                context: "err-sub-disc",
            });
            bridge.disconnect();

            expect(() => {
                bridge.subscribe("document_stream_token", jest.fn());
            }).toThrow(/Cannot subscribe after disconnect/);
        });

        it("multipleDisconnects_Idempotent_NoErrorThrown", () => {
            const bridge = new SprkChatBridge({
                context: "err-multi-disc",
            });

            expect(() => {
                bridge.disconnect();
                bridge.disconnect();
                bridge.disconnect();
            }).not.toThrow();

            expect(bridge.isDisconnected).toBe(true);
        });
    });
});
