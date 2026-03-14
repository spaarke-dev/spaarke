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
import type { ILogger, SpeFileMetadata, UploadFilesRequest, UploadProgress, UploadFilesResult } from './types';
import { consoleLogger } from './types';

/**
 * Service for orchestrating multi-file uploads.
 */
export class MultiFileUploadService {
  private readonly logger: ILogger;

  constructor(
    private readonly fileUploadService: FileUploadService,
    logger?: ILogger
  ) {
    this.logger = logger ?? consoleLogger;
  }

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
  async uploadFiles(
    request: UploadFilesRequest,
    onProgress?: (progress: UploadProgress) => void
  ): Promise<UploadFilesResult> {
    const { files, containerId } = request;

    this.logger.info('MultiFileUploadService', `Starting upload of ${files.length} files to container: ${containerId}`);

    const errors: { fileName: string; error: string }[] = [];
    const uploadedFiles: SpeFileMetadata[] = [];

    // Upload all files in parallel
    const uploadResults = await Promise.allSettled(
      files.map(file => this.fileUploadService.uploadFile({ file, driveId: containerId }))
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
          status: 'uploading',
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
          status: 'complete',
        });
      } catch (error) {
        const errorMessage = error instanceof Error ? error.message : 'Unknown error';
        errors.push({ fileName: file.name, error: errorMessage });

        this.logger.error('MultiFileUploadService', `Failed to upload: ${file.name}`, error);

        // Report progress: failed
        onProgress?.({
          current: i + 1,
          total: files.length,
          currentFileName: file.name,
          status: 'failed',
          error: errorMessage,
        });
      }
    }

    const result: UploadFilesResult = {
      success: uploadedFiles.length > 0,
      totalFiles: files.length,
      successCount: uploadedFiles.length,
      failureCount: errors.length,
      uploadedFiles,
      errors,
    };

    this.logger.info(
      'MultiFileUploadService',
      `Upload complete: ${result.successCount}/${result.totalFiles} successful`
    );
    return result;
  }
}
