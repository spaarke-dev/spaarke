/**
 * EntityCreationService.ts
 * Generic service for entity creation workflows (Matter, Project, Event, etc.).
 *
 * Responsibilities:
 *   1. Upload files to SPE via BFF OBO endpoint
 *   2. Create Dataverse entity records via WebApi
 *   3. Create sprk_document records linking uploaded files to the parent entity
 *   4. Trigger Document Profile analysis via BFF Service Bus
 *
 * Entity-agnostic: uses entityName and navigation properties as parameters,
 * not hardcoded to any specific entity.
 *
 * Dependencies are injected via constructor (no solution-specific imports):
 *   - webApi: Dataverse WebApi interface (IWebApiWithCreate)
 *   - authenticatedFetch: BFF-authenticated fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * @example
 * ```typescript
 * import { EntityCreationService } from '@spaarke/ui-components';
 * import { authenticatedFetch } from '../services/authInit';
 * import { getBffBaseUrl } from '../config/bffConfig';
 *
 * const service = new EntityCreationService(webApi, authenticatedFetch, getBffBaseUrl());
 * const uploadResult = await service.uploadFilesToSpe(containerId, files);
 * const matterId = await service.createEntityRecord('sprk_matter', matterData);
 * await service.createDocumentRecords('sprk_matters', matterId, 'sprk_Matter', uploadResult.uploadedFiles);
 * ```
 */
import type { IWebApiWithCreate } from '../types/WebApiLike';
import type { IUploadedFile } from '../components/FileUpload/fileUploadTypes';
/** Result of a file upload operation. */
export interface IFileUploadResult {
    /** Whether at least some files were uploaded successfully. */
    success: boolean;
    /** Number of files that uploaded successfully. */
    successCount: number;
    /** Number of files that failed. */
    failureCount: number;
    /** Metadata for successfully uploaded files (drive item IDs). */
    uploadedFiles: ISpeFileMetadata[];
    /** Per-file error details. */
    errors: Array<{
        fileName: string;
        error: string;
    }>;
}
/** SPE file metadata returned from the BFF upload endpoint. */
export interface ISpeFileMetadata {
    id: string;
    name: string;
    size: number;
    webUrl?: string;
}
/** Result of creating sprk_document records. */
export interface IDocumentLinkResult {
    success: boolean;
    linkedCount: number;
    /** GUIDs of successfully created sprk_document records. */
    createdDocumentIds: string[];
    warnings: string[];
}
/** Input for sending email via BFF Communication service. */
export interface ISendEmailInput {
    to: string | string[];
    cc?: string | string[];
    subject: string;
    body: string;
    bodyFormat?: 'HTML' | 'Text';
    associations?: Array<{
        entityType: string;
        entityId: string;
        entityName?: string;
    }>;
}
/** Result of a send-email operation. */
export interface ISendEmailResult {
    success: boolean;
    warning?: string;
}
/** Progress callback for multi-file uploads. */
export interface IUploadProgress {
    current: number;
    total: number;
    currentFileName: string;
    status: 'uploading' | 'complete' | 'failed';
    error?: string;
}
/** Authenticated fetch function signature (injected by caller). */
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;
export declare class EntityCreationService {
    private readonly _webApi;
    private readonly _authenticatedFetch;
    private readonly _bffBaseUrl;
    constructor(_webApi: IWebApiWithCreate, _authenticatedFetch: AuthenticatedFetchFn, _bffBaseUrl: string);
    /**
     * Upload files to SPE via the BFF OBO upload endpoint.
     *
     * Uses: PUT /api/obo/containers/{containerId}/files/{path}
     * Each file is uploaded individually with Bearer token auth.
     *
     * @param containerId SPE container/drive ID for the target storage
     * @param files Files to upload
     * @param onProgress Optional progress callback
     */
    uploadFilesToSpe(containerId: string, files: IUploadedFile[], onProgress?: (progress: IUploadProgress) => void): Promise<IFileUploadResult>;
    /**
     * Create a Dataverse entity record via WebApi.
     *
     * @param entityName Dataverse logical name (e.g., 'sprk_matter', 'sprk_project')
     * @param entityData Entity payload with field values and @odata.bind lookups
     * @returns The GUID of the created record
     */
    createEntityRecord(entityName: string, entityData: Record<string, unknown>): Promise<string>;
    /**
     * Create sprk_document records in Dataverse linking uploaded SPE files to a parent entity.
     *
     * Each document record contains:
     *   - sprk_documentname / sprk_filename: file name
     *   - sprk_driveitemid: SPE drive item ID
     *   - sprk_filepath: web URL to the file
     *   - sprk_filesize: file size in bytes
     *   - Navigation property @odata.bind to parent entity
     *
     * @param parentEntityName Logical name of the parent entity set (e.g., 'sprk_matters')
     * @param parentEntityId GUID of the parent entity record
     * @param navigationProperty Navigation property name on sprk_document (e.g., 'sprk_Matter')
     * @param uploadedFiles SPE file metadata from uploadFilesToSpe()
     * @param options Additional context for the document records
     */
    createDocumentRecords(parentEntityName: string, parentEntityId: string, navigationProperty: string, uploadedFiles: ISpeFileMetadata[], options?: {
        /** SPE container/drive ID — stored as sprk_graphdriveid and sprk_containerid */
        containerId?: string;
        /** Parent record display name — stored in sprk_regardingrecordname for resolver fields */
        parentRecordName?: string;
        /** Parent entity logical name (e.g., 'sprk_matter'). If omitted, derived from parentEntityName by removing trailing 's'. */
        parentEntityLogicalName?: string;
    }): Promise<IDocumentLinkResult>;
    /**
     * Trigger Document Profile analysis for created documents via the BFF.
     * Calls POST /api/documents/{id}/analyze which queues a Service Bus job
     * for each document. Failures are non-fatal (added as warnings).
     */
    private _triggerDocumentAnalysis;
    /**
     * Send an email via the BFF Communication service (Graph API).
     *
     * Normalizes `to`/`cc` from string or array (splits on `;,`).
     * Returns `{ success, warning? }` — never throws.
     */
    sendEmail(input: ISendEmailInput): Promise<ISendEmailResult>;
}
//# sourceMappingURL=EntityCreationService.d.ts.map