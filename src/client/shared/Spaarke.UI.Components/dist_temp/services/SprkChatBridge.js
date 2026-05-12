/**
 * @deprecated Use AnalysisAiContext streaming callbacks instead.
 * This bridge is retained for potential future cross-pane scenarios.
 * In the unified AnalysisWorkspace, SprkChat communicates via React
 * context (AnalysisAiContext) rather than BroadcastChannel.
 *
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
// BroadcastChannel transport
// ---------------------------------------------------------------------------
function createBroadcastTransport(channelName) {
    const bc = new BroadcastChannel(channelName);
    let receiveHandler = null;
    bc.onmessage = (event) => {
        if (receiveHandler && event.data && event.data.channel === channelName) {
            receiveHandler(event.data);
        }
    };
    return {
        send(envelope) {
            bc.postMessage(envelope);
        },
        onReceive(handler) {
            receiveHandler = handler;
        },
        close() {
            receiveHandler = null;
            bc.close();
        },
    };
}
// ---------------------------------------------------------------------------
// postMessage fallback transport
// ---------------------------------------------------------------------------
function createPostMessageTransport(channelName, allowedOrigin) {
    let receiveHandler = null;
    const messageListener = (event) => {
        // Validate origin
        if (event.origin !== allowedOrigin) {
            return;
        }
        // Validate envelope structure and channel name
        const data = event.data;
        if (data &&
            typeof data === 'object' &&
            data.channel === channelName &&
            typeof data.event === 'string' &&
            data.payload !== undefined) {
            if (receiveHandler) {
                receiveHandler(data);
            }
        }
    };
    if (typeof window !== 'undefined') {
        window.addEventListener('message', messageListener);
    }
    return {
        send(envelope) {
            if (typeof window !== 'undefined') {
                window.postMessage(envelope, allowedOrigin);
            }
        },
        onReceive(handler) {
            receiveHandler = handler;
        },
        close() {
            receiveHandler = null;
            if (typeof window !== 'undefined') {
                window.removeEventListener('message', messageListener);
            }
        },
    };
}
// ---------------------------------------------------------------------------
// SprkChatBridge class
// ---------------------------------------------------------------------------
/**
 * @deprecated Use AnalysisAiContext streaming callbacks instead.
 * This class is retained for potential future cross-pane scenarios.
 *
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
    constructor(options) {
        this.handlers = new Map();
        this.disconnected = false;
        const { context, transport: transportPref = 'auto', allowedOrigin } = options;
        this.channelName = `sprk-workspace-${context}`;
        const origin = allowedOrigin ?? (typeof window !== 'undefined' ? window.location.origin : '*');
        // Determine transport
        const useBroadcast = transportPref === 'broadcast' || (transportPref === 'auto' && typeof BroadcastChannel !== 'undefined');
        if (transportPref === 'broadcast' && typeof BroadcastChannel === 'undefined') {
            throw new Error('SprkChatBridge: BroadcastChannel transport requested but BroadcastChannel is not available in this environment.');
        }
        if (useBroadcast) {
            this.transport = createBroadcastTransport(this.channelName);
            this.transportType = 'broadcast';
        }
        else {
            this.transport = createPostMessageTransport(this.channelName, origin);
            this.transportType = 'postmessage';
        }
        // Wire transport receive to internal router
        this.transport.onReceive(envelope => this.routeMessage(envelope));
    }
    /**
     * @deprecated Use AnalysisAiContext streaming callbacks instead.
     *
     * Emit a typed event to all listeners on the channel.
     *
     * @param event - The event name
     * @param payload - The typed payload for this event
     */
    emit(event, payload) {
        if (this.disconnected) {
            throw new Error('SprkChatBridge: Cannot emit after disconnect.');
        }
        const envelope = {
            channel: this.channelName,
            event,
            payload,
        };
        this.transport.send(envelope);
    }
    /**
     * @deprecated Use AnalysisAiContext streaming callbacks instead.
     *
     * Subscribe to a typed event. Returns an unsubscribe function.
     *
     * @param event - The event name to listen for
     * @param handler - The handler that receives the typed payload
     * @returns A function that removes this subscription when called
     */
    subscribe(event, handler) {
        if (this.disconnected) {
            throw new Error('SprkChatBridge: Cannot subscribe after disconnect.');
        }
        let handlerSet = this.handlers.get(event);
        if (!handlerSet) {
            handlerSet = new Set();
            this.handlers.set(event, handlerSet);
        }
        // Cast is safe: the type constraint ensures K-specific handler is added
        // to the K-specific set. Runtime routing in routeMessage ensures correct dispatch.
        const typedHandler = handler;
        handlerSet.add(typedHandler);
        return () => {
            handlerSet.delete(typedHandler);
            if (handlerSet.size === 0) {
                this.handlers.delete(event);
            }
        };
    }
    /**
     * @deprecated Use AnalysisAiContext streaming callbacks instead.
     *
     * Close the channel and remove all event listeners.
     * After calling disconnect(), emit() and subscribe() will throw.
     */
    disconnect() {
        if (this.disconnected) {
            return; // Idempotent
        }
        this.disconnected = true;
        this.handlers.clear();
        this.transport.close();
    }
    /**
     * @deprecated Use AnalysisAiContext streaming callbacks instead.
     *
     * Whether the bridge has been disconnected.
     */
    get isDisconnected() {
        return this.disconnected;
    }
    // -----------------------------------------------------------------------
    // Private: message routing
    // -----------------------------------------------------------------------
    routeMessage(envelope) {
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
            }
            catch (error) {
                console.error(`SprkChatBridge: Error in handler for "${envelope.event}":`, error);
            }
        }
    }
}
//# sourceMappingURL=SprkChatBridge.js.map