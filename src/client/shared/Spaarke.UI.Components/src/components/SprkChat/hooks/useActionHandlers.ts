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

import { useCallback, useState, useRef } from "react";
import type { IChatAction, ChatActionCategory, IHostContext } from "../types";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Write mode preference — "stream" for streaming insert, "diff" for diff compare view. */
export type WriteMode = "stream" | "diff";

/** localStorage key for persisting the user's write mode preference. */
const WRITE_MODE_STORAGE_KEY = "sprk-chat-write-mode";

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
export type ActionHandler = (
    action: IChatAction,
    context: ActionHandlerContext
) => Promise<void>;

/**
 * Registry mapping action categories (and optionally action IDs) to handler functions.
 */
export type ActionHandlerMap = Record<string, ActionHandler>;

// ─────────────────────────────────────────────────────────────────────────────
// Hook Options
// ─────────────────────────────────────────────────────────────────────────────

export interface UseActionHandlersOptions extends ActionHandlerContext {}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Result
// ─────────────────────────────────────────────────────────────────────────────

export interface IUseActionHandlersResult {
    /** Dispatch an action to the appropriate handler. */
    handleAction: (action: IChatAction) => Promise<void>;
    /** Current write mode preference ("stream" or "diff"). */
    writeMode: WriteMode;
    /** Whether a handler is currently executing (e.g., playbook switch API call). */
    isHandling: boolean;
}

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
const handleSwitchPlaybook: ActionHandler = async (action, context) => {
    const { sessionId, switchPlaybook, documentId, refetchActions, onError } = context;

    if (!sessionId) {
        onError("No active session. Please start a conversation first.");
        return;
    }

    // The playbook ID is either in the action's id (for playbook-category actions
    // where each action represents a specific playbook) or could be embedded in
    // action metadata. By convention, playbook actions use the playbookId as
    // the action id.
    const playbookId = action.id;
    if (!playbookId) {
        onError("Invalid playbook action — no playbook ID found.");
        return;
    }

    try {
        // Delegate to useChatSession.switchContext which handles the PATCH API call
        await switchPlaybook(documentId, playbookId);
        // Playbook switch changes capabilities — invalidate action menu cache
        refetchActions();
    } catch (err: unknown) {
        const message =
            err instanceof Error
                ? err.message
                : "Failed to switch playbook. Please try again.";
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
const handleChangeMode = (
    currentMode: WriteMode,
    setWriteMode: (mode: WriteMode) => void
): void => {
    const newMode: WriteMode = currentMode === "stream" ? "diff" : "stream";
    setWriteMode(newMode);

    // Persist to localStorage for cross-session continuity
    try {
        localStorage.setItem(WRITE_MODE_STORAGE_KEY, newMode);
    } catch {
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
const handleReanalyze: ActionHandler = async (_action, context) => {
    context.sendMessage("Rerun analysis");
};

/**
 * Summarize handler.
 *
 * Sends a "Summarize this document" chat message via the existing sendMessage
 * flow. The AI agent processes the message normally through the chat pipeline.
 */
const handleSummarize: ActionHandler = async (_action, context) => {
    context.sendMessage("Summarize this document");
};

/**
 * Search handler.
 *
 * Sets the chat input value to a search prompt prefix (e.g., "Search for: ")
 * and focuses the input. The user completes the search query and sends normally.
 */
const handleSearch: ActionHandler = async (_action, context) => {
    context.setInputValue("Search for: ");
};

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Reads the initial write mode from localStorage, defaulting to "stream".
 */
function getInitialWriteMode(): WriteMode {
    try {
        const stored = localStorage.getItem(WRITE_MODE_STORAGE_KEY);
        if (stored === "stream" || stored === "diff") {
            return stored;
        }
    } catch {
        // localStorage unavailable — use default
    }
    return "stream";
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
export function useActionHandlers(
    options: UseActionHandlersOptions
): IUseActionHandlersResult {
    const [writeMode, setWriteMode] = useState<WriteMode>(getInitialWriteMode);
    const [isHandling, setIsHandling] = useState<boolean>(false);

    // Keep a stable ref to the options to avoid re-creating the handleAction callback
    // on every render while still accessing the latest values.
    const optionsRef = useRef<UseActionHandlersOptions>(options);
    optionsRef.current = options;

    /**
     * Dispatches to the appropriate handler based on action category and ID.
     *
     * Dispatch priority:
     * 1. Exact match on action.id (for specific actions like "reanalyze")
     * 2. Category-level match (for categories like "playbooks" where any action triggers the same handler)
     */
    const handleAction = useCallback(
        async (action: IChatAction): Promise<void> => {
            const ctx = optionsRef.current;

            setIsHandling(true);
            try {
                // Dispatch by action ID first, then by category
                switch (action.category as ChatActionCategory) {
                    case "playbooks":
                        await handleSwitchPlaybook(action, ctx);
                        break;

                    case "settings":
                        if (action.id === "change_mode") {
                            handleChangeMode(writeMode, setWriteMode);
                        }
                        break;

                    case "actions":
                        if (action.id === "reanalyze") {
                            await handleReanalyze(action, ctx);
                        } else if (action.id === "summarize") {
                            await handleSummarize(action, ctx);
                        } else {
                            // Unknown action in "actions" category — send as chat command
                            ctx.sendMessage(`/${action.id}`);
                        }
                        break;

                    case "search":
                        await handleSearch(action, ctx);
                        break;

                    default:
                        // Unknown category — send as chat command as fallback
                        ctx.sendMessage(`/${action.id}`);
                        break;
                }
            } finally {
                setIsHandling(false);
            }
        },
        [writeMode]
    );

    return {
        handleAction,
        writeMode,
        isHandling,
    };
}
