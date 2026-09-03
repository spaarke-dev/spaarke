/**
 * File Upload Service
 *
 * Orchestrates single-file upload to SharePoint Embedded (SPE) via SDAP BFF API.
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
        driveId: request.driveId,
      });

      // Validate request
      if (!request.file) {
        return { success: false, error: 'No file provided' };
      }

      if (!request.driveId) {
        return { success: false, error: 'No drive ID provided' };
      }

      // Upload via the shared client (auth: `authenticatedFetch`, ADR-028).
      const item = await this.apiClient.uploadFile(request.driveId, request.file, {
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
