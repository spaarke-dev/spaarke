/**
 * workAssignmentService.ts
 * Service for the Work Assignment wizard.
 *
 * Creates sprk_workassignment records in Dataverse via IDataService.
 * Follows the nav-prop discovery pattern from MatterService/EventService.
 *
 * Dependencies are injected via constructor -- no solution-specific imports.
 * authenticatedFetch and bffBaseUrl are passed at construction time.
 *
 * Reuses search helpers from the shared CreateMatterWizard matterService for:
 *   - searchMatterTypes, searchPracticeAreas
 *   - searchContactsAsLookup, searchOrganizationsAsLookup, searchUsersAsLookup
 */
import type { ICreateWorkAssignmentFormState, IAssignWorkState, ICreateFollowOnEventState, ICreateWorkAssignmentResult } from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { IUploadProgress, AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
export { searchMatterTypes, searchPracticeAreas, searchContactsAsLookup, searchOrganizationsAsLookup, searchUsersAsLookup, } from '../CreateMatterWizard/matterService';
export declare class WorkAssignmentService {
    private readonly _containerId?;
    private readonly _dataService;
    private readonly _entityService;
    constructor(dataService: IDataService, authenticatedFetch: AuthenticatedFetchFn, bffBaseUrl: string, _containerId?: string | undefined);
    /**
     * Search records by entity type for the "Work to Assign" step.
     * Supports: matter, project, invoice, event.
     */
    searchRecordsByType(recordType: 'matter' | 'project' | 'invoice' | 'event', nameFilter: string): Promise<ILookupItem[]>;
    /**
     * Read the selected record's fields for pre-filling the Enter Info step.
     * Maps entity-specific field names to form state fields.
     */
    readRecordForPrefill(recordType: 'matter' | 'project' | 'invoice' | 'event', recordId: string): Promise<Partial<ICreateWorkAssignmentFormState>>;
    /**
     * Search contacts filtered by parent organization.
     * Used for "Law Firm Attorney" lookup, filtered by the selected law firm.
     */
    searchContactsByOrganization(orgId: string, nameFilter: string): Promise<ILookupItem[]>;
    /**
     * Full work assignment creation flow:
     *   1. Create sprk_workassignment record
     *   2. Upload files to SPE + create sprk_document records
     *   3. Apply Assign Work data if provided
     *
     * Returns ICreateWorkAssignmentResult -- never throws.
     */
    createWorkAssignment(form: ICreateWorkAssignmentFormState, _linkedDocIds: string[], uploadedFiles: IUploadedFile[], assignWork?: IAssignWorkState, onUploadProgress?: (progress: IUploadProgress) => void): Promise<ICreateWorkAssignmentResult>;
    /**
     * Create a sprk_event record linked to the work assignment.
     * Event type is "Assign Work" by default.
     */
    createFollowOnEvent(workAssignmentId: string, eventState: ICreateFollowOnEventState): Promise<{
        success: boolean;
        warning?: string;
    }>;
    /**
     * Send an email via the BFF communications endpoint.
     */
    sendEmail(workAssignmentId: string, workAssignmentName: string, to: string, subject: string, body: string, cc?: string): Promise<{
        success: boolean;
        warning?: string;
    }>;
}
//# sourceMappingURL=workAssignmentService.d.ts.map