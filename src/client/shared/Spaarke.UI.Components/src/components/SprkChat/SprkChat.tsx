/**
 * SprkChat - Main reusable chat component
 *
 * Composes all SprkChat sub-components into a complete chat experience:
 * - Context selector (document + playbook)
 * - Predefined prompt suggestions
 * - Message list with streaming support
 * - Input area with Ctrl+Enter
 * - Highlight-and-refine on text selection
 *
 * Integrates with ChatEndpoints SSE API for real-time streaming responses.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from 'react';
import { makeStyles, shorthands, tokens, Spinner, Text, Button } from '@fluentui/react-components';
import {
  SparkleRegular,
  SearchRegular,
  DocumentTextRegular,
  CheckmarkCircleRegular,
  LightbulbRegular,
  ArrowSyncRegular,
  StopRegular,
} from '@fluentui/react-icons';
import {
  ISprkChatProps,
  IChatMessage,
  IPlaybookOption,
  IDocumentInsertEvent,
  IDocumentStatusChatMessage,
  IDocumentStatusMessage,
} from './types';
import { SprkChatMessage, ISprkChatMessageExtendedProps } from './SprkChatMessage';
import { SprkChatInput } from './SprkChatInput';
import { SprkChatContextSelector } from './SprkChatContextSelector';
import { SprkChatPredefinedPrompts } from './SprkChatPredefinedPrompts';
import { SprkChatHighlightRefine } from './SprkChatHighlightRefine';
import { SprkChatSuggestions } from './SprkChatSuggestions';
import { QuickActionChips } from './QuickActionChips';
import { SprkChatTypingIndicator } from './SprkChatTypingIndicator';
import { SprkChatUploadZone } from './SprkChatUploadZone';
import type { UploadedDocument } from './SprkChatUploadZone';
import type { InlineAiAction, InlineActionBroadcastEvent } from '../InlineAiToolbar/inlineAiToolbar.types';
import { useSseStream, parseSseEvent } from './hooks/useSseStream';
import { useChatSession } from './hooks/useChatSession';
import { useChatPlaybooks } from './hooks/useChatPlaybooks';
import { useSelectionListener } from './hooks/useSelectionListener';
import { useChatContextMapping } from './hooks/useChatContextMapping';
import type { IInlineActionInfo } from './hooks/useChatContextMapping';
import { useDynamicSlashCommands } from './hooks/useDynamicSlashCommands';
import { SprkChatExportWord } from './SprkChatExportWord';
import { Toaster, useToastController, useId, Toast, ToastTitle, ToastBody } from '@fluentui/react-components';
import { ActionConfirmationDialog } from './ActionConfirmationDialog';
import { openCodePageDialog, navigateToTarget, dispatchConfirmedAction } from './hooks/useActionHandlers';
import type { IPendingAction, IChatSseEventData } from './types';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * BroadcastChannel name for inline AI actions from the AnalysisWorkspace.
 *
 * Must match the channel name in useInlineAiToolbarState (AnalysisWorkspace)
 * and useInlineAiActions (shared library). Defined here as a local constant
 * to avoid a cross-module dependency (ADR-012: no Xrm/PCF imports in shared library).
 *
 * spec-FR-04: Inline actions MUST appear in SprkChat history.
 */
const INLINE_ACTION_CHANNEL = 'sprk-inline-action';

/**
 * BroadcastChannel name for document insert events.
 *
 * SprkChat dispatches `document_insert` events on this channel when the user
 * clicks the "Insert" button on an AI response message. The AnalysisWorkspace
 * Lexical editor subscribes to this channel in task 051 and handles insertion
 * at the current cursor position.
 *
 * Must match the channel name used by the task-051 Lexical handler in
 * AnalysisWorkspace (useLexicalInsertHandler.ts).
 *
 * @see IDocumentInsertEvent in types.ts
 * @see spec-2D - Insert-to-Editor phase requirements
 */
const DOCUMENT_INSERT_CHANNEL = 'sprk-document-insert';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.overflow('hidden'),
  },
  messageList: {
    flexGrow: 1,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    position: 'relative',
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flexGrow: 1,
    ...shorthands.gap(tokens.spacingVerticalS),
    color: tokens.colorNeutralForeground3,
  },
  loadingContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalL),
  },
  errorBanner: {
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorPaletteRedBackground1,
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
    textAlign: 'center',
  },
  playbookChips: {
    display: 'flex',
    flexWrap: 'wrap',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
  },
  stopGeneratingContainer: {
    display: 'flex',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
  },
  inputToolbar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalM),
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SprkChat - Full-featured chat component with SSE streaming.
 *
 * @example
 * ```tsx
 * <SprkChat
 *   playbookId="abc-123"
 *   apiBaseUrl="https://spe-api-dev-67e2xz.azurewebsites.net"
 *   accessToken={token}
 *   documentId={docId}
 *   onSessionCreated={(session) => console.log("Session:", session.sessionId)}
 * />
 * ```
 */
/**
 * Map an action ID or label to a Fluent v9 icon element for QuickActionChips.
 *
 * The API returns capability keys (e.g. "search", "selection_revise"); we assign
 * a semantically appropriate icon from the pre-imported icon set. Unknown IDs
 * fall back to SparkleRegular (generic AI action icon).
 */
function mapActionIdToIcon(id: string): React.ReactElement {
  switch (id) {
    case 'search':
      return React.createElement(SearchRegular);
    case 'selection_revise':
      return React.createElement(DocumentTextRegular);
    case 'fact_check':
      return React.createElement(CheckmarkCircleRegular);
    case 'suggest':
    case 'suggest_improvements':
      return React.createElement(LightbulbRegular);
    case 'summarize':
    case 'summary':
      return React.createElement(ArrowSyncRegular);
    default:
      return React.createElement(SparkleRegular);
  }
}

/**
 * Convert IInlineActionInfo[] from the API response to InlineAiAction[] for QuickActionChips.
 *
 * InlineAiAction requires a React element icon; IInlineActionInfo has none, so icons
 * are derived from the capability ID via mapActionIdToIcon().
 */
function mapInlineActionsToChipActions(actions: IInlineActionInfo[]): InlineAiAction[] {
  return actions.map(a => ({
    id: a.id,
    label: a.label,
    icon: mapActionIdToIcon(a.id),
    actionType: (a.actionType === 'diff' ? 'diff' : 'chat') as 'chat' | 'diff',
    description: a.description,
  }));
}

