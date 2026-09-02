import {
  SdapClientConfig,
  DriveItem,
  FileMetadata,
  AuthenticatedFetchFn,
  IndexFileRequest,
  IndexFileResult,
} from './types';
import { TokenProvider } from './auth/TokenProvider';
import { UploadOperation, type ConflictBehaviorOption } from './operations/UploadOperation';
import { DownloadOperation } from './operations/DownloadOperation';
import { DeleteOperation } from './operations/DeleteOperation';
import { IndexFileOperation } from './operations/IndexFileOperation';

/**
 * SDAP API Client for file operations with SharePoint Embedded.
 *
 * Supports:
 * - Small file uploads (< 4MB)
 * - Chunked uploads (≥ 4MB) with progress tracking
 * - File downloads with streaming
 * - File deletion
 * - Metadata retrieval
 *
 * @example
 * ```typescript
 * const client = new SdapApiClient({
 *   baseUrl: 'https://spe-bff-api.azurewebsites.net',
 *   timeout: 300000
 * });
 *
 * // Upload file
 * const item = await client.uploadFile(containerId, file, {
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
  private readonly tokenProvider: TokenProvider;
  private readonly authenticatedFetch?: AuthenticatedFetchFn;
  private readonly uploadOp: UploadOperation;
  private readonly downloadOp: DownloadOperation;
  private readonly deleteOp: DeleteOperation;
  private readonly indexFileOp?: IndexFileOperation;

  /**
   * Creates a new SDAP API client instance.
   *
   * @param config - Client configuration. Pass `authenticatedFetch` to enable
   *   operations that require Spaarke Auth v2 (ADR-028) — currently
   *   {@link indexFile}. Without it, those operations throw on call.
   *   Legacy operations (upload/download/delete) still use the internal
   *   `TokenProvider` shim until the full migration in
   *   `sdap-client-shared-library-fix-r1`.
   */
  constructor(config: SdapClientConfig & { authenticatedFetch?: AuthenticatedFetchFn }) {
    this.validateConfig(config);

    this.baseUrl = config.baseUrl.replace(/\/$/, ''); // Remove trailing slash
    this.timeout = config.timeout ?? 300000; // 5 minutes default
    this.authenticatedFetch = config.authenticatedFetch;

    this.tokenProvider = new TokenProvider();
    this.uploadOp = new UploadOperation(this.baseUrl, this.timeout, this.tokenProvider);
    this.downloadOp = new DownloadOperation(this.baseUrl, this.timeout, this.tokenProvider);
    this.deleteOp = new DeleteOperation(this.baseUrl, this.timeout, this.tokenProvider);

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
   * Uploads a file to SDAP.
   *
   * Automatically chooses small upload (< 4MB) or chunked upload (≥ 4MB).
   *
   * @param containerId - Container ID
   * @param file - File to upload
   * @param options - Upload options (progress callback, cancellation)
   * @returns Uploaded file metadata
   * @throws Error if upload fails
   */
  public async uploadFile(
    containerId: string,
    file: File,
    options?: {
      onProgress?: (percent: number) => void;
      signal?: AbortSignal;
      /**
       * Name-collision behaviour. Omit on the FIRST attempt — the BFF defaults to `fail`, so a
       * collision throws `UploadNameConflictError` with the existing file untouched. Pass
       * `'rename'` or `'replace'` only when retrying after the user has chosen.
       */
      conflictBehavior?: ConflictBehaviorOption;
    }
  ): Promise<DriveItem> {
    // Graph's simple `PUT .../content` boundary, which is what uploadSmall actually uses.
    //
    // Corrected 2026-09-02: this was 4 MiB, described as matching
    // "PathValidator.SmallUploadMaxBytes, enforced at UploadSessionManager.cs:131". Neither half was
    // true. The server guard at that site was DELETED by spaarkeai-compose-r8 task 015 (FR-S08)
    // because it enforced a Graph limit that had not existed since October 2023, and
    // PathValidator.SmallUploadMaxBytes is referenced by NOTHING but a comment — it is not enforced
    // anywhere. So this client was the only thing rejecting >4 MiB files, on the strength of a
    // server cap that is not there.
    //
    // 250 MB is the real, current boundary for `PUT /drives/{d}/root:/{path}:/content` (4 MB ->
    // 25 MB -> 256 MB -> 250 MB across Oct 2023; stable since) and SharePoint Embedded documents the
    // same figure for containers. Verified against MS Learn + the docs source repos, 2026-08-20:
    // src/server/api/Sprk.Bff.Api/.claude/agent-memory/researcher/graph-driveitem-upload-facts.md
    const SIMPLE_UPLOAD_MAX_BYTES = 250 * 1024 * 1024; // 250 MB — Graph simple-PUT ceiling

    if (file.size > SIMPLE_UPLOAD_MAX_BYTES) {
      // Still fails honestly, but now only where Graph itself would refuse. Above this a caller
      // genuinely needs a resumable upload session, which this client is not wired to.
      throw new Error(UploadOperation.fileTooLarge(file.size, SIMPLE_UPLOAD_MAX_BYTES));
    }

    return await this.uploadOp.uploadSmall(containerId, file, options);
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
    const token = await this.tokenProvider.getToken();

    const response = await fetch(`${this.baseUrl}/api/obo/drives/${driveId}/items/${itemId}`, {
      method: 'GET',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      signal: AbortSignal.timeout(this.timeout),
    });

    if (!response.ok) {
      throw new Error(`Failed to get file metadata: ${response.statusText}`);
    }

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
