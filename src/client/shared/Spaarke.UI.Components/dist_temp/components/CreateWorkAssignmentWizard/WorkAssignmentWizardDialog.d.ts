/**
 * WorkAssignmentWizardDialog.tsx
 * Orchestrator for the Work Assignment creation wizard.
 *
 * Uses WizardShell directly (not CreateRecordWizard) because the step
 * sequence differs: Step 1 is record selection, Step 2 is file upload,
 * and follow-on steps have different fields.
 *
 * Steps:
 *   [0] Work to Assign (SelectWorkStep)
 *   [1] Add Files (AddFilesStep) -- skippable
 *   [2] Enter Info (EnterInfoStep) -- pre-filled from record or AI
 *   [3] Next Steps (NextStepsSelectionStep) -- early finish if 0 selected
 *   [4+] Dynamic: Assign Work, Send Email, Create Event
 *
 * Processing:
 *   When files are uploaded, the finish handler uploads to SPE, creates
 *   document records, and triggers AI analysis (same as Create Matter).
 *   The finishing label changes to "Processing..." when files exist.
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
export interface IWorkAssignmentWizardDialogProps {
    open: boolean;
    onClose: () => void;
    /** IDataService for Dataverse operations. */
    dataService: IDataService;
    /**
     * Authenticated fetch function for BFF API calls.
     * Required for AI pre-fill and file upload features.
     */
    authenticatedFetch: AuthenticatedFetchFn;
    /**
     * BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api").
     */
    bffBaseUrl: string;
    /**
     * SPE container ID. If not provided, the wizard will attempt
     * to resolve it from the business unit (requires Xrm context).
     */
    containerId?: string;
    /**
     * Optional navigation service for opening entity records.
     * If provided, the success screen "View Record" button will use this.
     */
    navigationService?: INavigationService;
    /**
     * When `embedded={true}`, the wizard relies on the Dataverse modal chrome
     * for the title bar and close button. Default: false.
     */
    embedded?: boolean;
    /**
     * Resolves the SPE container ID for file uploads.
     * Called once during the finish handler. If not provided and containerId is
     * not set, file uploads will be skipped.
     */
    resolveSpeContainerId?: () => Promise<string>;
}
declare const WorkAssignmentWizardDialog: React.FC<IWorkAssignmentWizardDialogProps>;
export default WorkAssignmentWizardDialog;
//# sourceMappingURL=WorkAssignmentWizardDialog.d.ts.map