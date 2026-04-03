/**
 * SprkChat Types
 *
 * Type definitions for the SprkChat component and its sub-components.
 * Aligns with the ChatEndpoints.cs API contract (AIPL-054).
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 16 APIs only
 */

// ─────────────────────────────────────────────────────────────────────────────
// Chat Message Types
// ─────────────────────────────────────────────────────────────────────────────

/** Role of a chat message, matching the server-side ChatMessageRole enum. */
export type ChatMessageRole = 'User' | 'Assistant' | 'System';

// ─────────────────────────────────────────────────────────────────────────────
// Message Metadata Types (Phase 2E / 2F structured response wiring)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * A plan step as received in a plan_preview message's metadata.
 * Mirrors PlanStep from PlanPreviewCard but is defined here to avoid
 * circular imports between types.ts and PlanPreviewCard.tsx.
 */
export interface IChatMessagePlanStep {
  /** Stable unique identifier for this step. */
  id: string;
  /** Human-readable description of what this step does. */
  description: string;
  /** Current execution status; defaults to 'pending' before execution begins. */
  status: 'pending' | 'running' | 'completed' | 'failed';
  /** Optional partial result text shown below the step description. */
  result?: string;
}

/**
 * Optional metadata on a chat message carrying structured response data.
 *
 * When `responseType` is present the message is rendered by SprkChatMessageRenderer
 * or PlanPreviewCard rather than as plain text.
 *
 * - 'markdown'            → SprkChatMessageRenderer (default plain-text card)
 * - 'citations'           → SprkChatMessageRenderer citations layout
 * - 'diff'                → SprkChatMessageRenderer diff card
 * - 'entity_card'         → SprkChatMessageRenderer entity card
 * - 'action_confirmation' → SprkChatMessageRenderer action confirmation card
 * - 'plan_preview'        → PlanPreviewCard (plan gate — task 2F)
 */
export interface IChatMessageMetadata {
  /**
   * Discriminates the card renderer to use.
   * When absent or 'markdown', the message renders as plain text (legacy behaviour).
   */
  responseType?:
    | 'markdown'
    | 'citations'
    | 'diff'
    | 'entity_card'
    | 'action_confirmation'
    | 'plan_preview'
    | 'document_status'
    | string;

  /**
   * Structured response data passed to SprkChatMessageRenderer.
   * Shape depends on responseType:
   *   markdown:            { text: string }
   *   citations:           { text: string; citations: ICitationRef[] }
   *   diff:                { summary: string; proposedText: string }
   *   entity_card:         { entityName, entityType, entityId, fields? }
   *   action_confirmation: { actionName, status, summary }
   */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  data?: Record<string, any>;

  /**
   * Plan title shown in PlanPreviewCard header.
   * Only meaningful when responseType === 'plan_preview'.
   */
  planTitle?: string;

  /**
   * Ordered plan steps for PlanPreviewCard.
   * Only meaningful when responseType === 'plan_preview'.
   */
  plan?: IChatMessagePlanStep[];
}

/** A single chat message, matching ChatSessionMessageInfo from the history endpoint. */
export interface IChatMessage {
  /** Message role */
  role: ChatMessageRole;
  /** Message text content */
  content: string;
  /** UTC timestamp when the message was created */
  timestamp: string;
  /**
   * Optional structured response metadata (Phase 2E / 2F).
   * When present and responseType is not 'markdown', the message is rendered
   * by SprkChatMessageRenderer or PlanPreviewCard instead of plain text.
   */
  metadata?: IChatMessageMetadata;
}

// ─────────────────────────────────────────────────────────────────────────────
// Chat Session Types
// ─────────────────────────────────────────────────────────────────────────────

/** A chat session, matching ChatSessionCreatedResponse from the create endpoint. */
export interface IChatSession {
  /** The session identifier */
  sessionId: string;
  /** UTC timestamp of session creation */
  createdAt: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// SSE Event Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SSE event types emitted by the streaming endpoints.
 *
 * Phase 2F additions (task 072):
 * - 'plan_preview'       — emitted by /messages when compound intent is detected
 * - 'plan_step_start'    — emitted by /plan/approve before each step begins
 * - 'plan_step_complete' — emitted by /plan/approve after each step finishes
 */
export type ChatSseEventType =
  | 'token'
  | 'done'
  | 'error'
  | 'suggestions'
  | 'citations'
  | 'typing_start'
  | 'typing_end'
  | 'plan_preview'
  | 'plan_step_start'
  | 'plan_step_complete'
  | 'document_processing_start'
  | 'document_processing_complete'
  | 'document_processing_error'
  | 'action_confirmation'
  | 'action_success'
  | 'action_error'
  | 'dialog_open'
  | 'navigate'
  | 'document_stream_start'
  | 'document_stream_token'
  | 'document_stream_end';

/** A parsed SSE event from the stream, matching ChatSseEvent from the server. */
export interface IChatSseEvent {
  /** Event type */
  type: ChatSseEventType;
  /** Text content for token events; error message for error; null for done/suggestions/citations */
  content: string | null;
  /** Suggestions array for "suggestions" events; null/undefined for other event types */
  suggestions?: string[];
  /** Structured payload for rich event types (citations). Maps to ChatSseEvent.Data on the server. */
  data?: IChatSseEventData | null;
}

/**
 * Structured data payload carried by rich SSE event types.
 * For "citations" events, contains the ordered citations array.
 * For "suggestions" events, contains the suggestions string array.
 * For "plan_preview" events, contains the plan preview payload (Phase 2F).
 * For "plan_step_start" events, contains the step ID and index (Phase 2F).
 * For "plan_step_complete" events, contains the step result or error (Phase 2F).
 */
export interface IChatSseEventData {
  /** Citation items from a "citations" event. */
  citations?: ICitationSseItem[];
  /** Follow-up suggestion strings from a "suggestions" event (1-3 items, each max 80 chars). */
  suggestions?: string[];

