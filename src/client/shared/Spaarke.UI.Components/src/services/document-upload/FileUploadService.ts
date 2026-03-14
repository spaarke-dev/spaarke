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
      this.logger.error('FileUploadService', 'File upload failed', error);

      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error occurred during file upload',
      };
    }
  }
}
