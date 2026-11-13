/**
 * AI session context types: the host entity, document streaming and pane-routing
 * event shapes, and the chat-session identity.
 *
 * Moved here from the retired ai-context shared package on 2026-11-13
 * (reuse-governance-tightening-r1 task 031, owner decision in
 * `projects/reuse-governance-tightening-r1/notes/package-reviews.md`). That package
 * held only these types, and `AiSessionProvider` in this package was their main
 * consumer. Import them from `@spaarke/ai-widgets`.
 *
 * Standards: ADR-012 (shared library), ADR-015 (AI data governance).
 */

// ---------------------------------------------------------------------------
// Entity context
// ---------------------------------------------------------------------------

/**
 * Discriminated union of supported entity types.
 * Add new types here as the platform expands beyond legal/project use cases.
 */
export type EntityType = 'matter' | 'project' | 'document' | 'account' | 'contact' | 'unknown';

/**
 * The host entity context describing WHERE an AI surface is embedded.
 * Enables entity-scoped search, playbook discovery, and context mapping
 * (GET /api/ai/chat/context-mappings/standalone) without coupling AI components
 * to any specific workspace.
 *
 * Resolved from URL parameters by the host shell (standalone mode) or
 * from the Xrm form context by the host workspace.
 *
 * @see IHostContext in @spaarke/ui-components SprkChat/types.ts
 */
export interface EntityContext {
  /** Discriminated entity type (e.g., "matter", "project", "document") */
  entityType: EntityType;
  /** GUID of the parent entity record */
  entityId: string;
  /** Display name of the parent entity (for logging/UI label) */
  entityName?: string;
  /** Workspace hosting the AI surface (e.g., "LegalWorkspace", "SpaarkeAi") */
  workspaceType?: string;
  /** Page type where the AI surface is embedded (e.g., "form", "workspace", "dashboard") */
  pageType?: string;

  // ── Typed accessors for common entity ID fields ──────────────────────────
  // These mirror the URL params resolved by the host shell.
  // When entityType is "matter", matterId === entityId (and projectId/documentId are undefined).

  /** Matter GUID (populated when entityType === "matter") */
  matterId?: string;
  /** Project GUID (populated when entityType === "project") */
  projectId?: string;
  /** Document GUID (populated when entityType === "document") */
  documentId?: string;
}

// ---------------------------------------------------------------------------
// Streaming context
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
 * re-declared here so this package's public types do not depend on SprkChat's.
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

// ---------------------------------------------------------------------------
// Chat session
// ---------------------------------------------------------------------------

/** A chat session, matching ChatSessionCreatedResponse from the create endpoint. */
export interface IChatSession {
  sessionId: string;
  createdAt: string;
}