  // ── plan_preview fields (task 071, Phase 2F) ────────────────────────────────
  /** Unique plan ID echoed back on POST /plan/approve. Only in 'plan_preview' events. */
  planId?: string;
  /** Display title for the PlanPreviewCard header. Only in 'plan_preview' events. */
  planTitle?: string;
  /** Ordered steps for PlanPreviewCard. Only in 'plan_preview' events. */
  steps?: IPlanPreviewStep[];
  /** Optional analysis record ID for write-back plans. Only in 'plan_preview' events. */
  analysisId?: string;
  /** Canonical field path for write-back steps. Only in 'plan_preview' events. */
  writeBackTarget?: string;

  // ── plan_step_start fields (task 072, Phase 2F) ─────────────────────────────
  /** Step identifier. In 'plan_step_start' and 'plan_step_complete' events. */
  stepId?: string;
  /** 0-based step index. Only in 'plan_step_start' events. */
  stepIndex?: number;

  // ── plan_step_complete fields (task 072, Phase 2F) ──────────────────────────
  /** Execution status: "completed" or "failed". Only in 'plan_step_complete' events. */
  status?: 'completed' | 'failed';
  /** Brief result snippet shown on success. Only in 'plan_step_complete' events. */
  result?: string | null;
  /** Machine-readable error code on failure. Only in 'plan_step_complete' events. */
  errorCode?: string | null;
  /** Human-readable error message on failure. Only in 'plan_step_complete' events. */
  errorMessage?: string | null;

  // ── action_confirmation fields (task R2-039) ─────────────────────────────────
  /** Action identifier. In 'action_confirmation', 'action_success', 'action_error' events. */
  actionId?: string;
  /** Human-readable action name. In 'action_confirmation' events. */
  actionName?: string;
  /** Action summary. In 'action_confirmation' events. */
  summary?: string;
  /** Extracted parameters. In 'action_confirmation' events. */
  parameters?: Record<string, string>;
  /** Human-readable message. In 'action_success' and 'action_error' events. */
  message?: string;

  // ── dialog_open fields (task R2-039) ─────────────────────────────────────────
  /** Code Page web resource name. In 'dialog_open' events. */
  targetPage?: string;
  /** Pre-populated field values for the dialog. In 'dialog_open' events. */
  prePopulateFields?: Record<string, string>;
  /** Optional dialog width percentage. In 'dialog_open' events. */
  width?: number;
  /** Optional dialog height percentage. In 'dialog_open' events. */
  height?: number;

