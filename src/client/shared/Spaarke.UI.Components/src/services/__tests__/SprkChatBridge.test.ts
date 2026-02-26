/**
 * SprkChatBridge Unit Tests
 *
 * Tests both BroadcastChannel transport and postMessage fallback,
 * subscribe/unsubscribe lifecycle, disconnect cleanup, and channel naming.
 */

import {
  SprkChatBridge,
  DocumentStreamStartPayload,
  DocumentStreamTokenPayload,
  DocumentStreamEndPayload,
  DocumentReplacedPayload,
  SelectionChangedPayload,
  ContextChangedPayload,
} from "../SprkChatBridge";

// ---------------------------------------------------------------------------
// BroadcastChannel mock
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
// Test setup
// ---------------------------------------------------------------------------

describe("SprkChatBridge", () => {
  const originalBroadcastChannel = (globalThis as Record<string, unknown>).BroadcastChannel;

  beforeEach(() => {
    MockBroadcastChannel.reset();
    (globalThis as Record<string, unknown>).BroadcastChannel = MockBroadcastChannel;
  });

  afterEach(() => {
    MockBroadcastChannel.reset();
    if (originalBroadcastChannel) {
      (globalThis as Record<string, unknown>).BroadcastChannel = originalBroadcastChannel;
    } else {
      delete (globalThis as Record<string, unknown>).BroadcastChannel;
    }
  });

  // -----------------------------------------------------------------------
  // Channel naming
  // -----------------------------------------------------------------------

  describe("channel naming", () => {
    it("should create channel name with sprk-workspace- prefix", () => {
      const bridge = new SprkChatBridge({ context: "session-123" });
      expect(bridge.channelName).toBe("sprk-workspace-session-123");
      bridge.disconnect();
    });

    it("should handle complex context strings", () => {
      const bridge = new SprkChatBridge({ context: "matter-abc-def-456" });
      expect(bridge.channelName).toBe("sprk-workspace-matter-abc-def-456");
      bridge.disconnect();
    });
  });

  // -----------------------------------------------------------------------
  // Transport selection
  // -----------------------------------------------------------------------

  describe("transport selection", () => {
    it("should use BroadcastChannel when available (auto mode)", () => {
      const bridge = new SprkChatBridge({ context: "test" });
      expect(bridge.transportType).toBe("broadcast");
      bridge.disconnect();
    });

    it("should fall back to postMessage when BroadcastChannel unavailable", () => {
      delete (globalThis as Record<string, unknown>).BroadcastChannel;
      const bridge = new SprkChatBridge({ context: "test" });
      expect(bridge.transportType).toBe("postmessage");
      bridge.disconnect();
    });

    it("should use postMessage when explicitly requested", () => {
      const bridge = new SprkChatBridge({
        context: "test",
        transport: "postmessage",
      });
      expect(bridge.transportType).toBe("postmessage");
      bridge.disconnect();
    });

    it("should throw when broadcast explicitly requested but unavailable", () => {
      delete (globalThis as Record<string, unknown>).BroadcastChannel;
      expect(() => {
        new SprkChatBridge({ context: "test", transport: "broadcast" });
      }).toThrow("BroadcastChannel is not available");
    });
  });

  // -----------------------------------------------------------------------
  // BroadcastChannel transport: emit and receive events
  // -----------------------------------------------------------------------

  describe("BroadcastChannel transport", () => {
    it("should emit and receive document_stream_start events", (done) => {
      const sender = new SprkChatBridge({ context: "bc-test" });
      const receiver = new SprkChatBridge({ context: "bc-test" });

      const payload: DocumentStreamStartPayload = {
        operationId: "op-1",
        targetPosition: "cursor",
        operationType: "insert",
      };

      receiver.subscribe("document_stream_start", (received) => {
        expect(received).toEqual(payload);
        sender.disconnect();
        receiver.disconnect();
        done();
      });

      sender.emit("document_stream_start", payload);
    });

    it("should emit and receive document_stream_token events", (done) => {
      const sender = new SprkChatBridge({ context: "bc-token" });
      const receiver = new SprkChatBridge({ context: "bc-token" });

      const payload: DocumentStreamTokenPayload = {
        operationId: "op-1",
        token: "Hello",
        index: 0,
      };

      receiver.subscribe("document_stream_token", (received) => {
        expect(received).toEqual(payload);
        sender.disconnect();
        receiver.disconnect();
        done();
      });

      sender.emit("document_stream_token", payload);
    });

    it("should emit and receive document_stream_end events", (done) => {
      const sender = new SprkChatBridge({ context: "bc-end" });
      const receiver = new SprkChatBridge({ context: "bc-end" });

      const payload: DocumentStreamEndPayload = {
        operationId: "op-1",
        cancelled: false,
        totalTokens: 42,
      };

      receiver.subscribe("document_stream_end", (received) => {
        expect(received).toEqual(payload);
        sender.disconnect();
        receiver.disconnect();
        done();
      });

      sender.emit("document_stream_end", payload);
    });

    it("should emit and receive document_replaced events", (done) => {
      const sender = new SprkChatBridge({ context: "bc-replace" });
      const receiver = new SprkChatBridge({ context: "bc-replace" });

      const payload: DocumentReplacedPayload = {
        operationId: "op-2",
        html: "<p>New content</p>",
        previousVersionId: "v1",
      };

      receiver.subscribe("document_replaced", (received) => {
        expect(received).toEqual(payload);
        sender.disconnect();
        receiver.disconnect();
        done();
      });

      sender.emit("document_replaced", payload);
    });

    it("should emit and receive selection_changed events", (done) => {
      const sender = new SprkChatBridge({ context: "bc-select" });
      const receiver = new SprkChatBridge({ context: "bc-select" });

      const payload: SelectionChangedPayload = {
        text: "selected text",
        startOffset: 10,
        endOffset: 23,
        context: "paragraph-3",
      };

      receiver.subscribe("selection_changed", (received) => {
        expect(received).toEqual(payload);
        sender.disconnect();
        receiver.disconnect();
        done();
      });

      sender.emit("selection_changed", payload);
    });

    it("should emit and receive context_changed events", (done) => {
      const sender = new SprkChatBridge({ context: "bc-ctx" });
      const receiver = new SprkChatBridge({ context: "bc-ctx" });

      const payload: ContextChangedPayload = {
        entityType: "sprk_analysis",
        entityId: "abc-123",
        playbookId: "playbook-1",
      };

      receiver.subscribe("context_changed", (received) => {
        expect(received).toEqual(payload);
        sender.disconnect();
        receiver.disconnect();
        done();
      });

      sender.emit("context_changed", payload);
    });

    it("should deliver events to multiple subscribers", () => {
      const sender = new SprkChatBridge({ context: "bc-multi" });
      const receiver = new SprkChatBridge({ context: "bc-multi" });

      const handler1 = jest.fn();
      const handler2 = jest.fn();

      receiver.subscribe("document_stream_token", handler1);
      receiver.subscribe("document_stream_token", handler2);

      const payload: DocumentStreamTokenPayload = {
        operationId: "op-1",
        token: "word",
        index: 5,
      };

      sender.emit("document_stream_token", payload);

      expect(handler1).toHaveBeenCalledWith(payload);
      expect(handler2).toHaveBeenCalledWith(payload);

      sender.disconnect();
      receiver.disconnect();
    });

    it("should not deliver events of wrong type to handler", () => {
      const sender = new SprkChatBridge({ context: "bc-type" });
      const receiver = new SprkChatBridge({ context: "bc-type" });

      const tokenHandler = jest.fn();
      receiver.subscribe("document_stream_token", tokenHandler);

      sender.emit("document_stream_start", {
        operationId: "op-1",
        targetPosition: "cursor",
        operationType: "insert",
      });

      expect(tokenHandler).not.toHaveBeenCalled();

      sender.disconnect();
      receiver.disconnect();
    });
  });

  // -----------------------------------------------------------------------
  // postMessage fallback transport
  // -----------------------------------------------------------------------

  describe("postMessage fallback transport", () => {
    beforeEach(() => {
      delete (globalThis as Record<string, unknown>).BroadcastChannel;
    });

    it("should emit and receive events via postMessage", () => {
      const bridge = new SprkChatBridge({
        context: "pm-test",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });

      const handler = jest.fn();
      bridge.subscribe("document_stream_start", handler);

      const payload: DocumentStreamStartPayload = {
        operationId: "op-pm",
        targetPosition: "start",
        operationType: "replace",
      };

      // In jsdom, window.postMessage is async, so simulate the message
      // event dispatch that would normally occur when postMessage fires.
      const event = new MessageEvent("message", {
        data: {
          channel: "sprk-workspace-pm-test",
          event: "document_stream_start",
          payload,
        },
        origin: window.location.origin,
      });
      window.dispatchEvent(event);

      expect(handler).toHaveBeenCalledWith(payload);

      bridge.disconnect();
    });

    it("should ignore messages from wrong origin", () => {
      const handler = jest.fn();
      const bridge = new SprkChatBridge({
        context: "pm-origin",
        transport: "postmessage",
        allowedOrigin: "https://trusted.example.com",
      });

      bridge.subscribe("document_stream_start", handler);

      // Simulate a postMessage from a different origin
      const event = new MessageEvent("message", {
        data: {
          channel: "sprk-workspace-pm-origin",
          event: "document_stream_start",
          payload: {
            operationId: "evil",
            targetPosition: "x",
            operationType: "insert",
          },
        },
        origin: "https://evil.example.com",
      });
      window.dispatchEvent(event);

      expect(handler).not.toHaveBeenCalled();

      bridge.disconnect();
    });

    it("should ignore messages for a different channel name", () => {
      const handler = jest.fn();
      const bridge = new SprkChatBridge({
        context: "pm-channel-a",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });

      bridge.subscribe("document_stream_start", handler);

      // Send a message with a different channel name via direct dispatch
      const event = new MessageEvent("message", {
        data: {
          channel: "sprk-workspace-other-channel",
          event: "document_stream_start",
          payload: {
            operationId: "wrong",
            targetPosition: "x",
            operationType: "insert",
          },
        },
        origin: window.location.origin,
      });
      window.dispatchEvent(event);

      expect(handler).not.toHaveBeenCalled();

      bridge.disconnect();
    });

    it("should ignore malformed messages", () => {
      const handler = jest.fn();
      const bridge = new SprkChatBridge({
        context: "pm-malformed",
        transport: "postmessage",
        allowedOrigin: window.location.origin,
      });

      bridge.subscribe("document_stream_start", handler);

      // Send various malformed messages
      const malformedMessages = [
        null,
        "string data",
        42,
        { noChannel: true },
        { channel: "sprk-workspace-pm-malformed" }, // missing event
        { channel: "sprk-workspace-pm-malformed", event: 42 }, // event not string
      ];

      for (const data of malformedMessages) {
        const event = new MessageEvent("message", {
          data,
          origin: window.location.origin,
        });
        window.dispatchEvent(event);
      }

      expect(handler).not.toHaveBeenCalled();

      bridge.disconnect();
    });
  });

  // -----------------------------------------------------------------------
  // Subscribe / unsubscribe lifecycle
  // -----------------------------------------------------------------------

  describe("subscribe / unsubscribe", () => {
    it("should return an unsubscribe function that stops delivery", () => {
      const sender = new SprkChatBridge({ context: "unsub" });
      const receiver = new SprkChatBridge({ context: "unsub" });
      const handler = jest.fn();

      const unsub = receiver.subscribe("document_stream_token", handler);

      // First emit: handler receives
      sender.emit("document_stream_token", {
        operationId: "op-1",
        token: "a",
        index: 0,
      });
      expect(handler).toHaveBeenCalledTimes(1);

      // Unsubscribe
      unsub();

      // Second emit: handler does NOT receive
      sender.emit("document_stream_token", {
        operationId: "op-1",
        token: "b",
        index: 1,
      });
      expect(handler).toHaveBeenCalledTimes(1);

      sender.disconnect();
      receiver.disconnect();
    });

    it("should allow multiple unsubscribes without error", () => {
      const bridge = new SprkChatBridge({ context: "multi-unsub" });
      const handler = jest.fn();

      const unsub = bridge.subscribe("context_changed", handler);
      unsub();
      unsub(); // Should not throw
      unsub(); // Should still not throw

      bridge.disconnect();
    });

    it("should support multiple handlers for same event independently", () => {
      const sender = new SprkChatBridge({ context: "multi-handler" });
      const receiver = new SprkChatBridge({ context: "multi-handler" });

      const handler1 = jest.fn();
      const handler2 = jest.fn();

      const unsub1 = receiver.subscribe("selection_changed", handler1);
      receiver.subscribe("selection_changed", handler2);

      const payload: SelectionChangedPayload = {
        text: "foo",
        startOffset: 0,
        endOffset: 3,
      };

      sender.emit("selection_changed", payload);
      expect(handler1).toHaveBeenCalledTimes(1);
      expect(handler2).toHaveBeenCalledTimes(1);

      // Unsubscribe handler1 only
      unsub1();

      sender.emit("selection_changed", payload);
      expect(handler1).toHaveBeenCalledTimes(1); // Still 1
      expect(handler2).toHaveBeenCalledTimes(2); // Incremented

      sender.disconnect();
      receiver.disconnect();
    });
  });

  // -----------------------------------------------------------------------
  // Disconnect / cleanup
  // -----------------------------------------------------------------------

  describe("disconnect", () => {
    it("should stop delivering events after disconnect", () => {
      const sender = new SprkChatBridge({ context: "disc-deliver" });
      const receiver = new SprkChatBridge({ context: "disc-deliver" });
      const handler = jest.fn();

      receiver.subscribe("document_stream_token", handler);
      receiver.disconnect();

      sender.emit("document_stream_token", {
        operationId: "op-1",
        token: "x",
        index: 0,
      });

      expect(handler).not.toHaveBeenCalled();

      sender.disconnect();
    });

    it("should throw when emit is called after disconnect", () => {
      const bridge = new SprkChatBridge({ context: "disc-emit" });
      bridge.disconnect();

      expect(() => {
        bridge.emit("document_stream_start", {
          operationId: "op-1",
          targetPosition: "cursor",
          operationType: "insert",
        });
      }).toThrow("Cannot emit after disconnect");
    });

    it("should throw when subscribe is called after disconnect", () => {
      const bridge = new SprkChatBridge({ context: "disc-sub" });
      bridge.disconnect();

      expect(() => {
        bridge.subscribe("context_changed", jest.fn());
      }).toThrow("Cannot subscribe after disconnect");
    });

    it("should be idempotent (multiple disconnects do not throw)", () => {
      const bridge = new SprkChatBridge({ context: "disc-idem" });
      bridge.disconnect();
      bridge.disconnect(); // Should not throw
      bridge.disconnect(); // Should still not throw
      expect(bridge.isDisconnected).toBe(true);
    });

    it("should report isDisconnected correctly", () => {
      const bridge = new SprkChatBridge({ context: "disc-flag" });
      expect(bridge.isDisconnected).toBe(false);
      bridge.disconnect();
      expect(bridge.isDisconnected).toBe(true);
    });

    it("should clean up postMessage event listener on disconnect", () => {
      delete (globalThis as Record<string, unknown>).BroadcastChannel;

      const removeEventListenerSpy = jest.spyOn(window, "removeEventListener");

      const bridge = new SprkChatBridge({
        context: "disc-pm-cleanup",
        transport: "postmessage",
      });
      bridge.disconnect();

      expect(removeEventListenerSpy).toHaveBeenCalledWith(
        "message",
        expect.any(Function)
      );

      removeEventListenerSpy.mockRestore();
    });
  });

  // -----------------------------------------------------------------------
  // Messages for wrong channel are ignored (BroadcastChannel)
  // -----------------------------------------------------------------------

  describe("cross-channel isolation", () => {
    it("should not receive events from a different channel", () => {
      const sender = new SprkChatBridge({ context: "channel-a" });
      const receiver = new SprkChatBridge({ context: "channel-b" });
      const handler = jest.fn();

      receiver.subscribe("document_stream_start", handler);

      sender.emit("document_stream_start", {
        operationId: "op-1",
        targetPosition: "cursor",
        operationType: "insert",
      });

      expect(handler).not.toHaveBeenCalled();

      sender.disconnect();
      receiver.disconnect();
    });
  });

  // -----------------------------------------------------------------------
  // Event ordering
  // -----------------------------------------------------------------------

  describe("event ordering", () => {
    it("should receive events in the order they were sent", () => {
      const sender = new SprkChatBridge({ context: "order" });
      const receiver = new SprkChatBridge({ context: "order" });
      const receivedTokens: string[] = [];

      receiver.subscribe("document_stream_token", (payload) => {
        receivedTokens.push(payload.token);
      });

      const tokens = ["The", "quick", "brown", "fox"];
      tokens.forEach((token, index) => {
        sender.emit("document_stream_token", {
          operationId: "op-1",
          token,
          index,
        });
      });

      expect(receivedTokens).toEqual(tokens);

      sender.disconnect();
      receiver.disconnect();
    });
  });

  // -----------------------------------------------------------------------
  // Error handling in handlers
  // -----------------------------------------------------------------------

  describe("error handling", () => {
    it("should not propagate handler errors to other handlers", () => {
      const sender = new SprkChatBridge({ context: "err-test" });
      const receiver = new SprkChatBridge({ context: "err-test" });

      const errorHandler = jest.fn(() => {
        throw new Error("Handler error");
      });
      const safeHandler = jest.fn();

      // Suppress console.error during this test
      const consoleSpy = jest.spyOn(console, "error").mockImplementation();

      receiver.subscribe("document_stream_token", errorHandler);
      receiver.subscribe("document_stream_token", safeHandler);

      sender.emit("document_stream_token", {
        operationId: "op-1",
        token: "test",
        index: 0,
      });

      expect(errorHandler).toHaveBeenCalledTimes(1);
      expect(safeHandler).toHaveBeenCalledTimes(1);
      expect(consoleSpy).toHaveBeenCalled();

      consoleSpy.mockRestore();
      sender.disconnect();
      receiver.disconnect();
    });
  });
});
