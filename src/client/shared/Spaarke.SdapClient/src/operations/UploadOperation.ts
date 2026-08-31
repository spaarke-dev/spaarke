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
   * ⚠️ **Files >= 4 MiB have no working upload path, and did not before this change either.** The
   * server caps the small route at `PathValidator.SmallUploadMaxBytes` (4 MiB), and this was the
   * only alternative. {@link uploadFile} previously routed large files here and failed with a
   * misleading drive-resolution error; it now fails with an explicit, accurate message. Restoring
   * large-file upload needs a record-keyed upload-session route and is follow-up work.
   */
  public static readonly LARGE_FILE_UNSUPPORTED =
    'Files of 4 MiB or larger cannot be uploaded: the chunked upload path was removed in 2026-08 ' +
    'because it was non-functional (it depended on GET /api/obo/containers/{id}/drive, a route ' +
    'that does not exist). A record-keyed upload-session route is required to restore it.';

  private async parseError(response: Response): Promise<string> {
    try {
      const error = await response.json();
      return error.detail || error.title || response.statusText;
    } catch {
      return response.statusText;
    }
  }
}
