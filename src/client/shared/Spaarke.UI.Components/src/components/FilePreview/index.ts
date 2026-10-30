export type { IFilePreviewServices, IOpenLinksResponse } from './filePreviewTypes';

// RichFilePreviewDialog — 2-column document preview with prev/next nav, 3-dot menu,
// metadata pane. Promoted from SemanticSearchControl PCF (matter-ui-r1). Composes
// the `PreviewModal`/`BrowseModal` `SprkModal` presets (task 060, P4 re-base) —
// the sole preview/browse surface now that the simpler `@deprecated
// FilePreviewDialog` this comment used to reference has been deleted (its
// sole consumer, `FindSimilarResultsStep`, migrated onto this component).
export { RichFilePreviewDialog } from './RichFilePreviewDialog';
export type { IFilePreviewDialogProps, IFilePreviewDialogSummary } from './RichFilePreviewDialog';

// RichFilePreview — extracted renderer core (R5 task 013 D2-08). Hosts the
// title-bar + 2-column body grid + metadata pane + Prev/Next nav + 3-dot menu
// without the modal Dialog envelope. Non-modal consumers (Context-pane
// FilePreviewContextWidget, Workspace DocumentViewerWidget) mount this
// directly; the modal `RichFilePreviewDialog` above also composes this
// renderer.
export { RichFilePreview, DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS } from './RichFilePreview';
export type { IRichFilePreviewProps } from './RichFilePreview';

// PdfHighlightViewer — renders host-supplied PDF bytes page by page (lazy pdfjs-dist) and boxes an extracted
// line and field with the F-4 highlight and scroll behaviour (ebilling-r1 task 108). Unlike RichFilePreview's
// cross-origin iframe it owns the page canvases, so it can carry an overlay. Context-agnostic: the host
// supplies the bytes.
export { PdfHighlightViewer } from './PdfHighlightViewer';
export type { IPdfHighlightViewerProps, IPdfHighlight, PdfBytes } from './PdfHighlightViewer';
export type { IPdfNormalisedBox } from './pdfHighlightGeometry';
