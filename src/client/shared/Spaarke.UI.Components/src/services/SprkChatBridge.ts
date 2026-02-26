/**
 * SprkChatBridge - Cross-pane communication bridge for Code Pages
 *
 * Enables typed communication between the SprkChat side pane and Analysis
 * Workspace Code Page via BroadcastChannel (primary) with window.postMessage
 * fallback for environments where BroadcastChannel is unavailable.
 *
 * Channel naming: sprk-workspace-{context}
 *
 * SECURITY: Auth tokens MUST NEVER be transmitted via this bridge.
 * Each pane authenticates independently via Xrm.Utility.getGlobalContext().
 *
 * @see ADR-012 Shared component library
 */

// ---------------------------------------------------------------------------
// Event payload types
// ---------------------------------------------------------------------------

/** Signals the beginning of a streaming write operation */
export interface DocumentStreamStartPayload {
  operationId: string;
  targetPosition: string;
  /**
   * Type of streaming operation:
   * - "insert": Append tokens at targetPosition (default streaming behavior)
   * - "replace": Replace content at targetPosition with streamed tokens
   * - "diff": Buffer tokens and open DiffReviewPanel for user review before applying
   */
  operationType: "insert" | "replace" | "diff";
}

/** Individual token during a streaming write */
export interface DocumentStreamTokenPayload {
  operationId: string;
  token: string;
  index: number;
}

/** Signals completion of a streaming write */
export interface DocumentStreamEndPayload {
  operationId: string;
  cancelled: boolean;
  totalTokens: number;
}

/** Full document replacement (re-analysis) */
export interface DocumentReplacedPayload {
  operationId: string;
  html: string;
  previousVersionId?: string;
}

/** Re-analysis progress update (percent + status message) */
export interface ReAnalysisProgressPayload {
  operationId: string;
  percent: number;
  message: string;
}

/** User selected text in the editor pane */
export interface SelectionChangedPayload {
  text: string;
  startOffset: number;
  endOffset: number;
  context?: string;
}

/** User navigated to a different record */
export interface ContextChangedPayload {
  entityType: string;
  entityId: string;
  playbookId?: string;
}

// ---------------------------------------------------------------------------
// Event map — maps event name string literals to payload types
// ---------------------------------------------------------------------------

/** Discriminated map of all bridge event names to their payload types */
export interface SprkChatBridgeEventMap {
  document_stream_start: DocumentStreamStartPayload;
  document_stream_token: DocumentStreamTokenPayload;
  document_stream_end: DocumentStreamEndPayload;
  document_replaced: DocumentReplacedPayload;
  reanalysis_progress: ReAnalysisProgressPayload;
  selection_changed: SelectionChangedPayload;
  context_changed: ContextChangedPayload;
}

/** Union of all valid event names */
export type SprkChatBridgeEventName = keyof SprkChatBridgeEventMap;

// ---------------------------------------------------------------------------
// Internal wire-format envelope
// ---------------------------------------------------------------------------

/** Internal envelope sent across the transport */
interface BridgeEnvelope<K extends SprkChatBridgeEventName = SprkChatBridgeEventName> {
  channel: string;
  event: K;
  payload: SprkChatBridgeEventMap[K];
}

// ---------------------------------------------------------------------------
// Options
// ---------------------------------------------------------------------------

/** Configuration options for SprkChatBridge */
export interface SprkChatBridgeOptions {
  /** Context identifier used in channel name (sprk-workspace-{context}) */
  context: string;
  /**
   * Force a specific transport.
   * - "broadcast": Use BroadcastChannel only (throws if unavailable)
   * - "postmessage": Use window.postMessage only
   * - "auto" (default): Prefer BroadcastChannel, fall back to postMessage
   */
  transport?: "broadcast" | "postmessage" | "auto";
  /**
   * Allowed origin for postMessage transport. Defaults to current origin.
   * Only relevant when transport is "postmessage" or "auto" falls back.
   */
  allowedOrigin?: string;
}

