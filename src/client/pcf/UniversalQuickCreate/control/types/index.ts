/**
 * Type definitions for Universal Dataset Grid PCF Control
 * Version 2.0.0 - With SDAP Integration and Fluent UI v9
 */

/**
 * Field mappings configuration.
 * Maps logical field names to actual Dataverse field names.
 *
 * Note: All field names use the 'sprk_' publisher prefix.
 */
export interface FieldMappings {
    /** Boolean field indicating if document has an attached file */
    hasFile: string;

    /** File name (e.g., "document.pdf") */
    fileName: string;

    /** File size in bytes */
    fileSize: string;

    /** MIME type (e.g., "application/pdf") */
    mimeType: string;

    /** SharePoint Graph API item ID */
    graphItemId: string;

    /** SharePoint Graph API drive ID */
    graphDriveId: string;
}

/**
 * Custom command button configuration.
 */
export interface CustomCommand {
    /** Unique command identifier */
    id: string;

    /** Display label for the button */
    label: string;

    /** Fluent UI icon name (e.g., "Add24Regular") */
    icon: string;

    /** Enable rule expression (evaluated at runtime) */
    enableRule: string;

    /** Error message to show when command cannot be executed */
    errorMessage: string;

    /** Button appearance (primary, secondary, subtle) */
    appearance?: 'primary' | 'secondary' | 'subtle';
}

/**
 * SDAP client configuration.
 */
export interface SdapConfig {
    /** Base URL of SDAP BFF API */
    baseUrl: string;

    /** Request timeout in milliseconds */
    timeout: number;
}

/**
 * Overall grid configuration.
 */
export interface GridConfiguration {
    /** Field name mappings */
    fieldMappings: FieldMappings;

    /** Custom command buttons */
    customCommands: CustomCommand[];

    /** SDAP client configuration */
    sdapConfig: SdapConfig;
}

/**
 * Default grid configuration with sprk_ field prefix.
 */
export const DEFAULT_GRID_CONFIG: GridConfiguration = {
    fieldMappings: {
        hasFile: 'sprk_hasfile',
        fileName: 'sprk_filename',
        fileSize: 'sprk_filesize',
        mimeType: 'sprk_mimetype',
        graphItemId: 'sprk_graphitemid',
        graphDriveId: 'sprk_graphdriveid'
    },
    customCommands: [
        {
            id: 'addFile',
            label: 'Add File',
            icon: 'Add24Regular',
            enableRule: 'selectedCount === 1 && !hasFile',
            errorMessage: 'Select a single document without a file',
            appearance: 'primary'
        },
        {
            id: 'removeFile',
            label: 'Remove File',
            icon: 'Delete24Regular',
            enableRule: 'selectedCount === 1 && hasFile',
            errorMessage: 'Select a single document with a file',
            appearance: 'secondary'
        },
        {
            id: 'updateFile',
            label: 'Update File',
            icon: 'ArrowUpload24Regular',
            enableRule: 'selectedCount === 1 && hasFile',
            errorMessage: 'Select a single document with a file',
            appearance: 'secondary'
        },
        {
            id: 'downloadFile',
            label: 'Download',
            icon: 'ArrowDownload24Regular',
            enableRule: 'selectedCount > 0 && (selectedCount > 1 || hasFile)',
            errorMessage: 'Select at least one document with a file',
            appearance: 'secondary'
        }
    ],
    sdapConfig: {
        baseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net',
        timeout: 300000 // 5 minutes
    }
};

/**
 * Command context for evaluating enable rules.
 */
export interface CommandContext {
    /** Number of selected records */
    selectedCount: number;

    /** Whether the single selected record has a file */
    hasFile: boolean;

    /** Selected record IDs */
    selectedRecordIds: string[];
}

/**
 * SDAP-specific type definitions for SharePoint Embedded operations
 */

