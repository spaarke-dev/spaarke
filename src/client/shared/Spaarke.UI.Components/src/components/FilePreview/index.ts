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
