/**
 * File Replace Service
 *
 * Handles replacing files in SharePoint Embedded and updating
 * Dataverse records with new file metadata.
 */

import { SdapApiClient } from './SdapApiClient';
import type { ServiceResult, SpeFileMetadata } from '../types';
import { logger } from '../utils/logger';

/**
 * Service for replacing files
 */
export class FileReplaceService {
    constructor(
        private apiClient: SdapApiClient,
        private context: ComponentFramework.Context<unknown>
    ) {}

    /**
     * Show file picker and replace existing file
     *
     * @param documentId - Dataverse document record ID
     * @param driveId - Graph API Drive ID (from sprk_graphdriveid)
     * @param itemId - Graph API Item ID (from sprk_graphitemid)
     * @returns ServiceResult indicating success or failure
     */
    async pickAndReplaceFile(
        documentId: string,
        driveId: string,
        itemId: string
    ): Promise<ServiceResult> {
        return new Promise((resolve) => {
            logger.info('FileReplaceService', 'Showing file picker for replace');

            // Create hidden file input
            const input = document.createElement('input');
            input.type = 'file';
            input.style.display = 'none';

            input.onchange = async () => {
                const file = input.files?.[0];

                if (!file) {
                    logger.warn('FileReplaceService', 'No file selected');
                    resolve({
                        success: false,
                        error: 'No file selected'
                    });
                    document.body.removeChild(input);
                    return;
                }

                const result = await this.replaceFile(documentId, driveId, itemId, file);
                resolve(result);

                // Cleanup
                document.body.removeChild(input);
            };

            input.oncancel = () => {
                logger.debug('FileReplaceService', 'Replace cancelled by user');
                resolve({
                    success: false,
                    error: 'Replace cancelled by user'
                });
                document.body.removeChild(input);
            };

            // Trigger file picker
            document.body.appendChild(input);
            input.click();
        });
    }

    /**
     * Replace existing file with new version
     *
     * Workflow:
     * 1. Call SdapApiClient.replaceFile() (handles delete + upload)
     * 2. Receive FileHandleDto with new metadata
     * 3. Update Dataverse record with new metadata
     *
     * @param documentId - Dataverse document record ID
     * @param driveId - Graph API Drive ID
     * @param itemId - Graph API Item ID (old file)
     * @param newFile - New file to upload
     * @returns ServiceResult indicating success or failure
     */
    async replaceFile(
        documentId: string,
        driveId: string,
        itemId: string,
        newFile: File
    ): Promise<ServiceResult> {
        try {
            logger.info('FileReplaceService', `Replacing file with: ${newFile.name}`, {
                documentId,
                driveId,
                oldItemId: itemId
            });

            // Step 1: Call API to replace file (delete old + upload new)
            const fileMetadata: SpeFileMetadata = await this.apiClient.replaceFile({
                driveId,
                itemId,
                file: newFile,
                fileName: newFile.name
            });

            logger.debug('FileReplaceService', 'File replaced in SPE', fileMetadata);

            // Step 2: Update Dataverse record with new metadata
            await this.context.webAPI.updateRecord(
                'sprk_document',
                documentId,
                {
                    sprk_filename: fileMetadata.name,
                    sprk_filesize: fileMetadata.size,
                    sprk_graphitemid: fileMetadata.id,
                    sprk_createddatetime: fileMetadata.createdDateTime,
                    sprk_lastmodifieddatetime: fileMetadata.lastModifiedDateTime,
                    sprk_etag: fileMetadata.eTag,
                    sprk_filepath: fileMetadata.webUrl,
                    sprk_parentfolderid: fileMetadata.parentId,
                    sprk_hasfile: true,
                    sprk_mimetype: newFile.type || 'application/octet-stream'
                }
            );

            logger.info('FileReplaceService', 'Dataverse record updated with new metadata');

            return {
                success: true
            };

        } catch (error) {
            logger.error('FileReplaceService', 'Replace failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown replace error'
            };
        }
    }
}
