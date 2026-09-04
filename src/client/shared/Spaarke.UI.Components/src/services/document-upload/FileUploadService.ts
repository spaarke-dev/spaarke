/**
 * File Upload Service
 *
 * Orchestrates single-file upload to SharePoint Embedded (SPE) via SDAP BFF API.
 *
 * **Cut over 2026-09-03 (task 076) to the record-keyed contract.** The caller names an
 * {@link FileUploadRequest.target} — an owning record, or an explicit "no record" — and the SERVER
 * resolves the container. There is no `driveId` parameter and no way to reintroduce one. Previously
 * this took a container the caller had resolved from the acting user's business unit, so files
 * attached to a SECURE record landed in the shared BU container; SPE permissions are additive-only,
 * so nothing retracted that afterwards.
 *
 * **Migrated 2026-09-03** onto `@spaarke/sdap-client`'s `SdapApiClient`, retiring the parallel
 * client that used to live beside this file at `./SdapApiClient.ts`. There were THREE upload
 * implementations in the repo (this one, `EntityCreationService`'s raw inline `fetch`, and the
 * shared package); this was the last of the three to converge. Upload behaviour — the explicit
 * `conflictBehavior`, the 250 MB simple-PUT ceiling, RFC7807 failure copy, the typed
 * `SdapHttpError` and `UploadNameConflictError` — now has ONE definition rather than one per
 * caller. Auth moved with it: from an `ITokenProvider` to `authenticatedFetch` (ADR-028), which
 * the rest of the client surface already used.
 *
 * ADR Compliance:
 * - ADR-007: All SPE operations through BFF API
 * - ADR-028: Auth via `authenticatedFetch` from `@spaarke/auth`
 *
 * @version 2.0.0
 */

import type { SdapApiClient } from '@spaarke/sdap-client';
import type { ILogger, SpeFileMetadata, ServiceResult, FileUploadRequest } from './types';
import { consoleLogger } from './types';
import { UploadNameConflictError } from '@spaarke/sdap-client';

/**
 * Service for uploading files to SharePoint Embedded.
 */
export class FileUploadService {
  private readonly logger: ILogger;

  constructor(
    private readonly apiClient: SdapApiClient,
    logger?: ILogger
  ) {
    this.logger = logger ?? consoleLogger;
  }

  /**
   * Upload a file to SharePoint Embedded.
   *
   * @param request - File upload request
   * @returns Service result with SPE file metadata
   */
  async uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>> {
    try {
      this.logger.info('FileUploadService', 'Starting file upload', {
        fileName: request.file.name,
        fileSize: request.file.size,
        target: request.target,
      });

      // Validate request
      if (!request.file) {
        return { success: false, error: 'No file provided' };
      }

      // A record target missing either half would build a URL like `/api/obo/records//` + a GUID,
      // which MISSES the constrained route and 404s — reaching the user as a bare "upload failed"
      // with no clue that the identifiers were the cause. Refuse here instead, and refuse CLOSED:
      // never quietly downgrade to the record-less contract, which would file the bytes in the
      // caller's business-unit container rather than the record's.
      if (request.target.kind === 'record') {
        if (!request.target.entityLogicalName || !request.target.recordId) {
          return {
            success: false,
            error:
              'No owning record provided. An upload against a record needs both its entity logical ' +
              'name and its id.',
          };
        }
      }

      // Upload via the shared client (auth: `authenticatedFetch`, ADR-028). Neither branch names a
      // container: the server resolves it — from the record for the first, from the acting user's
      // business unit for the second.
      const item =
        request.target.kind === 'record'
          ? await this.apiClient.uploadFileForRecord(
              request.target.entityLogicalName,
              request.target.recordId,
              request.file,
              { conflictBehavior: request.conflictBehavior }
            )
          : await this.apiClient.uploadFileWithoutRecord(request.file, {
              conflictBehavior: request.conflictBehavior,
            });

      // DriveItem -> SpeFileMetadata, field by field rather than by spread. The spread this
      // replaced was over a response typed AS SpeFileMetadata, so it carried anything the BFF sent;
      // an explicit mapping states which fields this contract actually depends on, and makes a
      // missing one a compile error instead of an undefined at runtime. `size` is nullable on
      // DriveItem (folders have none); an uploaded file always has one, and 0 is the honest value
      // for a zero-byte upload.
      const speMetadata: SpeFileMetadata = {
        id: item.id,
        name: item.name,
        parentId: item.parentId,
        // Where the bytes actually landed, per the SERVER. Consumers persist this as
        // `sprk_document.sprk_graphdriveid`; under the record-keyed contract it is the only
        // trustworthy value, because the caller no longer chooses the destination.
        driveId: item.driveId,
        size: item.size ?? 0,
        createdDateTime: item.createdDateTime,
        lastModifiedDateTime: item.lastModifiedDateTime,
        eTag: item.eTag,
        isFolder: item.isFolder,
        webUrl: item.webUrl,
        // Convenience aliases consumers read.
        driveItemId: item.id,
        fileName: item.name,
        sharePointUrl: item.webUrl || '',
        fileSize: item.size ?? 0,
      };

      this.logger.info('FileUploadService', 'File uploaded successfully', {
        fileName: speMetadata.fileName,
        driveItemId: speMetadata.driveItemId,
        sharePointUrl: speMetadata.sharePointUrl,
      });

      return { success: true, data: speMetadata };
    } catch (error) {
      // A name collision is surfaced as a DISTINCT result, not folded into `error`. Nothing was
      // written, so the caller can offer rename / save-as-new-version and retry the same upload
      // with `conflictBehavior`. Folding it into the generic error string is how this previously
      // reached the user as "Unknown error occurred" — after the original file had already been
      // overwritten by the old conflictBehavior=replace default.
      if (error instanceof UploadNameConflictError) {
        this.logger.info('FileUploadService', 'Upload blocked by a name collision', {
          fileName: error.fileName,
        });
        return {
          success: false,
          error: error.message,
          nameConflict: { fileName: error.fileName },
        };
      }

      this.logger.error('FileUploadService', 'File upload failed', error);

      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error occurred during file upload',
      };
    }
  }
}
