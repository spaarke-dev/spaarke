/**
 * SprkChat - Reusable chat component for @spaarke/ui-components
 *
 * Exports the main SprkChat component and all supporting types.
 *
 * @see ADR-012 - Shared Component Library
 */

// Main component
export { SprkChat } from './SprkChat';
export { SprkChatMessage } from './SprkChatMessage';
export type { ISprkChatMessageExtendedProps } from './SprkChatMessage';

// SprkChatMessageRenderer - Structured response card renderer (Phase 2E)
export { SprkChatMessageRenderer } from './SprkChatMessageRenderer';
export type {
  ISprkChatMessageRendererProps,
  StructuredResponseData,
  IMarkdownResponse,
  ICitationsResponse,
  ICitationRef,
  IDiffResponse,
  IEntityCardResponse,
  IEntityCardField,
  IActionConfirmationResponse,
} from './SprkChatMessageRenderer';

// PlanPreviewCard - Plan execution gate with step progress (Phase 2F)
export { PlanPreviewCard } from './PlanPreviewCard';
export type { PlanPreviewCardProps, PlanStep, PlanStepStatus } from './PlanPreviewCard';
export { SprkChatInput } from './SprkChatInput';
export { SprkChatContextSelector } from './SprkChatContextSelector';
export { SprkChatPredefinedPrompts } from './SprkChatPredefinedPrompts';
export { SprkChatHighlightRefine } from './SprkChatHighlightRefine';
export { SprkChatSuggestions } from './SprkChatSuggestions';
export { SprkChatCitationPopover, CitationMarker } from './SprkChatCitationPopover';
export { QuickActionChips } from './QuickActionChips';

// QuickActionChips types
export type { IQuickActionChipsProps } from './QuickActionChips';

// Upload zone (Phase 3E: drag-and-drop document upload)
export { SprkChatUploadZone } from './SprkChatUploadZone';
export type { ISprkChatUploadZoneProps, UploadedDocument } from './SprkChatUploadZone';

// Document upload status (Phase 3E: upload processing feedback)
export { SprkChatDocumentStatus } from './SprkChatDocumentStatus';

// Word export button (Phase 3E: Open in Word action)
export { SprkChatExportWord } from './SprkChatExportWord';
export type { ISprkChatExportWordProps } from './SprkChatExportWord';

// Hooks
export { useSseStream } from './hooks/useSseStream';
export { useChatSession } from './hooks/useChatSession';
export { useChatPlaybooks } from './hooks/useChatPlaybooks';

// Hooks (cross-pane selection)
export { useSelectionListener } from './hooks/useSelectionListener';

// Types
export type {
  ISprkChatProps,
  IChatMessage,
  IChatMessageMetadata,
  IChatMessagePlanStep,
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
  CitationSourceType,
  ICitation,
  ICitationMarkerProps,
  ISprkChatCitationPopoverProps,
  IChatSseEventData,
  ICitationSseItem,
  IQuickAction,
  IRefineRequest,
  IDocumentInsertEvent,
  IDocumentStreamSseEvent,
  DocumentProcessingStatus,
  IDocumentStatusMessage,
  IDocumentStatusChatMessage,
  ISprkChatDocumentStatusProps,
} from './types';
export { CROSS_PANE_SELECTION_MAX_PREVIEW, DEFAULT_QUICK_ACTIONS, DOCUMENT_PROCESSING_TIMEOUT_MS } from './types';
export type { IUseChatPlaybooksResult } from './hooks/useChatPlaybooks';
export type { UseSelectionListenerOptions, IUseSelectionListenerResult } from './hooks/useSelectionListener';

// Analysis context mapping hook (active when SprkChat opened from AnalysisWorkspace)
export { useChatContextMapping } from './hooks/useChatContextMapping';
export type {
  IUseChatContextMappingResult,
  IAnalysisChatContextResponse,
  IInlineActionInfo,
  IAnalysisPlaybookInfo,
  IAnalysisKnowledgeSourceInfo,
  IAnalysisContextInfo,
} from './hooks/useChatContextMapping';
