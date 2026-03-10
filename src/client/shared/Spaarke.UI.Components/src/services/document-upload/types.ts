/**
 * Document Upload Service Types
 *
 * Shared type definitions for document upload services extracted from
 * UniversalQuickCreate PCF control. These types support both PCF (context.webAPI)
 * and Code Page (OData fetch with MSAL) contexts.
 *
 * @version 1.0.0
 */

// ---------------------------------------------------------------------------
// Token Provider
// ---------------------------------------------------------------------------

/**
 * Token provider function type.
 *
 * Returns a bearer token for authenticating API requests.
 * Works with both PCF (MSAL via MsalAuthProvider) and Code Page (MSAL via @azure/msal-browser) contexts.
 *
 * @returns Promise resolving to a JWT access token string
 */
export type ITokenProvider = () => Promise<string>;

// ---------------------------------------------------------------------------
// Dataverse Client Interface (Strategy Pattern)
// ---------------------------------------------------------------------------

/**
 * Dataverse record creation/update abstraction.
 *
 * Two implementations:
 * - PcfDataverseClient: wraps ComponentFramework.WebApi (PCF controls)
 * - ODataDataverseClient: direct OData fetch calls with token auth (Code Pages)
 */
export interface IDataverseClient {
    /**
     * Create a record in Dataverse.
     *
     * @param entityLogicalName - Entity logical name (e.g., "sprk_document")
     * @param data - Record payload object
     * @returns Created record reference with id
     */
    createRecord(
        entityLogicalName: string,
        data: Record<string, unknown>
    ): Promise<DataverseRecordRef>;

    /**
     * Update a record in Dataverse.
     *
     * @param entityLogicalName - Entity logical name
     * @param id - Record GUID
     * @param data - Fields to update
     */
    updateRecord(
        entityLogicalName: string,
        id: string,
        data: Record<string, unknown>
    ): Promise<void>;
}

/**
 * Reference to a Dataverse record (returned from create operations).
 */
export interface DataverseRecordRef {
    /** Record GUID */
    id: string;
}

// ---------------------------------------------------------------------------
// SPE File Metadata (from SDAP BFF API)
// ---------------------------------------------------------------------------

/**
 * SPE File Metadata returned from SDAP API (matches FileHandleDto from Sprk.Bff.Api).
 *
 * Maps to Dataverse fields:
 * - id        -> sprk_graphitemid / sprk_driveitemid
 * - name      -> sprk_filename
 * - size      -> sprk_filesize
 * - webUrl    -> sprk_filepath / sprk_sharepointurl
 */
export interface SpeFileMetadata {
    /** Graph API Item ID */
    id: string;

    /** File name */
    name: string;

    /** Parent folder ID (optional) */
    parentId?: string;

    /** File size in bytes */
    size: number;

    /** Created date/time (ISO 8601) */
    createdDateTime: string;

    /** Last modified date/time (ISO 8601) */
    lastModifiedDateTime: string;

    /** Version identifier (ETag) */
    eTag?: string;

    /** Is this a folder */
    isFolder: boolean;

    /** SharePoint web URL (may not be available in all responses) */
    webUrl?: string;

    // Convenience aliases (populated by FileUploadService after upload)

    /** Alias for id */
    driveItemId?: string;

    /** Alias for name */
    fileName?: string;

    /** Alias for webUrl */
    sharePointUrl?: string;

    /** Alias for size */
    fileSize?: number;
}

// ---------------------------------------------------------------------------
// Service Result
// ---------------------------------------------------------------------------

/**
 * Generic service operation result wrapper.
 */
export interface ServiceResult<T = void> {
    success: boolean;
    data?: T;
    error?: string;
}

// ---------------------------------------------------------------------------
// File Operation Requests (SDAP API)
// ---------------------------------------------------------------------------

/**
 * File upload request parameters.
 * API: PUT /api/obo/containers/{containerId}/files/{fileName}
 */
export interface FileUploadApiRequest {
    /** File to upload */
    file: File;

    /** Graph API Drive ID / Container ID */
    driveId: string;

    /** File name */
    fileName: string;
}

/**
 * File download request parameters.
 * API: GET /obo/drives/{driveId}/items/{itemId}/content
 */
export interface FileDownloadRequest {
    /** Graph API Drive ID */
    driveId: string;

    /** Graph API Item ID */
    itemId: string;
}

/**
 * File delete request parameters.
 * API: DELETE /obo/drives/{driveId}/items/{itemId}
 */
export interface FileDeleteRequest {
    /** Graph API Drive ID */
    driveId: string;

    /** Graph API Item ID */
    itemId: string;
}

/**
 * File replace request parameters.
 * Replace = Delete existing + Upload new.
 */
export interface FileReplaceRequest {
    /** New file to upload */
    file: File;

    /** Graph API Drive ID */
    driveId: string;

    /** Graph API Item ID of file to replace */
    itemId: string;

    /** New file name */
    fileName: string;
}

// ---------------------------------------------------------------------------
// FileUploadService Request
// ---------------------------------------------------------------------------

/**
 * Request for uploading a single file via FileUploadService.
 */
export interface FileUploadRequest {
    /** File to upload */
    file: File;

    /** SPE Container / Drive ID */
    driveId: string;

    /** Optional override for file name */
    fileName?: string;
}

