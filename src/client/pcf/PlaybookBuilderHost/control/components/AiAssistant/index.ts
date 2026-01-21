/**
 * AI Assistant Components - Barrel Export
 *
 * @version 1.8.0
 */

export { AiAssistantModal, type AiAssistantModalProps } from './AiAssistantModal';
export { ChatHistory, type ChatHistoryProps } from './ChatHistory';
export { ChatInput, type ChatInputProps } from './ChatInput';
export { OperationFeedback, type OperationFeedbackProps } from './OperationFeedback';
export {
  TestOptionsDialog,
  type TestOptionsDialogProps,
  type TestOptions,
  type TestMode,
} from './TestOptionsDialog';
export { TestProgressView, type TestProgressViewProps } from './TestProgressView';
export { TestResultPreview, type TestResultPreviewProps } from './TestResultPreview';
export { ErrorDisplay, type ErrorDisplayProps, type AiBuilderError, type ErrorSeverity } from './ErrorDisplay';
export { TypingIndicator, type TypingIndicatorProps } from './TypingIndicator';
export { ClarificationOptions, type ClarificationOptionsProps } from './ClarificationOptions';

// Slash command system (Claude Code-like experience)
export { CommandPalette, type CommandPaletteProps } from './CommandPalette';
export { SuggestionBar, type SuggestionBarProps } from './SuggestionBar';
export {
  COMMANDS,
  filterCommands,
  parseSlashCommand,
  findCommand,
  CATEGORY_LABELS,
  CATEGORY_ORDER,
  type SlashCommand,
} from './commands';
