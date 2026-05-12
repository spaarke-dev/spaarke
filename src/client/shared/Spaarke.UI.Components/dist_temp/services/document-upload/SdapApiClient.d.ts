/**
 * SDAP (SharePoint Document Access Platform) API Client
 *
 * Provides type-safe methods for interacting with the SDAP BFF API
 * for file operations (upload, download, delete, replace) via SharePoint Embedded.
 *
 * Authentication: Accepts an ITokenProvider function -- works with both
 * PCF (MSAL OBO) and Code Page (MSAL browser) contexts.
 *
 * ADR Compliance:
 * - ADR-007: All SPE operations through BFF API
 * - ADR-008: Requires authentication (Bearer token via ITokenProvider)
 *
 * @version 1.0.0
 */
import type { ITokenProvider, ILogger, SpeFileMetadata, FileUploadApiRequest, FileDownloadRequest, FileDeleteRequest, FileReplaceRequest } from './types';
/**
 * Optional callback invoked on 401 responses before retry.
 * Allows the caller to clear token caches (e.g., MSAL cache).
 */
export type OnUnauthorizedCallback = () => void;
/**
 * Configuration for SdapApiClient.
 */
export interface SdapApiClientOptions {
    /** SDAP BFF API base URL */
    baseUrl: string;
    /** Token provider function -- returns a bearer token */
    getAccessToken: ITokenProvider;
    /** Request timeout in milliseconds (default: 300000 = 5 minutes) */
    timeout?: number;
    /** Logger implementation (default: consoleLogger) */
    logger?: ILogger;
    /**
     * Callback invoked when a 401 response is received, before retrying.
     * Use this to clear MSAL or other token caches.
     */
    onUnauthorized?: OnUnauthorizedCallback;
}
/**
 * SDAP API Client
 *
 * Stateless HTTP client for SDAP BFF API file operations.
 * Authentication is delegated to the injected ITokenProvider.
 */
export declare class SdapApiClient {
    private readonly baseUrl;
    private readonly timeout;
    private readonly getAccessToken;
    private readonly logger;
    private readonly onUnauthorized?;
    constructor(options: SdapApiClientOptions);
    /**
     * Upload file to SharePoint Embedded.
     * API: PUT /api/obo/containers/{containerId}/files/{fileName}
     */
    uploadFile(request: FileUploadApiRequest): Promise<SpeFileMetadata>;
    /**
     * Download file from SharePoint Embedded.
     * API: GET /obo/drives/{driveId}/items/{itemId}/content
     */
    downloadFile(request: FileDownloadRequest): Promise<Blob>;
    /**
     * Delete file from SharePoint Embedded.
     * API: DELETE /obo/drives/{driveId}/items/{itemId}
     */
    deleteFile(request: FileDeleteRequest): Promise<void>;
    /**
     * Replace file in SharePoint Embedded.
     * Implemented as: DELETE old file + UPLOAD new file.
     */
    replaceFile(request: FileReplaceRequest): Promise<SpeFileMetadata>;
    /**
     * Fetch with timeout support and automatic 401 retry.
     *
     * On 401: invokes onUnauthorized callback (if provided), refreshes token, retries once.
     */
    private fetchWithTimeout;
    /**
     * Handle API response and parse JSON.
     */
    private handleResponse;
    /**
     * Get user-friendly error message based on HTTP status code.
     */
    private getUserFriendlyErrorMessage;
    /**
     * Enhance error with context.
     */
    private enhanceError;
}
//# sourceMappingURL=SdapApiClient.d.ts.map