/**
 * SPE File Metadata returned from SDAP API (matches FileHandleDto from Spe.Bff.Api)
 *
 * Maps to Dataverse fields:
 * - id → sprk_graphitemid / sprk_driveitemid
 * - name → sprk_filename
 * - size → sprk_filesize
 * - createdDateTime → sprk_createddatetime / sprk_createddate
 * - lastModifiedDateTime → sprk_lastmodifieddatetime / sprk_modifieddate
 * - eTag → sprk_etag
 * - parentId → sprk_parentfolderid
 * - webUrl → sprk_filepath / sprk_sharepointurl (URL field)
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

    // Convenience aliases for Quick Create (maps API response to our expected fields)
    /** Alias for id (for Quick Create compatibility) */
    driveItemId?: string;

    /** Alias for name (for Quick Create compatibility) */
    fileName?: string;

    /** Alias for webUrl (for Quick Create compatibility) */
    sharePointUrl?: string;

    /** Alias for size (for Quick Create compatibility) */
    fileSize?: number;
}

/**
 * File upload request parameters
 * API: PUT /api/drives/{driveId}/upload?fileName={name}
 */
export interface FileUploadRequest {
    /** File to upload */
    file: File;

    /** Graph API Drive ID (from sprk_graphdriveid or Container) */
    driveId: string;

    /** File name */
    fileName: string;
}

/**
 * File download request parameters
 * API: GET /api/drives/{driveId}/items/{itemId}/content
 */
export interface FileDownloadRequest {
    /** Graph API Drive ID (from sprk_graphdriveid) */
    driveId: string;

    /** Graph API Item ID (from sprk_graphitemid) */
    itemId: string;
}

/**
 * File delete request parameters
 * API: DELETE /api/drives/{driveId}/items/{itemId}
 */
export interface FileDeleteRequest {
    /** Graph API Drive ID (from sprk_graphdriveid) */
    driveId: string;

    /** Graph API Item ID (from sprk_graphitemid) */
    itemId: string;
}

/**
 * File replace request parameters
 * Replace = Delete existing + Upload new
 */
export interface FileReplaceRequest {
    /** New file to upload */
    file: File;

    /** Graph API Drive ID (from sprk_graphdriveid) */
    driveId: string;

    /** Graph API Item ID of file to replace (from sprk_graphitemid) */
    itemId: string;

    /** New file name */
    fileName: string;
}

/**
 * API Response wrapper
 */
export interface ApiResponse<T> {
    success: boolean;
    data?: T;
    error?: string;
    details?: string;
}

/**
 * Service operation result
 */
export interface ServiceResult<T = void> {
    success: boolean;
    data?: T;
    error?: string;
}

/**
 * Custom Page Types for Universal Document Upload
 * Version 2.0.0.0
 */

/**
 * Parent entity context passed to custom page dialog
 *
 * Passed via Xrm.Navigation.navigateTo() data property
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
 * Form data collected from user input
 */
export interface FormData {
    /** Document name/title */
    documentName: string;

    /** Optional document description */
    description?: string;
}

/**
 * Uploaded file metadata (from SPE)
 */
export interface UploadedFileMetadata {
    /** Original file name */
    fileName: string;

    /** File size in bytes */
    size: number;

    /** SharePoint Graph API item ID */
    itemId: string;

    /** MIME type */
    mimeType: string;
}

/**
 * Result of creating a single Document record
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

/**
 * File validation error
 */
export interface FileValidationError {
    /** File name that failed validation */
    fileName: string;

    /** Validation error message */
    message: string;
}

/**
 * File upload constraints
 */
export const FILE_UPLOAD_LIMITS = {
    /** Maximum number of files */
    MAX_FILES: 10,

    /** Maximum file size (10MB in bytes) */
    MAX_FILE_SIZE: 10 * 1024 * 1024,

    /** Maximum total size (100MB in bytes) */
    MAX_TOTAL_SIZE: 100 * 1024 * 1024,

    /** Dangerous file extensions (blocked for security) */
    DANGEROUS_EXTENSIONS: [
        '.exe', '.dll', '.bat', '.cmd', '.ps1', '.vbs', '.js', '.jar',
        '.app', '.msi', '.scr', '.com', '.pif', '.gadget', '.sh'
    ]
} as const;
