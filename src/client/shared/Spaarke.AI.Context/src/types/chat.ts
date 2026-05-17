/**
 * @spaarke/ai-context — Chat type definitions
 *
 * Re-exports and standalone chat types for use by the hooks and service client.
 * These mirror the SprkChat types in @spaarke/ui-components but are defined
 * here to keep @spaarke/ai-context self-contained (no dependency on ui-components).
 *
 * @see SprkChat/types.ts in @spaarke/ui-components (authoritative source)
 * @see ADR-012 — Shared Component Library (no cross-library type coupling)
 */

// ─────────────────────────────────────────────────────────────────────────────
// Message Types
// ─────────────────────────────────────────────────────────────────────────────

/** Role of a chat message, matching the server-side ChatMessageRole enum. */
export type ChatMessageRole = 'User' | 'Assistant' | 'System';

/**
 * A plan step as received in a plan_preview message's metadata.
 */
export interface IChatMessagePlanStep {
  id: string;
  description: string;
  status: 'pending' | 'running' | 'completed' | 'failed';
  result?: string;
}

/**
 * Optional metadata on a chat message carrying structured response data.
 */
export interface IChatMessageMetadata {
  responseType?:
    | 'markdown'
    | 'citations'
    | 'diff'
    | 'entity_card'
    | 'action_confirmation'
    | 'plan_preview'
    | 'document_status'
    | string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  data?: Record<string, any>;
  planTitle?: string;
  plan?: IChatMessagePlanStep[];
}

/** A single chat message, matching ChatSessionMessageInfo from the history endpoint. */
export interface IChatMessage {
  role: ChatMessageRole;
  content: string;
  timestamp: string;
  metadata?: IChatMessageMetadata;
}

// ─────────────────────────────────────────────────────────────────────────────
// Session Types
// ─────────────────────────────────────────────────────────────────────────────

/** A chat session, matching ChatSessionCreatedResponse from the create endpoint. */
export interface IChatSession {
  sessionId: string;
  createdAt: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// SSE Event Types
// ─────────────────────────────────────────────────────────────────────────────

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

/** A citation item as received in a "citations" SSE event. */
export interface ICitationSseItem {
  id: number;
  sourceName: string;
  page?: number | null;
  excerpt: string;
  chunkId?: string;
  sourceType?: 'document' | 'web';
  url?: string;
  snippet?: string;
}

/** Structured data payload carried by rich SSE event types. */
export interface IChatSseEventData {
  citations?: ICitationSseItem[];
  suggestions?: string[];

  // plan_preview fields
  planId?: string;
  planTitle?: string;
  steps?: Array<{ id: string; description: string; status: 'pending' | 'running' | 'completed' | 'failed' }>;
  analysisId?: string;
  writeBackTarget?: string;

  // plan_step_start / plan_step_complete fields
  stepId?: string;
  stepIndex?: number;
  status?: 'completed' | 'failed';
  result?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;

  // action_confirmation / action_success / action_error fields
  actionId?: string;
  actionName?: string;
  summary?: string;
  parameters?: Record<string, string>;
  message?: string;

  // dialog_open fields
  targetPage?: string;
  prePopulateFields?: Record<string, string>;
  width?: number;
  height?: number;

