export { FilePreviewDialog } from './FilePreviewDialog';
export type { IFilePreviewDialogProps } from './FilePreviewDialog';
export type { IFilePreviewServices, IOpenLinksResponse } from './filePreviewTypes';

// RichFilePreviewDialog — 2-column document preview with prev/next nav, 3-dot menu,
// metadata pane. Promoted from SemanticSearchControl PCF (matter-ui-r1).
// New consumers should prefer this over the simpler `FilePreviewDialog` above.
export { RichFilePreviewDialog } from './RichFilePreviewDialog';
export type {
  IFilePreviewDialogProps as IRichFilePreviewDialogProps,
  IFilePreviewDialogSummary,
} from './RichFilePreviewDialog';

// RichFilePreview — extracted renderer core (R5 task 013 D2-08). Hosts the
// title-bar + 2-column body grid + metadata pane + Prev/Next nav + 3-dot menu
// without the modal Dialog envelope. Non-modal consumers (Context-pane
// FilePreviewContextWidget, Workspace DocumentViewerWidget) mount this
// directly; the modal `RichFilePreviewDialog` above also composes this
// renderer.
export { RichFilePreview, DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS } from './RichFilePreview';
export type { IRichFilePreviewProps } from './RichFilePreview';
