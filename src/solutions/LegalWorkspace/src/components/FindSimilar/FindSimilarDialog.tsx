/**
 * FindSimilarDialog.tsx
 * LegalWorkspace adapter for the shared FindSimilarDialog component.
 *
 * Maintains the simple `{ open, onClose }` interface expected by WorkspaceGrid
 * while injecting the environment-specific service dependencies required by the
 * shared @spaarke/ui-components version (authenticatedFetch, BFF base URL,
 * navigation, file preview services).
 */
import * as React from 'react';
import { FindSimilarDialog as SharedFindSimilarDialog } from '@spaarke/ui-components/components/FindSimilar';
import type { IFindSimilarServiceConfig, INavigationMessage } from '@spaarke/ui-components/components/FindSimilar/findSimilarTypes';
import type { IFilePreviewServices } from '@spaarke/ui-components/components/FilePreview/filePreviewTypes';
import { authenticatedFetch } from '../../services/authInit';
import { getBffBaseUrl } from '../../config/runtimeConfig';
import { navigateToEntity } from '../../utils/navigation';
import { getDocumentPreviewUrl, getDocumentOpenLinks } from '../../services/DocumentApiService';
import { copyDocumentLink, setWorkspaceFlag } from '../FilePreview/filePreviewService';

// ---------------------------------------------------------------------------
// Props (unchanged — consumers pass only open + onClose)
// ---------------------------------------------------------------------------

export interface IFindSimilarDialogProps {
  open: boolean;
  onClose: () => void;
}

// ---------------------------------------------------------------------------
// Service configuration singletons (stable references)
// ---------------------------------------------------------------------------

const findSimilarServiceConfig: IFindSimilarServiceConfig = {
  getBffBaseUrl,
  authenticatedFetch,
};

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

export const FindSimilarDialog: React.FC<IFindSimilarDialogProps> = ({
  open,
  onClose,
}) => {
  const handleNavigateToEntity = React.useCallback(
    (message: INavigationMessage) => navigateToEntity(message),
    [],
  );

  return (
    <SharedFindSimilarDialog
      open={open}
      onClose={onClose}
      serviceConfig={findSimilarServiceConfig}
      onNavigateToEntity={handleNavigateToEntity}
      filePreviewServices={filePreviewServices}
    />
  );
};

// Default export enables React.lazy() dynamic import for bundle-size optimization.
export default FindSimilarDialog;
