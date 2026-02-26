/**
 * SprkChat - Reusable chat component for @spaarke/ui-components
 *
 * Exports the main SprkChat component and all supporting types.
 *
 * @see ADR-012 - Shared Component Library
 */

// Main component
export { SprkChat } from "./SprkChat";
export { SprkChatMessage } from "./SprkChatMessage";
export { SprkChatInput } from "./SprkChatInput";
export { SprkChatContextSelector } from "./SprkChatContextSelector";
export { SprkChatPredefinedPrompts } from "./SprkChatPredefinedPrompts";
export { SprkChatHighlightRefine } from "./SprkChatHighlightRefine";
export { SprkChatSuggestions } from "./SprkChatSuggestions";
export { SprkChatCitationPopover, CitationMarker } from "./SprkChatCitationPopover";

// Hooks
export { useSseStream } from "./hooks/useSseStream";
export { useChatSession } from "./hooks/useChatSession";
export { useChatPlaybooks } from "./hooks/useChatPlaybooks";

// Hooks (cross-pane selection)
export { useSelectionListener } from "./hooks/useSelectionListener";

// Types
export type {
    ISprkChatProps,
    IChatMessage,
    IChatSession,
    IChatSseEvent,
    ISprkChatMessageProps,
    ISprkChatInputProps,
    ISprkChatContextSelectorProps,
    ISprkChatPredefinedPromptsProps,
    ISprkChatHighlightRefineProps,
    IUseSseStreamResult,
    IUseChatSessionResult,
    ChatMessageRole,
    ChatSseEventType,
    IDocumentOption,
    IPlaybookOption,
    IPredefinedPrompt,
    IHostContext,
    ICrossPaneSelection,
    ISprkChatSuggestionsProps,
    ICitation,
    ICitationMarkerProps,
    ISprkChatCitationPopoverProps,
    IChatSseEventData,
    ICitationSseItem,
    IQuickAction,
    IRefineRequest,
} from "./types";
export { CROSS_PANE_SELECTION_MAX_PREVIEW, DEFAULT_QUICK_ACTIONS } from "./types";
export type { IUseChatPlaybooksResult } from "./hooks/useChatPlaybooks";
export type {
    UseSelectionListenerOptions,
    IUseSelectionListenerResult,
} from "./hooks/useSelectionListener";
