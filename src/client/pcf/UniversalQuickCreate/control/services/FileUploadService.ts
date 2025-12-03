/**
 * File Upload Service
 *
 * Orchestrates file upload to SharePoint Embedded (SPE) via SDAP BFF API.
 * Uses MSAL-enabled SdapApiClient for authentication.
 *
 * @version 1.0.0
 */

import { SdapApiClient } from './SdapApiClient';
import { SpeFileMetadata, ServiceResult } from '../types';
import { logger } from '../utils/logger';

/**
 * Request for uploading a file to SPE
 */
export interface FileUploadRequest {
    file: File;
    driveId: string;
    fileName?: string;
}

/**
 * Service for uploading files to SharePoint Embedded
 */
export class FileUploadService {
    constructor(private apiClient: SdapApiClient) {}

    /**
     * Upload a file to SharePoint Embedded
     *
     * @param request - File upload request
     * @returns Service result with SPE file metadata
     */
    async uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>> {
        try {
            logger.info('FileUploadService', 'Starting file upload', {
                fileName: request.file.name,
                fileSize: request.file.size,
                driveId: request.driveId
            });

            // Validate request
            if (!request.file) {
                return {
                    success: false,
                    error: 'No file provided'
                };
            }

            if (!request.driveId) {
                return {
                    success: false,
                    error: 'No drive ID provided'
                };
            }

            // Upload file via SDAP API (MSAL authentication automatic)
            const apiResponse = await this.apiClient.uploadFile({
                file: request.file,
                driveId: request.driveId,
                fileName: request.fileName || request.file.name
            });

            // Normalize API response to include convenience aliases
            const speMetadata: SpeFileMetadata = {
                ...apiResponse,
                driveItemId: apiResponse.id,
                fileName: apiResponse.name,
                sharePointUrl: apiResponse.webUrl || '',
                fileSize: apiResponse.size
            };

            logger.info('FileUploadService', 'File uploaded successfully', {
                fileName: speMetadata.fileName,
                driveItemId: speMetadata.driveItemId,
                sharePointUrl: speMetadata.sharePointUrl
            });

            return {
                success: true,
                data: speMetadata
            };
        } catch (error) {
            logger.error('FileUploadService', 'File upload failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown error occurred during file upload'
            };
        }
    }
}
