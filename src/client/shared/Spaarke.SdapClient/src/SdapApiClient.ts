import {
  SdapClientConfig,
  DriveItem,
  FileMetadata,
  AuthenticatedFetchFn,
  IndexFileRequest,
  IndexFileResult,
} from './types';
import { UploadOperation, type ConflictBehaviorOption } from './operations/UploadOperation';
import { DownloadOperation } from './operations/DownloadOperation';
import { DeleteOperation } from './operations/DeleteOperation';
import { IndexFileOperation } from './operations/IndexFileOperation';
import { requireAuthenticatedFetch, requestOrThrow } from './operations/httpFailure';

/**
 * SDAP API Client for file operations with SharePoint Embedded.
 *
 * Supports:
 * - Single-request file uploads (Graph simple PUT — up to 250 MB)
 * - File downloads
 * - File deletion
 * - Sync OBO indexing into Azure AI Search
 *
 * 🔴 **Corrected 2026-09-02.** This list used to advertise "Small file uploads (< 4MB)" and
 * "Chunked uploads (≥ 4MB) with progress tracking". Both were false: the chunked path was DELETED
 * (task 076 — its first request hit a route the BFF maps nowhere, so it never worked), and the 4 MB
 * figure came from retired OneDrive REST docs. The simple PUT has accepted up to 250 MB since
 * October 2023. Above 250 MB a resumable session is genuinely required and this client does not yet
 * wire one. Do not re-derive the 4 MB claim from this file's history — see FAILURE-MODES AP-12.
 *
 * @example
 * ```typescript
 * const client = new SdapApiClient({
 *   baseUrl: 'https://spe-bff-api.azurewebsites.net',
 *   authenticatedFetch,
 * });
 *
 * // Upload against the owning record — the server resolves the container from it.
 * const item = await client.uploadFileForRecord('sprk_matter', matterId, file, {
 *   onProgress: (percent) => console.log(`${percent}% uploaded`)
 * });
 *
 * // Download file
 * const blob = await client.downloadFile(driveId, itemId);
 * ```
 */
export class SdapApiClient {
  private readonly baseUrl: string;
  private readonly timeout: number;
  private readonly authenticatedFetch?: AuthenticatedFetchFn;
  private readonly uploadOp: UploadOperation;
  private readonly downloadOp: DownloadOperation;
  private readonly deleteOp: DeleteOperation;
  private readonly indexFileOp?: IndexFileOperation;

  /**
   * Creates a new SDAP API client instance.
   *
   * @param config - Client configuration. `authenticatedFetch` (from `@spaarke/auth`, ADR-028) is
   *   required by EVERY operation on this client — upload, download, delete and {@link indexFile}.
   *   Operations throw a named error if it is absent rather than issuing an unauthenticated
   *   request. It is typed optional only so a consumer can construct the client before auth is
   *   initialised; there is no unauthenticated mode.
   */
  constructor(config: SdapClientConfig & { authenticatedFetch?: AuthenticatedFetchFn }) {
    this.validateConfig(config);

    this.baseUrl = config.baseUrl.replace(/\/$/, ''); // Remove trailing slash
    this.timeout = config.timeout ?? 300000; // 5 minutes default
    this.authenticatedFetch = config.authenticatedFetch;

    // Every operation now authenticates through `authenticatedFetch` (ADR-028). Previously
    // upload/download/delete shared a `TokenProvider` shim that returned '' — so they sent NO
    // Authorization header to a RequireAuthorization BFF. Passing it here (rather than only to
    // indexFile) is what makes those three operations usable at all.
    this.uploadOp = new UploadOperation(this.baseUrl, this.timeout, this.authenticatedFetch);
    this.downloadOp = new DownloadOperation(this.baseUrl, this.timeout, this.authenticatedFetch);
    this.deleteOp = new DeleteOperation(this.baseUrl, this.timeout, this.authenticatedFetch);

    if (this.authenticatedFetch) {
      this.indexFileOp = new IndexFileOperation(this.baseUrl, this.timeout, this.authenticatedFetch);
    }
  }

  /**
   * Triggers sync OBO indexing of a SPE-resident file into Azure AI Search.
   * Call after a successful upload to make the file searchable in
   * `spaarke-files-index`.
   *
   * Requires `authenticatedFetch` in the constructor config (Spaarke Auth v2).
   *
   * @param request - File identifiers, tenant, optional parent + index targeting
   * @returns Indexing outcome (non-throwing for HTTP failures — inspect `success`)
   * @throws Error if `authenticatedFetch` was not provided at construction
   */
  public async indexFile(request: IndexFileRequest): Promise<IndexFileResult> {
    if (!this.indexFileOp) {
      throw new Error(
        'SdapApiClient.indexFile requires `authenticatedFetch` in the client config. ' +
          'Pass `authenticatedFetch` from `@spaarke/auth` when constructing the client.'
      );
    }
    return this.indexFileOp.indexFile(request);
  }

  /**
   * 🔴 `uploadFile(containerId, file, options)` was DELETED here 2026-09-03 (unified-access-control-r2
   * task 076), together with `UploadOperation.uploadSmall` and the BFF route they called,
   * `PUT /api/obo/containers/{id}/files/{*path}`.
   *
   * The caller named a CONTAINER, and the server obeyed it with no per-resource authorization
   * decision behind it. For a SECURE record that meant its documents could be written into the
   * shared business-unit container — and SPE permissions are additive-only, so nothing retracts that
   * afterwards. It is replaced, not renamed:
   *
   *   · content WITH an owning record  -> {@link uploadFileForRecord}
   *   · content with genuinely NONE    -> {@link uploadFileWithoutRecord}
   *
   * ⚠️ Do not reintroduce a container parameter on either of those "just for one caller". That is
   * the shape this deletion removed.
   */

