/**
 * SprkChatBridge Integration Tests
 *
 * Validates cross-pane communication scenarios between two or more SprkChatBridge
 * instances, simulating side-pane and workspace-pane communication patterns.
 *
 * Tests cover:
 * - Cross-pane message flow (BroadcastChannel and postMessage transports)
 * - Streaming sequence ordering and delivery guarantees (50+ tokens)
 * - Channel isolation (different channels don't cross-talk)
 * - Disconnect behavior and cleanup completeness
 *
 * @see ADR-012 Shared component library
 */

import {
  SprkChatBridge,
  DocumentStreamStartPayload,
  DocumentStreamTokenPayload,
  DocumentStreamEndPayload,
  SelectionChangedPayload,
  ContextChangedPayload,
  SprkChatBridgeEventName,
} from "../SprkChatBridge";

// ---------------------------------------------------------------------------
// BroadcastChannel mock — simulates cross-tab delivery
// ---------------------------------------------------------------------------

class MockBroadcastChannel {
  static instances: MockBroadcastChannel[] = [];
  name: string;
  onmessage: ((event: MessageEvent) => void) | null = null;
  closed = false;

  constructor(name: string) {
    this.name = name;
    MockBroadcastChannel.instances.push(this);
  }

  postMessage(data: unknown): void {
    // Deliver to all OTHER instances with same name (simulates cross-tab)
    for (const instance of MockBroadcastChannel.instances) {
      if (instance !== this && instance.name === this.name && !instance.closed) {
        if (instance.onmessage) {
          instance.onmessage(new MessageEvent("message", { data }));
        }
      }
    }
  }

  close(): void {
    this.closed = true;
    const idx = MockBroadcastChannel.instances.indexOf(this);
    if (idx >= 0) {
      MockBroadcastChannel.instances.splice(idx, 1);
    }
  }

  static reset(): void {
    MockBroadcastChannel.instances = [];
  }
}

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/** Creates a stream start payload */
function makeStreamStart(operationId: string): DocumentStreamStartPayload {
  return {
    operationId,
    targetPosition: "cursor",
    operationType: "insert",
  };
}

/** Creates a stream token payload */
function makeStreamToken(
  operationId: string,
  token: string,
  index: number
): DocumentStreamTokenPayload {
  return { operationId, token, index };
}

/** Creates a stream end payload */
function makeStreamEnd(
  operationId: string,
  totalTokens: number,
  cancelled = false
): DocumentStreamEndPayload {
  return { operationId, cancelled, totalTokens };
}

