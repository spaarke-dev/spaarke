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

// Word export button removed (FR-08, task 025 — replaced by toolbar restructure).
// `SprkChatExportWord` is no longer exported from `@spaarke/ui-components`.
// The file remains in the tree as historical reference but is unreferenced.

// Hooks
// Note: useSseStream is exported from src/hooks/useSseStream (canonical) via src/hooks/index.ts.
// Re-exporting here would cause a duplicate named export when both src/hooks and src/components
// are barrel-exported from src/index.ts. Import from '@spaarke/ui-components' directly.
export { useChatSession } from './hooks/useChatSession';
export { useChatPlaybooks } from './hooks/useChatPlaybooks';

// FR-07: multi-file chat attachment hook (task 024)
export {
  useChatFileAttachment,
  MAX_ATTACHMENTS,
  MAX_FILE_BYTES,
  MAX_PDF_PAGES,
  ALLOWED_MIME_TYPES,
} from './hooks/useChatFileAttachment';
export type {
  ChatAttachment,
  AttachmentChip,
  AttachmentChipStatus,
  AttachmentError,
  AttachmentErrorReason,
  AttachmentExtractionErrorCallback,
  UseChatFileAttachmentOptions,
  IUseChatFileAttachmentResult,
} from './hooks/useChatFileAttachment';

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
  ISprkChatInputHandle,
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
  IAiPaneEvent,
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
