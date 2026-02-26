/**
 * Mock module for @spaarke/ui-components/services/SprkChatBridge
 *
 * Used by jest moduleNameMapper to replace the real SprkChatBridge
 * in hook/component tests for isolation.
 */

export class SprkChatBridge {
    context: string;
    channelName: string;
    isDisconnected = false;
    transportType = "broadcast";

    private _handlers: Map<string, Array<(payload: unknown) => void>> = new Map();

    constructor(options: { context: string; transport?: string; allowedOrigin?: string }) {
        this.context = options.context;
        this.channelName = `sprk-workspace-${options.context}`;
    }

    subscribe<T = unknown>(event: string, handler: (payload: T) => void): () => void {
        if (this.isDisconnected) {
            throw new Error("Cannot subscribe after disconnect");
        }
        const handlers = this._handlers.get(event) ?? [];
        handlers.push(handler as (payload: unknown) => void);
        this._handlers.set(event, handlers);

        return () => {
            const current = this._handlers.get(event) ?? [];
            const idx = current.indexOf(handler as (payload: unknown) => void);
            if (idx >= 0) current.splice(idx, 1);
        };
    }

    emit(event: string, payload: unknown): void {
        if (this.isDisconnected) {
            throw new Error("Cannot emit after disconnect");
        }
        const handlers = this._handlers.get(event) ?? [];
        for (const handler of handlers) {
            try {
                handler(payload);
            } catch (err) {
                console.error(`Error in handler for "${event}":`, err);
            }
        }
    }

    disconnect(): void {
        this.isDisconnected = true;
        this._handlers.clear();
    }
}

// Re-export types as stubs
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

export type SprkChatBridgeEventName =
    | "document_stream_start"
    | "document_stream_token"
    | "document_stream_end"
    | "document_replaced"
    | "selection_changed";
