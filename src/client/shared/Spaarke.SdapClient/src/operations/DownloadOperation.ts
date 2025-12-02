import { TokenProvider } from '../auth/TokenProvider';

export class DownloadOperation {
    constructor(
        private readonly baseUrl: string,
        private readonly timeout: number,
        private readonly tokenProvider: TokenProvider
    ) {}

    /**
     * Download file from SDAP.
     */
    public async download(driveId: string, itemId: string): Promise<Blob> {
        const token = await this.tokenProvider.getToken();

        const response = await fetch(
            `${this.baseUrl}/api/obo/drives/${driveId}/items/${itemId}/content`,
            {
                method: 'GET',
                headers: token ? { 'Authorization': `Bearer ${token}` } : {},
                signal: AbortSignal.timeout(this.timeout)
            }
        );

        if (!response.ok) {
            throw new Error(`Download failed: ${response.statusText}`);
        }

        return await response.blob();
    }
}