// ---------------------------------------------------------------------------
// Integration Tests
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
  // Cross-pane message flow — BroadcastChannel transport
  // -----------------------------------------------------------------------

  describe("cross-pane message flow (BroadcastChannel)", () => {
    let sidePane: SprkChatBridge;
    let workspacePane: SprkChatBridge;

    beforeEach(() => {
      sidePane = new SprkChatBridge({ context: "integ-session-bc" });
      workspacePane = new SprkChatBridge({ context: "integ-session-bc" });
    });

    afterEach(() => {
      sidePane.disconnect();
      workspacePane.disconnect();
    });

    it("emit_SidePaneSendsStreamStart_WorkspacePaneReceivesPayload", () => {
      // Arrange
      const received: DocumentStreamStartPayload[] = [];
      workspacePane.subscribe("document_stream_start", (payload) => {
        received.push(payload);
      });

      const payload = makeStreamStart("op-bc-1");

      // Act
      sidePane.emit("document_stream_start", payload);

      // Assert
      expect(received).toHaveLength(1);
      expect(received[0]).toEqual(payload);
    });

    it("emit_WorkspacePaneSendsSelectionChanged_SidePaneReceivesPayload", () => {
      // Arrange
      const received: SelectionChangedPayload[] = [];
      sidePane.subscribe("selection_changed", (payload) => {
        received.push(payload);
      });

      const payload: SelectionChangedPayload = {
        text: "important clause",
        startOffset: 100,
        endOffset: 116,
        context: "section-5",
      };

      // Act
      workspacePane.emit("selection_changed", payload);

      // Assert
      expect(received).toHaveLength(1);
      expect(received[0]).toEqual(payload);
    });

    it("emit_BidirectionalCommunication_BothPanesReceiveCorrectEvents", () => {
      // Arrange
      const sidePaneReceived: SelectionChangedPayload[] = [];
      const workspacePaneReceived: DocumentStreamStartPayload[] = [];

      sidePane.subscribe("selection_changed", (payload) => {
        sidePaneReceived.push(payload);
      });
      workspacePane.subscribe("document_stream_start", (payload) => {
        workspacePaneReceived.push(payload);
      });

      const selectionPayload: SelectionChangedPayload = {
        text: "highlighted text",
        startOffset: 50,
        endOffset: 66,
      };
      const streamPayload = makeStreamStart("op-bidir");

      // Act — workspace sends selection, side pane sends stream start
      workspacePane.emit("selection_changed", selectionPayload);
      sidePane.emit("document_stream_start", streamPayload);

      // Assert
      expect(sidePaneReceived).toHaveLength(1);
      expect(sidePaneReceived[0]).toEqual(selectionPayload);
      expect(workspacePaneReceived).toHaveLength(1);
      expect(workspacePaneReceived[0]).toEqual(streamPayload);
    });

    it("emit_ContextChangedFromWorkspace_SidePaneReceivesAndCanRespond", () => {
      // Arrange — simulates context switch: workspace notifies side pane,
      // side pane acknowledges with a stream start on the new context
      const contextEvents: ContextChangedPayload[] = [];
      const streamEvents: DocumentStreamStartPayload[] = [];

      sidePane.subscribe("context_changed", (payload) => {
        contextEvents.push(payload);
        // Side pane responds by starting a new stream
        sidePane.emit("document_stream_start", makeStreamStart("ctx-resp"));
      });
      workspacePane.subscribe("document_stream_start", (payload) => {
        streamEvents.push(payload);
      });

      const ctxPayload: ContextChangedPayload = {
        entityType: "sprk_matter",
        entityId: "matter-abc-123",
        playbookId: "playbook-7",
      };

      // Act
      workspacePane.emit("context_changed", ctxPayload);

      // Assert
      expect(contextEvents).toHaveLength(1);
      expect(contextEvents[0]).toEqual(ctxPayload);
      expect(streamEvents).toHaveLength(1);
      expect(streamEvents[0].operationId).toBe("ctx-resp");
    });
  });

  // -----------------------------------------------------------------------
  // Streaming sequence ordering and delivery guarantees
  // -----------------------------------------------------------------------

  describe("streaming sequence ordering", () => {
    let sender: SprkChatBridge;
    let receiver: SprkChatBridge;

    beforeEach(() => {
      sender = new SprkChatBridge({ context: "integ-stream-order" });
      receiver = new SprkChatBridge({ context: "integ-stream-order" });
    });

    afterEach(() => {
      sender.disconnect();
      receiver.disconnect();
    });

    it("emit_50TokenStreamingSequence_AllEventsReceivedInExactOrder", () => {
      // Arrange
      const TOKEN_COUNT = 50;
      const operationId = "op-stream-50";
      const allEvents: Array<{
        type: SprkChatBridgeEventName;
        payload: unknown;
      }> = [];

      receiver.subscribe("document_stream_start", (payload) => {
        allEvents.push({ type: "document_stream_start", payload });
      });
      receiver.subscribe("document_stream_token", (payload) => {
        allEvents.push({ type: "document_stream_token", payload });
      });
      receiver.subscribe("document_stream_end", (payload) => {
        allEvents.push({ type: "document_stream_end", payload });
      });

      // Act — emit start, 50 tokens, end
      sender.emit("document_stream_start", makeStreamStart(operationId));

      for (let i = 0; i < TOKEN_COUNT; i++) {
        sender.emit(
          "document_stream_token",
          makeStreamToken(operationId, `token-${i}`, i)
        );
      }

      sender.emit(
        "document_stream_end",
        makeStreamEnd(operationId, TOKEN_COUNT)
      );

      // Assert — total event count: 1 start + 50 tokens + 1 end = 52
      expect(allEvents).toHaveLength(TOKEN_COUNT + 2);

      // First event is stream_start
      expect(allEvents[0].type).toBe("document_stream_start");
      expect(
        (allEvents[0].payload as DocumentStreamStartPayload).operationId
      ).toBe(operationId);

      // Middle events are tokens in exact order
      for (let i = 0; i < TOKEN_COUNT; i++) {
        const event = allEvents[i + 1];
        expect(event.type).toBe("document_stream_token");
        const tokenPayload = event.payload as DocumentStreamTokenPayload;
        expect(tokenPayload.token).toBe(`token-${i}`);
        expect(tokenPayload.index).toBe(i);
        expect(tokenPayload.operationId).toBe(operationId);
      }

      // Last event is stream_end
      const endEvent = allEvents[TOKEN_COUNT + 1];
      expect(endEvent.type).toBe("document_stream_end");
      expect(
        (endEvent.payload as DocumentStreamEndPayload).totalTokens
      ).toBe(TOKEN_COUNT);
      expect(
        (endEvent.payload as DocumentStreamEndPayload).cancelled
      ).toBe(false);
    });

    it("emit_100TokenStreamingSequence_NoDroppedTokens", () => {
      // Arrange — larger sequence to stress-test delivery guarantees
      const TOKEN_COUNT = 100;
      const operationId = "op-stream-100";
      const receivedTokens: DocumentStreamTokenPayload[] = [];

      receiver.subscribe("document_stream_token", (payload) => {
        receivedTokens.push(payload);
      });

      // Act
      for (let i = 0; i < TOKEN_COUNT; i++) {
        sender.emit(
          "document_stream_token",
          makeStreamToken(operationId, `w${i}`, i)
        );
      }

      // Assert — every token received, none dropped
      expect(receivedTokens).toHaveLength(TOKEN_COUNT);
      for (let i = 0; i < TOKEN_COUNT; i++) {
        expect(receivedTokens[i].index).toBe(i);
        expect(receivedTokens[i].token).toBe(`w${i}`);
      }
    });

    it("emit_StreamTokensWithMixedEvents_EventTypesDoNotInterfere", () => {
      // Arrange — interleave stream tokens with selection_changed events
      const tokenEvents: DocumentStreamTokenPayload[] = [];
      const selectionEvents: SelectionChangedPayload[] = [];

      receiver.subscribe("document_stream_token", (payload) => {
        tokenEvents.push(payload);
      });
      receiver.subscribe("selection_changed", (payload) => {
        selectionEvents.push(payload);
      });

      // Act — send tokens interleaved with selection changes
      sender.emit(
        "document_stream_token",
        makeStreamToken("op-mix", "first", 0)
      );
      sender.emit("selection_changed", {
        text: "sel1",
        startOffset: 0,
        endOffset: 4,
      });
      sender.emit(
        "document_stream_token",
        makeStreamToken("op-mix", "second", 1)
      );
      sender.emit("selection_changed", {
        text: "sel2",
        startOffset: 10,
        endOffset: 14,
      });
      sender.emit(
        "document_stream_token",
        makeStreamToken("op-mix", "third", 2)
      );

      // Assert — each event type received in order, independent of each other
      expect(tokenEvents).toHaveLength(3);
      expect(tokenEvents[0].token).toBe("first");
      expect(tokenEvents[1].token).toBe("second");
      expect(tokenEvents[2].token).toBe("third");

      expect(selectionEvents).toHaveLength(2);
      expect(selectionEvents[0].text).toBe("sel1");
      expect(selectionEvents[1].text).toBe("sel2");
    });
  });

  // -----------------------------------------------------------------------
  // postMessage fallback transport — cross-pane flow
  // -----------------------------------------------------------------------

  describe("cross-pane message flow (postMessage fallback)", () => {
    beforeEach(() => {
      // Remove BroadcastChannel to force postMessage fallback
      delete (globalThis as Record<string, unknown>).BroadcastChannel;
    });

    /** Helper: dispatch a postMessage-style event on window */
    function dispatchPostMessage(
      channelName: string,
      event: SprkChatBridgeEventName,
      payload: unknown
    ): void {
      const msgEvent = new MessageEvent("message", {
        data: { channel: channelName, event, payload },
        origin: window.location.origin,
      });
      window.dispatchEvent(msgEvent);
    }

    it("emit_SidePaneSendsViaPostMessage_WorkspacePaneReceivesPayload", () => {
      // Arrange
      const receiver = new SprkChatBridge({
        context: "integ-pm-flow",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });

      const received: DocumentStreamStartPayload[] = [];
      receiver.subscribe("document_stream_start", (payload) => {
        received.push(payload);
      });

      const payload = makeStreamStart("op-pm-1");

      // Act — simulate the message arriving via postMessage
      // In jsdom, window.postMessage dispatches are async, so we simulate
      // the message event that would be dispatched by the sending bridge.
      dispatchPostMessage(
        "sprk-workspace-integ-pm-flow",
        "document_stream_start",
        payload
      );

      // Assert
      expect(received).toHaveLength(1);
      expect(received[0]).toEqual(payload);

      receiver.disconnect();
    });

    it("emit_BidirectionalPostMessage_BothPanesReceiveEvents", () => {
      // Arrange
      const bridgeA = new SprkChatBridge({
        context: "integ-pm-bidir",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });
      const bridgeB = new SprkChatBridge({
        context: "integ-pm-bidir",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });

      const bridgeAReceived: SelectionChangedPayload[] = [];
      const bridgeBReceived: DocumentStreamStartPayload[] = [];

      bridgeA.subscribe("selection_changed", (p) => bridgeAReceived.push(p));
      bridgeB.subscribe("document_stream_start", (p) => bridgeBReceived.push(p));

      const channelName = "sprk-workspace-integ-pm-bidir";

      // Act — simulate messages from each direction
      dispatchPostMessage(channelName, "selection_changed", {
        text: "pm-text",
        startOffset: 0,
        endOffset: 7,
      });
      dispatchPostMessage(
        channelName,
        "document_stream_start",
        makeStreamStart("op-pm-bidir")
      );

      // Assert — both bridges listen on same window, both receive matching events
      expect(bridgeAReceived).toHaveLength(1);
      expect(bridgeAReceived[0].text).toBe("pm-text");
      expect(bridgeBReceived).toHaveLength(1);
      expect(bridgeBReceived[0].operationId).toBe("op-pm-bidir");

      bridgeA.disconnect();
      bridgeB.disconnect();
    });

    it("emit_50TokenStreamViaPostMessage_AllTokensReceivedInOrder", () => {
      // Arrange
      const TOKEN_COUNT = 50;
      const operationId = "op-pm-stream-50";
      const receiver = new SprkChatBridge({
        context: "integ-pm-stream",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });

      const allEvents: Array<{
        type: SprkChatBridgeEventName;
        payload: unknown;
      }> = [];

      receiver.subscribe("document_stream_start", (p) => {
        allEvents.push({ type: "document_stream_start", payload: p });
      });
      receiver.subscribe("document_stream_token", (p) => {
        allEvents.push({ type: "document_stream_token", payload: p });
      });
      receiver.subscribe("document_stream_end", (p) => {
        allEvents.push({ type: "document_stream_end", payload: p });
      });

      const channelName = "sprk-workspace-integ-pm-stream";

      // Act — dispatch full streaming sequence via postMessage
      dispatchPostMessage(
        channelName,
        "document_stream_start",
        makeStreamStart(operationId)
      );
      for (let i = 0; i < TOKEN_COUNT; i++) {
        dispatchPostMessage(
          channelName,
          "document_stream_token",
          makeStreamToken(operationId, `pm-tok-${i}`, i)
        );
      }
      dispatchPostMessage(
        channelName,
        "document_stream_end",
        makeStreamEnd(operationId, TOKEN_COUNT)
      );

      // Assert — all 52 events received in exact order
      expect(allEvents).toHaveLength(TOKEN_COUNT + 2);
      expect(allEvents[0].type).toBe("document_stream_start");
      for (let i = 0; i < TOKEN_COUNT; i++) {
        const event = allEvents[i + 1];
        expect(event.type).toBe("document_stream_token");
        expect((event.payload as DocumentStreamTokenPayload).index).toBe(i);
        expect((event.payload as DocumentStreamTokenPayload).token).toBe(
          `pm-tok-${i}`
        );
      }
      expect(allEvents[TOKEN_COUNT + 1].type).toBe("document_stream_end");

      receiver.disconnect();
    });

    it("emit_PostMessageWrongOrigin_EventNotDelivered", () => {
      // Arrange
      const receiver = new SprkChatBridge({
        context: "integ-pm-origin",
        transport: "postmessage",
        allowedOrigin: "https://trusted.example.com",
      });

      const handler = jest.fn();
      receiver.subscribe("document_stream_start", handler);

      // Act — dispatch from untrusted origin
      const event = new MessageEvent("message", {
        data: {
          channel: "sprk-workspace-integ-pm-origin",
          event: "document_stream_start",
          payload: makeStreamStart("evil-op"),
        },
        origin: "https://evil.example.com",
      });
      window.dispatchEvent(event);

      // Assert
      expect(handler).not.toHaveBeenCalled();

      receiver.disconnect();
    });
  });

  // -----------------------------------------------------------------------
  // Channel isolation
  // -----------------------------------------------------------------------

  describe("channel isolation", () => {
    it("emit_DifferentChannels_MessagesDoNotCrossTalk", () => {
      // Arrange — three bridges: A and C share channel, B is on different channel
      const bridgeA = new SprkChatBridge({ context: "session1" });
      const bridgeB = new SprkChatBridge({ context: "session2" });
      const bridgeC = new SprkChatBridge({ context: "session1" });

      const bridgeAReceived: DocumentStreamStartPayload[] = [];
      const bridgeBReceived: DocumentStreamStartPayload[] = [];
      const bridgeCReceived: DocumentStreamStartPayload[] = [];

      bridgeA.subscribe("document_stream_start", (p) =>
        bridgeAReceived.push(p)
      );
      bridgeB.subscribe("document_stream_start", (p) =>
        bridgeBReceived.push(p)
      );
      bridgeC.subscribe("document_stream_start", (p) =>
        bridgeCReceived.push(p)
      );

      const payload = makeStreamStart("op-isolation");

      // Act — Bridge A emits on session1
      bridgeA.emit("document_stream_start", payload);

      // Assert — Bridge C (same channel) receives; Bridge B (different channel) does not
      expect(bridgeCReceived).toHaveLength(1);
      expect(bridgeCReceived[0]).toEqual(payload);
      expect(bridgeBReceived).toHaveLength(0);
      // Bridge A should not receive its own messages (BroadcastChannel delivers to OTHER instances)
      expect(bridgeAReceived).toHaveLength(0);

      bridgeA.disconnect();
      bridgeB.disconnect();
      bridgeC.disconnect();
    });

    it("emit_MultipleChannelsInParallel_EachChannelIndependent", () => {
      // Arrange — two completely independent communication pairs
      const pairA_sender = new SprkChatBridge({ context: "pair-a" });
      const pairA_receiver = new SprkChatBridge({ context: "pair-a" });
      const pairB_sender = new SprkChatBridge({ context: "pair-b" });
      const pairB_receiver = new SprkChatBridge({ context: "pair-b" });

      const pairATokens: string[] = [];
      const pairBTokens: string[] = [];

      pairA_receiver.subscribe("document_stream_token", (p) =>
        pairATokens.push(p.token)
      );
      pairB_receiver.subscribe("document_stream_token", (p) =>
        pairBTokens.push(p.token)
      );

      // Act — interleave tokens between the two pairs
      pairA_sender.emit(
        "document_stream_token",
        makeStreamToken("op-a", "alpha-0", 0)
      );
      pairB_sender.emit(
        "document_stream_token",
        makeStreamToken("op-b", "beta-0", 0)
      );
      pairA_sender.emit(
        "document_stream_token",
        makeStreamToken("op-a", "alpha-1", 1)
      );
      pairB_sender.emit(
        "document_stream_token",
        makeStreamToken("op-b", "beta-1", 1)
      );

      // Assert — each pair only sees its own tokens
      expect(pairATokens).toEqual(["alpha-0", "alpha-1"]);
      expect(pairBTokens).toEqual(["beta-0", "beta-1"]);

      pairA_sender.disconnect();
      pairA_receiver.disconnect();
      pairB_sender.disconnect();
      pairB_receiver.disconnect();
    });

    it("emit_PostMessageChannelIsolation_DifferentChannelsDoNotCrossTalk", () => {
      // Arrange — force postMessage transport
      delete (globalThis as Record<string, unknown>).BroadcastChannel;

      const bridgeX = new SprkChatBridge({
        context: "iso-x",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });
      const bridgeY = new SprkChatBridge({
        context: "iso-y",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });

      const xReceived: DocumentStreamStartPayload[] = [];
      const yReceived: DocumentStreamStartPayload[] = [];

      bridgeX.subscribe("document_stream_start", (p) => xReceived.push(p));
      bridgeY.subscribe("document_stream_start", (p) => yReceived.push(p));

      // Act — send event to channel iso-x only
      const msgEvent = new MessageEvent("message", {
        data: {
          channel: "sprk-workspace-iso-x",
          event: "document_stream_start",
          payload: makeStreamStart("op-iso-x"),
        },
        origin: window.location.origin,
      });
      window.dispatchEvent(msgEvent);

      // Assert — only bridge X receives
      expect(xReceived).toHaveLength(1);
      expect(yReceived).toHaveLength(0);

      bridgeX.disconnect();
      bridgeY.disconnect();
    });
  });

  // -----------------------------------------------------------------------
  // Disconnect behavior
  // -----------------------------------------------------------------------

  describe("disconnect behavior", () => {
    it("disconnect_ReceiverDisconnects_NoEventsDeliveredToDisconnectedBridge", () => {
      // Arrange
      const sender = new SprkChatBridge({ context: "integ-disc-recv" });
      const receiver = new SprkChatBridge({ context: "integ-disc-recv" });
      const handler = jest.fn();

      receiver.subscribe("document_stream_token", handler);

      // Act — disconnect receiver, then sender emits
      receiver.disconnect();
      sender.emit(
        "document_stream_token",
        makeStreamToken("op-disc", "orphan", 0)
      );

      // Assert — handler was never called
      expect(handler).not.toHaveBeenCalled();

      sender.disconnect();
    });

    it("disconnect_SenderDisconnects_EmitThrowsError", () => {
      // Arrange
      const sender = new SprkChatBridge({ context: "integ-disc-send" });
      const receiver = new SprkChatBridge({ context: "integ-disc-send" });

      receiver.subscribe("document_stream_token", jest.fn());
      sender.disconnect();

      // Act & Assert — emitting after disconnect throws
      expect(() => {
        sender.emit(
          "document_stream_token",
          makeStreamToken("op-disc-send", "fail", 0)
        );
      }).toThrow("Cannot emit after disconnect");

      receiver.disconnect();
    });

    it("disconnect_DisconnectedSenderDoesNotCrashReceiver_ReceiverRemainsOperational", () => {
      // Arrange — verify that one pane disconnecting does not affect the other
      const bridgeA = new SprkChatBridge({ context: "integ-disc-safe" });
      const bridgeB = new SprkChatBridge({ context: "integ-disc-safe" });
      const bridgeC = new SprkChatBridge({ context: "integ-disc-safe" });

      const bReceived: DocumentStreamTokenPayload[] = [];
      bridgeB.subscribe("document_stream_token", (p) => bReceived.push(p));

      // Act — disconnect A, then C (still connected) sends to B
      bridgeA.disconnect();
      bridgeC.emit(
        "document_stream_token",
        makeStreamToken("op-c-sends", "still-alive", 0)
      );

      // Assert — B still receives from C even though A disconnected
      expect(bReceived).toHaveLength(1);
      expect(bReceived[0].token).toBe("still-alive");

      bridgeB.disconnect();
      bridgeC.disconnect();
    });

    it("disconnect_MidStream_RemainingTokensNotDelivered", () => {
      // Arrange — simulate a stream being interrupted by disconnect
      const sender = new SprkChatBridge({ context: "integ-disc-mid" });
      const receiver = new SprkChatBridge({ context: "integ-disc-mid" });

      const received: DocumentStreamTokenPayload[] = [];
      receiver.subscribe("document_stream_token", (p) => received.push(p));

      // Act — send first 5 tokens, disconnect receiver, send 5 more
      for (let i = 0; i < 5; i++) {
        sender.emit(
          "document_stream_token",
          makeStreamToken("op-mid", `tok-${i}`, i)
        );
      }

      receiver.disconnect();

      for (let i = 5; i < 10; i++) {
        sender.emit(
          "document_stream_token",
          makeStreamToken("op-mid", `tok-${i}`, i)
        );
      }

      // Assert — only first 5 tokens received
      expect(received).toHaveLength(5);
      for (let i = 0; i < 5; i++) {
        expect(received[i].token).toBe(`tok-${i}`);
      }

      sender.disconnect();
    });

    it("disconnect_SubscribeAfterDisconnect_ThrowsError", () => {
      // Arrange
      const bridge = new SprkChatBridge({ context: "integ-disc-sub" });
      bridge.disconnect();

      // Act & Assert
      expect(() => {
        bridge.subscribe("context_changed", jest.fn());
      }).toThrow("Cannot subscribe after disconnect");
    });
  });

  // -----------------------------------------------------------------------
  // Cleanup completeness
  // -----------------------------------------------------------------------

  describe("cleanup completeness", () => {
    it("disconnect_BroadcastChannelBridge_ClosesUnderlyingChannel", () => {
      // Arrange — track MockBroadcastChannel instance count
      const bridge = new SprkChatBridge({ context: "integ-cleanup-bc" });
      bridge.subscribe("document_stream_start", jest.fn());
      bridge.subscribe("document_stream_token", jest.fn());
      bridge.subscribe("document_stream_end", jest.fn());
      bridge.subscribe("selection_changed", jest.fn());
      bridge.subscribe("context_changed", jest.fn());

      // Before disconnect: instance exists in MockBroadcastChannel.instances
      const instancesBefore = MockBroadcastChannel.instances.filter(
        (i) => i.name === "sprk-workspace-integ-cleanup-bc"
      );
      expect(instancesBefore.length).toBeGreaterThanOrEqual(1);

      // Act
      bridge.disconnect();

      // Assert — after disconnect, the BroadcastChannel instance is removed
      // (MockBroadcastChannel.close() removes from instances array)
      const instancesAfter = MockBroadcastChannel.instances.filter(
        (i) => i.name === "sprk-workspace-integ-cleanup-bc"
      );
      expect(instancesAfter).toHaveLength(0);
    });

    it("disconnect_PostMessageBridge_RemovesEventListener", () => {
      // Arrange
      delete (globalThis as Record<string, unknown>).BroadcastChannel;

      const removeListenerSpy = jest.spyOn(window, "removeEventListener");

      const bridge = new SprkChatBridge({
        context: "integ-cleanup-pm",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });
      bridge.subscribe("document_stream_start", jest.fn());
      bridge.subscribe("document_stream_token", jest.fn());
      bridge.subscribe("context_changed", jest.fn());

      // Act
      bridge.disconnect();

      // Assert — window.removeEventListener was called for "message"
      expect(removeListenerSpy).toHaveBeenCalledWith(
        "message",
        expect.any(Function)
      );

      removeListenerSpy.mockRestore();
    });

    it("disconnect_MultipleSubscriptions_AllHandlersCleared", () => {
      // Arrange
      const sender = new SprkChatBridge({ context: "integ-cleanup-all" });
      const receiver = new SprkChatBridge({ context: "integ-cleanup-all" });

      const handlers = {
        start: jest.fn(),
        token: jest.fn(),
        end: jest.fn(),
        replaced: jest.fn(),
        selection: jest.fn(),
        context: jest.fn(),
      };

      receiver.subscribe("document_stream_start", handlers.start);
      receiver.subscribe("document_stream_token", handlers.token);
      receiver.subscribe("document_stream_end", handlers.end);
      receiver.subscribe("document_replaced", handlers.replaced);
      receiver.subscribe("selection_changed", handlers.selection);
      receiver.subscribe("context_changed", handlers.context);

      // Act — disconnect receiver, then send all event types
      receiver.disconnect();

      sender.emit("document_stream_start", makeStreamStart("op-cleanup"));
      sender.emit(
        "document_stream_token",
        makeStreamToken("op-cleanup", "x", 0)
      );
      sender.emit("document_stream_end", makeStreamEnd("op-cleanup", 1));
      sender.emit("document_replaced", {
        operationId: "op-cleanup",
        html: "<p>test</p>",
      });
      sender.emit("selection_changed", {
        text: "t",
        startOffset: 0,
        endOffset: 1,
      });
      sender.emit("context_changed", {
        entityType: "sprk_matter",
        entityId: "e1",
      });

      // Assert — no handler was called after disconnect
      expect(handlers.start).not.toHaveBeenCalled();
      expect(handlers.token).not.toHaveBeenCalled();
      expect(handlers.end).not.toHaveBeenCalled();
      expect(handlers.replaced).not.toHaveBeenCalled();
      expect(handlers.selection).not.toHaveBeenCalled();
      expect(handlers.context).not.toHaveBeenCalled();

      sender.disconnect();
    });

    it("disconnect_Idempotent_MultipleDisconnectsDoNotThrow", () => {
      // Arrange
      const bridge = new SprkChatBridge({ context: "integ-cleanup-idem" });
      bridge.subscribe("document_stream_token", jest.fn());

      // Act & Assert — multiple disconnects should not throw
      bridge.disconnect();
      expect(() => bridge.disconnect()).not.toThrow();
      expect(() => bridge.disconnect()).not.toThrow();
      expect(bridge.isDisconnected).toBe(true);
    });

    it("disconnect_UnsubscribeFunctionsStillSafe_AfterDisconnect", () => {
      // Arrange — get unsubscribe function, then disconnect, then call unsub
      const bridge = new SprkChatBridge({ context: "integ-cleanup-unsub" });
      const unsub1 = bridge.subscribe("document_stream_start", jest.fn());
      const unsub2 = bridge.subscribe("document_stream_token", jest.fn());
      const unsub3 = bridge.subscribe("context_changed", jest.fn());

      // Act — disconnect first, then call unsubscribe functions
      bridge.disconnect();

      // Assert — calling unsub after disconnect does not throw
      expect(() => unsub1()).not.toThrow();
      expect(() => unsub2()).not.toThrow();
      expect(() => unsub3()).not.toThrow();
    });
  });
});
