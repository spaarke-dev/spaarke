/**
 * CreateEventWizard.tsx
 * Thin wrapper around CreateRecordWizard for "Create New Event".
 *
 * Provides only:
 *   - Entity-specific form step (CreateEventStep)
 *   - Finish handler (EventService.createEvent + EntityCreationService for email)
 *   - Search callbacks (contacts, organizations, users)
 *   - Email template builders
 *
 * All generic wizard mechanics (file upload, follow-on steps, state
 * management) are handled by the shared CreateRecordWizard component.
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *   - navigationService: optional INavigationService for opening records
 *
 * Default export enables React.lazy() dynamic import for bundle-size
 * optimization (same pattern as CreateProjectWizard).
 *
 * @see IDataService — high-level data access abstraction
 * @see INavigationService — navigation abstraction
 */
import * as React from 'react';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
export interface ICreateEventWizardProps {
    /** Whether the wizard dialog is open. */
    open: boolean;
    /** Called when the wizard is closed/cancelled. */
    onClose: () => void;
    /** IDataService for Dataverse entity operations. */
    dataService: IDataService;
    /** Optional INavigationService for opening records after creation. */
    navigationService?: INavigationService;
    /** When true, hides the title bar (Dataverse modal provides chrome). */
    embedded?: boolean;
    /** MSAL-backed authenticated fetch function for BFF API calls. */
    authenticatedFetch?: typeof fetch;
    /** BFF API base URL. */
    bffBaseUrl?: string;
    /**
     * Optional callback to resolve the SPE container ID for file uploads.
     * Called when the wizard opens. If not provided, file uploads will be skipped.
     */
    resolveSpeContainerId?: () => Promise<string>;
}
declare const CreateEventWizard: React.FC<ICreateEventWizardProps>;
export default CreateEventWizard;
export { CreateEventWizard };
//# sourceMappingURL=CreateEventWizard.d.ts.map