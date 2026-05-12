/**
 * FindSimilarDialog.tsx
 * Two-step wizard dialog for "Find Similar".
 *
 * Uses WizardShell with 2 steps:
 *   0 — Upload file(s)   (FileUploadZone + UploadedFileList — from shared FileUpload)
 *   1 — Results           (FindSimilarResultsStep — tabbed grid of Documents / Matters / Projects)
 *
 * Shared library version — all external dependencies are injected via props:
 *   - IFindSimilarServiceConfig for BFF calls (authenticatedFetch, getBffBaseUrl)
 *   - IFilePreviewServices for the document preview dialog
 *   - onNavigateToEntity for Dataverse record navigation
 */
import * as React from 'react';
import type { IFindSimilarServiceConfig, INavigationMessage } from './findSimilarTypes';
import type { IFilePreviewServices } from '../FilePreview/filePreviewTypes';
export interface IFindSimilarDialogProps {
    open: boolean;
    onClose: () => void;
    /** Service configuration for BFF API calls (text extraction, search). */
    serviceConfig: IFindSimilarServiceConfig;
    /** Navigate to a Dataverse entity record (used by results grid). */
    onNavigateToEntity: (message: INavigationMessage) => void;
    /** Service callbacks for the FilePreviewDialog (preview URL, open links, etc.). */
    filePreviewServices: IFilePreviewServices;
}
export declare const FindSimilarDialog: React.FC<IFindSimilarDialogProps>;
export default FindSimilarDialog;
//# sourceMappingURL=FindSimilarDialog.d.ts.map