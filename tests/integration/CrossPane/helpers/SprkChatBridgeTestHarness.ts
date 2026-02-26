/**
 * SprkChatBridge Test Harness - Portable typed bridge for cross-pane integration tests.
 *
 * This is a standalone implementation that mirrors the real SprkChatBridge API
 * without requiring imports from @spaarke/ui-components. This avoids module
 * resolution issues in the standalone integration test suite.
 *
 * The implementation matches the real SprkChatBridge behavior:
 * - Channel naming: sprk-workspace-{context}
 * - BroadcastChannel primary transport with postMessage fallback
 * - Typed event map with subscribe/emit/disconnect
 * - Handler error isolation (one handler error does not prevent others)
 *
 * @see src/client/shared/Spaarke.UI.Components/src/services/SprkChatBridge.ts
 */

// ---------------------------------------------------------------------------
// Event payload types (mirror of the real types)
// ---------------------------------------------------------------------------

export interface DocumentStreamStartPayload {
    operationId: string;
    targetPosition: string;
    operationType: "insert" | "replace" | "diff";
}

export interface DocumentStreamTokenPayload {
    operationId: string;
    token: string;
    index: number;
}

export interface DocumentStreamEndPayload {
    operationId: string;
    cancelled: boolean;
    totalTokens: number;
}

export interface DocumentReplacedPayload {
    operationId: string;
    html: string;
    previousVersionId?: string;
}

export interface ReAnalysisProgressPayload {
    operationId: string;
    percent: number;
    message: string;
}

export interface SelectionChangedPayload {
    text: string;
    startOffset: number;
    endOffset: number;
    context?: string;
}

export interface ContextChangedPayload {
    entityType: string;
    entityId: string;
    playbookId?: string;
}

export interface SprkChatBridgeEventMap {
    document_stream_start: DocumentStreamStartPayload;
    document_stream_token: DocumentStreamTokenPayload;
    document_stream_end: DocumentStreamEndPayload;
    document_replaced: DocumentReplacedPayload;
    reanalysis_progress: ReAnalysisProgressPayload;
    selection_changed: SelectionChangedPayload;
    context_changed: ContextChangedPayload;
}

export type SprkChatBridgeEventName = keyof SprkChatBridgeEventMap;

// ---------------------------------------------------------------------------
// Internal wire-format envelope
// ---------------------------------------------------------------------------

interface BridgeEnvelope<K extends SprkChatBridgeEventName = SprkChatBridgeEventName> {
    channel: string;
    event: K;
    payload: SprkChatBridgeEventMap[K];
}

// ---------------------------------------------------------------------------
// Transport interface
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
        if (event.origin !== allowedOrigin) {
            return;
        }
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
// Options
// ---------------------------------------------------------------------------

export interface SprkChatBridgeOptions {
    context: string;
    transport?: "broadcast" | "postmessage" | "auto";
    allowedOrigin?: string;
}

// ---------------------------------------------------------------------------
// Handler types
// ---------------------------------------------------------------------------

export type SprkChatBridgeHandler<K extends SprkChatBridgeEventName> = (
    payload: SprkChatBridgeEventMap[K]
) => void;

export type SprkChatBridgeUnsubscribe = () => void;

// ---------------------------------------------------------------------------
// SprkChatBridge class
// ---------------------------------------------------------------------------

export class SprkChatBridge {
    public readonly channelName: string;
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

        this.transport.onReceive((envelope) => this.routeMessage(envelope));
    }

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

        const typedHandler = handler as SprkChatBridgeHandler<SprkChatBridgeEventName>;
        handlerSet.add(typedHandler);

        return () => {
            handlerSet!.delete(typedHandler);
            if (handlerSet!.size === 0) {
                this.handlers.delete(event);
            }
        };
    }

    disconnect(): void {
        if (this.disconnected) {
            return;
        }
        this.disconnected = true;
        this.handlers.clear();
        this.transport.close();
    }

    get isDisconnected(): boolean {
        return this.disconnected;
    }

    private routeMessage(envelope: BridgeEnvelope): void {
        if (envelope.channel !== this.channelName) {
            return;
        }

        const handlerSet = this.handlers.get(envelope.event);
        if (!handlerSet) {
            return;
        }

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