  // navigate fields
  url?: string;
  playbookId?: string;
  playbookName?: string;
}

/** A parsed SSE event from the stream. */
export interface IChatSseEvent {
  type: ChatSseEventType;
  content: string | null;
  suggestions?: string[];
  data?: IChatSseEventData | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Document Stream SSE Event Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Document stream SSE events forwarded from the BFF /messages endpoint.
 * SECURITY (ADR-015): Only content tokens and structural metadata — no auth tokens.
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
// Citation Types
// ─────────────────────────────────────────────────────────────────────────────

export type CitationSourceType = 'document' | 'web';

export interface ICitation {
  id: number;
  source: string;
  page?: number;
  excerpt: string;
  chunkId?: string;
  sourceUrl?: string;
  sourceType?: CitationSourceType;
  url?: string;
  snippet?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Context Selector Types
// ─────────────────────────────────────────────────────────────────────────────

/** A selectable playbook option for the context selector. */
export interface IPlaybookOption {
  id: string;
  name: string;
  description?: string;
  isPublic?: boolean;
}

/**
 * Host context describing WHERE the AI surface is embedded.
 * Matches IHostContext in @spaarke/ui-components SprkChat/types.ts.
 */
export interface IHostContext {
  entityType: string;
  entityId: string;
  entityName?: string;
  workspaceType?: string;
  pageType?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Return Types
// ─────────────────────────────────────────────────────────────────────────────

/** Return type for the useChatSession hook. */
export interface IUseChatSessionResult {
  session: IChatSession | null;
  messages: IChatMessage[];
  isLoading: boolean;
  error: Error | null;
  createSession: (documentId?: string, playbookId?: string, hostContext?: IHostContext) => Promise<IChatSession | null>;
  loadHistory: () => Promise<void>;
  switchContext: (
    documentId?: string,
    playbookId?: string,
    hostContext?: IHostContext,
    additionalDocumentIds?: string[]
  ) => Promise<void>;
  deleteSession: () => Promise<void>;
  addMessage: (message: IChatMessage) => void;
  updateLastMessage: (content: string) => void;
  updateLastMessageMetadata: (metadata: IChatMessageMetadata) => void;
  updateMessageMetadataAt: (
    index: number,
    metadataOrUpdater: IChatMessageMetadata | ((current: IChatMessageMetadata | undefined) => IChatMessageMetadata)
  ) => void;
}

/** Return type for the useSseStream hook. */
export interface IUseSseStreamResult {
  content: string;
  isDone: boolean;
  error: Error | null;
  isStreaming: boolean;
  isTyping: boolean;
  suggestions: string[];
  citations: ICitation[];
  pendingPlanId: string | null;
  pendingPlanData: IChatSseEventData | null;
  pendingActionEvent: {
    type: 'action_confirmation' | 'action_success' | 'action_error' | 'dialog_open' | 'navigate';
    data: IChatSseEventData;
  } | null;
  pendingDocumentStreamEvent: IDocumentStreamSseEvent | null;
  startStream: (url: string, body: Record<string, unknown>, token: string) => void;
  cancelStream: () => void;
  clearSuggestions: () => void;
  clearPendingActionEvent: () => void;
  clearPendingDocumentStreamEvent: () => void;
  setOnDocumentStreamEvent: (handler: ((event: IDocumentStreamSseEvent) => void) | null) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Context Mapping Response Types
// ─────────────────────────────────────────────────────────────────────────────

/** Inline action descriptor from the context mapping response. */
export interface IInlineActionInfo {
  id: string;
  label: string;
  actionType: string;
  description?: string;
}

/** Lightweight playbook descriptor in the analysis context response. */
export interface IAnalysisPlaybookInfo {
  id: string;
  name: string;
  description?: string;
}

/** Knowledge source scoped to the analysis context. */
export interface IAnalysisKnowledgeSourceInfo {
  type: string;
  id: string;
  label?: string;
}

/** Contextual metadata about the analysis record. */
export interface IAnalysisContextInfo {
  analysisId: string;
  analysisType?: string;
  matterType?: string;
  practiceArea?: string;
  sourceFileId?: string;
  sourceContainerId?: string;
}

/** Command entry from the DynamicCommandResolver catalog. */
export interface ICommandEntry {
  id: string;
  label: string;
  description: string;
  trigger: string;
  category: string;
  source: string | null;
}

/** Metadata about the active analysis scope. */
export interface IAnalysisScopeMetadata {
  scopeId: string;
  scopeName: string;
  description?: string;
  focusArea?: string;
}

/** Full analysis chat context response from the BFF API. */
export interface IAnalysisChatContextResponse {
  defaultPlaybookId: string;
  defaultPlaybookName: string;
  availablePlaybooks: IAnalysisPlaybookInfo[];
  inlineActions: IInlineActionInfo[];
  knowledgeSources: IAnalysisKnowledgeSourceInfo[];
  analysisContext: IAnalysisContextInfo;
  commands?: ICommandEntry[];
  searchGuidance?: string;
  scopeMetadata?: IAnalysisScopeMetadata;
}

/** Return type for the useChatContextMapping hook. */
export interface IUseChatContextMappingResult {
  contextMapping: IAnalysisChatContextResponse | null;
  isLoading: boolean;
  error: Error | null;
  refresh: () => void;
}

/** Return type for the useChatPlaybooks hook. */
export interface IUseChatPlaybooksResult {
  playbooks: IPlaybookOption[];
  isLoading: boolean;
  error: Error | null;
  refresh: () => Promise<void>;
}
