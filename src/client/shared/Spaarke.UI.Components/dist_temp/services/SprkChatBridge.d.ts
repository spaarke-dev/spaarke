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
    operationType: 'insert' | 'replace' | 'diff';
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
    transport?: 'broadcast' | 'postmessage' | 'auto';
    /**
     * Allowed origin for postMessage transport. Defaults to current origin.
     * Only relevant when transport is "postmessage" or "auto" falls back.
     */
    allowedOrigin?: string;
}
/** Typed event handler */
export type SprkChatBridgeHandler<K extends SprkChatBridgeEventName> = (payload: SprkChatBridgeEventMap[K]) => void;
/** Unsubscribe function returned by subscribe() */
export type SprkChatBridgeUnsubscribe = () => void;
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
export declare class SprkChatBridge {
    /** The channel name derived from the context: sprk-workspace-{context} */
    readonly channelName: string;
    /** The transport type in use ("broadcast" | "postmessage") */
    readonly transportType: 'broadcast' | 'postmessage';
    private readonly transport;
    private readonly handlers;
    private disconnected;
    constructor(options: SprkChatBridgeOptions);
    /**
     * @deprecated Use AnalysisAiContext streaming callbacks instead.
     *
     * Emit a typed event to all listeners on the channel.
     *
     * @param event - The event name
     * @param payload - The typed payload for this event
     */
    emit<K extends SprkChatBridgeEventName>(event: K, payload: SprkChatBridgeEventMap[K]): void;
    /**
     * @deprecated Use AnalysisAiContext streaming callbacks instead.
     *
     * Subscribe to a typed event. Returns an unsubscribe function.
     *
     * @param event - The event name to listen for
     * @param handler - The handler that receives the typed payload
     * @returns A function that removes this subscription when called
     */
    subscribe<K extends SprkChatBridgeEventName>(event: K, handler: SprkChatBridgeHandler<K>): SprkChatBridgeUnsubscribe;
    /**
     * @deprecated Use AnalysisAiContext streaming callbacks instead.
     *
     * Close the channel and remove all event listeners.
     * After calling disconnect(), emit() and subscribe() will throw.
     */
    disconnect(): void;
    /**
     * @deprecated Use AnalysisAiContext streaming callbacks instead.
     *
     * Whether the bridge has been disconnected.
     */
    get isDisconnected(): boolean;
    private routeMessage;
}
//# sourceMappingURL=SprkChatBridge.d.ts.map