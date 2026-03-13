/**
 * EntityCreationService.ts
 * Generic service for entity creation workflows (Matter, Project, Event, etc.).
 *
 * Responsibilities:
 *   1. Upload files to SPE via BFF OBO endpoint
 *   2. Create Dataverse entity records via WebApi
 *   3. Create sprk_document records linking uploaded files to the parent entity
 *   4. Trigger Document Profile analysis via BFF Service Bus
 *
 * Entity-agnostic: uses entityName and navigation properties as parameters,
 * not hardcoded to any specific entity.
 *
 * Dependencies are injected via constructor (no solution-specific imports):
 *   - webApi: Dataverse WebApi interface (IWebApiWithCreate)
 *   - authenticatedFetch: BFF-authenticated fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * @example
 * ```typescript
 * import { EntityCreationService } from '@spaarke/ui-components';
 * import { authenticatedFetch } from '../services/authInit';
 * import { getBffBaseUrl } from '../config/bffConfig';
 *
 * const service = new EntityCreationService(webApi, authenticatedFetch, getBffBaseUrl());
 * const uploadResult = await service.uploadFilesToSpe(containerId, files);
 * const matterId = await service.createEntityRecord('sprk_matter', matterData);
 * await service.createDocumentRecords('sprk_matters', matterId, 'sprk_Matter', uploadResult.uploadedFiles);
 * ```
 */

import type { IWebApiWithCreate } from "../types/WebApiLike";
import type { IUploadedFile } from "../components/FileUpload/fileUploadTypes";
// PolymorphicResolverService not needed — document records use canonical field set only

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Result of a file upload operation. */
export interface IFileUploadResult {
  /** Whether at least some files were uploaded successfully. */
  success: boolean;
  /** Number of files that uploaded successfully. */
  successCount: number;
  /** Number of files that failed. */
  failureCount: number;
  /** Metadata for successfully uploaded files (drive item IDs). */
  uploadedFiles: ISpeFileMetadata[];
  /** Per-file error details. */
  errors: Array<{ fileName: string; error: string }>;
}

/** SPE file metadata returned from the BFF upload endpoint. */
export interface ISpeFileMetadata {
  id: string;
  name: string;
  size: number;
  webUrl?: string;
}

/** Result of creating sprk_document records. */
export interface IDocumentLinkResult {
  success: boolean;
  linkedCount: number;
  /** GUIDs of successfully created sprk_document records. */
  createdDocumentIds: string[];
  warnings: string[];
}

/** Input for sending email via BFF Communication service. */
export interface ISendEmailInput {
  to: string | string[];
  cc?: string | string[];
  subject: string;
  body: string;
  bodyFormat?: "HTML" | "Text";
  associations?: Array<{
    entityType: string;
    entityId: string;
    entityName?: string;
  }>;
}

/** Result of a send-email operation. */
export interface ISendEmailResult {
  success: boolean;
  warning?: string;
}

/** Progress callback for multi-file uploads. */
export interface IUploadProgress {
  current: number;
  total: number;
  currentFileName: string;
  status: "uploading" | "complete" | "failed";
  error?: string;
}

/** Authenticated fetch function signature (injected by caller). */
export type AuthenticatedFetchFn = (
  url: string,
  init?: RequestInit,
) => Promise<Response>;

// ---------------------------------------------------------------------------
// EntityCreationService
// ---------------------------------------------------------------------------

export class EntityCreationService {
  constructor(
    private readonly _webApi: IWebApiWithCreate,
    private readonly _authenticatedFetch: AuthenticatedFetchFn,
    private readonly _bffBaseUrl: string,
  ) {}

