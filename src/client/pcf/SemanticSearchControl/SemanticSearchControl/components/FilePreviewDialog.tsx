/**
 * FilePreviewDialog — re-export shim.
 *
 * The rich 2-column document preview dialog was promoted to
 * `@spaarke/ui-components` as `RichFilePreviewDialog`. This file preserves
 * the PCF-local import path (`./FilePreviewDialog`) for sibling components
 * (ListView, ResultCard, SemanticSearchControl) while routing to the
 * canonical shared implementation.
 *
 * Future PCF code: import directly from
 *   `@spaarke/ui-components/dist/components/FilePreview/RichFilePreviewDialog`
 * and stop relying on this shim.
 */
export { RichFilePreviewDialog as FilePreviewDialog } from '@spaarke/ui-components/dist/components/FilePreview/RichFilePreviewDialog';
export type {
  IFilePreviewDialogProps,
  IFilePreviewDialogSummary,
} from '@spaarke/ui-components/dist/components/FilePreview/RichFilePreviewDialog';
