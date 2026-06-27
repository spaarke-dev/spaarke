/**
 * FilePreviewDialog — re-export shim.
 *
 * Routes to the canonical shared `RichFilePreviewDialog` in `@spaarke/ui-components`.
 * Preserves the existing import path (`./components/FilePreviewDialog`) for App.tsx.
 *
 * App.tsx must pass `documentId` (required by the rich version).
 */
export { RichFilePreviewDialog as FilePreviewDialog } from '@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog';
export type {
  IFilePreviewDialogProps,
  IFilePreviewDialogSummary,
} from '@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog';