// ---------------------------------------------------------------------------
// Multi-File Upload Types
// ---------------------------------------------------------------------------

/**
 * Request for uploading multiple files.
 */
export interface UploadFilesRequest {
    /** Files to upload */
    files: File[];

    /** SharePoint Embedded Container ID (from parent record) */
    containerId: string;
}

/**
 * Progress update for multi-file upload.
 */
export interface UploadProgress {
    /** 1-based index of current file */
    current: number;

    /** Total number of files */
    total: number;

    /** Name of file currently being processed */
    currentFileName: string;

    /** Current status */
    status: 'uploading' | 'complete' | 'failed';

    /** Error message (when status is 'failed') */
    error?: string;
}

/**
 * Result of multi-file upload operation.
 *
 * Returns uploaded file metadata only -- NO record creation.
 * Caller is responsible for creating Dataverse records via DocumentRecordService.
 */
export interface UploadFilesResult {
    /** Overall success flag (true if at least one file uploaded) */
    success: boolean;

    /** Total files attempted */
    totalFiles: number;

    /** Number of successful uploads */
    successCount: number;

    /** Number of failed uploads */
    failureCount: number;

    /** SPE metadata for successfully uploaded files */
    uploadedFiles: SpeFileMetadata[];

    /** Errors for failed uploads */
    errors: { fileName: string; error: string }[];
}

// ---------------------------------------------------------------------------
// Document Record Types (Dataverse)
// ---------------------------------------------------------------------------

/**
 * Parent entity context for document creation.
 */
export interface ParentContext {
    /** Parent entity logical name (e.g., "sprk_matter") */
    parentEntityName: string;

    /** Parent record GUID */
    parentRecordId: string;

    /** SharePoint Embedded Container ID */
    containerId: string;

    /** Parent record display name (e.g., "MAT-2024-001") */
    parentDisplayName: string;
}

/**
 * Form data collected from user input.
 */
export interface DocumentFormData {
    /** Document name/title */
    documentName: string;

    /** Optional document description */
    description?: string;
}

/**
 * Result of creating a single Document record in Dataverse.
 */
export interface CreateResult {
    /** Success flag */
    success: boolean;

    /** File name that was processed */
    fileName: string;

    /** Created record ID (if successful) */
    recordId?: string;

    /** Document ID (Dataverse GUID, if successful) */
    documentId?: string;

    /** SharePoint Embedded drive ID (if successful) */
    driveId?: string;

    /** SharePoint Embedded item ID (if successful) */
    itemId?: string;

    /** Error message (if failed) */
    error?: string;
}

// ---------------------------------------------------------------------------
// Entity Configuration
// ---------------------------------------------------------------------------

/**
 * Configuration for a parent entity that supports document uploads.
 */
export interface EntityDocumentConfig {
    /** Entity logical name (e.g., "sprk_matter") */
    entityName: string;

    /** Lookup field name on Document entity (e.g., "sprk_matter") */
    lookupFieldName: string;

    /** Relationship schema name for metadata queries (e.g., "sprk_matter_document") */
    relationshipSchemaName: string;

    /**
     * Hardcoded navigation property name for @odata.bind fallback.
     * Used when NavMap API is unavailable. CASE-SENSITIVE.
     * Example: "sprk_Matter" (capital M)
     */
    navigationPropertyName?: string;

    /** Container ID field name on parent entity */
    containerIdField: string;

    /** Display name field on parent entity */
    displayNameField: string;

    /** Entity set name for OData (e.g., "sprk_matters") */
    entitySetName: string;
}

// ---------------------------------------------------------------------------
// NavMap Types (Navigation Property Metadata)
// ---------------------------------------------------------------------------

/**
 * Lookup Navigation Response from BFF NavMap API.
 * Contains the case-sensitive navigation property name required for @odata.bind.
 */
export interface LookupNavigationResponse {
    /** Child entity logical name */
    childEntity: string;

    /** Relationship schema name */
    relationship: string;

    /** Lookup attribute logical name (lowercase) */
    logicalName: string;

    /** Lookup attribute schema name */
    schemaName: string;

    /**
     * Navigation property name for @odata.bind (CASE-SENSITIVE).
     * Example: "sprk_Matter" (capital M)
     */
    navigationPropertyName: string;

    /** Target entity logical name (parent) */
    targetEntity: string;

    /** Data source: "dataverse", "cache", or "hardcoded" */
    source: string;
}

// ---------------------------------------------------------------------------
// Logger Interface
// ---------------------------------------------------------------------------

/**
 * Minimal logger interface used by document upload services.
 * Consumers must provide an implementation (e.g., wrapping console, PCF logger, etc.).
 */
export interface ILogger {
    info(source: string, message: string, data?: unknown): void;
    warn(source: string, message: string, data?: unknown): void;
    error(source: string, message: string, error?: unknown): void;
    debug(source: string, message: string, data?: unknown): void;
}

/**
 * Default console logger implementation.
 */
export const consoleLogger: ILogger = {
    info: (source, message, data) => console.log(`[${source}] ${message}`, data ?? ''),
    warn: (source, message, data) => console.warn(`[${source}] ${message}`, data ?? ''),
    error: (source, message, error) => console.error(`[${source}] ${message}`, error ?? ''),
    debug: (source, message, data) => console.debug(`[${source}] ${message}`, data ?? ''),
};
