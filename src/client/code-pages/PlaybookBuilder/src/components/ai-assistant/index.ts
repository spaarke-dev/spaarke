/**
 * AI Assistant Components - Barrel Export
 *
 * Re-exports all AI Assistant components for convenient importing.
 *
 * @version 2.0.0 (Code Page migration)
 */

// Modal
export { AiAssistantModal } from "./AiAssistantModal";
export type { AiAssistantModalProps } from "./AiAssistantModal";

// Chat
export { ChatHistory } from "./ChatHistory";
export type { ChatHistoryProps } from "./ChatHistory";

export { ChatInput } from "./ChatInput";
export type { ChatInputProps } from "./ChatInput";

// Clarification
export { ClarificationOptions } from "./ClarificationOptions";
export type { ClarificationOptionsProps } from "./ClarificationOptions";

// Suggestions
export { SuggestionBar } from "./SuggestionBar";
export type { SuggestionBarProps } from "./SuggestionBar";

// Command Palette
export { CommandPalette } from "./CommandPalette";
export type { CommandPaletteProps } from "./CommandPalette";

// Commands
export {
    COMMANDS,
    NODE_TYPE_INFO,
    CATEGORY_LABELS,
    CATEGORY_ORDER,
    filterCommands,
    getCommandsByCategory,
    findCommand,
    parseSlashCommand,
} from "./commands";
export type { SlashCommand } from "./commands";

// Error Display
export { ErrorDisplay } from "./ErrorDisplay";
export type { ErrorDisplayProps, AiBuilderError, ErrorSeverity } from "./ErrorDisplay";

// Operation Feedback
export { OperationFeedback } from "./OperationFeedback";
export type { OperationFeedbackProps } from "./OperationFeedback";

// Typing Indicator
export { TypingIndicator } from "./TypingIndicator";
export type { TypingIndicatorProps } from "./TypingIndicator";

// Test Components
export { TestOptionsDialog } from "./TestOptionsDialog";
export type { TestOptionsDialogProps } from "./TestOptionsDialog";

export { TestProgressView } from "./TestProgressView";
export type { TestProgressViewProps } from "./TestProgressView";

export { TestResultPreview } from "./TestResultPreview";
export type { TestResultPreviewProps } from "./TestResultPreview";