  // ── navigate fields (task R2-052) ───────────────────────────────────────────
  /** Fully constructed navigation URL. In 'navigate' events. */
  url?: string;
  // targetPage is reused from dialog_open fields above for navigate events.
  // parameters is reused from action_confirmation fields above for navigate events.
  /** Playbook ID that triggered the navigation. In 'navigate' events. */
  playbookId?: string;
  /** Playbook display name. In 'dialog_open' and 'navigate' events. */
  playbookName?: string;
}

/**
 * A single step as received in the 'plan_preview' SSE event data.
 * Maps to ChatSsePlanStep on the backend.
 */
export interface IPlanPreviewStep {
  /** Step identifier (e.g., "step-1"). */
  id: string;
  /** Human-readable description shown in PlanPreviewCard. */
  description: string;
  /** Initial status: always "pending" at plan_preview time. */
  status: 'pending' | 'running' | 'completed' | 'failed';
}

/**
 * A single citation item as received in a "citations" SSE event.
 * Maps to ChatSseCitationItem on the backend.
 */
export interface ICitationSseItem {
  /** 1-based citation number matching [N] markers in the response text. */
  id: number;
  /** Display name of the source document or knowledge article. */
  sourceName: string;
  /** Page number in the source document (null when not available). */
  page?: number | null;
  /** Short excerpt from the matched content. */
  excerpt: string;
  /** Chunk ID from the search index for traceability. Optional for web citations. */
  chunkId?: string;
  /**
   * Citation source type.
   * - 'document' (default) — internal SPE document/knowledge article
   * - 'web' — external web search result
   * When absent, defaults to 'document' for backward compatibility.
   */
  sourceType?: CitationSourceType;
  /** Full URL of the web search result. Present when sourceType is 'web'. */
  url?: string;
  /** Short text snippet from the web search result. Present when sourceType is 'web'. */
  snippet?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Context Selector Types
// ─────────────────────────────────────────────────────────────────────────────

/** A selectable document option for the context selector. */
export interface IDocumentOption {
  /** Document ID */
  id: string;
  /** Display name */
  name: string;
}

/** A selectable playbook option for the context selector. */
export interface IPlaybookOption {
  /** Playbook ID (GUID) */
  id: string;
  /** Display name */
  name: string;
  /** Optional description */
  description?: string;
  /** Whether this playbook is public/shared */
  isPublic?: boolean;
}

/**
 * Host context describing WHERE SprkChat is embedded.
 * Enables entity-scoped search and playbook discovery without
 * coupling SprkChat to any specific host workspace.
 */
export interface IHostContext {
  /** Business entity type (e.g., "matter", "project", "invoice", "account", "contact") */
  entityType: string;
  /** GUID of the parent entity record */
  entityId: string;
  /** Display name of the parent entity (for logging/UI) */
  entityName?: string;
  /** Workspace hosting SprkChat (e.g., "LegalWorkspace", "AnalysisWorkspace") */
  workspaceType?: string;
  /** Page type where SprkChat is embedded (e.g., "form", "list", "dashboard", "workspace", "unknown") */
  pageType?: string;
}

/** A predefined prompt suggestion shown before the first message. */
export interface IPredefinedPrompt {
  /** Unique key */
  key: string;
  /** Display label */
  label: string;
  /** Full prompt text sent when clicked */
  prompt: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component Props
// ─────────────────────────────────────────────────────────────────────────────

/** Props for the main SprkChat component. */
export interface ISprkChatProps {
  /** Existing session ID to resume (omit to create new session) */
  sessionId?: string;
  /** Document ID for the chat context */
  documentId?: string;
  /**
   * Analysis record ID (sprk_analysisoutput GUID without braces).
   * When provided, SprkChat fetches analysis-scoped context mapping from
   * GET /api/ai/chat/context-mappings/analysis/{analysisId} and populates
   * QuickActionChips with the returned inline actions.
   * Omit for non-analysis contexts (generic chat mode).
   */
  analysisId?: string;
  /**
   * Playbook ID (GUID string) governing the agent's behavior.
   * When undefined/omitted, the session is created without a playbook
   * (generic conversational mode) and the playbook selector is hidden.
   * This is a valid configuration — not an error state.
   */
  playbookId?: string;
  /** Base URL for the BFF API (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net") */
  apiBaseUrl: string;
  /** Bearer token for API authentication */
  accessToken: string;
  /** Callback fired when a new session is created */
  onSessionCreated?: (session: IChatSession) => void;
  /**
   * Callback fired when the user switches playbooks via the context selector
   * or playbook chips. Allows the host to update its own state and persist
   * the choice (e.g., to sessionStorage).
   */
  onPlaybookChange?: (playbookId: string) => void;
  /** Optional CSS class name applied to the root element */
  className?: string;
  /** Available documents for context switching */
  documents?: IDocumentOption[];
  /** Available playbooks for context switching */
  playbooks?: IPlaybookOption[];
  /** Predefined prompt suggestions shown before conversation starts */
  predefinedPrompts?: IPredefinedPrompt[];
  /** Content element ref for highlight-refine feature (detects text selection) */
  contentRef?: React.RefObject<HTMLElement>;
  /** Maximum character count for input (default 2000) */
  maxCharCount?: number;
  /** Host context describing where SprkChat is embedded (entity type, entity ID, workspace) */
  hostContext?: IHostContext;
  /**
   * SprkChatBridge instance for cross-pane communication.
   * When provided, SprkChat subscribes to `selection_changed` events
   * from the Analysis Workspace editor and displays selection context
   * in the highlight-refine toolbar.
   *
   * Pass null or omit when no bridge is available (standalone mode).
   */
  bridge?: import('../../services/SprkChatBridge').SprkChatBridge | null;

  /**
   * Direct callback for document stream SSE events (Task 007).
   *
   * When provided, SprkChat forwards document_stream_start/token/end events
   * through this callback instead of (or in addition to) the BroadcastChannel
   * bridge. This enables zero-serialization streaming in the unified
   * AnalysisWorkspace where SprkChat and the editor share the same React tree.
   *
   * When both `bridge` and `onDocumentStreamEvent` are provided, events are
   * forwarded to BOTH (bridge for legacy compatibility, callback for direct path).
   * When only `onDocumentStreamEvent` is provided (bridge is null), events go
   * exclusively through the callback.
   *
   * SECURITY (ADR-015): Only content tokens and structural metadata are included.
   */
  onDocumentStreamEvent?: ((event: IDocumentStreamSseEvent) => void) | null;