// ---------------------------------------------------------------------------
// Handler type
// ---------------------------------------------------------------------------

/** Typed event handler */
export type SprkChatBridgeHandler<K extends SprkChatBridgeEventName> = (
  payload: SprkChatBridgeEventMap[K]
) => void;

/** Unsubscribe function returned by subscribe() */
export type SprkChatBridgeUnsubscribe = () => void;

// ---------------------------------------------------------------------------
// Transport interface (internal)
// ---------------------------------------------------------------------------

interface Transport {
  send(envelope: BridgeEnvelope): void;
  onReceive(handler: (envelope: BridgeEnvelope) => void): void;
  close(): void;
}

// ---------------------------------------------------------------------------
// BroadcastChannel transport
// ---------------------------------------------------------------------------

function createBroadcastTransport(channelName: string): Transport {
  const bc = new BroadcastChannel(channelName);
  let receiveHandler: ((envelope: BridgeEnvelope) => void) | null = null;

  bc.onmessage = (event: MessageEvent) => {
    if (receiveHandler && event.data && event.data.channel === channelName) {
      receiveHandler(event.data as BridgeEnvelope);
    }
  };

  return {
    send(envelope: BridgeEnvelope): void {
      bc.postMessage(envelope);
    },
    onReceive(handler: (envelope: BridgeEnvelope) => void): void {
      receiveHandler = handler;
    },
    close(): void {
      receiveHandler = null;
      bc.close();
    },
  };
}

// ---------------------------------------------------------------------------
// postMessage fallback transport
// ---------------------------------------------------------------------------

function createPostMessageTransport(
  channelName: string,
  allowedOrigin: string
): Transport {
  let receiveHandler: ((envelope: BridgeEnvelope) => void) | null = null;

  const messageListener = (event: MessageEvent): void => {
    // Validate origin
    if (event.origin !== allowedOrigin) {
      return;
    }

    // Validate envelope structure and channel name
    const data = event.data;
    if (
      data &&
      typeof data === "object" &&
      data.channel === channelName &&
      typeof data.event === "string" &&
      data.payload !== undefined
    ) {
      if (receiveHandler) {
        receiveHandler(data as BridgeEnvelope);
      }
    }
  };

  if (typeof window !== "undefined") {
    window.addEventListener("message", messageListener);
  }

  return {
    send(envelope: BridgeEnvelope): void {
      if (typeof window !== "undefined") {
        window.postMessage(envelope, allowedOrigin);
      }
    },
    onReceive(handler: (envelope: BridgeEnvelope) => void): void {
      receiveHandler = handler;
    },
    close(): void {
      receiveHandler = null;
      if (typeof window !== "undefined") {
        window.removeEventListener("message", messageListener);
      }
    },
  };
}

// ---------------------------------------------------------------------------
// SprkChatBridge class
// ---------------------------------------------------------------------------

/**
 * Cross-pane communication bridge for SprkChat Code Pages.
 *
 * Uses BroadcastChannel as the primary transport (same-origin, low latency)
 * with window.postMessage as a fallback when BroadcastChannel is unavailable.
 *
 * @example
 * ```ts
 * const bridge = new SprkChatBridge({ context: "session-abc" });
 *
 * // Subscribe to events
 * const unsub = bridge.subscribe("document_stream_token", (payload) => {
 *   console.log(payload.token);
 * });
 *
 * // Emit events
 * bridge.emit("document_stream_start", {
 *   operationId: "op-1",
 *   targetPosition: "cursor",
 *   operationType: "insert",
 * });
 *
 * // Clean up
 * unsub();
 * bridge.disconnect();
 * ```
 */
export class SprkChatBridge {
  /** The channel name derived from the context: sprk-workspace-{context} */
  public readonly channelName: string;

  /** The transport type in use ("broadcast" | "postmessage") */
  public readonly transportType: "broadcast" | "postmessage";