  /**
   * The Graph simple-PUT ceiling, enforced in ONE place for both upload methods.
   *
   * Extracted 2026-09-03 with the record-keyed/record-less pair, so the two cannot drift on the
   * limit. This project has already deleted THREE separate copies of a 4 MiB ceiling that no server
   * ever enforced (`PathValidator.SmallUploadMaxBytes`, the client constant, and
   * `CHUNKED_UPLOAD_THRESHOLD_BYTES`) — a fourth divergence is the predictable next instance.
   *
   * 250 MB is the real, current boundary for `PUT /drives/{d}/root:/{path}:/content` (4 MB ->
   * 25 MB -> 256 MB -> 250 MB across Oct 2023; stable since) and SharePoint Embedded documents the
   * same figure for containers. Verified against MS Learn + the docs source repos, 2026-08-20:
   * src/server/api/Sprk.Bff.Api/.claude/agent-memory/researcher/graph-driveitem-upload-facts.md
   */
  private guardSimpleUploadSize(file: File): void {
    const SIMPLE_UPLOAD_MAX_BYTES = 250 * 1024 * 1024; // 250 MB — Graph simple-PUT ceiling

    if (file.size > SIMPLE_UPLOAD_MAX_BYTES) {
      // Fails only where Graph itself would refuse. Above this a caller genuinely needs a resumable
      // upload session; the record-keyed one is at POST /api/obo/records/{entity}/{id}/upload-session.
      throw new Error(UploadOperation.fileTooLarge(file.size, SIMPLE_UPLOAD_MAX_BYTES));
    }
  }

  /**
   * Uploads a file against its OWNING RECORD (task 076 contract).
   *
   * The server resolves the container from the record — the same record it authorizes the caller
   * against — so the authorization key and the storage destination are one value and cannot
   * disagree. There is no container parameter, by design.
   *
   * This is THE upload method for content that has an owning record. Use
   * {@link uploadFileWithoutRecord} only for the flows where bytes genuinely move first.
   *
   * @throws UploadNameConflictError on a name collision (nothing was overwritten)
   * @throws SdapHttpError with the server's typed refusal — notably a secure record with no
   *   container of its own, which FAILS CLOSED rather than falling back
   */
  public async uploadFileForRecord(
    entityLogicalName: string,
    recordId: string,
    file: File,
    options?: {
      onProgress?: (percent: number) => void;
      signal?: AbortSignal;
      conflictBehavior?: ConflictBehaviorOption;
    }
  ): Promise<DriveItem> {
    this.guardSimpleUploadSize(file);
    return await this.uploadOp.uploadSmallForRecord(entityLogicalName, recordId, file, options);
  }

  /**
   * Uploads content that has NO owning record yet; the server resolves the container from the acting
   * user's business unit.
   *
   * ⚠️ Only for the flows that genuinely cannot create the record first. If a record exists, use
   * {@link uploadFileForRecord} — sending it here stores it in the caller's business-unit container
   * instead of the record's, which for a secure record is the wrong container and is not reversible.
   */
  public async uploadFileWithoutRecord(
    file: File,
    options?: {
      onProgress?: (percent: number) => void;
      signal?: AbortSignal;
      conflictBehavior?: ConflictBehaviorOption;
    }
  ): Promise<DriveItem> {
    this.guardSimpleUploadSize(file);
    return await this.uploadOp.uploadSmallWithoutRecord(file, options);
  }

  /**
   * Downloads a file from SDAP.
   *
   * @param driveId - Drive ID
   * @param itemId - Item ID
   * @returns File blob
   * @throws Error if download fails
   */
  public async downloadFile(driveId: string, itemId: string): Promise<Blob> {
    return await this.downloadOp.download(driveId, itemId);
  }

  /**
   * Deletes a file from SDAP.
   *
   * @param driveId - Drive ID
   * @param itemId - Item ID
   * @throws Error if deletion fails
   */
  public async deleteFile(driveId: string, itemId: string): Promise<void> {
    return await this.deleteOp.delete(driveId, itemId);
  }

  /**
   * Gets file metadata from SDAP.
   *
   * @param driveId - Drive ID
   * @param itemId - Item ID
   * @returns File metadata
   * @throws Error if retrieval fails
   */
  public async getFileMetadata(driveId: string, itemId: string): Promise<FileMetadata> {
    // The FOURTH site with the same dead-auth defect — this one inline in the client rather than in
    // an operation class, which is why converting the three operations did not cover it. Same fix:
    // `authenticatedFetch` (ADR-028), and a named failure instead of an unauthenticated request.
    const authFetch = requireAuthenticatedFetch(this.authenticatedFetch, 'getFileMetadata');

    const response = await requestOrThrow(
      authFetch,
      `${this.baseUrl}/api/obo/drives/${encodeURIComponent(driveId)}/items/${encodeURIComponent(itemId)}`,
      {
        method: 'GET',
        signal: AbortSignal.timeout(this.timeout),
      },
      'Failed to get file metadata'
    );

    return await response.json();
  }

  private validateConfig(config: SdapClientConfig): void {
    if (!config.baseUrl) {
      throw new Error('baseUrl is required');
    }

    try {
      new URL(config.baseUrl);
    } catch {
      throw new Error('baseUrl must be a valid URL');
    }

    if (config.timeout !== undefined && config.timeout < 0) {
      throw new Error('timeout must be >= 0');
    }
  }
}