export const SprkChat: React.FC<ISprkChatProps> = ({
  sessionId: initialSessionId,
  documentId,
  playbookId,
  analysisId,
  apiBaseUrl,
  accessToken,
  onSessionCreated,
  onPlaybookChange,
  className,
  documents = [],
  playbooks = [],
  predefinedPrompts = [],
  contentRef: externalContentRef,
  maxCharCount,
  hostContext,
  bridge,
  onDocumentStreamEvent: onDocumentStreamEventProp,
  initialMessages,
}) => {
  const styles = useStyles();
  const messageListRef = React.useRef<HTMLDivElement>(null);
  const highlightContainerRef = externalContentRef || messageListRef;

  // Ref to the root container — passed to QuickActionChips for width-based visibility (NFR-04)
  const rootContainerRef = React.useRef<HTMLDivElement>(null);

  // Playbook discovery (fetches available playbooks for quick-action chips)
  const { playbooks: discoveredPlaybooks } = useChatPlaybooks({
    apiBaseUrl,
    accessToken,
  });

  // Analysis context mapping — only active when analysisId is provided (analysis mode)
  // Re-fetches when playbookId changes so chips reflect the active playbook's capabilities (FR-08)
  const { contextMapping } = useChatContextMapping({
    analysisId,
    playbookId,
    apiBaseUrl,
    accessToken,
  });

  // Convert API inline actions to InlineAiAction[] for QuickActionChips
  const quickChipActions = React.useMemo((): InlineAiAction[] => {
    if (!contextMapping || contextMapping.inlineActions.length === 0) return [];
    return mapInlineActionsToChipActions(contextMapping.inlineActions);
  }, [contextMapping]);

  // Merge passed-in playbooks with discovered ones (deduplicate by id)
  const allPlaybooks = React.useMemo((): IPlaybookOption[] => {
    const seen = new Set<string>(playbooks.map(p => p.id));
    const merged = [...playbooks];
    for (const pb of discoveredPlaybooks) {
      if (!seen.has(pb.id)) {
        seen.add(pb.id);
        merged.push(pb);
      }
    }
    return merged;
  }, [playbooks, discoveredPlaybooks]);

  // Session management
  const chatSession = useChatSession({ apiBaseUrl, accessToken, initialMessages });
  const {
    session,
    messages,
    isLoading: isSessionLoading,
    error: sessionError,
    createSession,
    loadHistory,
    switchContext,
    addMessage,
    updateLastMessage,
    updateLastMessageMetadata,
    updateMessageMetadataAt,
  } = chatSession;

  // Dynamic slash commands — fetches from BFF on session init, re-fetches on context change (FR-05, FR-17)
  // dynamicSlashCommands is the BFF-only portion; SprkChatInput merges with DEFAULT_SLASH_COMMANDS internally
  const { dynamicCommands: dynamicSlashCommands } = useDynamicSlashCommands({
    sessionId: session?.sessionId,
    apiBaseUrl,
    accessToken,
    playbookId,
    hostContext,
  });

  // SSE streaming
  const sseStream = useSseStream();
  const {
    content: streamedContent,
    isDone: streamDone,
    isStreaming,
    isTyping,
    error: streamError,
    suggestions,
    citations: streamCitations,
    pendingPlanId,
    pendingPlanData,
    pendingActionEvent,
    startStream,
    cancelStream,
    clearSuggestions,
    clearPendingActionEvent,
    setOnDocumentStreamEvent,
  } = sseStream;

  // Track current streaming state
  const isStreamingRef = React.useRef<boolean>(false);

  // Editor-sourced refine state (separate from chat SSE stream)
  const [isEditorRefining, setIsEditorRefining] = React.useState(false);
  const [editorRefineError, setEditorRefineError] = React.useState<Error | null>(null);
  const editorRefineAbortRef = React.useRef<AbortController | null>(null);

  // Plan approval streaming state (Phase 2F — task 072)
  // Separate from the main sseStream so plan execution doesn't overwrite chat content.
  const [isPlanApproving, setIsPlanApproving] = React.useState(false);
  const planApproveAbortRef = React.useRef<AbortController | null>(null);

  // ── Document persistence state (FR-14: Save to matter files) ─────────────
  // Tracks persistence state per document ID, keyed by documentId.
  // Stored separately from message state to avoid adding mutation methods to useChatSession.
  const [documentPersistenceState, setDocumentPersistenceState] = React.useState<
    Map<string, { state: 'idle' | 'saving' | 'saved' | 'error'; savedFileUrl?: string }>
  >(new Map());

  // ── Document upload drag-and-drop state (FR-13: drag-and-drop to context injection) ──
  // Tracks whether the user is dragging a file over the chat area.
  // dragCounter handles nested enter/leave events from child elements.
  const [isDragging, setIsDragging] = React.useState(false);
  const dragCounterRef = React.useRef(0);

  // Additional documents state for multi-document context
  const [additionalDocumentIds, setAdditionalDocumentIds] = React.useState<string[]>([]);
  const additionalDocsDebounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  // Cross-pane selection listener (receives selection_changed from Analysis Workspace editor)
  const { selection: crossPaneSelection, clearSelection: clearCrossPaneSelection } = useSelectionListener({
    bridge: bridge ?? null,
    enabled: !!bridge,
  });

  // ── Task R2-039: HITL Action Confirmation + Autonomous Execution UX ──────
  // pendingAction: holds the action awaiting user confirmation (HITL path)
  // isConfirmingAction: true while the confirmed action is being dispatched
  const [pendingAction, setPendingAction] = React.useState<IPendingAction | null>(null);
  const [isConfirmingAction, setIsConfirmingAction] = React.useState(false);

  // Fluent v9 Toaster for autonomous action success/error feedback (ADR-021)
  const toasterId = useId('sprkchat-toaster');
  const { dispatchToast } = useToastController(toasterId);

  /**
   * Handle action_confirmation SSE event (requiresConfirmation=true).
   * Sets the pendingAction state to show the ActionConfirmationDialog.
   */
  const handleActionConfirmationEvent = React.useCallback(
    (data: IChatSseEventData) => {
      if (!data.actionId || !data.actionName || !session) return;

      const action: IPendingAction = {
        actionId: data.actionId,
        actionName: data.actionName,
        summary: data.summary || 'This action requires your confirmation before proceeding.',
        parameters: data.parameters || {},
        sessionId: session.sessionId,
      };
      setPendingAction(action);
    },
    [session]
  );

  /**
   * Handle action_success SSE event (requiresConfirmation=false — autonomous execution).
   * Shows a Fluent v9 success toast (ADR-021).
   */
  const handleActionSuccessEvent = React.useCallback(
    (data: IChatSseEventData) => {
      dispatchToast(
        React.createElement(
          Toast,
          null,
          React.createElement(ToastTitle, null, 'Action Completed'),
          React.createElement(ToastBody, null, data.message || 'Action executed successfully.')
        ),
        { intent: 'success', timeout: 5000 }
      );
    },
    [dispatchToast]
  );

  /**
   * Handle action_error SSE event (action execution failed).
   * Shows a Fluent v9 error toast (ADR-021).
   */
  const handleActionErrorEvent = React.useCallback(
    (data: IChatSseEventData) => {
      dispatchToast(
        React.createElement(
          Toast,
          null,
          React.createElement(ToastTitle, null, 'Action Failed'),
          React.createElement(ToastBody, null, data.message || 'The action could not be completed.')
        ),
        { intent: 'error', timeout: 8000 }
      );
    },
    [dispatchToast]
  );

  /**
   * Handle dialog_open SSE event.
   * Opens a Code Page dialog via Xrm.Navigation.navigateTo with pre-populated fields.
   *
   * @see ADR-006 — MUST use Xrm.Navigation.navigateTo with pageType="webresource"
   */
  const handleDialogOpenEvent = React.useCallback((data: IChatSseEventData) => {
    if (!data.targetPage) {
      console.warn('[SprkChat] dialog_open event missing targetPage, ignoring.');
      return;
    }

    openCodePageDialog({
      targetPage: data.targetPage,
      prePopulateFields: data.prePopulateFields || {},
      width: data.width,
      height: data.height,
    });
  }, []);

  /**
   * Handle navigate SSE event (Task R2-052).
   * Navigates to a Dataverse record, external URL, or Code Page.
   *
   * @see ADR-006 — MUST use Xrm.Navigation for Code Page navigation
   */
  const handleNavigateEvent = React.useCallback((data: IChatSseEventData) => {
    if (!data.url && !data.targetPage) {
      console.warn('[SprkChat] navigate event missing both url and targetPage, ignoring.');
      return;
    }

    navigateToTarget({
      url: data.url,
      targetPage: data.targetPage,
      parameters: data.parameters || {},
      playbookId: data.playbookId,
    });
  }, []);

  /**
   * Confirm the pending action (user clicked Confirm in ActionConfirmationDialog).
   * Dispatches the action to the BFF and shows success/error toast.
   */
  const handleActionConfirm = React.useCallback(
    async (action: IPendingAction) => {
      setIsConfirmingAction(true);
      try {
        const result = await dispatchConfirmedAction(action, apiBaseUrl, accessToken);

        if (result.success) {
          dispatchToast(
            React.createElement(
              Toast,
              null,
              React.createElement(ToastTitle, null, 'Action Completed'),
              React.createElement(ToastBody, null, result.message)
            ),
            { intent: 'success', timeout: 5000 }
          );
        } else {
          dispatchToast(
            React.createElement(
              Toast,
              null,
              React.createElement(ToastTitle, null, 'Action Failed'),
              React.createElement(ToastBody, null, result.message)
            ),
            { intent: 'error', timeout: 8000 }
          );
        }
      } finally {
        setIsConfirmingAction(false);
        setPendingAction(null);
      }
    },
    [apiBaseUrl, accessToken, dispatchToast]
  );

  /**
   * Cancel the pending action (user clicked Cancel in ActionConfirmationDialog).
   * Clears the dialog without executing.
   */
  const handleActionCancel = React.useCallback(() => {
    setPendingAction(null);
  }, []);

  // Initialize session on mount
  React.useEffect(() => {
    const initSession = async () => {
      if (initialSessionId) {
        // Resume existing session
        const newSession = await createSession(documentId, playbookId, hostContext);
        if (newSession) {
          // loadHistory needs the session to be set first - handled by useChatSession
        }
      } else {
        // Create new session
        const newSession = await createSession(documentId, playbookId, hostContext);
        if (newSession && onSessionCreated) {
          onSessionCreated(newSession);
        }
      }
    };

    initSession();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Load history when session is available
  React.useEffect(() => {
    if (session && initialSessionId) {
      loadHistory();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session?.sessionId]);

  // Update streaming message content as tokens arrive
  React.useEffect(() => {
    if (isStreaming && streamedContent) {
      updateLastMessage(streamedContent);
    }
  }, [streamedContent, isStreaming, updateLastMessage]);

  // Handle stream completion
  React.useEffect(() => {
    if (streamDone && isStreamingRef.current) {
      isStreamingRef.current = false;
      // Final content is already set by the token updates
    }
  }, [streamDone]);

  // Phase 2F: When a plan_preview SSE event arrives, update the last assistant message's metadata
  // so PlanPreviewCard renders correctly instead of the plain text bubble.
  // This runs whenever pendingPlanId becomes non-null (set by useSseStream on plan_preview event).
  React.useEffect(() => {
    if (!pendingPlanId || !pendingPlanData) {
      return;
    }
    // Map backend step shape to IChatMessagePlanStep format
    const planSteps = (pendingPlanData.steps ?? []).map(s => ({
      id: s.id,
      description: s.description,
      status: s.status as 'pending' | 'running' | 'completed' | 'failed',
    }));
    updateLastMessageMetadata({
      responseType: 'plan_preview',
      planTitle: pendingPlanData.planTitle,
      plan: planSteps,
      data: {
        planId: pendingPlanData.planId,
        analysisId: pendingPlanData.analysisId,
        writeBackTarget: pendingPlanData.writeBackTarget,
      },
    });
  }, [pendingPlanId, pendingPlanData, updateLastMessageMetadata]);

  // Task R2-039/R2-052: Handle action/dialog/navigate SSE events from the stream.
  // Dispatches to the appropriate handler based on event type:
  // - action_confirmation → show ActionConfirmationDialog
  // - action_success → show success toast
  // - action_error → show error toast
  // - dialog_open → open Code Page via Xrm.Navigation.navigateTo
  // - navigate → navigate to record/URL via Xrm.Navigation (R2-052)
  React.useEffect(() => {
    if (!pendingActionEvent) return;

    const { type, data } = pendingActionEvent;

    switch (type) {
      case 'action_confirmation':
        handleActionConfirmationEvent(data);
        break;
      case 'action_success':
        handleActionSuccessEvent(data);
        break;
      case 'action_error':
        handleActionErrorEvent(data);
        break;
      case 'dialog_open':
        handleDialogOpenEvent(data);
        break;
      case 'navigate':
        handleNavigateEvent(data);
        break;
    }

    // Clear the event so it's not processed again
    clearPendingActionEvent();
  }, [
    pendingActionEvent,
    handleActionConfirmationEvent,
    handleActionSuccessEvent,
    handleActionErrorEvent,
    handleDialogOpenEvent,
    handleNavigateEvent,
    clearPendingActionEvent,
  ]);

  // ── Task R2-051 + Task 007: Forward BFF document_stream SSE events ──
  //
  // Registers a synchronous callback with useSseStream that fires for every
  // document_stream_start/token/end SSE event.
  //
  // Two forwarding paths (may both be active simultaneously):
  //   1. bridge.emit() → BroadcastChannel → useDocumentStreamConsumer (legacy)
  //   2. onDocumentStreamEventProp → direct callback → AnalysisAiContext (unified page)
  //
  // When bridge is null and onDocumentStreamEventProp is provided, events go
  // exclusively through the direct callback (zero-serialization path).
  //
  // Uses a callback (not React state) because document_stream_token events
  // arrive at high frequency (one per AI-generated token). React state batching
  // would coalesce multiple token events in a single render frame, causing
  // lost tokens. The synchronous callback ensures every token is forwarded.
  //
  // SECURITY (ADR-015): Only content tokens and structural metadata are forwarded.
  // Auth tokens, credentials, and user PII are NEVER included in bridge messages.
  React.useEffect(() => {
    const hasBridge = !!bridge;
    const hasDirectCallback = !!onDocumentStreamEventProp;

    if (!hasBridge && !hasDirectCallback) {
      setOnDocumentStreamEvent(null);
      return;
    }

    setOnDocumentStreamEvent(event => {
      try {
        // Path 1: BroadcastChannel bridge (legacy cross-pane path)
        if (bridge) {
          switch (event.type) {
            case 'document_stream_start':
              bridge.emit('document_stream_start', {
                operationId: event.operationId,
                targetPosition: event.targetPosition,
                operationType: event.operationType,
              });
              break;

            case 'document_stream_token':
              bridge.emit('document_stream_token', {
                operationId: event.operationId,
                token: event.token,
                index: event.index,
              });
              break;

            case 'document_stream_end':
              bridge.emit('document_stream_end', {
                operationId: event.operationId,
                cancelled: event.cancelled,
                totalTokens: event.totalTokens,
              });
              break;
          }
        }

        // Path 2: Direct callback (unified page, zero-serialization)
        if (onDocumentStreamEventProp) {
          onDocumentStreamEventProp(event);
        }
      } catch (err) {
        console.error('[SprkChat] Failed to forward document_stream event:', err);
      }
    });

    return () => {
      setOnDocumentStreamEvent(null);
    };
  }, [bridge, onDocumentStreamEventProp, setOnDocumentStreamEvent]);

  // Auto-scroll to bottom on new messages
  React.useEffect(() => {
    if (messageListRef.current) {
      messageListRef.current.scrollTop = messageListRef.current.scrollHeight;
    }
  }, [messages, streamedContent]);

  // Send a message and start streaming the response
  const handleSend = React.useCallback(
    (messageText: string) => {
      if (!session || isStreaming) {
        return;
      }

      // Clear follow-up suggestions from previous response
      clearSuggestions();

      // Add user message
      const userMessage: IChatMessage = {
        role: 'User',
        content: messageText,
        timestamp: new Date().toISOString(),
      };
      addMessage(userMessage);

      // Add placeholder assistant message for streaming
      const assistantMessage: IChatMessage = {
        role: 'Assistant',
        content: '',
        timestamp: new Date().toISOString(),
      };
      addMessage(assistantMessage);
      isStreamingRef.current = true;

      // Start SSE stream
      const baseUrl = apiBaseUrl.replace(/\/+$/, '');
      startStream(
        `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/messages`,
        { message: messageText, documentId },
        accessToken
      );
    },
    [session, isStreaming, addMessage, startStream, clearSuggestions, apiBaseUrl, documentId, accessToken]
  );

  // Handle predefined prompt selection
  const handlePromptSelect = React.useCallback(
    (prompt: string) => {
      handleSend(prompt);
    },
    [handleSend]
  );

  // Handle follow-up suggestion selection — sends suggestion text as a new user message
  const handleSuggestionSelect = React.useCallback(
    (suggestion: string) => {
      handleSend(suggestion);
    },
    [handleSend]
  );

  // ── Plan Preview callbacks (Phase 2F) ──────────────────────────────────────

  // Extract tenant ID from JWT for X-Tenant-Id header (same logic as useSseStream)
  // NOTE: Must be declared before handlePlanProceed and handleEditorRefineRequest which both call it.
  const extractTenantIdFromToken = React.useCallback((token: string): string | null => {
    try {
      const parts = token.split('.');
      if (parts.length !== 3) return null;
      const payload = JSON.parse(atob(parts[1]));
      return payload.tid || null;
    } catch {
      return null;
    }
  }, []);

  /**
   * Called when the user clicks Proceed on a PlanPreviewCard.
   *
   * Phase 2F (task 072): Calls POST /api/ai/chat/sessions/{sessionId}/plan/approve and
   * processes the SSE stream to update plan step statuses in real time.
   *
   * Protocol:
   *   1. POST to /plan/approve with { planId } from the last plan_preview event.
   *   2. Stream SSE events:
   *        plan_step_start       → update step status to 'running' on the plan card
   *        token                 → show streaming content (appended to a temporary message)
   *        plan_step_complete    → update step status to 'completed' or 'failed'
   *        done / error          → finalize
   *   3. 409 Conflict → plan already approved (double-click); log and ignore.
   *   4. 404 Not Found → plan expired; show informational message.
   *
   * @param messageIndex - Index of the PlanPreviewCard message in the messages array.
   *   Used to update its step statuses via updateMessageMetadataAt.
   */
  const handlePlanProceed = React.useCallback(
    (messageIndex: number) => {
      if (!session || !pendingPlanId || isPlanApproving) {
        console.warn(
          '[SprkChat] handlePlanProceed: cannot proceed — session:',
          !!session,
          ', pendingPlanId:',
          pendingPlanId,
          ', isPlanApproving:',
          isPlanApproving
        );
        return;
      }

      // Cancel any existing plan approval stream
      if (planApproveAbortRef.current) {
        planApproveAbortRef.current.abort();
      }

      const controller = new AbortController();
      planApproveAbortRef.current = controller;
      setIsPlanApproving(true);

      // Diagnostic: log plan approval context
      console.log(
        '[SprkChat] handlePlanProceed: approving plan —',
        'planId:',
        pendingPlanId,
        ', pendingPlanData:',
        JSON.stringify(pendingPlanData),
        ', sessionId:',
        session.sessionId
      );

      const planIdToApprove = pendingPlanId;
      const baseUrl = apiBaseUrl.replace(/\/+$/, '');
      const approveUrl = `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/plan/approve`;

      // Add an assistant message placeholder to receive step execution output
      const executionMessage: IChatMessage = {
        role: 'Assistant',
        content: '',
        timestamp: new Date().toISOString(),
      };
      addMessage(executionMessage);

      const runApprovalStream = async () => {
        try {
          const tenantId = extractTenantIdFromToken(accessToken);
          const response = await fetch(approveUrl, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              Authorization: `Bearer ${accessToken}`,
              ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
            },
            body: JSON.stringify({ planId: planIdToApprove }),
            signal: controller.signal,
          });

          if (response.status === 409) {
            // Double-click or already approved — update message to reflect this
            updateLastMessage('This plan was already approved or has expired. Please send a new message.');
            return;
          }

          if (response.status === 404) {
            updateLastMessage('Plan not found. It may have expired (30-minute limit). Please resend your request.');
            return;
          }

          if (!response.ok) {
            const errorText = await response.text();
            updateLastMessage(`Plan approval failed (${response.status}): ${errorText}`);
            return;
          }

          if (!response.body) {
            updateLastMessage('Plan approval response body is empty.');
            return;
          }

          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = '';
          let accumulated = '';

          while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });
            const parts = buffer.split('\n\n');
            buffer = parts.pop() || '';

            for (const part of parts) {
              const lines = part.split('\n');
              for (const line of lines) {
                const event = parseSseEvent(line);
                if (!event) continue;

                if (event.type === 'plan_step_start' && event.data?.stepId) {
                  // Update the plan preview card to show this step as running.
                  // Uses function updater to avoid stale closure on `messages`.
                  const stepId = event.data.stepId;
                  updateMessageMetadataAt(messageIndex, current => ({
                    ...current,
                    responseType: 'plan_preview',
                    plan:
                      current?.plan?.map(s => (s.id === stepId ? { ...s, status: 'running' as const } : s)) ??
                      current?.plan,
                  }));
                } else if (event.type === 'token' && event.content) {
                  accumulated += event.content;
                  updateLastMessage(accumulated);
                } else if (event.type === 'plan_step_complete' && event.data?.stepId) {
                  const stepId = event.data.stepId;
                  const stepStatus = event.data.status === 'failed' ? 'failed' : 'completed';
                  const stepResult = event.data.result ?? undefined;
                  // Update the plan step status on the plan preview card.
                  // Uses function updater to avoid stale closure on `messages`.
                  updateMessageMetadataAt(messageIndex, current => ({
                    ...current,
                    responseType: 'plan_preview',
                    plan:
                      current?.plan?.map(s =>
                        s.id === stepId ? { ...s, status: stepStatus as 'completed' | 'failed', result: stepResult } : s
                      ) ?? current?.plan,
                  }));
                } else if (event.type === 'done') {
                  if (accumulated.length === 0) {
                    updateLastMessage('Plan executed successfully.');
                  }
                  console.debug('[SprkChat] Plan approval stream complete — planId:', planIdToApprove);
                } else if (event.type === 'error') {
                  // Mark all non-completed steps as 'failed' (ErrorCircle icon)
                  updateMessageMetadataAt(messageIndex, current => ({
                    ...current,
                    responseType: 'plan_preview',
                    plan:
                      current?.plan?.map(s => (s.status === 'completed' ? s : { ...s, status: 'failed' as const })) ??
                      current?.plan,
                  }));
                  updateLastMessage(`Plan execution error: ${event.content ?? 'Unknown error'}`);
                }
              }
            }
          }

          // Process any remaining buffer
          if (buffer.trim()) {
            const lines = buffer.split('\n');
            for (const line of lines) {
              const event = parseSseEvent(line);
              if (!event) continue;
              if (event.type === 'token' && event.content) {
                accumulated += event.content;
                updateLastMessage(accumulated);
              } else if (event.type === 'done') {
                if (accumulated.length === 0) {
                  updateLastMessage('Plan executed successfully.');
                }
              } else if (event.type === 'error') {
                // Mark all non-completed steps as 'failed' (ErrorCircle icon)
                updateMessageMetadataAt(messageIndex, current => ({
                  ...current,
                  responseType: 'plan_preview',
                  plan:
                    current?.plan?.map(s => (s.status === 'completed' ? s : { ...s, status: 'failed' as const })) ??
                    current?.plan,
                }));
                updateLastMessage(`Plan execution error: ${event.content ?? 'Unknown error'}`);
              }
            }
          }
        } catch (err: unknown) {
          if (err instanceof DOMException && err.name === 'AbortError') {
            // Cancelled (component unmount or new plan approval started) — not an error
            return;
          }
          const errorMsg = err instanceof Error ? err.message : 'Unknown plan approval error';
          console.error('[SprkChat] Plan approval stream failed:', errorMsg);
          // Mark all non-completed steps as 'failed' on network/stream error
          updateMessageMetadataAt(messageIndex, current => ({
            ...current,
            responseType: 'plan_preview',
            plan:
              current?.plan?.map(s => (s.status === 'completed' ? s : { ...s, status: 'failed' as const })) ??
              current?.plan,
          }));
          updateLastMessage(`Plan approval failed: ${errorMsg}`);
        } finally {
          setIsPlanApproving(false);
          planApproveAbortRef.current = null;
        }
      };

      runApprovalStream();
    },
    [
      session,
      pendingPlanId,
      isPlanApproving,
      apiBaseUrl,
      accessToken,
      addMessage,
      updateLastMessage,
      updateMessageMetadataAt,
      extractTenantIdFromToken,
    ]
  );

  /**
   * Called when the user clicks Cancel on a PlanPreviewCard.
   * Logs the cancellation; full dismissal logic added in task 072.
   */
  const handlePlanCancel = React.useCallback((_messageIndex: number) => {
    console.log('[SprkChat] Plan preview cancelled by user');
  }, []);

  /**
   * Called when the user submits an edit message from within PlanPreviewCard.
   * Routes the edit as a new user message so the BFF can regenerate the plan.
   */
  const handlePlanEditMessage = React.useCallback(
    (editMessage: string) => {
      handleSend(editMessage);
    },
    [handleSend]
  );

  /**
   * Called when the user clicks Cancel Execution on a PlanPreviewCard during
   * active plan execution. Aborts the SSE stream via AbortController.
   *
   * After abort, marks all non-completed steps as 'failed' so the UI reflects
   * that execution was interrupted.
   *
   * @param messageIndex - Index of the PlanPreviewCard message in the messages array.
   */
  const handleCancelExecution = React.useCallback(
    (messageIndex: number) => {
      console.log('[SprkChat] Plan execution cancelled by user');
      // Abort the plan approval SSE stream
      if (planApproveAbortRef.current) {
        planApproveAbortRef.current.abort();
        planApproveAbortRef.current = null;
      }
      setIsPlanApproving(false);

      // Mark all non-completed steps as 'failed' to reflect cancellation
      updateMessageMetadataAt(messageIndex, current => ({
        ...current,
        responseType: 'plan_preview',
        plan:
          current?.plan?.map(s => (s.status === 'completed' ? s : { ...s, status: 'failed' as const })) ??
          current?.plan,
      }));
    },
    [updateMessageMetadataAt]
  );

  // ── Phase 2D: Insert-to-Editor ─────────────────────────────────────────────

  /**
   * Handle "Insert" button click on an AI response message.
   *
   * Dispatches an `IDocumentInsertEvent` on the `sprk-document-insert`
   * BroadcastChannel so the AnalysisWorkspace Lexical editor can insert the
   * content at the current cursor position (task 051 adds the Lexical handler).
   *
   * ADR-012: MUST NOT call Xrm directly — uses BroadcastChannel as the bridge.
   * ADR-015: MUST NOT include auth tokens in BroadcastChannel messages.
   *
   * Falls back gracefully when BroadcastChannel is unavailable (e.g., unit tests,
   * JSDOM environments) — logs a warning but does not throw.
   *
   * @param content - Plain text content from the AI message to insert.
   */
  const handleInsert = React.useCallback(
    (content: string) => {
      if (!content) {
        return;
      }

      if (typeof BroadcastChannel === 'undefined') {
        console.warn('[SprkChat] BroadcastChannel unavailable — document_insert event not dispatched');
        return;
      }

      try {
        const event: IDocumentInsertEvent = {
          type: 'document_insert',
          content,
          contentType: 'text',
          insertAt: 'cursor',
          timestamp: Date.now(),
        };

        const channel = new BroadcastChannel(DOCUMENT_INSERT_CHANNEL);
        channel.postMessage(event);
        channel.close();

        console.debug('[SprkChat] document_insert dispatched — contentLength:', content.length);
      } catch (err) {
        console.warn('[SprkChat] Failed to dispatch document_insert event:', err);
      }
    },
    [] // no external dependencies — BroadcastChannel is a global; content comes from call site
  );

  // ── FR-14: Save to matter files ─────────────────────────────────────────────

  /**
   * Persist an uploaded document to the matter's SPE container.
   *
   * Calls POST /api/ai/chat/sessions/{sessionId}/documents/{documentId}/persist
   * with the containerId from hostContext. Updates the local persistence state
   * map which SprkChatDocumentStatus reads to show button / spinner / saved link.
   *
   * On success: persistenceState -> 'saved', savedFileUrl from response.
   * On failure: persistenceState -> 'error', button restored for retry.
   *
   * @param docId - The session document ID to persist.
   * @see spec-FR-14 -- Optional SPE persistence
   * @see spec-NFR-06 -- Creates a COPY in SPE, session document remains
   */
  const handleSaveToMatterFiles = React.useCallback(
    async (docId: string) => {
      if (!session || !hostContext?.entityId) {
        console.warn('[SprkChat] handleSaveToMatterFiles: no session or hostContext');
        return;
      }

      // Set saving state
      setDocumentPersistenceState(prev => {
        const next = new Map(prev);
        next.set(docId, { state: 'saving' });
        return next;
      });

      try {
        const tenantId = extractTenantIdFromToken(accessToken);
        const normalizedBase = apiBaseUrl.replace(/\/+$/, '');
        const persistUrl = `${normalizedBase}/api/ai/chat/sessions/${session.sessionId}/documents/${encodeURIComponent(docId)}/persist`;

        const response = await fetch(persistUrl, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${accessToken}`,
            ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
          },
          body: JSON.stringify({
            containerId: hostContext.entityId,
          }),
        });

        if (!response.ok) {
          let errorMsg = `Save failed (${response.status})`;
          try {
            const errorBody = await response.json();
            if (errorBody?.detail) errorMsg = errorBody.detail;
            else if (errorBody?.title) errorMsg = errorBody.title;
          } catch {
            // Use default message
          }
          throw new Error(errorMsg);
        }

        // Parse response -- BFF returns SpeFilePersistResponse: { speFileId, filename, url, sizeBytes, uploadedAt }
        const result = (await response.json()) as {
          speFileId: string;
          filename: string;
          url: string;
          sizeBytes: number;
        };

        // Update persistence state to saved
        setDocumentPersistenceState(prev => {
          const next = new Map(prev);
          next.set(docId, { state: 'saved', savedFileUrl: result.url });
          return next;
        });

        console.debug('[SprkChat] Document persisted to SPE -- docId:', docId, ', url:', result.url);
      } catch (err: unknown) {
        const errorMsg = err instanceof Error ? err.message : 'Unknown error';
        console.error('[SprkChat] Save to matter files failed:', errorMsg);

        // Restore to idle (error state) so the user can retry
        setDocumentPersistenceState(prev => {
          const next = new Map(prev);
          next.set(docId, { state: 'error' });
          return next;
        });
      }
    },
    [session, hostContext, apiBaseUrl, accessToken, extractTenantIdFromToken]
  );

  /**
   * Derive whether the host context has a containerId for SPE persistence.
   * Used to conditionally show the "Save to matter files" button on
   * document_status messages (FR-14: hide when containerId is absent).
   */
  const hasContainerId = !!hostContext?.entityId;

  // ── FR-13: Drag-and-drop event handlers for document upload ──────────────

  const handleRootDragEnter = React.useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    dragCounterRef.current += 1;
    if (dragCounterRef.current === 1) {
      setIsDragging(true);
    }
  }, []);

  const handleRootDragOver = React.useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
  }, []);

  const handleRootDragLeave = React.useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    dragCounterRef.current -= 1;
    if (dragCounterRef.current <= 0) {
      dragCounterRef.current = 0;
      setIsDragging(false);
    }
  }, []);

  const handleRootDrop = React.useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    dragCounterRef.current = 0;
    // Don't dismiss isDragging here — let SprkChatUploadZone handle the drop
    // and control the phase transition (success/error auto-dismiss overlay).
  }, []);

  /**
   * Handle successful document upload from SprkChatUploadZone.
   *
   * The BFF returns the document synchronously (status: "ready") after Document
   * Intelligence extraction. This handler:
   * 1. Inserts a document_status system message into chat (shows "Document added to context")
   * 2. Dismisses the upload overlay after a brief delay
   *
   * The document content is already stored in session-scoped Redis by the BFF;
   * the AI can now reference it in subsequent messages.
   *
   * @see spec-FR-13 -- Document upload via drag-and-drop
   * @see ChatDocumentEndpoints.cs -- BFF returns 202 with status: "ready"
   */
  const handleUploadComplete = React.useCallback(
    (doc: UploadedDocument) => {
      // Map BFF response status to document processing status
      const processingStatus = doc.status === 'ready' ? ('complete' as const) : ('processing' as const);

      // Build the document status metadata
      const docStatus: IDocumentStatusMessage = {
        documentId: doc.documentId,
        fileName: doc.fileName,
        status: processingStatus,
        pageCount: doc.pageCount,
        startedAt: Date.now(),
        persistenceState: 'idle',
      };

      // Create a system message carrying the document status
      const statusMessage: IDocumentStatusChatMessage = {
        role: 'System',
        content: '',
        timestamp: new Date().toISOString(),
        metadata: {
          responseType: 'document_status',
        },
        documentStatus: docStatus,
      };

      addMessage(statusMessage);

      // Dismiss the upload overlay after a short delay so the user sees the success state
      setTimeout(() => {
        setIsDragging(false);
      }, 1200);

      console.debug(
        '[SprkChat] Document uploaded and added to context:',
        'docId:',
        doc.documentId,
        ', fileName:',
        doc.fileName,
        ', status:',
        doc.status
      );
    },
    [addMessage]
  );

  /**
   * Handle document upload error from SprkChatUploadZone.
   * Shows an error toast via the Fluent v9 Toaster (ADR-021).
   */
  const handleUploadError = React.useCallback(
    (errorMsg: string) => {
      dispatchToast(
        React.createElement(
          Toast,
          null,
          React.createElement(ToastTitle, null, 'Upload Failed'),
          React.createElement(ToastBody, null, errorMsg)
        ),
        { intent: 'error', timeout: 8000 }
      );

      // Dismiss the upload overlay after showing the error briefly
      setTimeout(() => {
        setIsDragging(false);
      }, 2000);
    },
    [dispatchToast]
  );

  // Handle context changes — clear cross-pane selection when document/record changes
  const handleDocumentChange = React.useCallback(
    (newDocId: string) => {
      clearCrossPaneSelection();
      switchContext(newDocId || undefined, playbookId);
    },
    [switchContext, playbookId, clearCrossPaneSelection]
  );

  const handlePlaybookChange = React.useCallback(
    (newPlaybookId: string) => {
      switchContext(documentId, newPlaybookId);
      if (onPlaybookChange) {
        onPlaybookChange(newPlaybookId);
      }
    },
    [switchContext, documentId, onPlaybookChange]
  );

  // Handle additional documents change with debounce (300ms)
  const handleAdditionalDocumentsChange = React.useCallback(
    (newIds: string[]) => {
      setAdditionalDocumentIds(newIds);

      // Debounce the API call to avoid rapid-fire PATCH requests
      if (additionalDocsDebounceRef.current) {
        clearTimeout(additionalDocsDebounceRef.current);
      }
      additionalDocsDebounceRef.current = setTimeout(() => {
        switchContext(documentId, playbookId, hostContext, newIds);
      }, 300);
    },
    [switchContext, documentId, playbookId, hostContext]
  );

  // Clean up debounce timer, editor refine, and plan approval abort controllers on unmount
  React.useEffect(() => {
    return () => {
      if (additionalDocsDebounceRef.current) {
        clearTimeout(additionalDocsDebounceRef.current);
      }
      if (editorRefineAbortRef.current) {
        editorRefineAbortRef.current.abort();
      }
      if (planApproveAbortRef.current) {
        planApproveAbortRef.current.abort();
      }
    };
  }, []);

  /**
   * Handle editor-sourced refine requests by streaming SSE response through the bridge.
   *
   * When the selection comes from the Analysis Workspace editor (source="editor"),
   * the revised text is routed through SprkChatBridge as document_stream_* events
   * so the Analysis Workspace can handle it via its diff review or streaming pipeline.
   *
   * The operationType is set to "diff" for selection-revision operations, which causes
   * the Analysis Workspace's useDiffReview hook to buffer tokens and show the
   * DiffReviewPanel for user review (Accept/Reject/Edit).
   */
  const handleEditorRefine = React.useCallback(
    (selectedText: string, instruction: string) => {
      if (!session || !bridge || isEditorRefining || isStreaming) {
        return;
      }

      // Cancel any existing editor refine
      if (editorRefineAbortRef.current) {
        editorRefineAbortRef.current.abort();
      }

      // Add a brief user message in the chat for awareness
      const refineMessage: IChatMessage = {
        role: 'User',
        content: `Refining editor selection: "${selectedText.substring(0, 100)}${selectedText.length > 100 ? '\u2026' : ''}"\n\nInstruction: ${instruction}`,
        timestamp: new Date().toISOString(),
      };
      addMessage(refineMessage);

      // Add a placeholder assistant message that will show status
      const statusMessage: IChatMessage = {
        role: 'Assistant',
        content: 'Generating revision\u2026',
        timestamp: new Date().toISOString(),
      };
      addMessage(statusMessage);

      setIsEditorRefining(true);
      setEditorRefineError(null);

      const controller = new AbortController();
      editorRefineAbortRef.current = controller;

      const operationId = `refine-${Date.now()}-${Math.random().toString(36).substring(2, 8)}`;
      let tokenIndex = 0;

      const runRefineStream = async () => {
        try {
          // Emit document_stream_start through the bridge with operationType "diff"
          // so the Analysis Workspace's useDiffReview hook captures and buffers tokens.
          bridge.emit('document_stream_start', {
            operationId,
            targetPosition: 'selection',
            operationType: 'diff',
          });

          const tenantId = extractTenantIdFromToken(accessToken);
          const baseUrl = apiBaseUrl.replace(/\/+$/, '');
          const refineUrl = `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/refine`;

          const response = await fetch(refineUrl, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              Authorization: `Bearer ${accessToken}`,
              ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
            },
            body: JSON.stringify({
              selectedText,
              instruction,
              // TRACKED: GitHub #234 - PH-112-A: surroundingContext not yet available
              surroundingContext: null,
            }),
            signal: controller.signal,
          });

          if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Refine request failed (${response.status}): ${errorText}`);
          }

          if (!response.body) {
            throw new Error('Response body is empty');
          }

          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = '';

          while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            // Parse SSE events separated by double newlines
            const parts = buffer.split('\n\n');
            buffer = parts.pop() || '';

            for (const part of parts) {
              const lines = part.split('\n');
              for (const line of lines) {
                const event = parseSseEvent(line);
                if (!event) continue;

                if (event.type === 'token' && event.content) {
                  // Route token through bridge for Analysis Workspace consumption
                  bridge.emit('document_stream_token', {
                    operationId,
                    token: event.content,
                    index: tokenIndex++,
                  });
                } else if (event.type === 'done') {
                  // Stream completed successfully
                  bridge.emit('document_stream_end', {
                    operationId,
                    cancelled: false,
                    totalTokens: tokenIndex,
                  });
                } else if (event.type === 'error') {
                  throw new Error(event.content || 'Refinement stream error');
                }
              }
            }
          }

          // Process any remaining buffer
          if (buffer.trim()) {
            const lines = buffer.split('\n');
            for (const line of lines) {
              const event = parseSseEvent(line);
              if (!event) continue;
              if (event.type === 'token' && event.content) {
                bridge.emit('document_stream_token', {
                  operationId,
                  token: event.content,
                  index: tokenIndex++,
                });
              } else if (event.type === 'done') {
                bridge.emit('document_stream_end', {
                  operationId,
                  cancelled: false,
                  totalTokens: tokenIndex,
                });
              } else if (event.type === 'error') {
                throw new Error(event.content || 'Refinement stream error');
              }
            }
          }

          // Update the chat status message
          if (tokenIndex === 0) {
            updateLastMessage('No changes suggested.');
          } else {
            updateLastMessage('Revision sent to editor for review.');
          }

          // Clear cross-pane selection after successful submission
          clearCrossPaneSelection();
        } catch (err: unknown) {
          if (err instanceof DOMException && err.name === 'AbortError') {
            // Cancelled by user, not an error
            bridge.emit('document_stream_end', {
              operationId,
              cancelled: true,
              totalTokens: tokenIndex,
            });
            updateLastMessage('Refinement cancelled.');
            return;
          }

          const errorObj = err instanceof Error ? err : new Error('Unknown refine error');
          setEditorRefineError(errorObj);

          // Emit stream end with cancel to clean up Analysis Workspace state
          bridge.emit('document_stream_end', {
            operationId,
            cancelled: true,
            totalTokens: tokenIndex,
          });

          // Show error in the chat status message
          updateLastMessage(`Refinement error: ${errorObj.message}`);
        } finally {
          setIsEditorRefining(false);
          editorRefineAbortRef.current = null;
        }
      };

      runRefineStream();
    },
    [
      session,
      bridge,
      isEditorRefining,
      isStreaming,
      addMessage,
      updateLastMessage,
      apiBaseUrl,
      accessToken,
      clearCrossPaneSelection,
      extractTenantIdFromToken,
    ]
  );

  /**
   * Handle chat-sourced refine requests (local text selection in chat messages).
   * Uses the standard useSseStream hook to show the refined text as a chat response.
   */
  const handleChatRefine = React.useCallback(
    (selectedText: string, instruction: string) => {
      if (!session || isStreaming) {
        return;
      }

      // Add the refinement request as a user message
      const refineMessage: IChatMessage = {
        role: 'User',
        content: `Refine the following text: "${selectedText}"\n\nInstruction: ${instruction}`,
        timestamp: new Date().toISOString(),
      };
      addMessage(refineMessage);

      // Add placeholder for assistant response
      const assistantMessage: IChatMessage = {
        role: 'Assistant',
        content: '',
        timestamp: new Date().toISOString(),
      };
      addMessage(assistantMessage);
      isStreamingRef.current = true;

      // Start SSE stream to refine endpoint
      const baseUrl = apiBaseUrl.replace(/\/+$/, '');
      startStream(
        `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/refine`,
        { selectedText, instruction },
        accessToken
      );
    },
    [session, isStreaming, addMessage, startStream, apiBaseUrl, accessToken]
  );

  /**
   * Handle highlight-and-refine: routes to either editor-sourced or chat-sourced handler.
   *
   * Task 112: Selection-based revision flow
   * - Editor source (cross-pane): SSE tokens are routed through SprkChatBridge
   *   as document_stream_* events for diff review in the Analysis Workspace.
   * - Chat source (local DOM): SSE tokens are displayed as a chat response.
   */
  const handleRefine = React.useCallback(
    (selectedText: string, instruction: string) => {
      // Determine source from crossPaneSelection state
      const isEditorSource = !!crossPaneSelection && crossPaneSelection.text.length > 0;

      if (isEditorSource && bridge) {
        handleEditorRefine(selectedText, instruction);
      } else {
        handleChatRefine(selectedText, instruction);
      }
    },
    [crossPaneSelection, bridge, handleEditorRefine, handleChatRefine]
  );

  /**
   * Handle QuickActionChip click — send the chip's label as a user message.
   *
   * The chip carries an InlineAiAction whose label is used as the message text,
   * pre-prefixed with "[Quick Action]" so the AI agent can identify it as a
   * structured action request. When the session is not ready or a stream is
   * in progress the click is ignored.
   */
  const handleChipAction = React.useCallback(
    (action: InlineAiAction) => {
      if (!session || isStreaming) return;
      handleSend(`[Quick Action: ${action.label}]`);
    },
    [session, isStreaming, handleSend]
  );

  // ── Task 031: Subscribe to inline_action BroadcastChannel events ─────
  //
  // When the user selects text in the AnalysisWorkspace editor and clicks an
  // action button in InlineAiToolbar, an InlineActionBroadcastEvent is posted
  // to the 'sprk-inline-action' BroadcastChannel. This effect subscribes to
  // that channel and routes each event into the active SprkChat session as a
  // user message, which triggers the AI to respond (spec-FR-04).
  //
  // Message format: "[{actionLabel}] {selectedText}"
  //   - The bracketed prefix lets the BFF/AI agent identify the intent
  //     (e.g. [Summarize], [Simplify], [Ask]).
  //   - For diff-type actions (Simplify, Expand) the AnalysisWorkspace has
  //     ALREADY opened DiffReviewPanel; SprkChat receives the same event so
  //     the action appears in chat history.
  //
  // Constraints:
  //   - No auth tokens in BroadcastChannel messages (ADR-015)
  //   - No Xrm/PCF dependency in this shared library component (ADR-012)
  //   - BroadcastChannel is cleaned up on unmount (no memory leak)
  //   - If the session is not ready or streaming is in progress the event is
  //     silently dropped (handleSend guards both conditions internally).
  React.useEffect(() => {
    // Guard: BroadcastChannel may not be available in test environments.
    if (typeof BroadcastChannel === 'undefined') {
      return;
    }

    const channel = new BroadcastChannel(INLINE_ACTION_CHANNEL);

    const handleMessage = (event: MessageEvent<InlineActionBroadcastEvent>) => {
      const payload = event.data;

      // Validate event shape — guard against malformed messages from other origins.
      if (
        !payload ||
        payload.type !== 'inline_action' ||
        typeof payload.actionId !== 'string' ||
        typeof payload.selectedText !== 'string' ||
        typeof payload.label !== 'string'
      ) {
        console.warn('[SprkChat] Received malformed inline_action event, ignoring:', payload);
        return;
      }

      // Build a user message that carries the action intent and selected text.
      // Format: "[{Label}] {selectedText}"
      // Example: "[Summarize] The court held that reasonable doubt requires..."
      const messageText = `[${payload.label}] ${payload.selectedText}`;

      // Route to the active chat session. handleSend guards "session not ready"
      // and "streaming in progress" internally (returns early without side-effects).
      handleSend(messageText);

      console.debug(
        `[SprkChat] Inline action received — actionId: "${payload.actionId}", actionType: "${payload.actionType}", textLength: ${payload.selectedText.length}`
      );
    };

    channel.addEventListener('message', handleMessage);

    return () => {
      channel.removeEventListener('message', handleMessage);
      channel.close();
    };
    // handleSend is stable (useCallback on session, isStreaming, addMessage, startStream).
    // Re-subscribe only when handleSend changes (i.e. when the underlying session changes).
  }, [handleSend]);

  // Handle playbook chip selection — switches context to the selected playbook
  const handlePlaybookChipClick = React.useCallback(
    (pb: IPlaybookOption) => {
      switchContext(documentId, pb.id, hostContext);
      if (onPlaybookChange) {
        onPlaybookChange(pb.id);
      }
    },
    [switchContext, documentId, hostContext, onPlaybookChange]
  );

  // ── Task R2-057: Word export error/success handlers ──────────────────────
  // Uses the existing Fluent v9 Toaster for user feedback (ADR-021).
  const handleExportWordError = React.useCallback(
    (errorMessage: string) => {
      dispatchToast(
        React.createElement(
          Toast,
          null,
          React.createElement(ToastTitle, null, 'Export to Word failed'),
          React.createElement(ToastBody, null, errorMessage)
        ),
        { intent: 'error' }
      );
    },
    [dispatchToast]
  );

  const handleExportWordSuccess = React.useCallback(
    (_wordOnlineUrl: string) => {
      dispatchToast(
        React.createElement(Toast, null, React.createElement(ToastTitle, null, 'Document opened in Word Online')),
        { intent: 'success' }
      );
    },
    [dispatchToast]
  );

  const displayError = sessionError || streamError || editorRefineError;
  const showPredefinedPrompts = messages.length === 0 && predefinedPrompts.length > 0 && !isSessionLoading;
  const showPlaybookChips = messages.length === 0 && allPlaybooks.length > 1 && !isSessionLoading;

  return (
    <div
      ref={rootContainerRef}
      className={className ? `${styles.root} ${className}` : styles.root}
      data-testid="sprkchat-root"
      onDragEnter={handleRootDragEnter}
      onDragOver={handleRootDragOver}
      onDragLeave={handleRootDragLeave}
      onDrop={handleRootDrop}
    >
      {/* Context selector bar */}
      {(documents.length > 0 || allPlaybooks.length > 0) && (
        <SprkChatContextSelector
          selectedDocumentId={documentId}
          selectedPlaybookId={playbookId}
          documents={documents}
          playbooks={allPlaybooks}
          onDocumentChange={handleDocumentChange}
          onPlaybookChange={handlePlaybookChange}
          disabled={isStreaming || isEditorRefining}
          additionalDocumentIds={additionalDocumentIds}
          onAdditionalDocumentsChange={handleAdditionalDocumentsChange}
        />
      )}

      {/* Error banner */}
      {displayError && (
        <div className={styles.errorBanner} role="alert" data-testid="chat-error-banner">
          {displayError.message}
        </div>
      )}

      {/* Message list */}
      <div
        className={styles.messageList}
        ref={messageListRef}
        role="list"
        aria-label="Chat messages"
        data-testid="chat-message-list"
      >
        {isSessionLoading && messages.length === 0 && (
          <div className={styles.loadingContainer}>
            <Spinner label="Loading chat..." />
          </div>
        )}

        {showPlaybookChips && (
          <div className={styles.playbookChips} data-testid="playbook-chips">
            {allPlaybooks
              .filter(pb => pb.id !== playbookId)
              .map(pb => (
                <Button
                  key={pb.id}
                  appearance="outline"
                  size="small"
                  onClick={() => handlePlaybookChipClick(pb)}
                  disabled={isStreaming || !session}
                  title={pb.description || pb.name}
                >
                  {pb.name}
                </Button>
              ))}
          </div>
        )}

        {showPredefinedPrompts && (
          <SprkChatPredefinedPrompts
            prompts={predefinedPrompts}
            onSelect={handlePromptSelect}
            disabled={isStreaming || !session}
          />
        )}

        {!isSessionLoading && messages.length === 0 && !showPredefinedPrompts && (
          <div className={styles.emptyState}>
            <Text size={300}>No messages yet</Text>
            <Text size={200}>Send a message to start the conversation</Text>
          </div>
        )}

        {messages.map((msg, index) => {
          // Citations apply to the last assistant message (the one that was just streamed).
          // They are cleared on each new startStream call, so they always correspond
          // to the most recent response.
          const isLastAssistant = index === messages.length - 1 && msg.role === 'Assistant';
          const messageCitations = isLastAssistant && streamCitations.length > 0 ? streamCitations : undefined;

          // Build plan preview callbacks — only meaningful for plan_preview messages.
          // Capture the index so each plan card targets the correct message position
          // (task 072 will use this index when updating step execution state).
          const isPlanPreview = msg.metadata?.responseType === 'plan_preview';
          const isDocumentStatus = msg.metadata?.responseType === 'document_status';

          // ── FR-14: Merge persistence state into document_status messages ────
          // The persistence state is tracked locally in documentPersistenceState map
          // (not in useChatSession messages) to avoid adding mutation methods.
          // Merge it into the message before passing to SprkChatMessage.
          let effectiveMessage = msg;
          if (isDocumentStatus) {
            const docMsg = msg as IDocumentStatusChatMessage;
            if (docMsg.documentStatus) {
              const persState = documentPersistenceState.get(docMsg.documentStatus.documentId);
              if (persState) {
                effectiveMessage = {
                  ...msg,
                  documentStatus: {
                    ...docMsg.documentStatus,
                    persistenceState: persState.state,
                    savedFileUrl: persState.savedFileUrl,
                  },
                } as IDocumentStatusChatMessage;
              }
            }
          }

          const msgProps: ISprkChatMessageExtendedProps = {
            message: effectiveMessage,
            isStreaming: isStreaming && isLastAssistant,
            citations: messageCitations,
            // Phase 2D: wire Insert button on all completed assistant messages.
            // SprkChat dispatches document_insert BroadcastChannel event.
            // Only pass onInsert for assistant messages to prevent button showing on user messages.
            ...(msg.role === 'Assistant' && {
              onInsert: handleInsert,
            }),
            ...(isPlanPreview && {
              onProceed: () => handlePlanProceed(index),
              onCancel: () => handlePlanCancel(index),
              onEditPlan: handlePlanEditMessage,
              isPlanExecuting: isPlanApproving,
              onCancelExecution: () => handleCancelExecution(index),
            }),
            // FR-14: Wire save-to-matter-files action on document_status messages.
            // Button is only shown when hasContainerId is true (containerId available).
            ...(isDocumentStatus && {
              onSaveToMatterFiles: handleSaveToMatterFiles,
              hasContainerId,
            }),
          };

          return <SprkChatMessage key={`msg-${index}`} {...msgProps} />;
        })}

        {/* Typing indicator — visible between typing_start and first token arrival */}
        {isTyping && !streamedContent && <SprkChatTypingIndicator />}

        {/* Follow-up suggestions shown after the latest assistant response */}
        {suggestions.length > 0 && (
          <SprkChatSuggestions
            suggestions={suggestions}
            onSelect={handleSuggestionSelect}
            visible={!isStreaming && suggestions.length > 0}
          />
        )}

        {/* Highlight-and-refine floating toolbar (local DOM selection + cross-pane bridge selection) */}
        <SprkChatHighlightRefine
          contentRef={highlightContainerRef}
          onRefine={handleRefine}
          isRefining={isStreaming || isEditorRefining}
          crossPaneSelection={crossPaneSelection}
        />

        {/* FR-13: Document upload overlay — visible when dragging files over the chat area.
            SprkChatUploadZone handles its own drag validation, upload progress, and file drop.
            On success, handleUploadComplete inserts a document_status message into the chat.
            Disabled when no active session exists. */}
        {isDragging && session && (
          <SprkChatUploadZone
            sessionId={session.sessionId}
            apiBaseUrl={apiBaseUrl}
            accessToken={accessToken}
            onUploadComplete={handleUploadComplete}
            onUploadError={handleUploadError}
            disabled={!session}
          />
        )}
      </div>

      {/* Quick-action chips — shown above input when analysisId context provides inline actions.
           Hidden automatically by QuickActionChips when pane is narrower than 350px (NFR-04).
           Chips only appear when at least one inlineAction is returned by the context mapping
           endpoint. Clicking a chip sends "[Quick Action: {label}]" as a user message. */}
      {quickChipActions.length > 0 && (
        <QuickActionChips
          actions={quickChipActions}
          onChipClick={handleChipAction}
          containerRef={rootContainerRef as React.RefObject<HTMLElement>}
          disabled={isStreaming || !session || isSessionLoading}
        />
      )}

      {/* Stop generating button — visible during active streaming */}
      {isStreaming && (
        <div className={styles.stopGeneratingContainer} data-testid="stop-generating-container">
          <Button
            appearance="subtle"
            size="small"
            icon={React.createElement(StopRegular)}
            onClick={cancelStream}
            data-testid="stop-generating-button"
          >
            Stop generating
          </Button>
        </div>
      )}

      {/* Input toolbar — export actions above the input area (task R2-057) */}
      <div className={styles.inputToolbar} data-testid="chat-input-toolbar">
        <SprkChatExportWord
          sessionId={session?.sessionId ?? null}
          messages={messages}
          apiBaseUrl={apiBaseUrl}
          accessToken={accessToken}
          onError={handleExportWordError}
          onSuccess={handleExportWordSuccess}
        />
      </div>

      {/* Input area */}
      <SprkChatInput
        onSend={handleSend}
        disabled={isStreaming || !session || isSessionLoading}
        maxCharCount={maxCharCount}
        dynamicSlashCommands={dynamicSlashCommands}
      />

      {/* Task R2-039: HITL Action Confirmation Dialog — shown when an action requires user confirmation */}
      <ActionConfirmationDialog
        pendingAction={pendingAction}
        onConfirm={handleActionConfirm}
        onCancel={handleActionCancel}
        isConfirming={isConfirmingAction}
      />

      {/* Task R2-039: Fluent v9 Toaster for autonomous action success/error feedback (ADR-021) */}
      <Toaster toasterId={toasterId} position="top-end" />
    </div>
  );
};

export default SprkChat;
