/**
 * Multi-File Upload Service
 *
 * Orchestrates multi-file uploads to SharePoint Embedded ONLY.
 * Does NOT create Dataverse records - that is handled separately by DocumentRecordService.
 *
 * Refactored for Custom Page Context (v2.0.0.0):
 * - Removed Phase 2 (record creation) from this service
 * - Returns uploaded file metadata for separate record creation
 * - Uses containerId from ParentContext instead of form data
 *
 * ADR Compliance:
 * - ADR-003: Separation of Concerns (file upload vs record creation)
 *
 * @version 2.0.0.0
 */

import { FileUploadService } from './FileUploadService';
import { SpeFileMetadata } from '../types';
import { logInfo, logError } from '../utils/logger';

/**
 * Request for uploading multiple files
 */
export interface UploadFilesRequest {
    /** Files to upload */
    files: File[];

    /** SharePoint Embedded Container ID (from parent record) */
    containerId: string;
}

/**
 * Progress update for multi-file upload
 */
export interface UploadProgress {
    current: number;  // 1-based index
    total: number;
    currentFileName: string;
    status: 'uploading' | 'complete' | 'failed';
    error?: string;
}

/**
 * Result of multi-file upload operation
 *
 * Returns uploaded file metadata only - NO record creation.
 * Caller is responsible for creating Dataverse records using DocumentRecordService.
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

/**
 * Service for orchestrating multi-file uploads
 */
export class MultiFileUploadService {
    constructor(
        private fileUploadService: FileUploadService
    ) {}

    /**
     * Upload multiple files to SharePoint Embedded
     *
     * Strategy: Parallel uploads (simple, fast for 10 files max)
     * Does NOT create Dataverse records - returns metadata only.
     *
     * @param request - Multi-file upload request
     * @param onProgress - Progress callback
     * @returns Upload result with SPE metadata
     */
    async uploadFiles(
        request: UploadFilesRequest,
        onProgress?: (progress: UploadProgress) => void
    ): Promise<UploadFilesResult> {
        const { files, containerId } = request;

        logInfo('MultiFileUploadService', `Starting upload of ${files.length} files to container: ${containerId}`);

        const errors: { fileName: string; error: string }[] = [];
        const uploadedFiles: SpeFileMetadata[] = [];

        // Upload all files in parallel
        const uploadResults = await Promise.allSettled(
            files.map(file =>
                this.fileUploadService.uploadFile({ file, driveId: containerId })
            )
        );

        // Process results
        for (let i = 0; i < files.length; i++) {
            const file = files[i];
            const uploadResult = uploadResults[i];

            try {
                // Report progress: uploading
                onProgress?.({
                    current: i + 1,
                    total: files.length,
                    currentFileName: file.name,
                    status: 'uploading'
                });

                // Check if upload succeeded
                if (uploadResult.status === 'rejected') {
                    throw new Error(uploadResult.reason?.message || 'Upload failed');
                }

                const serviceResult = uploadResult.value;
                if (!serviceResult.success || !serviceResult.data) {
                    throw new Error(serviceResult.error || 'Upload failed');
                }

                // Store SPE metadata
                uploadedFiles.push(serviceResult.data);

                // Report progress: complete
                onProgress?.({
                    current: i + 1,
                    total: files.length,
                    currentFileName: file.name,
                    status: 'complete'
                });

            } catch (error) {
                const errorMessage = error instanceof Error ? error.message : 'Unknown error';
                errors.push({ fileName: file.name, error: errorMessage });

                logError('MultiFileUploadService', `Failed to upload: ${file.name}`, error);

                // Report progress: failed
                onProgress?.({
                    current: i + 1,
                    total: files.length,
                    currentFileName: file.name,
                    status: 'failed',
                    error: errorMessage
                });
            }
        }

        const result: UploadFilesResult = {
            success: uploadedFiles.length > 0,
            totalFiles: files.length,
            successCount: uploadedFiles.length,
            failureCount: errors.length,
            uploadedFiles,
            errors
        };

        logInfo('MultiFileUploadService', `Upload complete: ${result.successCount}/${result.totalFiles} successful`);
        return result;
    }
}
