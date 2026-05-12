/**
 * matterService.ts
 * Matter creation flow orchestrator for the Create New Matter wizard.
 *
 * Responsibilities:
 *   1. Create sprk_matter Dataverse record via IDataService
 *   2. Upload files to SPE via BFF SpeFileStore endpoint
 *   3. Execute selected follow-on actions (assign counsel, draft summary, send email)
 *
 * Partial failure handling:
 *   - Matter record creation failure -> hard error (abort, return error result)
 *   - File upload failure -> soft warning (matter created, return warning result)
 *   - Follow-on action failure -> soft warning (matter created, log per-action error)
 *
 * All methods return ICreateMatterResult -- never throw.
 *
 * Dependencies are injected via constructor -- no solution-specific imports.
 * authenticatedFetch and bffBaseUrl are passed at construction time.
 */
import type { ICreateMatterFormState } from './formTypes';
import type { IUploadedFile } from './wizardTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IUploadProgress, AuthenticatedFetchFn } from '../../services/EntityCreationService';
/**
 * Minimal contact shape returned by searchContacts.
 * Mirrors the LegalWorkspace IContact interface for backward compatibility.
 */
export interface IContact {
    sprk_contactid: string;
    sprk_name: string;
    sprk_email?: string;
}
export type CreateMatterResultStatus = 'success' | 'partial' | 'error';
export interface ICreateMatterResult {
    /** Overall status. */
    status: CreateMatterResultStatus;
    /** The GUID of the created sprk_matter record (present on success and partial). */
    matterId?: string;
    /** Display name of the created matter. */
    matterName?: string;
    /** Human-readable error message (set on error status). */
    errorMessage?: string;
    /** Non-fatal warnings (e.g. file upload failed after record was created). */
    warnings: string[];
}
export interface IAssignCounselInput {
    /** Contact GUID to assign as lead counsel. */
    contactId: string;
    /** Display name (used for optimistic UI, not sent to server). */
    contactName: string;
}
export interface IDraftSummaryInput {
    /** Recipient email addresses for distribution. */
    recipientEmails: string[];
}
export interface IFollowOnActions {
    assignCounsel?: IAssignCounselInput;
    draftSummary?: IDraftSummaryInput;
    sendEmail?: {
        to: string;
        subject: string;
        body: string;
    };
}
export declare class MatterService {
    private readonly _containerId?;
    private readonly _dataService;
    private readonly _entityService;
    constructor(dataService: IDataService, authenticatedFetch: AuthenticatedFetchFn, bffBaseUrl: string, _containerId?: string | undefined);
    /**
     * Full matter creation flow:
     *   1. Create sprk_matter record
     *   2. Upload files to SPE via BFF (using EntityCreationService)
     *   3. Create sprk_document records linking files to the matter
     *   4. Execute selected follow-on actions
     *
     * Returns ICreateMatterResult -- never throws.
     */
    createMatter(form: ICreateMatterFormState, uploadedFiles: IUploadedFile[], followOnActions: IFollowOnActions, onUploadProgress?: (progress: IUploadProgress) => void): Promise<ICreateMatterResult>;
    private _assignCounsel;
    private _distributeSummary;
}
/**
 * Search contact records by name fragment.
 * Uses standard Dataverse contact entity (fullname, emailaddress1).
 * Returns up to 10 matching contacts.
 * Throws on error -- callers should handle gracefully.
 */
export declare function searchContacts(dataService: IDataService, nameFilter: string): Promise<IContact[]>;
/**
 * Search contacts and return as ILookupItem[] (for LookupField compatibility).
 */
export declare function searchContactsAsLookup(dataService: IDataService, nameFilter: string): Promise<ILookupItem[]>;
/**
 * Search sprk_mattertype records by name fragment.
 * Returns up to 10 matching matter types as ILookupItem.
 */
export declare function searchMatterTypes(dataService: IDataService, nameFilter: string): Promise<ILookupItem[]>;
/**
 * Search sprk_practicearea records by name fragment.
 * Returns up to 10 matching practice areas as ILookupItem.
 */
export declare function searchPracticeAreas(dataService: IDataService, nameFilter: string): Promise<ILookupItem[]>;
export interface IAiDraftSummaryResponse {
    /** The generated summary text. */
    summary: string;
}
export interface StreamAiSummaryCallbacks {
    onProgress?: (stepId: string) => void;
}
/**
 * Streams the BFF AI endpoint to generate a draft matter summary (SSE).
 * Fires onProgress callbacks as the backend emits progress events.
 * Returns a fallback response if the endpoint is unavailable (graceful degradation).
 *
 * @param matterName - Name of the matter
 * @param matterType - Type of the matter
 * @param practiceArea - Practice area
 * @param callbacks - SSE progress callbacks
 * @param signal - Optional abort signal
 * @param authenticatedFetch - Authenticated fetch function for BFF API calls
 * @param bffBaseUrl - BFF API base URL
 */
export declare function streamAiDraftSummary(matterName: string, matterType: string, practiceArea: string, callbacks?: StreamAiSummaryCallbacks, signal?: AbortSignal, authenticatedFetch?: (url: string, init?: RequestInit) => Promise<Response>, bffBaseUrl?: string): Promise<IAiDraftSummaryResponse>;
/**
 * @deprecated Use streamAiDraftSummary for SSE-based progress feedback.
 * Calls the BFF AI endpoint to generate a draft matter summary (REST, no progress).
 *
 * @param matterName - Name of the matter
 * @param matterType - Type of the matter
 * @param practiceArea - Practice area
 * @param authenticatedFetch - Authenticated fetch function for BFF API calls
 * @param bffBaseUrl - BFF API base URL
 */
export declare function fetchAiDraftSummary(matterName: string, matterType: string, practiceArea: string, authenticatedFetch?: (url: string, init?: RequestInit) => Promise<Response>, bffBaseUrl?: string): Promise<IAiDraftSummaryResponse>;
/**
 * Search sprk_organization records by name fragment.
 * Returns up to 10 matching organizations as ILookupItem.
 */
export declare function searchOrganizationsAsLookup(dataService: IDataService, nameFilter: string): Promise<ILookupItem[]>;
/**
 * Search systemuser records by name fragment.
 * Returns up to 10 active users as ILookupItem.
 * Name format: "Full Name (email)" for disambiguation.
 */
export declare function searchUsersAsLookup(dataService: IDataService, nameFilter: string): Promise<ILookupItem[]>;
//# sourceMappingURL=matterService.d.ts.map