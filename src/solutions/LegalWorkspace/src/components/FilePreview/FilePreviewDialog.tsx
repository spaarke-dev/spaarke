/**
 * FilePreviewDialog.tsx
 * LegalWorkspace adapter for the shared FilePreviewDialog component.
 *
 * Maintains the simple interface (without `services` prop) expected by
 * DocumentCard and other local consumers, while injecting the environment-
 * specific service implementations required by the shared @spaarke/ui-components
 * version.
 */
import * as React from 'react';
import { FilePreviewDialog as SharedFilePreviewDialog } from '@spaarke/ui-components/components/FilePreview';
import type { IFilePreviewServices } from '@spaarke/ui-components/components/FilePreview/filePreviewTypes';
import { getDocumentPreviewUrl, getDocumentOpenLinks } from '../../services/DocumentApiService';
import { navigateToEntity } from '../../utils/navigation';
import { copyDocumentLink, setWorkspaceFlag } from './filePreviewService';

// ---------------------------------------------------------------------------
// Props (unchanged — consumers do NOT need to supply services)
// ---------------------------------------------------------------------------

export interface IFilePreviewDialogProps {
  open: boolean;
  documentId: string;
  documentName: string;
  onClose: () => void;
  /** Whether this document is currently in the user's workspace. */
  isInWorkspace?: boolean;
  /** Called when the workspace flag changes. */
  onWorkspaceFlagChanged?: (newFlag: boolean) => void;
}

// ---------------------------------------------------------------------------
// Service implementation (stable reference)
// ---------------------------------------------------------------------------

const filePreviewServices: IFilePreviewServices = {
  getDocumentPreviewUrl,
  getDocumentOpenLinks,
  navigateToEntity: (params) => navigateToEntity(params),
  copyDocumentLink,
  setWorkspaceFlag,
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FilePreviewDialog: React.FC<IFilePreviewDialogProps> = (props) => (
  <SharedFilePreviewDialog
    {...props}
    services={filePreviewServices}
  />
);

FilePreviewDialog.displayName = 'FilePreviewDialog';
