/**
 * File Upload Service
 *
 * Orchestrates single-file upload to SharePoint Embedded (SPE) via SDAP BFF API.
 * Uses SdapApiClient with injected ITokenProvider for authentication.
 *
 * ADR Compliance:
 * - ADR-007: All SPE operations through BFF API
 *
 * @version 1.0.0
 */
import { SdapApiClient } from './SdapApiClient';
import type { ILogger, SpeFileMetadata, ServiceResult, FileUploadRequest } from './types';
/**
 * Service for uploading files to SharePoint Embedded.
 */
export declare class FileUploadService {
    private readonly apiClient;
    private readonly logger;
    constructor(apiClient: SdapApiClient, logger?: ILogger);
    /**
     * Upload a file to SharePoint Embedded.
     *
     * @param request - File upload request
     * @returns Service result with SPE file metadata
     */
    uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>>;
}
//# sourceMappingURL=FileUploadService.d.ts.map