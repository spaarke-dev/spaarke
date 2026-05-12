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
// SprkChatMessageRenderer - Structured response card renderer (Phase 2E)
export { SprkChatMessageRenderer } from './SprkChatMessageRenderer';
// PlanPreviewCard - Plan execution gate with step progress (Phase 2F)
export { PlanPreviewCard } from './PlanPreviewCard';
export { SprkChatInput } from './SprkChatInput';
export { SprkChatContextSelector } from './SprkChatContextSelector';
export { SprkChatPredefinedPrompts } from './SprkChatPredefinedPrompts';
export { SprkChatHighlightRefine } from './SprkChatHighlightRefine';
export { SprkChatSuggestions } from './SprkChatSuggestions';
export { SprkChatCitationPopover, CitationMarker } from './SprkChatCitationPopover';
export { QuickActionChips } from './QuickActionChips';
// Upload zone (Phase 3E: drag-and-drop document upload)
export { SprkChatUploadZone } from './SprkChatUploadZone';
// Document upload status (Phase 3E: upload processing feedback)
export { SprkChatDocumentStatus } from './SprkChatDocumentStatus';
// Word export button (Phase 3E: Open in Word action)
export { SprkChatExportWord } from './SprkChatExportWord';
// Hooks
export { useSseStream } from './hooks/useSseStream';
export { useChatSession } from './hooks/useChatSession';
export { useChatPlaybooks } from './hooks/useChatPlaybooks';
// Hooks (cross-pane selection)
export { useSelectionListener } from './hooks/useSelectionListener';
export { CROSS_PANE_SELECTION_MAX_PREVIEW, DEFAULT_QUICK_ACTIONS, DOCUMENT_PROCESSING_TIMEOUT_MS } from './types';
// Analysis context mapping hook (active when SprkChat opened from AnalysisWorkspace)
export { useChatContextMapping } from './hooks/useChatContextMapping';
//# sourceMappingURL=index.js.map