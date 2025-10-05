/**
 * SDAP Client configuration options.
 */
export interface SdapClientConfig {
    /** Base URL of SDAP BFF API (e.g., 'https://spe-bff-api.azurewebsites.net') */
    baseUrl: string;

    /** Request timeout in milliseconds (default: 300000 = 5 minutes) */
    timeout?: number;
}

/**
 * SharePoint Drive Item metadata.
 */
export interface DriveItem {
    /** Unique item ID */
    id: string;

    /** File/folder name */
    name: string;

    /** File size in bytes (null for folders) */
    size: number | null;

    /** Drive ID containing this item */
    driveId: string;

    /** Parent folder reference ID */
    parentReferenceId?: string;

    /** Created date/time */
    createdDateTime: string;

    /** Last modified date/time */
    lastModifiedDateTime: string;

    /** ETag for versioning */
    eTag?: string;

    /** Whether this is a folder */
    isFolder: boolean;

    /** MIME type (files only) */
    mimeType?: string;
}

/**
 * Upload session for chunked uploads.
 */
export interface UploadSession {
    /** Upload URL for PUT requests */
    uploadUrl: string;

    /** Session expiration date/time */
    expirationDateTime: string;

    /** Next expected byte ranges (for resumption) */
    nextExpectedRanges?: string[];
}

/**
 * File metadata.
 */
export interface FileMetadata extends DriveItem {
    /** Download URL */
    downloadUrl?: string;

    /** Web URL for browser viewing */
    webUrl?: string;
}

/**
 * Upload progress callback.
 */
export type UploadProgressCallback = (percent: number) => void;

/**
 * SDAP API error response.
 */
export interface SdapApiError {
    /** Error status code */
    status: number;

    /** Error title */
    title: string;

    /** Detailed error message */
    detail: string;

    /** Trace ID for correlation */
    traceId?: string;
}

/**
 * Container information.
 */
export interface Container {
    /** Container ID */
    id: string;

    /** Container display name */
    displayName: string;

    /** Drive ID */
    driveId: string;
}
