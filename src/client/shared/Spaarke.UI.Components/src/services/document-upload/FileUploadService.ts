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
import { consoleLogger } from './types';
import { UploadNameConflictError } from '@spaarke/sdap-client';

/**
 * Service for uploading files to SharePoint Embedded.
 */
export class FileUploadService {
  private readonly logger: ILogger;

  constructor(
    private readonly apiClient: SdapApiClient,
    logger?: ILogger
  ) {
    this.logger = logger ?? consoleLogger;
  }

  /**
   * Upload a file to SharePoint Embedded.
   *
   * @param request - File upload request
   * @returns Service result with SPE file metadata
   */
  async uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>> {
    try {
      this.logger.info('FileUploadService', 'Starting file upload', {
        fileName: request.file.name,
        fileSize: request.file.size,
        driveId: request.driveId,
      });

      // Validate request
      if (!request.file) {
        return { success: false, error: 'No file provided' };
      }

      if (!request.driveId) {
        return { success: false, error: 'No drive ID provided' };
      }

      // Upload file via SDAP API (authentication handled by ITokenProvider)
      const apiResponse = await this.apiClient.uploadFile({
        file: request.file,
        driveId: request.driveId,
        fileName: request.fileName || request.file.name,
        conflictBehavior: request.conflictBehavior,
      });

      // Normalize API response to include convenience aliases
      const speMetadata: SpeFileMetadata = {
        ...apiResponse,
        driveItemId: apiResponse.id,
        fileName: apiResponse.name,
        sharePointUrl: apiResponse.webUrl || '',
        fileSize: apiResponse.size,
      };

      this.logger.info('FileUploadService', 'File uploaded successfully', {
        fileName: speMetadata.fileName,
        driveItemId: speMetadata.driveItemId,
        sharePointUrl: speMetadata.sharePointUrl,
      });

      return { success: true, data: speMetadata };
    } catch (error) {
      // A name collision is surfaced as a DISTINCT result, not folded into `error`. Nothing was
      // written, so the caller can offer rename / save-as-new-version and retry the same upload
      // with `conflictBehavior`. Folding it into the generic error string is how this previously
      // reached the user as "Unknown error occurred" — after the original file had already been
      // overwritten by the old conflictBehavior=replace default.
      if (error instanceof UploadNameConflictError) {
        this.logger.info('FileUploadService', 'Upload blocked by a name collision', {
          fileName: error.fileName,
        });
        return {
          success: false,
          error: error.message,
          nameConflict: { fileName: error.fileName },
        };
      }

      this.logger.error('FileUploadService', 'File upload failed', error);

      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error occurred during file upload',
      };
    }
  }
}