  /**
   * Upload files to SPE via the BFF OBO upload endpoint.
   *
   * Uses: PUT /api/obo/containers/{containerId}/files/{path}
   * Each file is uploaded individually with Bearer token auth.
   *
   * @param containerId SPE container/drive ID for the target storage
   * @param files Files to upload
   * @param onProgress Optional progress callback
   */
  async uploadFilesToSpe(
    containerId: string,
    files: IUploadedFile[],
    onProgress?: (progress: IUploadProgress) => void,
  ): Promise<IFileUploadResult> {
    if (files.length === 0) {
      return {
        success: true,
        successCount: 0,
        failureCount: 0,
        uploadedFiles: [],
        errors: [],
      };
    }

    const uploadedFiles: ISpeFileMetadata[] = [];
    const errors: Array<{ fileName: string; error: string }> = [];

    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const fileName = encodeURIComponent(file.name);

      onProgress?.({
        current: i + 1,
        total: files.length,
        currentFileName: file.name,
        status: "uploading",
      });

      try {
        const response = await this._authenticatedFetch(
          `${this._bffBaseUrl}/obo/containers/${containerId}/files/${fileName}`,
          {
            method: "PUT",
            body: file.file,
            headers: {
              "Content-Type": file.file.type || "application/octet-stream",
            },
          },
        );

        if (!response.ok) {
          const errorText = await response
            .text()
            .catch(() => response.statusText);
          errors.push({
            fileName: file.name,
            error: `HTTP ${response.status}: ${errorText}`,
          });
          onProgress?.({
            current: i + 1,
            total: files.length,
            currentFileName: file.name,
            status: "failed",
            error: `HTTP ${response.status}`,
          });
          continue;
        }

        const metadata: ISpeFileMetadata = await response.json();
        uploadedFiles.push(metadata);

        onProgress?.({
          current: i + 1,
          total: files.length,
          currentFileName: file.name,
          status: "complete",
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : "Upload failed";
        errors.push({ fileName: file.name, error: message });
        onProgress?.({
          current: i + 1,
          total: files.length,
          currentFileName: file.name,
          status: "failed",
          error: message,
        });
      }
    }

    return {
      success: uploadedFiles.length > 0,
      successCount: uploadedFiles.length,
      failureCount: errors.length,
      uploadedFiles,
      errors,
    };
  }

  /**
   * Create a Dataverse entity record via WebApi.
   *
   * @param entityName Dataverse logical name (e.g., 'sprk_matter', 'sprk_project')
   * @param entityData Entity payload with field values and @odata.bind lookups
   * @returns The GUID of the created record
   */
  async createEntityRecord(
    entityName: string,
    entityData: Record<string, unknown>,
  ): Promise<string> {
    const result = await this._webApi.createRecord(entityName, entityData);
    return result.id;
  }

  /**
   * Create sprk_document records in Dataverse linking uploaded SPE files to a parent entity.
   *
   * Each document record contains:
   *   - sprk_documentname / sprk_filename: file name
   *   - sprk_driveitemid: SPE drive item ID
   *   - sprk_filepath: web URL to the file
   *   - sprk_filesize: file size in bytes
   *   - Navigation property @odata.bind to parent entity
   *
   * @param parentEntityName Logical name of the parent entity set (e.g., 'sprk_matters')
   * @param parentEntityId GUID of the parent entity record
   * @param navigationProperty Navigation property name on sprk_document (e.g., 'sprk_Matter')
   * @param uploadedFiles SPE file metadata from uploadFilesToSpe()
   * @param options Additional context for the document records
   */
  async createDocumentRecords(
    parentEntityName: string,
    parentEntityId: string,
    navigationProperty: string,
    uploadedFiles: ISpeFileMetadata[],
    options?: {
      /** SPE container/drive ID — stored as sprk_graphdriveid and sprk_containerid */
      containerId?: string;
      /** Parent record display name — stored in sprk_regardingrecordname for resolver fields */
      parentRecordName?: string;
      /** Parent entity logical name (e.g., 'sprk_matter'). If omitted, derived from parentEntityName by removing trailing 's'. */
      parentEntityLogicalName?: string;
    },
  ): Promise<IDocumentLinkResult> {
    const warnings: string[] = [];
    let linkedCount = 0;
    const createdDocumentIds: string[] = [];

    const containerId = options?.containerId;

    for (const file of uploadedFiles) {
      try {
        // Payload aligned with canonical DocumentRecordService fields
        const documentEntity: Record<string, unknown> = {
          sprk_documentname: file.name,
          sprk_filename: file.name,
          sprk_filesize: file.size ?? null,
          sprk_graphitemid: file.id,
          sprk_graphdriveid: containerId ?? null,
          sprk_filepath: file.webUrl ?? null,
        };

        // Add @odata.bind navigation property to link document to parent entity
        if (navigationProperty) {
          documentEntity[`${navigationProperty}@odata.bind`] =
            `/${parentEntityName}(${parentEntityId})`;
        }

        console.info(
          "[EntityCreationService] createDocumentRecord payload:",
          JSON.stringify(documentEntity, null, 2),
        );
        const result = await this._webApi.createRecord(
          "sprk_document",
          documentEntity,
        );
        createdDocumentIds.push(result.id);
        linkedCount++;
      } catch (err) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const errObj = err as any;
        const message =
          errObj?.message ||
          (err instanceof Error ? err.message : "Unknown error");
        console.error(
          "[EntityCreationService] createDocumentRecord failed:",
          message,
          err,
        );
        warnings.push(
          `Failed to create document record for "${file.name}": ${message}`,
        );
      }
    }

    // Queue Document Profile analysis for each created document via BFF Service Bus
    if (createdDocumentIds.length > 0) {
      await this._triggerDocumentAnalysis(createdDocumentIds, warnings);
    }

    return {
      success: linkedCount > 0 || uploadedFiles.length === 0,
      linkedCount,
      createdDocumentIds,
      warnings,
    };
  }

