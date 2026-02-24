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

// Hooks
export { useSseStream } from "./hooks/useSseStream";
export { useChatSession } from "./hooks/useChatSession";
export { useChatPlaybooks } from "./hooks/useChatPlaybooks";

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
} from "./types";
export type { IUseChatPlaybooksResult } from "./hooks/useChatPlaybooks";
