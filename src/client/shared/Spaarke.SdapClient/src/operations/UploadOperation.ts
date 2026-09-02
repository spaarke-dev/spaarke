import { DriveItem } from '../types';
import { TokenProvider } from '../auth/TokenProvider';

// `UploadSession` and the 320 KB CHUNK_SIZE constant were dropped from this file on 2026-08-27 with
// the chunked path (task 076). The `UploadSession` TYPE is deliberately left in ../types and in the
// package barrel: it describes Graph's own upload-session shape, and a future record-keyed
// upload-session route will need it. Deleting a type that has to come straight back is churn.
/**
 * Name-collision behaviour for an upload, mirroring Graph's `@microsoft.graph.conflictBehavior`.
 *
 * There are deliberately only two values a UI offers:
 *   - `rename`  — keep both; the server stores the new file under a non-colliding name
 *   - `replace` — save as a new version; SharePoint retains the prior content, so it stays recoverable
 *
 * `fail` exists for completeness but is the SERVER's default, so a caller normally omits the option
 * entirely and handles {@link UploadNameConflictError}. There is no "replace and discard" — at the
 * Graph level that is the same call as `replace`; a user who wants the old document gone deletes it.
 */
export type ConflictBehaviorOption = 'fail' | 'rename' | 'replace';

/**
 * Thrown when an upload would collide with an existing file of the same name.
 *
 * ⚠️ Reaching this means **nothing was overwritten** — the BFF uploads with
 * `conflictBehavior=fail` unless told otherwise, so the existing file is intact and the user can
 * safely be asked what to do. Retry the SAME upload with `conflictBehavior: 'rename' | 'replace'`.
 *
 * Before 2026-09-02 there was no such type: a collision silently replaced the stored bytes and the
 * user instead saw a Dataverse 412 titled "Duplicate Record" from the follow-on document insert,
 * by which point the original content was already gone. Catch this by `instanceof`, never by
 * matching a message string.
 */
export class UploadNameConflictError extends Error {
  public readonly fileName: string;

  constructor(fileName: string) {
    super(`A file named "${fileName}" already exists in this location.`);
    this.name = 'UploadNameConflictError';
    this.fileName = fileName;
    // Required for `instanceof` to survive the ES5 downlevel target some consumers build with.
    Object.setPrototypeOf(this, UploadNameConflictError.prototype);
  }
}

export class UploadOperation {
  constructor(
    private readonly baseUrl: string,
    private readonly timeout: number,
    private readonly tokenProvider: TokenProvider
  ) {}

  /**
   * Upload a file in a single request (Graph simple PUT — up to 250 MB).
   */
  public async uploadSmall(
    containerId: string,
    file: File,
    options?: {
      onProgress?: (percent: number) => void;
      signal?: AbortSignal;
      /**
       * Name-collision behaviour. Omitted ⇒ the BFF defaults to `fail`, which returns 409 and
       * leaves the existing file untouched. Pass `rename` or `replace` only after the USER has
       * chosen — see UploadNameConflictError.
       */
      conflictBehavior?: ConflictBehaviorOption;
    }
  ): Promise<DriveItem> {
    const token = await this.tokenProvider.getToken();

    // Report initial progress
    options?.onProgress?.(0);

    const query = options?.conflictBehavior ? `?conflictBehavior=${encodeURIComponent(options.conflictBehavior)}` : '';

    const response = await fetch(
      `${this.baseUrl}/api/obo/containers/${containerId}/files/${encodeURIComponent(file.name)}${query}`,
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

    // A name collision is a DISTINCT, RECOVERABLE outcome, not a generic failure. It must be
    // distinguishable by type rather than by string-matching a message, so the UI can offer the
    // rename / new-version choice. Nothing was overwritten to get here — the BFF sends
    // conflictBehavior=fail unless the caller says otherwise.
    if (response.status === 409) {
      throw new UploadNameConflictError(file.name);
    }

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
