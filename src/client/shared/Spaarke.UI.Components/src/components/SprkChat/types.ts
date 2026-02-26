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
export type ChatMessageRole = "User" | "Assistant" | "System";

/** A single chat message, matching ChatSessionMessageInfo from the history endpoint. */
export interface IChatMessage {
    /** Message role */
    role: ChatMessageRole;
    /** Message text content */
    content: string;
    /** UTC timestamp when the message was created */
    timestamp: string;
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

/** SSE event types emitted by the streaming endpoints. */
export type ChatSseEventType = "token" | "done" | "error" | "suggestions" | "citations";

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
 */
export interface IChatSseEventData {
    /** Citation items from a "citations" event. */
    citations?: ICitationSseItem[];
    /** Follow-up suggestion strings from a "suggestions" event (1-3 items, each max 80 chars). */
    suggestions?: string[];
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
    /** Chunk ID from the search index for traceability. */
    chunkId: string;
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
    /** Playbook ID (GUID string) governing the agent's behavior */
    playbookId: string;
    /** Base URL for the BFF API (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net") */
    apiBaseUrl: string;
    /** Bearer token for API authentication */
    accessToken: string;
    /** Callback fired when a new session is created */
    onSessionCreated?: (session: IChatSession) => void;
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
    bridge?: import("../../services/SprkChatBridge").SprkChatBridge | null;
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
    { key: "simplify", label: "Simplify", instruction: "Simplify this text" },
    { key: "expand", label: "Expand", instruction: "Expand this text with more detail" },
    { key: "concise", label: "Make Concise", instruction: "Make this text more concise" },
    { key: "formal", label: "Make Formal", instruction: "Rewrite this text in a more formal tone" },
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
    source: "editor" | "chat";
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
 * A citation reference from the AI response, linking back to a
 * source document, page, and text excerpt.
 */
export interface ICitation {
    /** Numeric citation ID displayed as superscript [N]. */
    id: number;
    /** Display name of the source document or resource. */
    source: string;
    /** Page number within the source (optional). */
    page?: number;
    /** Text excerpt from the source that supports the citation. */
    excerpt: string;
    /** Chunk identifier from the RAG pipeline (for traceability). */
    chunkId: string;
    /** URL to open the source document directly (optional). */
    sourceUrl?: string;
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
    /** Follow-up suggestions received from the suggestions SSE event (1-3 strings) */
    suggestions: string[];
    /** Citation metadata received from the citations SSE event, keyed by citation ID for fast lookup */
    citations: ICitation[];
    /** Start a new SSE stream */
    startStream: (url: string, body: Record<string, unknown>, token: string) => void;
    /** Cancel the active stream */
    cancelStream: () => void;
    /** Clear stored suggestions (called when user sends a new message) */
    clearSuggestions: () => void;
}

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
    switchContext: (documentId?: string, playbookId?: string, hostContext?: IHostContext, additionalDocumentIds?: string[]) => Promise<void>;
    /** Delete the current session */
    deleteSession: () => Promise<void>;
    /** Add a message to the local history (used by streaming) */
    addMessage: (message: IChatMessage) => void;
    /** Update the last message content (used during streaming) */
    updateLastMessage: (content: string) => void;
}
