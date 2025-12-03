/**
 * File Delete Service
 *
 * Handles deleting files from SharePoint Embedded and updating
 * Dataverse records to mark files as removed.
 */

import { SdapApiClient } from './SdapApiClient';
import type { ServiceResult } from '../types';
import { logger } from '../utils/logger';

/**
 * Service for deleting files and updating records
 */
export class FileDeleteService {
    constructor(
        private apiClient: SdapApiClient,
        private context: ComponentFramework.Context<unknown>
    ) {}

    /**
     * Delete file from SPE and update Dataverse record
     *
     * Order matters:
     * 1. Delete file from SharePoint Embedded first
     * 2. Then update Dataverse record to mark hasFile = false
     *
     * This prevents orphaned files if Dataverse update fails.
     *
     * @param documentId - Dataverse document record ID
     * @param driveId - Graph API Drive ID (from sprk_graphdriveid)
     * @param itemId - Graph API Item ID (from sprk_graphitemid)
     * @param fileName - File name for logging
     * @returns ServiceResult indicating success or failure
     */
    async deleteFile(
        documentId: string,
        driveId: string,
        itemId: string,
        fileName: string
    ): Promise<ServiceResult> {
        try {
            logger.info('FileDeleteService', `Deleting file: ${fileName}`, {
                documentId,
                driveId,
                itemId
            });

            // Step 1: Delete file from SharePoint Embedded
            await this.apiClient.deleteFile({ driveId, itemId });
            logger.debug('FileDeleteService', 'File deleted from SPE');

            // Step 2: Update Dataverse record to clear file metadata
            await this.context.webAPI.updateRecord(
                'sprk_document',
                documentId,
                {
                    sprk_hasfile: false,
                    sprk_graphitemid: null,
                    sprk_filesize: null,
                    sprk_createddatetime: null,
                    sprk_lastmodifieddatetime: null,
                    sprk_etag: null,
                    sprk_filepath: null
                }
            );
            logger.info('FileDeleteService', 'Dataverse record updated (hasFile=false)');

            return {
                success: true
            };

        } catch (error) {
            logger.error('FileDeleteService', 'Delete failed', error);

            // Check if we got a partial failure
            const errorMessage = error instanceof Error ? error.message : 'Unknown delete error';

            // If file was deleted but Dataverse update failed, log it
            if (errorMessage.includes('updateRecord')) {
                logger.error('FileDeleteService', 'PARTIAL FAILURE: File deleted from SPE but Dataverse update failed');
            }

            return {
                success: false,
                error: errorMessage
            };
        }
    }

    /**
     * Delete multiple files (batch operation)
     *
     * @param files - Array of file metadata to delete
     * @returns ServiceResult with count of successful deletes
     */
    async deleteMultipleFiles(
        files: { documentId: string; driveId: string; itemId: string; fileName: string }[]
    ): Promise<ServiceResult<{ successCount: number; failureCount: number }>> {
        logger.info('FileDeleteService', `Deleting ${files.length} files`);

        let successCount = 0;
        let failureCount = 0;

        for (const file of files) {
            const result = await this.deleteFile(
                file.documentId,
                file.driveId,
                file.itemId,
                file.fileName
            );

            if (result.success) {
                successCount++;
            } else {
                failureCount++;
                logger.warn('FileDeleteService', `Failed to delete: ${file.fileName}`);
            }

            // Small delay between deletes
            if (files.length > 1) {
                await new Promise(resolve => setTimeout(resolve, 200));
            }
        }

        logger.info('FileDeleteService', `Delete complete: ${successCount} success, ${failureCount} failed`);

        return {
            success: failureCount === 0,
            data: { successCount, failureCount },
            error: failureCount > 0 ? `${failureCount} file(s) failed to delete` : undefined
        };
    }
}
