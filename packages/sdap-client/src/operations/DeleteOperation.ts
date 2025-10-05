import { TokenProvider } from '../auth/TokenProvider';

export class DeleteOperation {
    constructor(
        private readonly baseUrl: string,
        private readonly timeout: number,
        private readonly tokenProvider: TokenProvider
    ) {}

    /**
     * Delete file from SDAP.
     */
    public async delete(driveId: string, itemId: string): Promise<void> {
        const token = await this.tokenProvider.getToken();

        const response = await fetch(
            `${this.baseUrl}/api/obo/drives/${driveId}/items/${itemId}`,
            {
                method: 'DELETE',
                headers: token ? { 'Authorization': `Bearer ${token}` } : {},
                signal: AbortSignal.timeout(this.timeout)
            }
        );

        if (!response.ok) {
            throw new Error(`Delete failed: ${response.statusText}`);
        }
    }
}
