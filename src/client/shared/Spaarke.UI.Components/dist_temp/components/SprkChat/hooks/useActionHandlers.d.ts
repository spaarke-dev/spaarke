/**
 * useActionHandlers - Maps action menu selections to handler functions
 *
 * Provides a single `handleAction(action)` function that dispatches to the
 * appropriate handler based on the action's category and ID:
 *
 * - **Switch Playbook** (category: "playbooks"): Calls PATCH /sessions/{id}/context,
 *   updates SprkChat state, invalidates action menu cache.
 * - **Change Mode** (category: "settings", id: "change_mode"): Toggles write mode
 *   between "stream" and "diff", persists to localStorage.
 * - **Re-analyze** (category: "actions", id: "reanalyze"): Sends a chat message
 *   processed by server-side AnalysisExecutionTools.
 * - **Summarize** (category: "actions", id: "summarize"): Sends a chat message.
 * - **Search** (category: "search"): Focuses input with search prefix.
 *
 * @see ADR-012 - Shared Component Library (hook in @spaarke/ui-components)
 * @see ADR-021 - Fluent UI v9 for any UI feedback (toasts, status indicators)
 * @see spec-FR-11 - Actions governed by playbook capability declarations
 */
import type { IChatAction, IHostContext, IPendingAction, IDialogOpenPayload, INavigatePayload } from '../types';
/** Write mode preference — "stream" for streaming insert, "diff" for diff compare view. */
export type WriteMode = 'stream' | 'diff';
/**
 * Context required by action handlers to interact with SprkChat state and API.
 * Provided by the parent SprkChat component via the hook's options parameter.
 */
export interface ActionHandlerContext {
    /** Current chat session ID (required for API calls). */
    sessionId: string | undefined;
    /** Base URL for the BFF API (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net"). */
    apiBaseUrl: string;
    /** Bearer token for API authentication. */
    accessToken: string;
    /** Current document ID from the chat context. */
    documentId: string | undefined;
    /** Host context describing the embedding entity. */
    hostContext: IHostContext | undefined;
    /** Callback to send a chat message programmatically (uses the existing SSE send flow). */
    sendMessage: (message: string) => void;
    /**
     * Callback to switch the session's playbook context via PATCH /sessions/{id}/context.
     * This delegates to useChatSession.switchContext which handles the API call.
     */
    switchPlaybook: (documentId: string | undefined, playbookId: string) => Promise<void>;
    /** Callback to invalidate the action menu cache (refetch on next open). */
    refetchActions: () => void;
    /**
     * Callback to set the chat input value and focus it.
     * Used by the Search handler to pre-fill a search prefix.
     */
    setInputValue: (value: string) => void;
    /** Callback to report errors to the user (e.g., failed playbook switch). */
    onError: (message: string) => void;
}
/**
 * A handler function for a specific action type.
 * Receives the selected action and the handler context.
 */
export type ActionHandler = (action: IChatAction, context: ActionHandlerContext) => Promise<void>;
/**
 * Registry mapping action categories (and optionally action IDs) to handler functions.
 */
export type ActionHandlerMap = Record<string, ActionHandler>;
export interface UseActionHandlersOptions extends ActionHandlerContext {
}
export interface IUseActionHandlersResult {
    /** Dispatch an action to the appropriate handler. */
    handleAction: (action: IChatAction) => Promise<void>;
    /** Current write mode preference ("stream" or "diff"). */
    writeMode: WriteMode;
    /** Whether a handler is currently executing (e.g., playbook switch API call). */
    isHandling: boolean;
}
/**
 * Opens a Code Page dialog via Xrm.Navigation.navigateTo.
 *
 * Pre-populated field values are serialized as a URL query string and passed
 * via the `data` parameter. Only executes in Dataverse context where Xrm is
 * available; in non-Dataverse environments (Storybook, dev) logs a warning.
 *
 * @see ADR-006 — MUST use Xrm.Navigation.navigateTo with pageType="webresource"
 * @see dialog-patterns.md — Pattern 1: Opening a React Code Page Dialog
 */
export declare function openCodePageDialog(payload: IDialogOpenPayload): void;
/**
 * Navigates to a target page or URL via Xrm.Navigation.
 *
 * For Code Page targets (targetPage set): opens via navigateTo with pageType="webresource".
 * For external URLs (url set): opens via Xrm.Navigation.openUrl.
 * Parameters are passed as URL query string data.
 *
 * Only executes in Dataverse context where Xrm is available; in non-Dataverse
 * environments (Storybook, dev) logs a warning.
 *
 * @see ADR-006 — MUST use Xrm.Navigation.navigateTo with pageType="webresource"
 */
export declare function navigateToTarget(payload: INavigatePayload): void;
/**
 * Dispatches a confirmed action to the BFF API.
 *
 * Called after the user clicks Confirm in the ActionConfirmationDialog.
 * Sends POST /api/ai/chat/sessions/{sessionId}/actions/{actionId}/confirm.
 */
export declare function dispatchConfirmedAction(pendingAction: IPendingAction, apiBaseUrl: string, accessToken: string): Promise<{
    success: boolean;
    message: string;
}>;
/**
 * Hook that maps action menu selections to handler functions.
 *
 * Returns a `handleAction` dispatcher that routes each selected action to the
 * appropriate handler based on the action's category and ID.
 *
 * @param options - ActionHandlerContext providing access to SprkChat state and API
 * @returns handleAction dispatcher, current writeMode, and isHandling state
 *
 * @example
 * ```tsx
 * const { handleAction, writeMode, isHandling } = useActionHandlers({
 *   sessionId: session?.sessionId,
 *   apiBaseUrl,
 *   accessToken,
 *   documentId,
 *   hostContext,
 *   sendMessage: handleSend,
 *   switchPlaybook: (docId, pbId) => switchContext(docId, pbId, hostContext),
 *   refetchActions,
 *   setInputValue: (v) => { ... },
 *   onError: (msg) => setErrorMessage(msg),
 * });
 * ```
 */
export declare function useActionHandlers(options: UseActionHandlersOptions): IUseActionHandlersResult;
//# sourceMappingURL=useActionHandlers.d.ts.map