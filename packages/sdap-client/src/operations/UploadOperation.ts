import { DriveItem, UploadSession } from '../types';
import { TokenProvider } from '../auth/TokenProvider';

export class UploadOperation {
    private static readonly CHUNK_SIZE = 320 * 1024; // 320 KB (Microsoft recommended)

    constructor(
        private readonly baseUrl: string,
        private readonly timeout: number,
        private readonly tokenProvider: TokenProvider
    ) {}

    /**
     * Upload small file (< 4MB) in single request.
     */
    public async uploadSmall(
        containerId: string,
        file: File,
        options?: { onProgress?: (percent: number) => void; signal?: AbortSignal }
    ): Promise<DriveItem> {
        const token = await this.tokenProvider.getToken();

        // Report initial progress
        options?.onProgress?.(0);

        const response = await fetch(
            `${this.baseUrl}/api/obo/containers/${containerId}/files/${encodeURIComponent(file.name)}`,
            {
                method: 'PUT',
                headers: {
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
                    'Content-Type': 'application/octet-stream',
                    'Content-Length': file.size.toString()
                },
                body: file,
                signal: options?.signal ?? AbortSignal.timeout(this.timeout)
            }
        );

        if (!response.ok) {
            const error = await this.parseError(response);
            throw new Error(`Upload failed: ${error}`);
        }

        const result = await response.json();

        // Report completion
        options?.onProgress?.(100);

        return result;
    }

    /**
     * Upload large file (≥ 4MB) using chunked upload.
     */
    public async uploadChunked(
        containerId: string,
        file: File,
        options?: { onProgress?: (percent: number) => void; signal?: AbortSignal }
    ): Promise<DriveItem> {
        // Step 1: Create upload session
        const session = await this.createUploadSession(containerId, file.name);

        // Step 2: Upload chunks
        let uploadedBytes = 0;

        while (uploadedBytes < file.size) {
            // Check for cancellation
            if (options?.signal?.aborted) {
                throw new Error('Upload cancelled');
            }

            const chunkStart = uploadedBytes;
            const chunkEnd = Math.min(chunkStart + UploadOperation.CHUNK_SIZE, file.size);
            const chunk = file.slice(chunkStart, chunkEnd);

            const result = await this.uploadChunk(session, chunk, chunkStart, chunkEnd, file.size);

            uploadedBytes = chunkEnd;

            // Report progress
            const percent = Math.round((uploadedBytes / file.size) * 100);
            options?.onProgress?.(percent);

            // If upload complete, return result
            if (result.completedItem) {
                return result.completedItem;
            }
        }

        throw new Error('Upload completed but no item returned');
    }

    private async createUploadSession(
        containerId: string,
        fileName: string
    ): Promise<UploadSession> {
        const token = await this.tokenProvider.getToken();

        // Get drive ID first
        const driveResponse = await fetch(
            `${this.baseUrl}/api/obo/containers/${containerId}/drive`,
            {
                headers: token ? { 'Authorization': `Bearer ${token}` } : {}
            }
        );

        if (!driveResponse.ok) {
            throw new Error('Failed to get container drive');
        }

        const drive = await driveResponse.json();

        // Create upload session
        const response = await fetch(
            `${this.baseUrl}/api/obo/drives/${drive.id}/upload-session?path=/${encodeURIComponent(fileName)}&conflictBehavior=rename`,
            {
                method: 'POST',
                headers: token ? { 'Authorization': `Bearer ${token}` } : {}
            }
        );

        if (!response.ok) {
            throw new Error('Failed to create upload session');
        }

        return await response.json();
    }

    private async uploadChunk(
        session: UploadSession,
        chunk: Blob,
        start: number,
        end: number,
        totalSize: number
    ): Promise<{ completedItem?: DriveItem }> {
        const response = await fetch(session.uploadUrl, {
            method: 'PUT',
            headers: {
                'Content-Range': `bytes ${start}-${end - 1}/${totalSize}`,
                'Content-Length': chunk.size.toString()
            },
            body: chunk
        });

        if (!response.ok && response.status !== 202) {
            throw new Error(`Chunk upload failed: ${response.statusText}`);
        }

        const result = await response.json();

        // Status 200/201 = upload complete
        if (response.status === 200 || response.status === 201) {
            return { completedItem: result };
        }

        // Status 202 = more chunks expected
        return {};
    }

    private async parseError(response: Response): Promise<string> {
        try {
            const error = await response.json();
            return error.detail || error.title || response.statusText;
        } catch {
            return response.statusText;
        }
    }
}
