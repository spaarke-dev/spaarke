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
import { useCallback, useState, useRef } from 'react';
/** localStorage key for persisting the user's write mode preference. */
const WRITE_MODE_STORAGE_KEY = 'sprk-chat-write-mode';
// ─────────────────────────────────────────────────────────────────────────────
// Handler Implementations
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Switch Playbook handler.
 *
 * Calls PATCH /api/ai/chat/sessions/{sessionId}/context with the new playbookId.
 * On success: updates SprkChat state via switchPlaybook callback, invalidates
 * action menu cache. On failure: reports error via onError callback.
 */
const handleSwitchPlaybook = async (action, context) => {
    const { sessionId, switchPlaybook, documentId, refetchActions, onError } = context;
    if (!sessionId) {
        onError('No active session. Please start a conversation first.');
        return;
    }
    // The playbook ID is either in the action's id (for playbook-category actions
    // where each action represents a specific playbook) or could be embedded in
    // action metadata. By convention, playbook actions use the playbookId as
    // the action id.
    const playbookId = action.id;
    if (!playbookId) {
        onError('Invalid playbook action — no playbook ID found.');
        return;
    }
    try {
        // Delegate to useChatSession.switchContext which handles the PATCH API call
        await switchPlaybook(documentId, playbookId);
        // Playbook switch changes capabilities — invalidate action menu cache
        refetchActions();
    }
    catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to switch playbook. Please try again.';
        onError(message);
    }
};
/**
 * Change Mode handler.
 *
 * Toggles the write mode between "stream" and "diff". Persists the preference
 * to localStorage under the `sprk-chat-write-mode` key. No API call needed —
 * purely client-side preference that affects how Package B (streaming) and
 * Package F (diff) render AI outputs.
 *
 * @returns The new write mode value (resolved via the setWriteMode callback).
 */
const handleChangeMode = (currentMode, setWriteMode) => {
    const newMode = currentMode === 'stream' ? 'diff' : 'stream';
    setWriteMode(newMode);
    // Persist to localStorage for cross-session continuity
    try {
        localStorage.setItem(WRITE_MODE_STORAGE_KEY, newMode);
    }
    catch {
        // localStorage may be unavailable in some environments — fail silently
    }
};
/**
 * Re-analyze handler.
 *
 * Sends a "Rerun analysis" chat message via the existing sendMessage flow.
 * The server-side AnalysisExecutionTools (task 078) handles the message through
 * the chat pipeline with progress tracking, document_replace SSE events, and
 * undo stack management.
 */
const handleReanalyze = async (_action, context) => {
    context.sendMessage('Rerun analysis');
};
/**
 * Summarize handler.
 *
 * Sends a "Summarize this document" chat message via the existing sendMessage
 * flow. The AI agent processes the message normally through the chat pipeline.
 */
const handleSummarize = async (_action, context) => {
    context.sendMessage('Summarize this document');
};
/**
 * Search handler.
 *
 * Sets the chat input value to a search prompt prefix (e.g., "Search for: ")
 * and focuses the input. The user completes the search query and sends normally.
 */
const handleSearch = async (_action, context) => {
    context.setInputValue('Search for: ');
};
// ─────────────────────────────────────────────────────────────────────────────
// dialog_open SSE Event Handler (Task R2-039)
// ─────────────────────────────────────────────────────────────────────────────
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
export function openCodePageDialog(payload) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = window.Xrm;
    if (!xrm?.Navigation?.navigateTo) {
        console.warn('[useActionHandlers] Xrm.Navigation.navigateTo not available — dialog_open event ignored.', 'targetPage:', payload.targetPage, 'Environment may be Storybook, dev, or non-Dataverse context.');
        return;
    }
    // Serialize prePopulateFields as URL query string per ADR-006
    const data = new URLSearchParams(payload.prePopulateFields).toString();
    xrm.Navigation.navigateTo({
        pageType: 'webresource',
        webresourceName: payload.targetPage,
        data,
    }, {
        target: 2, // dialog
        width: { value: payload.width ?? 85, unit: '%' },
        height: { value: payload.height ?? 85, unit: '%' },
    }).catch((err) => {
        console.error('[useActionHandlers] Failed to open Code Page dialog:', err);
    });
}
// ─────────────────────────────────────────────────────────────────────────────
// navigate SSE Event Handler (Task R2-052)
// ─────────────────────────────────────────────────────────────────────────────
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
export function navigateToTarget(payload) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = window.Xrm;
    if (!xrm?.Navigation) {
        console.warn('[useActionHandlers] Xrm.Navigation not available — navigate event ignored.', 'targetPage:', payload.targetPage, 'url:', payload.url, 'Environment may be Storybook, dev, or non-Dataverse context.');
        return;
    }
    // If an explicit URL is provided, open it directly
    if (payload.url) {
        xrm.Navigation.openUrl(payload.url);
        return;
    }
    // If a Code Page target is provided, open via navigateTo (ADR-006)
    if (payload.targetPage) {
        const data = new URLSearchParams(payload.parameters).toString();
        xrm.Navigation.navigateTo({
            pageType: 'webresource',
            webresourceName: payload.targetPage,
            data,
        }, {
            target: 1, // inline navigation (not dialog)
        }).catch((err) => {
            console.error('[useActionHandlers] Failed to navigate to Code Page:', err);
        });
        return;
    }
    console.warn('[useActionHandlers] navigate event has neither url nor targetPage — ignoring.', 'parameters:', payload.parameters);
}
/**
 * Dispatches a confirmed action to the BFF API.
 *
 * Called after the user clicks Confirm in the ActionConfirmationDialog.
 * Sends POST /api/ai/chat/sessions/{sessionId}/actions/{actionId}/confirm.
 */
