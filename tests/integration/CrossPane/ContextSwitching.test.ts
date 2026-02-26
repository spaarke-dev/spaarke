/**
 * Context Switching Integration Tests
 *
 * Validates behavior when the user navigates to a different record while
 * streaming is active. Covers:
 * - Graceful stream cancellation on context switch
 * - State cleanup (no orphaned listeners, cleared buffers)
 * - New channel establishment for the new context
 * - Partial content preservation after context switch
 * - No event cross-contamination between old and new contexts
 *
 * Real-world scenario: user is viewing analysis for Matter A, AI is streaming
 * tokens into the editor, user clicks to navigate to Matter B. The old stream
 * must be cancelled gracefully, the old channel torn down, and a new channel
 * established for Matter B.
 *
 * @see SprkChatBridge (services/SprkChatBridge.ts)
 * @see context_changed event type
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - No real document content in tests
 */

import { MockBroadcastChannel } from "./helpers/MockBroadcastChannel";
import {
    SprkChatBridge,
    type DocumentStreamTokenPayload,
    type ContextChangedPayload,
} from "./helpers/SprkChatBridgeTestHarness";
import {
    makeStreamStart,
    makeStreamToken,
    makeStreamEnd,
    makeContextChanged,
    emitStreamingSequence,
    createMockEditorRef,
    TEST_ENTITY_TYPE,
} from "./helpers/testFixtures";

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("Context Switching Integration Tests", () => {
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
    // 1. Graceful cancellation on context switch
    // -----------------------------------------------------------------------

    describe("graceful stream cancellation during context switch", () => {
        it("contextSwitch_DuringActiveStream_StreamCancelledGracefully", () => {
            // Arrange: simulates both panes connected for Matter A
            const producerA = new SprkChatBridge({
                context: "matter-a-session",
            });
            const consumerA = new SprkChatBridge({
                context: "matter-a-session",
            });

            const mockEditor = createMockEditorRef();
            let streamActive = false;
            let streamCancelled = false;
            const receivedTokens: string[] = [];

            consumerA.subscribe("document_stream_start", (p) => {
                mockEditor.editorRef.current.beginStreamingInsert(
                    p.targetPosition
                );
                streamActive = true;
            });
            consumerA.subscribe("document_stream_token", (p) => {
                if (streamActive) {
                    receivedTokens.push(p.token);
                    mockEditor.editorRef.current.appendStreamToken(
                        mockEditor.getMockHandle(),
                        p.token
                    );
                }
            });
            consumerA.subscribe("document_stream_end", (p) => {
                mockEditor.editorRef.current.endStreamingInsert(
                    mockEditor.getMockHandle()
                );
                streamActive = false;
                streamCancelled = p.cancelled;
            });

            // Act: start streaming for Matter A
            producerA.emit(
                "document_stream_start",
                makeStreamStart("op-matter-a")
            );
            producerA.emit(
                "document_stream_token",
                makeStreamToken("op-matter-a", "Analysis ", 0)
            );
            producerA.emit(
                "document_stream_token",
                makeStreamToken("op-matter-a", "of ", 1)
            );

            // User navigates to Matter B -> cancel active stream
            producerA.emit(
                "document_stream_end",
                makeStreamEnd("op-matter-a", 2, true) // cancelled
            );

            // Assert: stream was cancelled
            expect(streamCancelled).toBe(true);
            expect(streamActive).toBe(false);
            expect(receivedTokens).toEqual(["Analysis ", "of "]);

            // Partial content preserved
            expect(mockEditor.getHtmlContent()).toBe("Analysis of ");

            producerA.disconnect();
            consumerA.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 2. State cleanup: old channel torn down, no orphaned listeners
    // -----------------------------------------------------------------------

    describe("state cleanup on context switch", () => {
        it("contextSwitch_OldBridgeDisconnected_NoOrphanedListeners", () => {
            // Arrange: create bridges for Matter A
            const producerA = new SprkChatBridge({
                context: "cleanup-matter-a",
            });
            const consumerA = new SprkChatBridge({
                context: "cleanup-matter-a",
            });
            const tokensReceivedAfterDisconnect: string[] = [];

            consumerA.subscribe("document_stream_token", (p) => {
                tokensReceivedAfterDisconnect.push(p.token);
            });

            // Simulate context switch: disconnect old bridges
            consumerA.disconnect();

            // Producer tries to send (user might have a stale reference)
            // This should not deliver to the disconnected consumer
            producerA.emit(
                "document_stream_token",
                makeStreamToken("op-orphan", "orphaned-token", 0)
            );

            expect(tokensReceivedAfterDisconnect).toHaveLength(0);
            expect(consumerA.isDisconnected).toBe(true);

            producerA.disconnect();
        });

        it("contextSwitch_BuffersCleared_NoStaleTokensInNewContext", () => {
            // Arrange: simulate context A streaming, then switch to context B
            const producerA = new SprkChatBridge({
                context: "stale-ctx-a",
            });
            const consumerA = new SprkChatBridge({
                context: "stale-ctx-a",
            });

            const contextATokens: string[] = [];
            consumerA.subscribe("document_stream_token", (p) => {
                contextATokens.push(p.token);
            });

            // Stream some tokens for Context A
            producerA.emit(
                "document_stream_token",
                makeStreamToken("op-ctx-a", "ctx-a-tok-1", 0)
            );
            producerA.emit(
                "document_stream_token",
                makeStreamToken("op-ctx-a", "ctx-a-tok-2", 1)
            );

            expect(contextATokens).toEqual(["ctx-a-tok-1", "ctx-a-tok-2"]);

            // Context switch: tear down A, set up B
            producerA.disconnect();
            consumerA.disconnect();

            const producerB = new SprkChatBridge({
                context: "stale-ctx-b",
            });
            const consumerB = new SprkChatBridge({
                context: "stale-ctx-b",
            });

            const contextBTokens: string[] = [];
            consumerB.subscribe("document_stream_token", (p) => {
                contextBTokens.push(p.token);
            });

            // Stream tokens for Context B
            producerB.emit(
                "document_stream_token",
                makeStreamToken("op-ctx-b", "ctx-b-tok-1", 0)
            );

            // Assert: Context B only gets its own tokens
            expect(contextBTokens).toEqual(["ctx-b-tok-1"]);
            // No cross-contamination
            expect(contextBTokens).not.toContain("ctx-a-tok-1");
            expect(contextBTokens).not.toContain("ctx-a-tok-2");

            producerB.disconnect();
            consumerB.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 3. New channel establishment for new context
    // -----------------------------------------------------------------------

    describe("new channel establishment after context switch", () => {
        it("contextSwitch_NewChannelWorks_AfterOldChannelTornDown", () => {
            // Arrange: Context A session
            const producerA = new SprkChatBridge({
                context: "switch-ctx-a",
            });
            const consumerA = new SprkChatBridge({
                context: "switch-ctx-a",
            });

            // Verify Context A works
            let ctxAReceived = false;
            consumerA.subscribe("document_stream_start", () => {
                ctxAReceived = true;
            });
            producerA.emit(
                "document_stream_start",
                makeStreamStart("op-ctx-a")
            );
            expect(ctxAReceived).toBe(true);

            // Tear down Context A
            producerA.disconnect();
            consumerA.disconnect();

            // Set up Context B with different entity/analysis
            const producerB = new SprkChatBridge({
                context: "switch-ctx-b",
            });
            const consumerB = new SprkChatBridge({
                context: "switch-ctx-b",
            });

            // Verify Context B works independently
            const mockEditorB = createMockEditorRef();
            let streamActiveB = false;

            consumerB.subscribe("document_stream_start", (p) => {
                mockEditorB.editorRef.current.beginStreamingInsert(
                    p.targetPosition
                );
                streamActiveB = true;
            });
            consumerB.subscribe("document_stream_token", (p) => {
                if (streamActiveB) {
                    mockEditorB.editorRef.current.appendStreamToken(
                        mockEditorB.getMockHandle(),
                        p.token
                    );
                }
            });
            consumerB.subscribe("document_stream_end", () => {
                mockEditorB.editorRef.current.endStreamingInsert(
                    mockEditorB.getMockHandle()
                );
                streamActiveB = false;
            });

            emitStreamingSequence(producerB, "op-ctx-b", [
                "Context B ",
                "content.",
            ]);

            expect(mockEditorB.getHtmlContent()).toBe("Context B content.");
            expect(consumerB.channelName).toBe(
                "sprk-workspace-switch-ctx-b"
            );

            producerB.disconnect();
            consumerB.disconnect();
        });

        it("contextSwitch_ContextChangedEvent_DeliveredBeforeTeardown", () => {
            // Arrange: AW sends context_changed to notify SprkChat of navigation
            const awBridge = new SprkChatBridge({
                context: "ctx-change-event",
            });
            const sprkChatBridge = new SprkChatBridge({
                context: "ctx-change-event",
            });

            let receivedContextChange: ContextChangedPayload | null = null;
            sprkChatBridge.subscribe("context_changed", (p) => {
                receivedContextChange = p;
            });

            // Act: AW emits context_changed before teardown
            awBridge.emit(
                "context_changed",
                makeContextChanged(
                    TEST_ENTITY_TYPE,
                    "new-entity-456",
                    "playbook-new"
                )
            );

            // Assert: SprkChat received the context change
            expect(receivedContextChange).not.toBeNull();
            expect(receivedContextChange!.entityType).toBe(TEST_ENTITY_TYPE);
            expect(receivedContextChange!.entityId).toBe("new-entity-456");
            expect(receivedContextChange!.playbookId).toBe("playbook-new");

            // Now tear down old bridges
            awBridge.disconnect();
            sprkChatBridge.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 4. No event cross-contamination between contexts
    // -----------------------------------------------------------------------

    describe("no cross-contamination between context channels", () => {
        it("twoActiveContexts_EventsIsolated_NoCrossDelivery", () => {
            // Simulate scenario where old context bridge hasn't been torn down
            // but new context is already established
            const producerA = new SprkChatBridge({
                context: "iso-ctx-a",
            });
            const consumerA = new SprkChatBridge({
                context: "iso-ctx-a",
            });
            const producerB = new SprkChatBridge({
                context: "iso-ctx-b",
            });
            const consumerB = new SprkChatBridge({
                context: "iso-ctx-b",
            });

            const tokensA: string[] = [];
            const tokensB: string[] = [];

            consumerA.subscribe("document_stream_token", (p) =>
                tokensA.push(p.token)
            );
            consumerB.subscribe("document_stream_token", (p) =>
                tokensB.push(p.token)
            );

            // Context A gets tokens
            producerA.emit(
                "document_stream_token",
                makeStreamToken("op-a", "ctx-a-only", 0)
            );
            // Context B gets tokens
            producerB.emit(
                "document_stream_token",
                makeStreamToken("op-b", "ctx-b-only", 0)
            );

            // Verify isolation
            expect(tokensA).toEqual(["ctx-a-only"]);
            expect(tokensB).toEqual(["ctx-b-only"]);
            expect(tokensA).not.toContain("ctx-b-only");
            expect(tokensB).not.toContain("ctx-a-only");

            producerA.disconnect();
            consumerA.disconnect();
            producerB.disconnect();
            consumerB.disconnect();
        });
    });

    // -----------------------------------------------------------------------
    // 5. Rapid context switches (stress test)
    // -----------------------------------------------------------------------

    describe("rapid context switching", () => {
        it("rapidContextSwitches_NoMemoryLeaks_AllBridgesCleanedUp", () => {
            // Simulate user rapidly navigating between records
            const bridges: SprkChatBridge[] = [];

            for (let i = 0; i < 10; i++) {
                const producer = new SprkChatBridge({
                    context: `rapid-ctx-${i}`,
                });
                const consumer = new SprkChatBridge({
                    context: `rapid-ctx-${i}`,
                });

                // Quick stream
                emitStreamingSequence(producer, `op-rapid-${i}`, [
                    `tok-${i}`,
                ]);

                bridges.push(producer, consumer);
            }

            // Disconnect all (simulates teardown on each context switch)
            for (const bridge of bridges) {
                bridge.disconnect();
            }

            // Verify all disconnected
            for (const bridge of bridges) {
                expect(bridge.isDisconnected).toBe(true);
            }

            // No active MockBroadcastChannel instances should remain
            expect(MockBroadcastChannel.instances).toHaveLength(0);
        });
    });
});