  /**
   * Trigger Document Profile analysis for created documents via the BFF.
   * Calls POST /api/documents/{id}/analyze which queues a Service Bus job
   * for each document. Failures are non-fatal (added as warnings).
   */
  private async _triggerDocumentAnalysis(
    documentIds: string[],
    warnings: string[],
  ): Promise<void> {
    for (const docId of documentIds) {
      try {
        const response = await this._authenticatedFetch(
          `${this._bffBaseUrl}/documents/${docId}/analyze`,
          { method: "POST" },
        );
        if (response.ok) {
          console.info(
            `[EntityCreationService] Document analysis queued for ${docId}`,
          );
        } else {
          console.warn(
            `[EntityCreationService] Failed to queue analysis for ${docId}: HTTP ${response.status}`,
          );
        }
      } catch (err) {
        console.warn(
          `[EntityCreationService] Could not queue analysis for ${docId}:`,
          err,
        );
      }
    }
  }

  /**
   * Send an email via the BFF Communication service (Graph API).
   *
   * Normalizes `to`/`cc` from string or array (splits on `;,`).
   * Returns `{ success, warning? }` — never throws.
   */
  async sendEmail(input: ISendEmailInput): Promise<ISendEmailResult> {
    try {
      const normalize = (val: string | string[] | undefined): string[] => {
        if (!val) return [];
        const arr = Array.isArray(val) ? val : val.split(/[;,]/);
        return arr.map((a) => a.trim()).filter(Boolean);
      };

      const to = normalize(input.to);
      if (to.length === 0) {
        return { success: true };
      }

      const cc = normalize(input.cc);
      const response = await this._authenticatedFetch(
        `${this._bffBaseUrl}/communications/send`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            to,
            cc: cc.length > 0 ? cc : undefined,
            subject: input.subject,
            body: input.body,
            bodyFormat: input.bodyFormat ?? "HTML",
            associations: input.associations,
          }),
        },
      );

      if (!response.ok) {
        const errorText = await response.text().catch(() => "Unknown error");
        console.warn(
          "[EntityCreationService] Email send failed:",
          response.status,
          errorText,
        );
        return {
          success: false,
          warning: `Could not send email (${response.status}). Please send manually.`,
        };
      }

      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      return {
        success: false,
        warning: `Could not send email (${message}). Please send manually.`,
      };
    }
  }
}
