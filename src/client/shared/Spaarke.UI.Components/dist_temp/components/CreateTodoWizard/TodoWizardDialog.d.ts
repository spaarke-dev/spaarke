/**
 * TodoWizardDialog.tsx
 * Thin wrapper around CreateRecordWizard for "Create New To Do".
 *
 * A To Do is a sprk_event record with sprk_todoflag=true.
 * Default export enables React.lazy() dynamic import.
 *
 * Dependencies are injected via props -- no solution-specific imports.
 * Uses IDataService (not IWebApi) for shared library portability.
 */
import * as React from 'react';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
export interface ICreateTodoWizardProps {
    /** Whether the dialog is currently open. */
    open: boolean;
    /** Callback invoked when the user clicks Cancel or closes the dialog. */
    onClose: () => void;
    /** IDataService for Dataverse operations. */
    dataService: IDataService;
    /**
     * Authenticated fetch function for BFF API calls.
     * Required for email send operations.
     */
    authenticatedFetch: AuthenticatedFetchFn;
    /**
     * BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api").
     */
    bffBaseUrl: string;
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
declare const TodoWizardDialog: React.FC<ICreateTodoWizardProps>;
export { TodoWizardDialog };
export default TodoWizardDialog;
//# sourceMappingURL=TodoWizardDialog.d.ts.map