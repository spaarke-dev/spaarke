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
    const { files, target, conflictBehavior } = request;

    // The destination is NOT logged, because there is no longer a client-side destination to log:
    // the server resolves the container from `target`. What identifies the batch is the target.
    this.logger.info(
      'MultiFileUploadService',
      target.kind === 'record'
        ? `Starting upload of ${files.length} files against ${target.entityLogicalName} ${target.recordId}`
        : `Starting upload of ${files.length} files with no owning record`
    );

    const errors: UploadFilesResult['errors'] = [];
    const uploadedFiles: SpeFileMetadata[] = [];

    // Upload all files in parallel
    const uploadResults = await Promise.allSettled(
      files.map(file => this.fileUploadService.uploadFile({ file, target, conflictBehavior }))
    );

    // Process results
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const uploadResult = uploadResults[i];

      // Report progress: uploading
      onProgress?.({
        current: i + 1,
        total: files.length,
        currentFileName: file.name,
        status: 'uploading',
      });

      // A failure is described by the ServiceResult, NOT by an exception. This loop used to wrap
      // everything in try/catch and re-throw `new Error(serviceResult.error)`, which discarded
      // `serviceResult.nameConflict` — the one field that tells the UI the failure is recoverable
      // and offers a choice. Branch on the result instead of round-tripping it through an Error.
      let failure: { error: string; nameConflict?: { fileName: string } } | null = null;
      let metadata: SpeFileMetadata | undefined;

      if (uploadResult.status === 'rejected') {
        failure = { error: uploadResult.reason?.message || 'Upload failed' };
      } else if (!uploadResult.value.success || !uploadResult.value.data) {
        failure = {
          error: uploadResult.value.error || 'Upload failed',
          nameConflict: uploadResult.value.nameConflict,
        };
      } else {
        metadata = uploadResult.value.data;
      }

      if (failure || !metadata) {
        failure ??= { error: 'Upload failed' };
        errors.push({ fileName: file.name, ...failure });

        // A name collision is an expected, user-resolvable outcome — log it at info, not error.
        if (failure.nameConflict) {
          this.logger.info('MultiFileUploadService', `Name collision — awaiting user choice: ${file.name}`);
        } else {
          this.logger.error('MultiFileUploadService', `Failed to upload: ${file.name}`, failure.error);
        }

        // Report progress: failed
        onProgress?.({
          current: i + 1,
          total: files.length,
          currentFileName: file.name,
          status: 'failed',
          error: failure.error,
          nameConflict: failure.nameConflict,
        });
        continue;
      }

      // Store SPE metadata
      uploadedFiles.push(metadata);

      // Report progress: complete
      onProgress?.({
        current: i + 1,
        total: files.length,
        currentFileName: file.name,
        status: 'complete',
      });
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