  private readonly transport: Transport;
  private readonly handlers: Map<
    SprkChatBridgeEventName,
    Set<SprkChatBridgeHandler<SprkChatBridgeEventName>>
  > = new Map();
  private disconnected = false;

  constructor(options: SprkChatBridgeOptions) {
    const { context, transport: transportPref = "auto", allowedOrigin } = options;

    this.channelName = `sprk-workspace-${context}`;

    const origin =
      allowedOrigin ??
      (typeof window !== "undefined" ? window.location.origin : "*");

    // Determine transport
    const useBroadcast =
      transportPref === "broadcast" ||
      (transportPref === "auto" && typeof BroadcastChannel !== "undefined");

    if (transportPref === "broadcast" && typeof BroadcastChannel === "undefined") {
      throw new Error(
        "SprkChatBridge: BroadcastChannel transport requested but BroadcastChannel is not available in this environment."
      );
    }

    if (useBroadcast) {
      this.transport = createBroadcastTransport(this.channelName);
      this.transportType = "broadcast";
    } else {
      this.transport = createPostMessageTransport(this.channelName, origin);
      this.transportType = "postmessage";
    }

    // Wire transport receive to internal router
    this.transport.onReceive((envelope) => this.routeMessage(envelope));
  }

  /**
   * Emit a typed event to all listeners on the channel.
   *
   * @param event - The event name
   * @param payload - The typed payload for this event
   */
  emit<K extends SprkChatBridgeEventName>(
    event: K,
    payload: SprkChatBridgeEventMap[K]
  ): void {
    if (this.disconnected) {
      throw new Error("SprkChatBridge: Cannot emit after disconnect.");
    }

    const envelope: BridgeEnvelope<K> = {
      channel: this.channelName,
      event,
      payload,
    };

    this.transport.send(envelope as BridgeEnvelope);
  }

  /**
   * Subscribe to a typed event. Returns an unsubscribe function.
   *
   * @param event - The event name to listen for
   * @param handler - The handler that receives the typed payload
   * @returns A function that removes this subscription when called
   */
  subscribe<K extends SprkChatBridgeEventName>(
    event: K,
    handler: SprkChatBridgeHandler<K>
  ): SprkChatBridgeUnsubscribe {
    if (this.disconnected) {
      throw new Error("SprkChatBridge: Cannot subscribe after disconnect.");
    }

    let handlerSet = this.handlers.get(event);
    if (!handlerSet) {
      handlerSet = new Set();
      this.handlers.set(event, handlerSet);
    }

    // Cast is safe: the type constraint ensures K-specific handler is added
    // to the K-specific set. Runtime routing in routeMessage ensures correct dispatch.
    const typedHandler = handler as SprkChatBridgeHandler<SprkChatBridgeEventName>;
    handlerSet.add(typedHandler);

    return () => {
      handlerSet!.delete(typedHandler);
      if (handlerSet!.size === 0) {
        this.handlers.delete(event);
      }
    };
  }

  /**
   * Close the channel and remove all event listeners.
   * After calling disconnect(), emit() and subscribe() will throw.
   */
  disconnect(): void {
    if (this.disconnected) {
      return; // Idempotent
    }

    this.disconnected = true;
    this.handlers.clear();
    this.transport.close();
  }

  /**
   * Whether the bridge has been disconnected.
   */
  get isDisconnected(): boolean {
    return this.disconnected;
  }

  // -----------------------------------------------------------------------
  // Private: message routing
  // -----------------------------------------------------------------------

  private routeMessage(envelope: BridgeEnvelope): void {
    // Only route if channel matches (defense in depth)
    if (envelope.channel !== this.channelName) {
      return;
    }

    const handlerSet = this.handlers.get(envelope.event);
    if (!handlerSet) {
      return;
    }

    // Iterate over a snapshot to allow unsubscribe during iteration
    for (const handler of Array.from(handlerSet)) {
      try {
        handler(envelope.payload);
      } catch (error) {
        console.error(
          `SprkChatBridge: Error in handler for "${envelope.event}":`,
          error
        );
      }
    }
  }
}
