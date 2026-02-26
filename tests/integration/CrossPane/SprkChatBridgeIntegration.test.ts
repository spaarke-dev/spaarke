/**
 * SprkChatBridge Integration Tests
 *
 * Validates cross-pane message send/receive across simulated pane boundaries,
 * channel naming conventions (sprk-workspace-{context}), error handling for
 * closed channels, message ordering guarantees, and transport selection.
 *
 * These tests simulate the real deployment topology:
 *   - SprkChatPane Code Page (side pane) = producer
 *   - AnalysisWorkspace Code Page (main form) = consumer
 *   - Both share the same context and communicate via BroadcastChannel
 *
 * @see SprkChatBridge (services/SprkChatBridge.ts)
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - No auth tokens via BroadcastChannel
 */

import { MockBroadcastChannel } from "./helpers/MockBroadcastChannel";
import {
    SprkChatBridge,
    type DocumentStreamStartPayload,
    type DocumentStreamTokenPayload,
    type DocumentStreamEndPayload,
    type SelectionChangedPayload,
    type ContextChangedPayload,
    type SprkChatBridgeEventName,
} from "./helpers/SprkChatBridgeTestHarness";
import {
    makeStreamStart,
    makeStreamToken,
    makeStreamEnd,
    makeSelectionChanged,
    makeContextChanged,
    emitStreamingSequence,
    TEST_CONTEXT,
    SAMPLE_TOKENS,
} from "./helpers/testFixtures";

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("SprkChatBridge Integration Tests", () => {
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
    // 1. Channel naming conventions
    // -----------------------------------------------------------------------

    describe("channel naming: sprk-workspace-{context}", () => {
        it("constructor_WithContext_CreatesCorrectChannelName", () => {
            const bridge = new SprkChatBridge({ context: "session-abc" });

            expect(bridge.channelName).toBe("sprk-workspace-session-abc");

            bridge.disconnect();
        });

        it("constructor_ProducerAndConsumer_ShareSameChannelName", () => {
            const producer = new SprkChatBridge({ context: TEST_CONTEXT });
            const consumer = new SprkChatBridge({ context: TEST_CONTEXT });

            expect(producer.channelName).toBe(consumer.channelName);
            expect(producer.channelName).toBe(
                `sprk-workspace-${TEST_CONTEXT}`
            );

            producer.disconnect();
            consumer.disconnect();
        });

        it("constructor_DifferentContexts_ProduceDifferentChannels", () => {
            const bridge1 = new SprkChatBridge({ context: "session-1" });
            const bridge2 = new SprkChatBridge({ context: "session-2" });

            expect(bridge1.channelName).not.toBe(bridge2.channelName);
            expect(bridge1.channelName).toBe("sprk-workspace-session-1");
            expect(bridge2.channelName).toBe("sprk-workspace-session-2");

            bridge1.disconnect();
            bridge2.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 2. Cross-pane message delivery
    // -----------------------------------------------------------------------

    describe("cross-pane message delivery", () => {
        let producer: SprkChatBridge;
        let consumer: SprkChatBridge;

        beforeEach(() => {
            producer = new SprkChatBridge({ context: "msg-delivery" });
            consumer = new SprkChatBridge({ context: "msg-delivery" });
        });

        afterEach(() => {
            producer.disconnect();
            consumer.disconnect();
        });

        it("emit_SingleEvent_DeliveredToConsumerOnSameChannel", () => {
            let received = false;
            consumer.subscribe("document_stream_start", () => {
                received = true;
            });

            producer.emit(
                "document_stream_start",
                makeStreamStart("op-1")
            );

            expect(received).toBe(true);
        });

        it("emit_MultipleEventTypes_AllDeliveredCorrectly", () => {
            const events: Array<{ type: string; payload: unknown }> = [];

            consumer.subscribe("document_stream_start", (p) => {
                events.push({ type: "start", payload: p });
            });
            consumer.subscribe("document_stream_token", (p) => {
                events.push({ type: "token", payload: p });
            });
            consumer.subscribe("document_stream_end", (p) => {
                events.push({ type: "end", payload: p });
            });
            consumer.subscribe("selection_changed", (p) => {
                events.push({ type: "selection", payload: p });
            });
            consumer.subscribe("context_changed", (p) => {
                events.push({ type: "context", payload: p });
            });

            producer.emit(
                "document_stream_start",
                makeStreamStart("op-multi")
            );
            producer.emit(
                "selection_changed",
                makeSelectionChanged("selected text")
            );
            producer.emit(
                "context_changed",
                makeContextChanged("sprk_analysisoutput", "entity-1")
            );

            expect(events).toHaveLength(3);
            expect(events[0].type).toBe("start");
            expect(events[1].type).toBe("selection");
            expect(events[2].type).toBe("context");
        });

        it("emit_PayloadIntegrity_AllFieldsPreservedAcrossTransport", () => {
            let receivedPayload: DocumentStreamTokenPayload | null = null;

            consumer.subscribe("document_stream_token", (p) => {
                receivedPayload = p;
            });

            const sent = makeStreamToken("op-integrity", "Hello world", 42);
            producer.emit("document_stream_token", sent);

            expect(receivedPayload).not.toBeNull();
            expect(receivedPayload!.operationId).toBe("op-integrity");
            expect(receivedPayload!.token).toBe("Hello world");
            expect(receivedPayload!.index).toBe(42);
        });
    });

    // -----------------------------------------------------------------------
    // 3. Channel isolation (different contexts do not cross-talk)
    // -----------------------------------------------------------------------

    describe("channel isolation", () => {
        it("emit_DifferentChannels_MessagesNotCrossDelivered", () => {
            const producerA = new SprkChatBridge({ context: "chan-a" });
            const consumerA = new SprkChatBridge({ context: "chan-a" });
            const producerB = new SprkChatBridge({ context: "chan-b" });
            const consumerB = new SprkChatBridge({ context: "chan-b" });

            const tokensA: string[] = [];
            const tokensB: string[] = [];

            consumerA.subscribe("document_stream_token", (p) =>
                tokensA.push(p.token)
            );
            consumerB.subscribe("document_stream_token", (p) =>
                tokensB.push(p.token)
            );

            producerA.emit(
                "document_stream_token",
                makeStreamToken("op-a", "alpha", 0)
            );
            producerB.emit(
                "document_stream_token",
                makeStreamToken("op-b", "beta", 0)
            );
            producerA.emit(
                "document_stream_token",
                makeStreamToken("op-a", "gamma", 1)
            );

            expect(tokensA).toEqual(["alpha", "gamma"]);
            expect(tokensB).toEqual(["beta"]);

            producerA.disconnect();
            consumerA.disconnect();
            producerB.disconnect();
            consumerB.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 4. Message ordering guarantees
    // -----------------------------------------------------------------------

    describe("message ordering", () => {
        it("emit_MultipleTokens_ReceivedInEmitOrder", () => {
            const producer = new SprkChatBridge({ context: "order-test" });
            const consumer = new SprkChatBridge({ context: "order-test" });
            const receivedIndices: number[] = [];

            consumer.subscribe("document_stream_token", (p) => {
                receivedIndices.push(p.index);
            });

            for (let i = 0; i < 50; i++) {
                producer.emit(
                    "document_stream_token",
                    makeStreamToken("op-order", `tok-${i}`, i)
                );
            }

            expect(receivedIndices).toEqual(
                Array.from({ length: 50 }, (_, i) => i)
            );

            producer.disconnect();
            consumer.disconnect();
        });

        it("emit_StreamSequence_EventsReceivedInCorrectOrder", () => {
            const producer = new SprkChatBridge({ context: "seq-order" });
            const consumer = new SprkChatBridge({ context: "seq-order" });
            const eventOrder: string[] = [];

            consumer.subscribe("document_stream_start", () =>
                eventOrder.push("start")
            );
            consumer.subscribe("document_stream_token", () =>
                eventOrder.push("token")
            );
            consumer.subscribe("document_stream_end", () =>
                eventOrder.push("end")
            );

            emitStreamingSequence(producer, "op-seq", SAMPLE_TOKENS);

            expect(eventOrder[0]).toBe("start");
            for (let i = 1; i <= SAMPLE_TOKENS.length; i++) {
                expect(eventOrder[i]).toBe("token");
            }
            expect(eventOrder[eventOrder.length - 1]).toBe("end");

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 5. Disconnected channel error handling
    // -----------------------------------------------------------------------

    describe("disconnected channel error handling", () => {
        it("emit_AfterDisconnect_ThrowsError", () => {
            const bridge = new SprkChatBridge({ context: "disc-emit" });
            bridge.disconnect();

            expect(() => {
                bridge.emit(
                    "document_stream_start",
                    makeStreamStart("op-dead")
                );
            }).toThrow("Cannot emit after disconnect");
        });

        it("subscribe_AfterDisconnect_ThrowsError", () => {
            const bridge = new SprkChatBridge({ context: "disc-sub" });
            bridge.disconnect();

            expect(() => {
                bridge.subscribe("document_stream_start", jest.fn());
            }).toThrow("Cannot subscribe after disconnect");
        });

        it("disconnect_CalledTwice_Idempotent", () => {
            const bridge = new SprkChatBridge({ context: "disc-idem" });

            expect(() => {
                bridge.disconnect();
                bridge.disconnect();
            }).not.toThrow();

            expect(bridge.isDisconnected).toBe(true);
        });

        it("disconnect_MidStream_ConsumerStopsReceiving", () => {
            const producer = new SprkChatBridge({ context: "disc-mid" });
            const consumer = new SprkChatBridge({ context: "disc-mid" });
            const receivedTokens: string[] = [];

            consumer.subscribe("document_stream_token", (p) =>
                receivedTokens.push(p.token)
            );

            // Emit 3 tokens
            for (let i = 0; i < 3; i++) {
                producer.emit(
                    "document_stream_token",
                    makeStreamToken("op-mid", `tok-${i}`, i)
                );
            }

            // Disconnect consumer
            consumer.disconnect();

            // Emit 3 more tokens
            for (let i = 3; i < 6; i++) {
                producer.emit(
                    "document_stream_token",
                    makeStreamToken("op-mid", `tok-${i}`, i)
                );
            }

            // Only first 3 received
            expect(receivedTokens).toEqual(["tok-0", "tok-1", "tok-2"]);

            producer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 6. Transport selection and fallback
    // -----------------------------------------------------------------------

    describe("transport selection", () => {
        it("constructor_Auto_PrefersBroadcastChannelWhenAvailable", () => {
            const bridge = new SprkChatBridge({ context: "transport-auto" });
            expect(bridge.transportType).toBe("broadcast");
            bridge.disconnect();
        });

        it("constructor_Auto_FallsBackToPostMessageWhenBCUnavailable", () => {
            delete (globalThis as Record<string, unknown>).BroadcastChannel;

            const bridge = new SprkChatBridge({
                context: "transport-fallback",
            });
            expect(bridge.transportType).toBe("postmessage");
            bridge.disconnect();
        });

        it("constructor_ForceBroadcast_ThrowsWhenUnavailable", () => {
            delete (globalThis as Record<string, unknown>).BroadcastChannel;

            expect(() => {
                new SprkChatBridge({
                    context: "transport-force-bc",
                    transport: "broadcast",
                });
            }).toThrow("BroadcastChannel transport requested but BroadcastChannel is not available");
        });

        it("postMessageTransport_DeliversEventsViaWindowMessages", () => {
            delete (globalThis as Record<string, unknown>).BroadcastChannel;

            const consumer = new SprkChatBridge({
                context: "pm-delivery",
                transport: "postmessage",
                allowedOrigin: window.location.origin,
            });

            const receivedTokens: string[] = [];
            consumer.subscribe("document_stream_token", (p) =>
                receivedTokens.push(p.token)
            );

            // Simulate postMessage from producer pane
            const channelName = "sprk-workspace-pm-delivery";
            const msgEvent = new MessageEvent("message", {
                data: {
                    channel: channelName,
                    event: "document_stream_token" as SprkChatBridgeEventName,
                    payload: makeStreamToken("op-pm", "pm-token", 0),
                },
                origin: window.location.origin,
            });
            window.dispatchEvent(msgEvent);

            expect(receivedTokens).toEqual(["pm-token"]);

            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 7. Handler error isolation
    // -----------------------------------------------------------------------

    describe("handler error isolation", () => {
        it("subscribe_HandlerThrows_OtherHandlersStillReceive", () => {
            const producer = new SprkChatBridge({ context: "err-isolate" });
            const consumer = new SprkChatBridge({ context: "err-isolate" });
            const consoleErrorSpy = jest
                .spyOn(console, "error")
                .mockImplementation(() => {});

            const validTokens: string[] = [];

            // First handler throws
            consumer.subscribe("document_stream_token", () => {
                throw new Error("Handler crash");
            });
            // Second handler works fine
            consumer.subscribe("document_stream_token", (p) => {
                validTokens.push(p.token);
            });

            producer.emit(
                "document_stream_token",
                makeStreamToken("op-err", "valid-token", 0)
            );

            expect(validTokens).toEqual(["valid-token"]);
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
    });

    // -----------------------------------------------------------------------
    // 8. Unsubscribe behavior
    // -----------------------------------------------------------------------

    describe("unsubscribe behavior", () => {
        it("unsubscribe_CalledBeforeEmit_HandlerNotInvoked", () => {
            const producer = new SprkChatBridge({ context: "unsub-test" });
            const consumer = new SprkChatBridge({ context: "unsub-test" });
            let received = false;

            const unsub = consumer.subscribe(
                "document_stream_start",
                () => {
                    received = true;
                }
            );

            // Unsubscribe before any emit
            unsub();

            producer.emit(
                "document_stream_start",
                makeStreamStart("op-unsub")
            );

            expect(received).toBe(false);

            producer.disconnect();
            consumer.disconnect();
        });

        it("unsubscribe_PartialRemoval_RemainingHandlersStillWork", () => {
            const producer = new SprkChatBridge({ context: "unsub-partial" });
            const consumer = new SprkChatBridge({ context: "unsub-partial" });
            const results: string[] = [];

            const unsub1 = consumer.subscribe(
                "document_stream_token",
                () => {
                    results.push("handler-1");
                }
            );
            consumer.subscribe("document_stream_token", () => {
                results.push("handler-2");
            });

            // Remove first handler
            unsub1();

            producer.emit(
                "document_stream_token",
                makeStreamToken("op-partial", "tok", 0)
            );

            expect(results).toEqual(["handler-2"]);

            producer.disconnect();
            consumer.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 9. Security: no auth tokens in payloads
    // -----------------------------------------------------------------------

    describe("security: auth tokens never in bridge payloads", () => {
        it("streamStartPayload_DoesNotContainAuthFields", () => {
            const payload = makeStreamStart("op-sec");
            const keys = Object.keys(payload);
            const authKeys = [
                "token",
                "accesstoken",
                "authtoken",
                "bearer",
                "authorization",
            ];

            for (const key of keys) {
                expect(authKeys).not.toContain(key.toLowerCase());
            }
        });

        it("streamTokenPayload_TokenFieldIsDocumentContent_NotAuthToken", () => {
            const payload = makeStreamToken("op-sec", "The analysis", 0);

            expect(payload.token).toBe("The analysis");
            const serialized = JSON.stringify(payload);
            expect(serialized).not.toContain("Bearer");
            expect(serialized).not.toContain("eyJ"); // JWT prefix
        });
    });
});
