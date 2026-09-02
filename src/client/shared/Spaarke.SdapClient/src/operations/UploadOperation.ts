import { DriveItem } from '../types';
import { TokenProvider } from '../auth/TokenProvider';

// `UploadSession` and the 320 KB CHUNK_SIZE constant were dropped from this file on 2026-08-27 with
// the chunked path (task 076). The `UploadSession` TYPE is deliberately left in ../types and in the
// package barrel: it describes Graph's own upload-session shape, and a future record-keyed
// upload-session route will need it. Deleting a type that has to come straight back is churn.
export class UploadOperation {
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
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          'Content-Type': 'application/octet-stream',
          'Content-Length': file.size.toString(),
        },
        body: file,
        signal: options?.signal ?? AbortSignal.timeout(this.timeout),
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
   * DELETED 2026-08-27 (unified-access-control-r2 task 076): `uploadChunked`,
   * `createUploadSession` and `uploadChunk`.
   *
   * The chunked path never worked. `createUploadSession` began by calling
   * `GET /api/obo/containers/{id}/drive` to obtain a drive id, and **that route is mapped nowhere
   * in the BFF** — so the first request 404'd and the method threw `'Failed to get container drive'`
   * before it ever reached the upload-session call. Its two server routes
   * (`POST /api/obo/drives/{driveId}/upload-session`, `PUT /api/obo/upload-session/chunk`) are
   * deleted in the same change.
   *
   * `uploadChunk` did not even use the BFF chunk route — it PUT directly to Graph's own
   * `session.uploadUrl` — so that route had no client at all.
   *
   * ⚠️ **Corrected 2026-09-02 — the 4 MiB ceiling described here never existed on the server.**
   * This block used to claim "the small route is capped at `PathValidator.SmallUploadMaxBytes`
   * (4 MiB)". That constant is referenced by NOTHING but a comment, and the guard that once enforced
   * it in `UploadSessionManager.UploadSmallAsUserAsync` was deleted by `spaarkeai-compose-r8` task
   * 015 (FR-S08) as a stale Graph limit. The simple `PUT .../content` this operation uses has
   * accepted up to **250 MB** since October 2023, so files between 4 MiB and 250 MB were being
   * refused by this client alone, for no server-side reason.
   *
   * Above 250 MB a caller genuinely does need a resumable session. The BFF exposes one:
   *
   *     POST /api/obo/records/{entityLogicalName}/{recordId}/upload-session
   *
   * This client is still not wired to it, and that remains blocked on the owner decision recorded in
   * `projects/unified-access-control-r2/notes/task-076-record-keyed-upload-contract.md` §5 (three
   * upload paths have no owning record at the moment the bytes move, so `(entityLogicalName,
   * recordId)` cannot be supplied). That is now a >250 MB concern rather than a >4 MiB one.
   */
  public static fileTooLarge(actualBytes: number, maxBytes: number): string {
    const mb = (n: number) => `${(n / (1024 * 1024)).toFixed(1)} MB`;
    return (
      `This file is ${mb(actualBytes)}, which exceeds the ${mb(maxBytes)} maximum for a single ` +
      `upload. Files larger than ${mb(maxBytes)} need a resumable upload session, which this ` +
      `client does not yet support. Try splitting the file or compressing it.`
    );
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
