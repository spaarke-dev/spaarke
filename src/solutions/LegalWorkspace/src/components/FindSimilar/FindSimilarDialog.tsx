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
import { createXrmNavigationService } from '@spaarke/ui-components';
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

// Retired local `navigation.ts`'s `openRecordDialog`/`navigateToEntity` are
// gone (task 091). Both callback shapes below are called by the shared
// FindSimilar component ONLY with `action: 'openRecord'` + a defined
// `entityId` (see FindSimilarResultsStep.tsx's `handleOpenRecord`) — so this
// is a thin call-shape translation onto the canonical shared
// `xrmNavigationServiceAdapter`, not a reintroduced navigation helper
// (ADR-012). The factory is side-effect-free at construction time, so a
// single module-level instance is safe to share across both callbacks.
const navigationService = createXrmNavigationService();

const filePreviewServices: IFilePreviewServices = {
  getDocumentPreviewUrl,
  getDocumentOpenLinks,
  navigateToEntity: (params) => {
    navigationService.openRecord(params.entityName, params.entityId).catch((err) => {
      console.error('[FindSimilarDialog] openRecord failed:', err);
    });
  },
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
    (message: INavigationMessage) => {
      if (message.entityId) {
        navigationService.openRecord(message.entityName, message.entityId).catch((err) => {
          console.error('[FindSimilarDialog] openRecord failed:', err);
        });
      }
    },
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