export async function dispatchConfirmedAction(pendingAction, apiBaseUrl, accessToken) {
    const baseUrl = apiBaseUrl.replace(/\/+$/, '');
    const url = `${baseUrl}/api/ai/chat/sessions/${pendingAction.sessionId}/actions/${pendingAction.actionId}/confirm`;
    try {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${accessToken}`,
            },
            body: JSON.stringify({
                actionId: pendingAction.actionId,
                parameters: pendingAction.parameters,
            }),
        });
        if (!response.ok) {
            const errorText = await response.text();
            return {
                success: false,
                message: `Action failed (${response.status}): ${errorText}`,
            };
        }
        const result = await response.json().catch(() => ({}));
        return {
            success: true,
            message: result.message || `${pendingAction.actionName} completed successfully.`,
        };
    }
    catch (err) {
        const message = err instanceof Error ? err.message : 'Unknown error';
        return { success: false, message: `Action dispatch failed: ${message}` };
    }
}
// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Reads the initial write mode from localStorage, defaulting to "stream".
 */
function getInitialWriteMode() {
    try {
        const stored = localStorage.getItem(WRITE_MODE_STORAGE_KEY);
        if (stored === 'stream' || stored === 'diff') {
            return stored;
        }
    }
    catch {
        // localStorage unavailable — use default
    }
    return 'stream';
}
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
export function useActionHandlers(options) {
    const [writeMode, setWriteMode] = useState(getInitialWriteMode);
    const [isHandling, setIsHandling] = useState(false);
    // Keep a stable ref to the options to avoid re-creating the handleAction callback
    // on every render while still accessing the latest values.
    const optionsRef = useRef(options);
    optionsRef.current = options;
    /**
     * Dispatches to the appropriate handler based on action category and ID.
     *
     * Dispatch priority:
     * 1. Exact match on action.id (for specific actions like "reanalyze")
     * 2. Category-level match (for categories like "playbooks" where any action triggers the same handler)
     */
    const handleAction = useCallback(async (action) => {
        const ctx = optionsRef.current;
        setIsHandling(true);
        try {
            // Dispatch by action ID first, then by category
            switch (action.category) {
                case 'playbooks':
                    await handleSwitchPlaybook(action, ctx);
                    break;
                case 'settings':
                    if (action.id === 'change_mode') {
                        handleChangeMode(writeMode, setWriteMode);
                    }
                    break;
                case 'actions':
                    if (action.id === 'reanalyze') {
                        await handleReanalyze(action, ctx);
                    }
                    else if (action.id === 'summarize') {
                        await handleSummarize(action, ctx);
                    }
                    else {
                        // Unknown action in "actions" category — send as chat command
                        ctx.sendMessage(`/${action.id}`);
                    }
                    break;
                case 'search':
                    await handleSearch(action, ctx);
                    break;
                default:
                    // Unknown category — send as chat command as fallback
                    ctx.sendMessage(`/${action.id}`);
                    break;
            }
        }
        finally {
            setIsHandling(false);
        }
    }, [writeMode]);
    return {
        handleAction,
        writeMode,
        isHandling,
    };
}
//# sourceMappingURL=useActionHandlers.js.map