/**
 * @spaarke/ai-outputs — Chat History barrel export
 *
 * The chat history panel shows prior AI chat sessions with search and resume.
 * Components added in Wave 3 (task 032).
 *
 * @see ADR-012 — Shared component library (all public exports here)
 * @see ADR-021 — Fluent UI v9, dark mode via FluentProvider
 */

// Types — ChatSession (UI-focused), ChatHistoryPanelProps, ChatSessionCardProps
export type {
  ChatSession,
  ChatHistoryPanelProps,
  ChatSessionCardProps,
} from "./ChatHistoryPanel.types";

// Hook — client-side session filtering with 200ms debounce
export { useChatHistoryFilter } from "./useChatHistoryFilter";

// Components — panel container and individual session card
export { ChatHistoryPanel } from "./ChatHistoryPanel";
export { ChatSessionCard } from "./ChatSessionCard";

// Note: ChatMessage and ChatSessionData (the BFF thread/messages data shape) are
// exported from the root package barrel via @spaarke/ai-outputs — no re-export
// needed here to avoid duplicate export conflicts.
