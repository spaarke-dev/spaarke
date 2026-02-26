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
export type ChatSseEventType = "token" | "done" | "error";

/** A parsed SSE event from the stream, matching ChatSseEvent from the server. */
export interface IChatSseEvent {
    /** Event type */
    type: ChatSseEventType;
    /** Text content for token events; error message for error; null for done */
    content: string | null;
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
}

/** Props for SprkChatMessage sub-component. */
export interface ISprkChatMessageProps {
    /** The message to display */
    message: IChatMessage;
    /** Whether this message is currently being streamed */
    isStreaming?: boolean;
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

/** Props for SprkChatHighlightRefine sub-component. */
export interface ISprkChatHighlightRefineProps {
    /** Ref to the content area where text selection is detected */
    contentRef: React.RefObject<HTMLElement>;
    /** Callback to initiate refinement */
    onRefine: (selectedText: string, instruction: string) => void;
    /** Whether refinement is currently in progress */
    isRefining?: boolean;
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
    /** Start a new SSE stream */
    startStream: (url: string, body: Record<string, unknown>, token: string) => void;
    /** Cancel the active stream */
    cancelStream: () => void;
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
    /** Switch document/playbook context */
    switchContext: (documentId?: string, playbookId?: string, hostContext?: IHostContext) => Promise<void>;
    /** Delete the current session */
    deleteSession: () => Promise<void>;
    /** Add a message to the local history (used by streaming) */
    addMessage: (message: IChatMessage) => void;
    /** Update the last message content (used during streaming) */
    updateLastMessage: (content: string) => void;
}
