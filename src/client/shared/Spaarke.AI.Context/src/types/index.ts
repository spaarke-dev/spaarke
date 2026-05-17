/**
 * @spaarke/ai-context — Core type definitions
 *
 * Shared interfaces for AI context providers, hooks, and services.
 * These types are extracted from AnalysisWorkspace and SprkChat for
 * reuse across the SpaarkeAi Code Page and future AI surfaces.
 *
 * Standards: ADR-012 (shared library), ADR-015 (AI data governance)
 * NOT PCF-safe — consumers must be React 19 Code Pages.
 */

import type { EntityContext } from './entity-context';

// ---------------------------------------------------------------------------
// Entity Context (re-exported from dedicated file)
// ---------------------------------------------------------------------------

export type { EntityContext, EntityType, EntityResolutionResult } from './entity-context';

// AiPaneEvent is declared in this file (index.ts) — no re-export needed; consumers
// import from '@spaarke/ai-context' which re-exports everything via the package index.

// ---------------------------------------------------------------------------
// Chat Session Context
// ---------------------------------------------------------------------------

/**
 * Chat session state managed by the AI context layer.
 * Decoupled from SprkChat component props — represents the session
 * lifecycle state that can be persisted and restored across navigation.
 */
export interface ChatSessionContext {
  /** Active chat session ID (null when no session is open) */
  sessionId: string | null;
  /** Active playbook ID governing the session's behavior */
  playbookId: string | undefined;
  /** Active document ID providing context to the chat */
  documentId: string | undefined;
  /** BFF API base URL for all chat API calls */
  bffBaseUrl: string;
}

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
   * OutputPanel and SourcePanel subscribe via useStandaloneAi() to receive these events.
   * Optional — when absent, pane events are silently dropped.
   */
  onPaneEvent?: (event: AiPaneEvent) => void;
}

// ---------------------------------------------------------------------------
// Auth Context Shape (for AI surfaces)
// ---------------------------------------------------------------------------

/**
 * Minimal auth state exposed to AI context consumers.
 * The full MSAL state stays in @spaarke/auth; this is the
 * subset needed by AI components.
 */
export interface AiAuthContext {
  /** Bearer access token for BFF API calls (null when not authenticated) */
  token: string | null;
  /** Whether the user is currently authenticated */
  isAuthenticated: boolean;
  /** Refresh the token (e.g., before a long streaming call) */
  refreshToken: () => Promise<string | null>;
}

// ---------------------------------------------------------------------------
// Standalone Chat Context (re-exported from dedicated file)
// ---------------------------------------------------------------------------

export type {
  StandaloneAiContextValue,
  StandaloneAiProviderProps,
  StandaloneContextMapping,
} from './standalone-context';
export {
  STANDALONE_SESSION_KEY_PREFIX,
  STANDALONE_CHAT_SESSION_KEY,
  STANDALONE_PLAYBOOK_KEY,
} from './standalone-context';

// ---------------------------------------------------------------------------
// Analysis Context (for AnalysisWorkspace — with analysis record)
// ---------------------------------------------------------------------------

/**
 * Context shape for analysis-scoped AI chat (tied to a specific analysis record).
 *
 * @see AnalysisAiContext.tsx in AnalysisWorkspace (source of this pattern)
 */
export interface AnalysisAiContextShape {
  /** Entity context describing what the user is working on */
  entityContext: EntityContext | null;
  /** Auth state for API calls */
  auth: AiAuthContext;
  /** Whether any AI context data is loading */
  isLoading: boolean;
  /** Analysis record ID (sprk_analysisoutput GUID) */
  analysisId: string | null;
  /** Document ID linked to the analysis */
  documentId: string | null;
}

// ---------------------------------------------------------------------------
// Widget Registration (for Output Pane)
// ---------------------------------------------------------------------------

/**
 * Registration descriptor for an AI output widget.
 * Widgets are registered by type string and loaded lazily via dynamic import().
 *
 * @see @spaarke/ai-outputs for the widget implementations
 */
export interface AiWidgetDescriptor {
  /** Unique widget type identifier (e.g., "document-diff", "entity-card") */
  type: string;
  /** Human-readable display name */
  displayName: string;
  /** Whether this widget can be shown without a complete AI response */
  supportsStreaming: boolean;
}

// ---------------------------------------------------------------------------
// Chat types (hooks, service client, SSE events)
// ---------------------------------------------------------------------------

export type {
  // Message types
  ChatMessageRole,
  IChatMessagePlanStep,
  IChatMessageMetadata,
  IChatMessage,
  // Session types
  IChatSession,
  // SSE event types
  ChatSseEventType,
  ICitationSseItem,
  IChatSseEventData,
  IChatSseEvent,
  // Document stream SSE
  IDocumentStreamSseEvent,
  // Citation types
  CitationSourceType,
  ICitation,
  // Context selector types
  IPlaybookOption,
  IHostContext,
  // Hook return types
  IUseChatSessionResult,
  IUseSseStreamResult,
  // Context mapping response types
  IInlineActionInfo,
  IAnalysisPlaybookInfo,
  IAnalysisKnowledgeSourceInfo,
  IAnalysisContextInfo,
  ICommandEntry,
  IAnalysisScopeMetadata,
  IAnalysisChatContextResponse,
  IUseChatContextMappingResult,
  IUseChatPlaybooksResult,
} from './chat';

// ---------------------------------------------------------------------------
// Service Client Types
// ---------------------------------------------------------------------------

/**
 * Configuration for the AI context service clients.
 * Passed to AiContextProvider at the application root.
 */
export interface AiContextConfig {
  /** BFF API base URL (from buildBffApiUrl() in @spaarke/auth) */
  bffBaseUrl: string;
  /** Access token factory — called before each API request */
  getAccessToken: () => Promise<string | null>;
  /** Feature flags (from @spaarke/auth runtime config) */
  features?: {
    /** Enable Azure AI Foundry Agent Service routing (ADR-013) */
    enableAgentService?: boolean;
    /** Enable standalone chat without entity context */
    enableStandaloneChat?: boolean;
  };
}