  /**
   * Pre-loaded chat messages to display before the session starts.
   * Typically sourced from the sprk_chathistory field on the analysis record
   * so that prior conversation context is visible when the workspace reopens.
   */
  initialMessages?: IChatMessage[];
}

/** Props for SprkChatMessage sub-component. */
export interface ISprkChatMessageProps {
  /** The message to display */
  message: IChatMessage;
  /** Whether this message is currently being streamed */
  isStreaming?: boolean;
  /**
   * Citation metadata for this message (assistant messages only).
   * When provided, [N] markers in the message text are rendered as
   * interactive CitationMarker components with popover details.
   */
  citations?: ICitation[];
}

/** Props for SprkChatInput sub-component. */
export interface ISprkChatInputProps {
  /** Callback fired when user sends a message */
  onSend: (message: string) => void;
  /** Whether the input is disabled (e.g., during streaming) */
  disabled?: boolean;
  /** Maximum character count (default 2000) */
  maxCharCount?: number;
  /** Placeholder text */
  placeholder?: string;
  /**
   * Optional dynamic slash commands appended to the static DEFAULT_SLASH_COMMANDS.
   * Use to inject playbook-capability commands resolved from the context mapping
   * endpoint at runtime.
   *
   * When omitted, only DEFAULT_SLASH_COMMANDS are shown in the menu.
   */
  dynamicSlashCommands?: import('../SlashCommandMenu/slashCommandMenu.types').SlashCommand[];
}

/** Props for SprkChatContextSelector sub-component. */
export interface ISprkChatContextSelectorProps {
  /** Currently selected document ID */
  selectedDocumentId?: string;
  /** Currently selected playbook ID */
  selectedPlaybookId?: string;
  /** Available documents */
  documents: IDocumentOption[];
  /** Available playbooks */
  playbooks: IPlaybookOption[];
  /** Callback when document selection changes */
  onDocumentChange: (documentId: string) => void;
  /** Callback when playbook selection changes */
  onPlaybookChange: (playbookId: string) => void;
  /** Whether selection is disabled */
  disabled?: boolean;
  /** Currently selected additional document IDs for multi-document context */
  additionalDocumentIds?: string[];
  /** Callback when additional document selection changes */
  onAdditionalDocumentsChange?: (documentIds: string[]) => void;
  /** Maximum number of additional documents allowed (default 5) */
  maxAdditionalDocuments?: number;
}

/** Props for SprkChatPredefinedPrompts sub-component. */
export interface ISprkChatPredefinedPromptsProps {
  /** Available prompt suggestions */
  prompts: IPredefinedPrompt[];
  /** Callback fired when a prompt is selected */
  onSelect: (prompt: string) => void;
  /** Whether prompts are disabled */
  disabled?: boolean;
}

/**
 * A quick action preset for the highlight-refine toolbar.
 * Clicking a quick action chip fills the instruction and auto-submits.
 */
export interface IQuickAction {
  /** Unique key for the action */
  key: string;
  /** Display label shown on the chip */
  label: string;
  /** Instruction text sent as the refinement instruction */
  instruction: string;
}

/** Default quick action presets for highlight-refine. */
export const DEFAULT_QUICK_ACTIONS: IQuickAction[] = [
  { key: 'simplify', label: 'Simplify', instruction: 'Simplify this text' },
  {
    key: 'expand',
    label: 'Expand',
    instruction: 'Expand this text with more detail',
  },
  {
    key: 'concise',
    label: 'Make Concise',
    instruction: 'Make this text more concise',
  },
  {
    key: 'formal',
    label: 'Make Formal',
    instruction: 'Rewrite this text in a more formal tone',
  },
];

/**
 * Structured refinement request emitted by SprkChatHighlightRefine.
 * Contains selected text, instruction, source identification, and optional quick action key.
 */
export interface IRefineRequest {
  /** The full selected text to refine */
  selectedText: string;
  /** The refinement instruction (free-text or from quick action) */
  instruction: string;
  /** Source of the selection: "editor" for cross-pane, "chat" for local DOM */
  source: 'editor' | 'chat';
  /** Key of the quick action used (undefined if free-text instruction) */
  quickAction?: string;
}

/** Props for SprkChatHighlightRefine sub-component. */
export interface ISprkChatHighlightRefineProps {
  /** Ref to the content area where text selection is detected */
  contentRef: React.RefObject<HTMLElement>;
  /** Callback to initiate refinement (legacy: selectedText + instruction) */
  onRefine: (selectedText: string, instruction: string) => void;
  /** Callback emitting a structured RefineRequest (preferred over onRefine) */
  onRefineRequest?: (request: IRefineRequest) => void;
  /** Whether refinement is currently in progress */
  isRefining?: boolean;
  /** Cross-pane selection received from SprkChatBridge (overrides local DOM selection when present) */
  crossPaneSelection?: ICrossPaneSelection | null;
  /**
   * Configurable quick action presets shown as chips in the toolbar.
   * Defaults to DEFAULT_QUICK_ACTIONS (Simplify, Expand, Make Concise, Make Formal).
   * Pass an empty array to hide quick actions.
   */
  quickActions?: IQuickAction[];
  /** Callback fired when the toolbar is dismissed (close button, Escape, click-outside) */
  onDismiss?: () => void;
}

/** Props for SprkChatSuggestions sub-component. */
export interface ISprkChatSuggestionsProps {
  /** Array of suggestion strings to display as clickable chips (max 3 shown). */
  suggestions: string[];
  /** Callback fired when a suggestion chip is clicked; receives the full suggestion text. */
  onSelect: (suggestion: string) => void;
  /** Controls visibility with fade-in / slide-up animation. */
  visible: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Citation Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Discriminates between internal document citations and external web search citations.
 * - 'document' — internal SPE file reference (default; omit for backward compatibility)
 * - 'web'      — external web search result with URL
 */
export type CitationSourceType = 'document' | 'web';

/**
 * A citation reference from the AI response, linking back to a
 * source document, page, and text excerpt.
 *
 * When `sourceType` is 'web', the citation represents an external web search
 * result and MUST include `url` and `snippet`. The `chunkId` field is optional
 * for web citations. Web citations display an "[External Source]" badge per ADR-015.
 */
export interface ICitation {
  /** Numeric citation ID displayed as superscript [N]. */
  id: number;
  /** Display name of the source document or resource. */
  source: string;
  /** Page number within the source (optional; typically absent for web citations). */
  page?: number;
  /** Text excerpt from the source that supports the citation. */
  excerpt: string;
  /** Chunk identifier from the RAG pipeline (for traceability). Optional for web citations. */
  chunkId?: string;
  /** URL to open the source document directly (optional for document citations). */
  sourceUrl?: string;
  /**
   * Citation source type discriminator.
   * - 'document' (default) — internal SPE document reference
   * - 'web' — external web search result
   * When absent, defaults to 'document' for backward compatibility.
   */
  sourceType?: CitationSourceType;
  /** Full URL of the web search result. Required when sourceType is 'web'. */
  url?: string;
  /** Short text snippet from the web search result. Required when sourceType is 'web'. */
  snippet?: string;
}

/** Props for the CitationMarker inline component. */
export interface ICitationMarkerProps {
  /** Citation data to display. */
  citation: ICitation;
}

/**
 * Props for the SprkChatCitationPopover controlled component.
 * Use when the parent manages open/close state explicitly.
 */
export interface ISprkChatCitationPopoverProps {
  /** Citation data to display in the popover. */
  citation: ICitation;
  /** Whether the popover is currently open (controlled mode). */
  open?: boolean;
  /** Callback fired when open state changes (click-outside, Escape, etc.). */
  onOpenChange?: (open: boolean) => void;
  /** Trigger element (typically a superscript marker). */
  children: React.ReactElement;
}

// ─────────────────────────────────────────────────────────────────────────────
// Document Insert Event Types (Phase 2D: Insert-to-Editor)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BroadcastChannel event dispatched when the user clicks the "Insert" button
 * on an AI response message in SprkChat.
 *
 * The AnalysisWorkspace Lexical editor subscribes to the `sprk-document-insert`
 * channel and handles this event in task 051 (Insert-at-Cursor handler).
 *
 * @see ADR-012 - Shared Component Library; no Xrm/PCF imports
 * @see ADR-015 - Auth tokens MUST NOT be transmitted via BroadcastChannel
 * @see spec-2D - Insert-to-Editor phase requirements
 */
export interface IDocumentInsertEvent {
  /** Discriminator — always 'document_insert'. */
  type: 'document_insert';
  /** The content to insert into the editor. */
  content: string;
  /**
   * Content format.
   * - 'text' — plain text; editor inserts as-is at cursor/selection
   * - 'html' — HTML string; editor parses and inserts as rich content
   */
  contentType: 'text' | 'html';
  /**
   * Where to insert the content.
   * - 'cursor' — insert at current cursor position
   * - 'selection' — replace current selection with content
   */
  insertAt: 'cursor' | 'selection';
  /** Unix timestamp (Date.now()) when the event was dispatched. */
  timestamp: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Cross-Pane Selection Types (SprkChatBridge → SprkChat)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parsed selection context from a cross-pane selection_changed event.
 * The raw bridge event carries text + offsets + a JSON-encoded `context` string.
 * This interface represents the fully-parsed result stored in SprkChat state.
 *
 * @see SelectionChangedPayload in SprkChatBridge.ts
 * @see useSelectionBroadcast in AnalysisWorkspace (emitter side)
 */
export interface ICrossPaneSelection {
  /** Plain text of the selection (may be truncated for display; full text kept in fullText) */
  text: string;
  /** Full untruncated selection text */
  fullText: string;
  /** HTML content of the selected range (from the editor) */
  selectedHtml: string;
  /** Start offset within the editor content */
  startOffset: number;
  /** End offset within the editor content */
  endOffset: number;
  /** Source pane that emitted the selection (e.g., "analysis-editor") */
  source: string;
}

/** Maximum character length for the truncated `text` preview. Selections longer than this are clipped. */
export const CROSS_PANE_SELECTION_MAX_PREVIEW = 5000;

// ─────────────────────────────────────────────────────────────────────────────
// Document Stream Event Types (Phase 2D: Streaming Insert bridge events)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Discriminated union for document stream SSE events used by useStreamingInsert.
 * These mirror the bridge event payload shapes from SprkChatBridge.
 */
export type IDocumentStreamEvent =
  | {
      type: 'document_stream_start';
      operationId: string;
      targetPosition?: string;
    }
  | {
      type: 'document_stream_token';
      operationId: string;
      token: string;
    }
  | {
      type: 'document_stream_end';
      operationId: string;
      cancelled?: boolean;
    }
  | {
      type: 'document_replace';
      operationId: string;
      content: string;
    }
  | {
      type: 'progress';
      operationId: string;
      message?: string;
    };

// ─────────────────────────────────────────────────────────────────────────────
// Document Stream SSE Event (Task R2-051: BFF SSE → SprkChatBridge forwarding)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * A document stream SSE event received from the BFF on the /messages endpoint.
 * useSseStream stores these in pendingDocumentStreamEvent state; SprkChat.tsx
 * watches via useEffect and forwards each to SprkChatBridge.emit().
 *
 * Matches the DocumentStreamEvent hierarchy in DocumentStreamEvent.cs:
 * - document_stream_start: { operationId, targetPosition, operationType }
 * - document_stream_token: { operationId, token, index }
 * - document_stream_end:   { operationId, cancelled, totalTokens }
 *
 * SECURITY (ADR-015): Only content tokens and structural metadata.
 * Auth tokens and user PII are NEVER included.
 */
export type IDocumentStreamSseEvent =
  | {
      type: 'document_stream_start';
      operationId: string;
      targetPosition: string;
      operationType: 'insert' | 'replace' | 'diff';
    }
  | {
      type: 'document_stream_token';
      operationId: string;
      token: string;
      index: number;
    }
  | {
      type: 'document_stream_end';
      operationId: string;
      cancelled: boolean;
      totalTokens: number;
    };

// ─────────────────────────────────────────────────────────────────────────────
// Action Confirmation Types (Task R2-039: HITL vs Autonomous Execution)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Pending action awaiting user confirmation (HITL — requiresConfirmation=true).
 * Shown in the ActionConfirmationDialog. On Confirm, the action is dispatched
 * to the BFF; on Cancel, the pending action is cleared without side effects.
 *
 * @see spec-FR-07 — HITL confirmation dialog
 * @see ADR-021 — Fluent v9 Dialog component
 */
export interface IPendingAction {
  /** Unique action identifier from the playbook dispatcher. */
  actionId: string;
  /** Human-readable action name (e.g., "Send Email", "Create Record"). */
  actionName: string;
  /** Brief description of what the action will do. */
  summary: string;
  /** Extracted parameters shown as key-value pairs in the dialog (e.g., "Recipient: john@example.com"). */
  parameters: Record<string, string>;
  /** The session ID to dispatch the confirmed action to. */
  sessionId: string;
}

/**
 * Payload for the `dialog_open` SSE event.
 * Instructs the frontend to open a Code Page dialog via Xrm.Navigation.navigateTo.
 *
 * @see spec-FR-08 — Dialog open event
 * @see ADR-006 — Code Page dialogs via Xrm.Navigation.navigateTo
 */
export interface IDialogOpenPayload {
  /** Web resource name of the Code Page to open (e.g., "sprk_emailcomposer"). */
  targetPage: string;
  /** Pre-populated field values to pass as URL query params in the navigateTo data attribute. */
  prePopulateFields: Record<string, string>;
  /** Optional dialog width percentage (default 85). */
  width?: number;
  /** Optional dialog height percentage (default 85). */
  height?: number;
}

/**
 * Payload for the `action_confirmation` SSE event (requiresConfirmation=true).
 * The BFF sends this when a playbook action requires user confirmation before execution.
 */
export interface IActionConfirmationPayload {
  /** Unique action identifier. */
  actionId: string;
  /** Human-readable action name. */
  actionName: string;
  /** Brief summary of the proposed action. */
  summary: string;
  /** Extracted parameters as key-value pairs. */
  parameters: Record<string, string>;
}

/**
 * Payload for `action_success` SSE event (requiresConfirmation=false — autonomous execution).
 */
export interface IActionSuccessPayload {
  /** Action identifier that completed. */
  actionId: string;
  /** Human-readable success message. */
  message: string;
}

/**
 * Payload for `action_error` SSE event (action execution failed).
 */
export interface IActionErrorPayload {
  /** Action identifier that failed. */
  actionId: string;
  /** Human-readable error message. */
  message: string;
}

/**
 * Payload for the `navigate` SSE event (Task R2-052).
 * Instructs the frontend to navigate to a Dataverse record, external URL,
 * or Code Page via Xrm.Navigation.navigateTo or Xrm.Navigation.openUrl.
 *
 * @see ADR-006 — Code Page dialogs via Xrm.Navigation.navigateTo
 */
export interface INavigatePayload {
  /** Fully constructed navigation URL (e.g., Dataverse record URL). Null when targetPage is provided. */
  url?: string;
  /** Code Page web resource name for internal navigation. Null when url is provided. */
  targetPage?: string;
  /** Extracted parameters for the navigation target (e.g., matterId, entityId). */
  parameters: Record<string, string>;
  /** Playbook ID that triggered the navigation. */
  playbookId?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Return Types
// ─────────────────────────────────────────────────────────────────────────────

/** Return type for the useSseStream hook. */
export interface IUseSseStreamResult {
  /** Accumulated token content as a single string */
  content: string;
  /** Whether the stream has completed */
  isDone: boolean;
  /** Error if the stream failed */
  error: Error | null;
  /** Whether a stream is currently active */
  isStreaming: boolean;
  /**
   * Whether the AI is processing the request (typing indicator should be shown).
   * True between `typing_start` and either the first `token` event or `typing_end`.
   * Used to display the animated typing indicator before content arrives.
   */
  isTyping: boolean;
  /** Follow-up suggestions received from the suggestions SSE event (1-3 strings) */
  suggestions: string[];
  /** Citation metadata received from the citations SSE event, keyed by citation ID for fast lookup */
  citations: ICitation[];
  /**
   * Plan ID received from a 'plan_preview' SSE event (Phase 2F).
   * Set when the last stream response included a plan_preview event.
   * Used by SprkChat.tsx to call POST /plan/approve with the correct planId.
   * Reset to null at the start of each new stream.
   */
  pendingPlanId: string | null;
  /**
   * Full plan_preview event data (Phase 2F).
   * Contains planTitle, steps, analysisId, writeBackTarget alongside planId.
   * Used by SprkChat.tsx to populate message metadata for PlanPreviewCard rendering.
   * Reset to null at the start of each new stream.
   */
  pendingPlanData: IChatSseEventData | null;
  /**
   * Pending action/dialog event from the SSE stream (Task R2-039).
   * Set when action_confirmation, action_success, action_error, dialog_open, or navigate events arrive.
   * SprkChat watches this via useEffect and dispatches to the appropriate handler.
   * Reset to null at the start of each new stream.
   */
  pendingActionEvent: {
    type: 'action_confirmation' | 'action_success' | 'action_error' | 'dialog_open' | 'navigate';
    data: IChatSseEventData;
  } | null;
  /** Start a new SSE stream */
  startStream: (url: string, body: Record<string, unknown>, token: string) => void;
  /** Cancel the active stream */
  cancelStream: () => void;
  /** Clear stored suggestions (called when user sends a new message) */
  clearSuggestions: () => void;
  /** Clear the pending action event after it has been handled (Task R2-039). */
  clearPendingActionEvent: () => void;

