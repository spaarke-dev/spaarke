/**
 * Multi-File Upload Service
 *
 * Orchestrates multi-file uploads to SharePoint Embedded ONLY.
 * Does NOT create Dataverse records -- that is handled separately by DocumentRecordService.
 *
 * ADR Compliance:
 * - ADR-003: Separation of Concerns (file upload vs record creation)
 * - ADR-007: All SPE operations through BFF API
 *
 * @version 1.0.0
 */
import { FileUploadService } from './FileUploadService';
import type { ILogger, UploadFilesRequest, UploadProgress, UploadFilesResult } from './types';
/**
 * Service for orchestrating multi-file uploads.
 */
export declare class MultiFileUploadService {
    private readonly fileUploadService;
    private readonly logger;
    constructor(fileUploadService: FileUploadService, logger?: ILogger);
    /**
     * Upload multiple files to SharePoint Embedded.
     *
     * Strategy: Parallel uploads (simple, fast for 10 files max).
     * Does NOT create Dataverse records -- returns metadata only.
     *
     * @param request - Multi-file upload request
     * @param onProgress - Progress callback
     * @returns Upload result with SPE metadata
     */
    uploadFiles(request: UploadFilesRequest, onProgress?: (progress: UploadProgress) => void): Promise<UploadFilesResult>;
}
//# sourceMappingURL=MultiFileUploadService.d.ts.map