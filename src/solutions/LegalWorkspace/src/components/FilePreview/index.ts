/**
 * FilePreview barrel export.
 *
 * FilePreviewDialog is a local adapter that injects LegalWorkspace-specific
 * services into the shared @spaarke/ui-components FilePreviewDialog.
 * filePreviewService contains domain-specific Xrm/Dataverse operations.
 */
export { FilePreviewDialog } from './FilePreviewDialog';
export type { IFilePreviewDialogProps } from './FilePreviewDialog';
export { copyDocumentLink, setWorkspaceFlag } from './filePreviewService';
