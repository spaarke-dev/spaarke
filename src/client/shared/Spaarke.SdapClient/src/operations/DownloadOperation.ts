import { AuthenticatedFetchFn } from '../types';
import { requireAuthenticatedFetch, requestOrThrow } from './httpFailure';

export class DownloadOperation {
  constructor(
    private readonly baseUrl: string,
    private readonly timeout: number,
    private readonly authenticatedFetch?: AuthenticatedFetchFn
  ) {}

  /**
   * Download file from SDAP.
   *
   * Auth is `authenticatedFetch` (@spaarke/auth, ADR-028). It replaced a `TokenProvider` shim that
   * returned '' and left this request with no Authorization header at all — see FAILURE-MODES AP-12.
   */
  public async download(driveId: string, itemId: string, signal?: AbortSignal): Promise<Blob> {
    const authFetch = requireAuthenticatedFetch(this.authenticatedFetch, 'downloadFile');

    const response = await requestOrThrow(
      authFetch,
      `${this.baseUrl}/api/obo/drives/${encodeURIComponent(driveId)}/items/${encodeURIComponent(itemId)}/content`,
      {
        method: 'GET',
        signal: signal ?? AbortSignal.timeout(this.timeout),
      },
      'Download failed'
    );

    return await response.blob();
  }
}