  /**
   * Pending document stream event from the BFF SSE stream (Task R2-051).
   * Set when document_stream_start, document_stream_token, or document_stream_end
   * events arrive on the main /messages SSE stream. SprkChat.tsx watches this via
   * useEffect and forwards each event to SprkChatBridge for cross-pane delivery
   * to the AnalysisWorkspace Lexical editor.
   *
   * SECURITY (ADR-015): Only content tokens and structural metadata are included.
   * Auth tokens, credentials, and user PII are NEVER present in these events.
   *
   * Reset to null after SprkChat.tsx processes the event.
   */
  pendingDocumentStreamEvent: IDocumentStreamSseEvent | null;

  /** Clear the pending document stream event after it has been forwarded (Task R2-051). */
  clearPendingDocumentStreamEvent: () => void;

  /**
   * Register a callback for document stream events (Task R2-051).
   *
   * Unlike pendingDocumentStreamEvent (state-based), this callback is invoked
   * synchronously from the fetch loop for every document_stream_start/token/end
   * event. This prevents React state batching from coalescing rapid token events.
   *
   * SprkChat.tsx registers a callback that calls bridge.emit() for each event.
   * Pass null to unregister.
   *
   * SECURITY (ADR-015): The callback receives only content tokens and structural
   * metadata. Auth tokens are NEVER included.
   */
  setOnDocumentStreamEvent: (handler: ((event: IDocumentStreamSseEvent) => void) | null) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Document Upload Status Types (Phase 3E: Upload Processing Feedback)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Processing status of an uploaded document.
 * - 'processing' — Document Intelligence is extracting text
 * - 'complete'   — Extraction finished, document added to context
 * - 'error'      — Extraction failed
 */
export type DocumentProcessingStatus = 'processing' | 'complete' | 'error';

/**
 * A system message representing document upload processing status.
 * Inserted into the chat message stream when a document is uploaded.
 * Updated in-place as SSE events arrive (processing_start → complete | error).
 *
 * @see spec-FR-13 — Document upload via drag-and-drop
 * @see ADR-015 — MUST NOT display extracted document text
 */
export interface IDocumentStatusMessage {
  /** Unique document identifier from the BFF upload response. */
  documentId: string;
  /** Original file name displayed in the status message. */
  fileName: string;
  /** Current processing status. */
  status: DocumentProcessingStatus;
  /** Number of pages extracted (available after 'complete' status). */
  pageCount?: number;
  /** Error description (available after 'error' status). */
  error?: string;
  /** UTC timestamp when the processing started. */
  startedAt: number;
  /**
   * SPE persistence state for the "Save to matter files" action (FR-14).
   * - 'idle'   — save button shown (default when containerId is available)
   * - 'saving' — save in progress (spinner on button)
   * - 'saved'  — copy persisted to SPE (shows "Saved — View in Files" link)
   * - 'error'  — persistence failed (button restored, toast shown by parent)
   *
   * @see spec-FR-14 — Optional SPE persistence for uploaded documents
   * @see spec-NFR-06 — Save creates a COPY in SPE; session-scoped temp document remains
   */
  persistenceState?: 'idle' | 'saving' | 'saved' | 'error';
  /** SharePoint file URL returned by BFF after successful SPE persistence. */
  savedFileUrl?: string;
}

/**
 * Extended chat message that can optionally carry document status data.
 * When `metadata.responseType === 'document_status'` and `documentStatus` is
 * present, SprkChatMessage delegates rendering to SprkChatDocumentStatus.
 *
 * This type extends IChatMessage to keep backward compatibility — existing
 * messages without documentStatus continue to render normally.
 */
export interface IDocumentStatusChatMessage extends IChatMessage {
  /** Document processing status metadata. Present only for document_status messages. */
  documentStatus?: IDocumentStatusMessage;
}

/** Props for the SprkChatDocumentStatus component. */
export interface ISprkChatDocumentStatusProps {
  /** Document processing status data. */
  status: IDocumentStatusMessage;
  /**
   * Callback to persist the uploaded document to SPE (matter files).
   * Called when the user clicks "Save to matter files".
   * Only invoked when containerId is present (FR-14).
   */
  onSaveToMatterFiles?: (documentId: string) => void;
  /**
   * Whether the host context has a containerId (SPE container available).
   * When false/undefined, the "Save to matter files" button is hidden.
   */
  hasContainerId?: boolean;
}

/**
 * Timeout threshold in milliseconds for document processing (NFR-02: 15 seconds).
 * When processing exceeds this threshold, the UI shows an extended wait message.
 */
export const DOCUMENT_PROCESSING_TIMEOUT_MS = 15_000;

// ─────────────────────────────────────────────────────────────────────────────
// Action Menu Types (SprkChatActionMenu — Phase 2E)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Category that groups actions in the SprkChatActionMenu.
 * Maps to CATEGORY_CONFIG keys in SprkChatActionMenu.tsx.
 */
export type ChatActionCategory = 'playbooks' | 'actions' | 'search' | 'settings';

/**
 * A single action item rendered in the SprkChatActionMenu.
 * Returned by GET /api/ai/chat/sessions/{id}/actions.
 */
export interface IChatAction {
  /** Unique action identifier (e.g., "reanalyze", "summarize", a playbookId GUID). */
  id: string;
  /** Display label shown in the menu. */
  label: string;
  /** Category grouping for the action. */
  category: ChatActionCategory;
  /** Optional description shown as secondary text. */
  description?: string;
  /** Optional keyboard shortcut hint (e.g., "⌘K"). */
  shortcut?: string;
  /** Whether this action is currently disabled. */
  disabled?: boolean;
}

/** Imperative handle exposed by SprkChatActionMenu via forwardRef. */
export interface ISprkChatActionMenuHandle {
  /** Navigate to the previous action in the menu. */
  navigateUp(): void;
  /** Navigate to the next action in the menu. */
  navigateDown(): void;
  /** Select the currently active action. */
  selectActive(): void;
  /** Focus the menu element. */
  focus(): void;
}

/** Props for the SprkChatActionMenu component. */
export interface ISprkChatActionMenuProps {
  /** Available actions to display. */
  actions: IChatAction[];
  /** Whether the menu is open/visible. */
  isOpen: boolean;
  /** Callback fired when an action is selected. */
  onSelect: (action: IChatAction) => void;
  /** Callback fired when the menu should close (Escape, click-outside). */
  onDismiss: () => void;
  /** Current filter text (from the input). */
  filterText?: string;
  /** Ref to the anchor element (e.g., the input) for click-outside detection. */
  anchorRef?: React.RefObject<HTMLElement | null>;
  /** Whether actions are being loaded. */
  isLoading?: boolean;
  /** Error message to display instead of actions. */
  errorMessage?: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────

/** Return type for the useChatSession hook. */
export interface IUseChatSessionResult {
  /** The current session */
  session: IChatSession | null;
  /** Message history */
  messages: IChatMessage[];
  /** Whether a session operation is in progress */
  isLoading: boolean;
  /** Error from the last session operation */
  error: Error | null;
  /** Create a new session */
  createSession: (documentId?: string, playbookId?: string, hostContext?: IHostContext) => Promise<IChatSession | null>;
  /** Load message history for the current session */
  loadHistory: () => Promise<void>;
  /** Switch document/playbook context (optionally with additional document IDs) */
  switchContext: (
    documentId?: string,
    playbookId?: string,
    hostContext?: IHostContext,
    additionalDocumentIds?: string[]
  ) => Promise<void>;
  /** Delete the current session */
  deleteSession: () => Promise<void>;
  /** Add a message to the local history (used by streaming) */
  addMessage: (message: IChatMessage) => void;
  /** Update the last message content (used during streaming) */
  updateLastMessage: (content: string) => void;
  /**
   * Update the metadata of the last message in history.
   * Used by Phase 2F (task 072) to set plan_preview metadata after a plan_preview SSE event
   * and to update plan step statuses during plan execution streaming.
   */
  updateLastMessageMetadata: (metadata: IChatMessageMetadata) => void;
  /**
   * Update a specific message's metadata by index (Phase 2F — task 072).
   * Used to update plan step statuses during plan execution streaming when the plan
   * preview card is not the last message.
   *
   * Accepts either a plain metadata object (merged with the existing metadata) or a
   * function updater `(current) => newMetadata` for safe updates that read current state.
   */
  updateMessageMetadataAt: (
    index: number,
    metadataOrUpdater: IChatMessageMetadata | ((current: IChatMessageMetadata | undefined) => IChatMessageMetadata)
  ) => void;
}
