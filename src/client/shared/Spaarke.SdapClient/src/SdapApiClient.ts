import {
    SdapClientConfig,
    DriveItem,
    FileMetadata
} from './types';
import { TokenProvider } from './auth/TokenProvider';
import { UploadOperation } from './operations/UploadOperation';
import { DownloadOperation } from './operations/DownloadOperation';
import { DeleteOperation } from './operations/DeleteOperation';

/**
 * SDAP API Client for file operations with SharePoint Embedded.
 *
 * Supports:
 * - Small file uploads (< 4MB)
 * - Chunked uploads (≥ 4MB) with progress tracking
 * - File downloads with streaming
 * - File deletion
 * - Metadata retrieval
 *
 * @example
 * ```typescript
 * const client = new SdapApiClient({
 *   baseUrl: 'https://spe-bff-api.azurewebsites.net',
 *   timeout: 300000
 * });
 *
 * // Upload file
 * const item = await client.uploadFile(containerId, file, {
 *   onProgress: (percent) => console.log(`${percent}% uploaded`)
 * });
 *
 * // Download file
 * const blob = await client.downloadFile(driveId, itemId);
 * ```
 */
export class SdapApiClient {
    private readonly baseUrl: string;
    private readonly timeout: number;
    private readonly tokenProvider: TokenProvider;
    private readonly uploadOp: UploadOperation;
    private readonly downloadOp: DownloadOperation;
    private readonly deleteOp: DeleteOperation;

    /**
     * Creates a new SDAP API client instance.
     *
     * @param config - Client configuration
     */
    constructor(config: SdapClientConfig) {
        this.validateConfig(config);

        this.baseUrl = config.baseUrl.replace(/\/$/, ''); // Remove trailing slash
        this.timeout = config.timeout ?? 300000; // 5 minutes default

        this.tokenProvider = new TokenProvider();
        this.uploadOp = new UploadOperation(this.baseUrl, this.timeout, this.tokenProvider);
        this.downloadOp = new DownloadOperation(this.baseUrl, this.timeout, this.tokenProvider);
        this.deleteOp = new DeleteOperation(this.baseUrl, this.timeout, this.tokenProvider);
    }

    /**
     * Uploads a file to SDAP.
     *
     * Automatically chooses small upload (< 4MB) or chunked upload (≥ 4MB).
     *
     * @param containerId - Container ID
     * @param file - File to upload
     * @param options - Upload options (progress callback, cancellation)
     * @returns Uploaded file metadata
     * @throws Error if upload fails
     */
    public async uploadFile(
        containerId: string,
        file: File,
        options?: {
            onProgress?: (percent: number) => void;
            signal?: AbortSignal;
        }
    ): Promise<DriveItem> {
        const SMALL_FILE_THRESHOLD = 4 * 1024 * 1024; // 4MB

        if (file.size < SMALL_FILE_THRESHOLD) {
            return await this.uploadOp.uploadSmall(containerId, file, options);
        } else {
            return await this.uploadOp.uploadChunked(containerId, file, options);
        }
    }

    /**
     * Downloads a file from SDAP.
     *
     * @param driveId - Drive ID
     * @param itemId - Item ID
     * @returns File blob
     * @throws Error if download fails
     */
    public async downloadFile(
        driveId: string,
        itemId: string
    ): Promise<Blob> {
        return await this.downloadOp.download(driveId, itemId);
    }

    /**
     * Deletes a file from SDAP.
     *
     * @param driveId - Drive ID
     * @param itemId - Item ID
     * @throws Error if deletion fails
     */
    public async deleteFile(
        driveId: string,
        itemId: string
    ): Promise<void> {
        return await this.deleteOp.delete(driveId, itemId);
    }

    /**
     * Gets file metadata from SDAP.
     *
     * @param driveId - Drive ID
     * @param itemId - Item ID
     * @returns File metadata
     * @throws Error if retrieval fails
     */
    public async getFileMetadata(
        driveId: string,
        itemId: string
    ): Promise<FileMetadata> {
        const token = await this.tokenProvider.getToken();

        const response = await fetch(
            `${this.baseUrl}/api/obo/drives/${driveId}/items/${itemId}`,
            {
                method: 'GET',
                headers: token ? { 'Authorization': `Bearer ${token}` } : {},
                signal: AbortSignal.timeout(this.timeout)
            }
        );

        if (!response.ok) {
            throw new Error(`Failed to get file metadata: ${response.statusText}`);
        }

        return await response.json();
    }

    private validateConfig(config: SdapClientConfig): void {
        if (!config.baseUrl) {
            throw new Error('baseUrl is required');
        }

        try {
            new URL(config.baseUrl);
        } catch {
            throw new Error('baseUrl must be a valid URL');
        }

        if (config.timeout !== undefined && config.timeout < 0) {
            throw new Error('timeout must be >= 0');
        }
    }
}
