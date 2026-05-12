/**
 * FindSimilarResultsStep.tsx
 * Step 2 of the Find Similar Records wizard — tabbed results grid.
 *
 * Displays search results in three domain tabs:
 *   - Documents (sprk_document) — Name, Score, File Type + action icons
 *   - Matters   (sprk_matter)   — Matter Name, Score, Description + action icon
 *   - Projects  (sprk_project)  — Project Name, Score, Description + action icon
 *
 * Uses progressive rendering (intersection observer) instead of scrollbars.
 *
 * Shared library version — external dependencies (navigation, file preview
 * services) are injected via callback props.
 */
import * as React from 'react';
import type { IFilePreviewServices } from '../FilePreview/filePreviewTypes';
import type { FindSimilarStatus, IFindSimilarResults, INavigationMessage } from './findSimilarTypes';
export interface IFindSimilarResultsStepProps {
    status: FindSimilarStatus;
    results: IFindSimilarResults | null;
    errorMessage: string | null;
    onRetry: () => void;
    /** Navigate to a Dataverse entity record. */
    onNavigateToEntity: (message: INavigationMessage) => void;
    /** Service callbacks for the FilePreviewDialog. */
    filePreviewServices: IFilePreviewServices;
}
export declare const FindSimilarResultsStep: React.FC<IFindSimilarResultsStepProps>;
//# sourceMappingURL=FindSimilarResultsStep.d.ts.map