import { DriveItem, AuthenticatedFetchFn } from '../types';
import { requireAuthenticatedFetch, requestOrThrow } from './httpFailure';

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
    private readonly authenticatedFetch?: AuthenticatedFetchFn
  ) {}

  /**
   * Upload against the OWNING RECORD. The server resolves the container from that record.
   *
   * This is the task-076 contract. The caller names the record it is already authorized against and
   * the server derives the container from it, so the authorization key and the container are the
   * same value by construction and cannot disagree. There is deliberately NO container parameter.
   *
   * Refusals are the contract, not faults: a secure record with no container of its own fails closed
   * (`secure_record_container_missing`), an unresolvable record 404s, and a non-secure record whose
   * business unit has no container returns 409. None of them fall back to a shared container.
   */
  public async uploadSmallForRecord(
    entityLogicalName: string,
    recordId: string,
    file: File,
    options?: {
      onProgress?: (percent: number) => void;
      signal?: AbortSignal;
      conflictBehavior?: ConflictBehaviorOption;
    }
  ): Promise<DriveItem> {
    return this.put(
      `/api/obo/records/${encodeURIComponent(entityLogicalName)}/${encodeURIComponent(recordId)}/files/${encodeURIComponent(file.name)}`,
      file,
      options
    );
  }

  /**
   * Upload content that has NO OWNING RECORD YET. The server resolves the container from the ACTING
   * USER's business unit.
   *
   * For the three flows where the bytes genuinely move before any record exists — an EmailComposer
   * local attachment, the Analysis wizard's standalone document, and DocumentUploadWizard's "skip
   * associate". Per the owner's 2026-08-28 resolution order.
   *
   * ⚠️ This is NOT a general-purpose escape hatch, and it is not "upload without authorization". If
   * the content HAS an owning record, use {@link uploadSmallForRecord} — routing it here would place
   * it in the caller's business-unit container rather than the record's, which for a secure record is
   * provably the wrong container and cannot be undone (SPE permissions are additive-only).
   */
  public async uploadSmallWithoutRecord(
    file: File,
    options?: {
      onProgress?: (percent: number) => void;
      signal?: AbortSignal;
      conflictBehavior?: ConflictBehaviorOption;
    }
  ): Promise<DriveItem> {
    return this.put(`/api/obo/me/files/${encodeURIComponent(file.name)}`, file, options);
  }

  /**
   * Shared transport for both record-keyed and record-less uploads.
   *
   * Extracted so the two contracts cannot drift in how they authenticate, report progress, encode
   * `conflictBehavior`, or translate a 409 — that drift is exactly what produced four divergent
   * copies of the association switch on the server side.
   */
  private async put(
    routePath: string,
    file: File,
    options?: {
      onProgress?: (percent: number) => void;
      signal?: AbortSignal;
      conflictBehavior?: ConflictBehaviorOption;
    }
  ): Promise<DriveItem> {
    const authFetch = requireAuthenticatedFetch(this.authenticatedFetch, 'uploadFile');

    options?.onProgress?.(0);

    const query = options?.conflictBehavior ? `?conflictBehavior=${encodeURIComponent(options.conflictBehavior)}` : '';

    const response = await requestOrThrow(
      authFetch,
      `${this.baseUrl}${routePath}${query}`,
      {
        method: 'PUT',
        headers: { 'Content-Type': 'application/octet-stream' },
        body: file,
        signal: options?.signal ?? AbortSignal.timeout(this.timeout),
      },
      'Upload failed',
      status => {
        if (status === 409) {
          throw new UploadNameConflictError(file.name);
        }
      }
    );

    const result = await response.json();
    options?.onProgress?.(100);
    return result;
  }

  /**
   * 🔴 `uploadSmall(containerId, file, options)` was DELETED here 2026-09-03
   * (unified-access-control-r2 task 076), together with `SdapApiClient.uploadFile` and the BFF route
   * it called, `PUT /api/obo/containers/{id}/files/{*path}`.
   *
   * The caller named the CONTAINER and the server wrote there, with no per-resource authorization
   * decision behind the destination. Use {@link uploadSmallForRecord} when the content has an owning
   * record, or {@link uploadSmallWithoutRecord} when it genuinely does not. Both are above, both
   * share {@link put}, and NEITHER takes a container — deliberately.
   *
   * Note the `Content-Length` gotcha this method carried, because {@link put} inherits it: the
   * header is deliberately NOT set. It is a forbidden header name, so the browser ignores any value
   * and computes the real one from the body.
   */

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
}
