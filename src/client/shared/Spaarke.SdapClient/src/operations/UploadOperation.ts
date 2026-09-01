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
   * ⚠️ **Files >= 4 MiB still have no working path FROM THIS CLIENT** — but the server side of the
   * gap is now closed. The small route is capped at `PathValidator.SmallUploadMaxBytes` (4 MiB), and
   * the BFF now exposes a working replacement for the chunked path:
   *
   *     POST /api/obo/records/{entityLogicalName}/{recordId}/upload-session
   *
   * It returns Graph's own upload-session URL, which a client PUTs chunks to directly — the same
   * mechanism the deleted `uploadChunk` used, minus the `GET /api/obo/containers/{id}/drive` call
   * that never existed and made the old path dead on arrival.
   *
   * This client has NOT been wired to it because the cutover is blocked on an owner decision: three
   * upload paths in the wider codebase have no owning record at the moment the bytes move, so
   * `(entityLogicalName, recordId)` cannot be supplied for them. See
   * `projects/unified-access-control-r2/notes/task-076-record-keyed-upload-contract.md` §5.
   * Leaving an honest error beats shipping a half-working upload.
   */
  public static readonly LARGE_FILE_UNSUPPORTED =
    'Files of 4 MiB or larger cannot be uploaded by this client. The BFF supports large uploads via ' +
    'POST /api/obo/records/{entityLogicalName}/{recordId}/upload-session, but this client has not ' +
    'been wired to it yet — the record-keyed cutover is pending (unified-access-control-r2 task 076).';

  private async parseError(response: Response): Promise<string> {
    try {
      const error = await response.json();
      return error.detail || error.title || response.statusText;
    } catch {
      return response.statusText;
    }
  }
}
