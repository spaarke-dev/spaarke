/**
 * CreateMatterWizard.tsx
 * Main exported component for the "Create New Matter" wizard.
 *
 * This is a thin wrapper around CreateRecordWizard that provides:
 *   - AssociateToStep as step 1 (optional, links matter to a Project or Account via N:N)
 *   - Entity-specific form step (CreateRecordStep)
 *   - Finish handler (MatterService.createMatter + success screen + N:N association)
 *   - Search callbacks (contacts, organizations, users)
 *   - Email template builders
 *
 * Step sequence:
 *   1. Associate To  — optional; links to Project (sprk_project) or Account (account)
 *   2. Add file(s)   — upload documents for AI pre-fill
 *   3. Enter Info    — matter form fields
 *   4. Next Steps    — follow-on action selection
 *
 * After matter creation, if a Project association was selected, the N:N
 * sprk_Project_Matter_nn relationship is established via IDataService.
 *
 * All generic wizard mechanics (file upload, follow-on steps, state
 * management) are handled by the shared CreateRecordWizard component.
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
export interface ICreateMatterWizardProps {
    /** Whether the dialog is currently open. */
    open: boolean;
    /** Callback invoked when the user clicks Cancel or closes the dialog. */
    onClose: () => void;
    /** IDataService for Dataverse operations. */
    dataService: IDataService;
    /**
     * Authenticated fetch function for BFF API calls.
     * Required for AI pre-fill and summary features.
     */
    authenticatedFetch: AuthenticatedFetchFn;
    /**
     * BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api").
     */
    bffBaseUrl: string;
    /**
     * Optional navigation service for opening entity records.
     * If provided, the success screen "View Matter" button will use this.
     */
    navigationService?: INavigationService;
    /**
     * When `embedded={true}`, the wizard relies on the Dataverse modal chrome
     * for the title bar and close button. Default: false.
     */
    embedded?: boolean;
    /**
     * Resolves the SPE container ID for file uploads.
     * Called once during the finish handler. If not provided, file uploads
     * will be skipped.
     */
    resolveSpeContainerId?: () => Promise<string>;
}
export declare const CreateMatterWizard: React.FC<ICreateMatterWizardProps>;
export default CreateMatterWizard;
//# sourceMappingURL=CreateMatterWizard.d.ts.map