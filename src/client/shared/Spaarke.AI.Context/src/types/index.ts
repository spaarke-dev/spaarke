/**
 * @spaarke/ai-context — Core type definitions
 *
 * Shared interfaces consumed by @spaarke/ai-widgets (AiSessionProvider) and the
 * SpaarkeAi Code Page. Types with zero consumers (ChatSessionContext,
 * AiAuthContext, AnalysisAiContextShape, AiWidgetDescriptor, AiContextConfig and
 * ~20 chat types) were DELETED 2026-10-03 — reuse audit C-14.
 *
 * Standards: ADR-012 (shared library), ADR-015 (AI data governance)
 * NOT PCF-safe — consumers must be React 19 Code Pages.
 */

// ---------------------------------------------------------------------------
// Entity Context (re-exported from dedicated file)
// ---------------------------------------------------------------------------

export type { EntityContext, EntityType } from './entity-context';

// AiPaneEvent is declared in this file (index.ts) — no re-export needed; consumers
// import from '@spaarke/ai-context' which re-exports everything via the package index.

// ---------------------------------------------------------------------------
// Streaming Context
// ---------------------------------------------------------------------------

/**
 * Tracks the lifecycle of a document streaming operation.
 * Used for UI indicators showing when the AI is streaming content
 * into the editor pane.
 */
export interface StreamingState {
  /** Whether a document stream is currently in progress */
  isStreaming: boolean;
  /** The operationId of the current (or last) streaming operation */
  operationId: string | null;
  /** Number of tokens received in the current streaming operation */
  tokenCount: number;
}

/**
 * AI pane-routing SSE event forwarded from the BFF stream.
 * Mirrors IAiPaneEvent from @spaarke/ui-components SprkChat types but is
 * re-declared here so @spaarke/ai-context has no dependency on ui-components.
 */
export interface AiPaneEvent {
  /** Discriminates the target pane and event semantics. */
  event: 'output_pane' | 'source_pane' | 'source_highlight';
  /**
   * Widget type string matching OutputWidgetType or SourceWidgetType enum values.
   * Present on output_pane and source_pane events.
   */
  widgetType?: string;
  /**
   * Widget-specific data payload (shape is widget-dependent).
   * Present on output_pane and source_pane events.
   */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  payload?: any;
  /** Source reference identifier for source_highlight events. */
  sourceRef?: string;
  /** Selection reference within the source widget for source_highlight events. */
  selectionRef?: string;
}

/**
 * Callbacks for direct SSE streaming into the editor (bypasses BroadcastChannel).
 * Registered by the editor pane; invoked by the chat pane as tokens arrive.
 *
 * Zero-serialization path: SprkChat → callback → editor ref.insert()
 */
export interface StreamingCallbacks {
  /** Called when a document stream operation begins */
  onStreamStart: (operationId: string) => void;
  /** Called for each token in the stream (high-frequency — avoid React state updates here) */
  onStreamToken: (token: string) => void;
  /** Called when the stream operation completes or is cancelled */
  onStreamEnd: (operationId: string) => void;
  /**
   * Called when an AI pane-routing SSE event arrives (output_pane / source_pane / source_highlight).
   * Invoked synchronously from the SprkChat SSE fetch loop.
   * OutputPanel and SourcePanel subscribe via the AI session context to receive these events.
   * Optional — when absent, pane events are silently dropped.
   */
  onPaneEvent?: (event: AiPaneEvent) => void;
}

export type { IChatSession } from './chat';
