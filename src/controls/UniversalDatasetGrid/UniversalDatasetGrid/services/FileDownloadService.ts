/**
 * File Download Service
 *
 * Handles downloading files from SharePoint Embedded via SDAP API
 * and triggering browser downloads.
 */

import { SdapApiClient } from './SdapApiClient';
import type { ServiceResult } from '../types';
import { logger } from '../utils/logger';

/**
 * Service for downloading files from SharePoint Embedded
 */
export class FileDownloadService {
    constructor(private apiClient: SdapApiClient) {}

    /**
     * Download file from SharePoint Embedded and trigger browser download
     *
     * @param driveId - Graph API Drive ID (from sprk_graphdriveid)
     * @param itemId - Graph API Item ID (from sprk_graphitemid)
     * @param fileName - Display name for downloaded file
     * @returns ServiceResult indicating success or failure
     */
    async downloadFile(
        driveId: string,
        itemId: string,
        fileName: string
    ): Promise<ServiceResult> {
        try {
            logger.info('FileDownloadService', `Downloading file: ${fileName}`, {
                driveId,
                itemId
            });

            // Step 1: Download file blob from SDAP API
            const blob = await this.apiClient.downloadFile({ driveId, itemId });

            logger.debug('FileDownloadService', `Received blob: ${blob.size} bytes`);

            // Step 2: Create blob URL for download
            const url = URL.createObjectURL(blob);

            // Step 3: Create temporary anchor element for download
            const link = document.createElement('a');
            link.href = url;
            link.download = fileName;
            link.style.display = 'none';

            // Step 4: Add to DOM and trigger download
            document.body.appendChild(link);
            link.click();

            // Step 5: Cleanup blob URL and anchor element
            // Delay cleanup to ensure browser has read the blob URL
            setTimeout(() => {
                URL.revokeObjectURL(url);
                document.body.removeChild(link);
                logger.debug('FileDownloadService', 'Cleanup complete');
            }, 100);

            logger.info('FileDownloadService', `Download triggered successfully: ${fileName}`);

            return {
                success: true
            };

        } catch (error) {
            logger.error('FileDownloadService', 'Download failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown download error'
            };
        }
    }

    /**
     * Download multiple files (creates separate downloads for each)
     *
     * @param files - Array of file metadata with driveId, itemId, fileName
     * @returns ServiceResult with count of successful downloads
     */
    async downloadMultipleFiles(
        files: { driveId: string; itemId: string; fileName: string }[]
    ): Promise<ServiceResult<{ successCount: number; failureCount: number }>> {
        logger.info('FileDownloadService', `Downloading ${files.length} files`);

        let successCount = 0;
        let failureCount = 0;

        for (const file of files) {
            const result = await this.downloadFile(
                file.driveId,
                file.itemId,
                file.fileName
            );

            if (result.success) {
                successCount++;
            } else {
                failureCount++;
                logger.warn('FileDownloadService', `Failed to download: ${file.fileName}`);
            }

            // Small delay between downloads to avoid overwhelming the browser
            if (files.length > 1) {
                await new Promise(resolve => setTimeout(resolve, 200));
            }
        }

        logger.info('FileDownloadService', `Download complete: ${successCount} success, ${failureCount} failed`);

        return {
            success: failureCount === 0,
            data: { successCount, failureCount },
            error: failureCount > 0 ? `${failureCount} file(s) failed to download` : undefined
        };
    }
}
