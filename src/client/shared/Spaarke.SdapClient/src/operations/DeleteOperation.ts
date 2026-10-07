import { AuthenticatedFetchFn } from '../types';
import { requireAuthenticatedFetch, requestOrThrow } from './httpFailure';

export class DeleteOperation {
  constructor(
    private readonly baseUrl: string,
    private readonly timeout: number,
    private readonly authenticatedFetch?: AuthenticatedFetchFn
  ) {}

  /**
   * Delete file from SDAP.
   *
   * Auth is `authenticatedFetch` (@spaarke/auth, ADR-028). It replaced a `TokenProvider` shim that
   * returned '' and left this request with no Authorization header at all — see FAILURE-MODES AP-12.
   */
  public async delete(driveId: string, itemId: string, signal?: AbortSignal): Promise<void> {
    const authFetch = requireAuthenticatedFetch(this.authenticatedFetch, 'deleteFile');

    await requestOrThrow(
      authFetch,
      `${this.baseUrl}/api/obo/drives/${encodeURIComponent(driveId)}/items/${encodeURIComponent(itemId)}`,
      {
        method: 'DELETE',
        signal: signal ?? AbortSignal.timeout(this.timeout),
      },
      'Delete failed'
    );
  }
}
