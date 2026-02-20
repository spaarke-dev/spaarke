/**
 * EntityCreationService.ts
 * Generic service for entity creation workflows (Matter, Project, future entity types).
 *
 * Responsibilities:
 *   1. Upload files to SPE via BFF OBO endpoint
 *   2. Create Dataverse entity records via Xrm.WebApi
 *   3. Create sprk_document records linking uploaded files to the parent entity
 *   4. Request AI pre-fill via BFF endpoint
 *
 * Entity-agnostic: uses entityName and navigation properties as parameters,
 * not hardcoded to sprk_matter.
 */

import type { IWebApi, WebApiEntity } from '../types/xrm';
import type { IUploadedFile } from '../components/CreateMatter/wizardTypes';
import { getBffBaseUrl } from '../config/bffConfig';
import { authenticatedFetch } from './bffAuthProvider';

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
  warnings: string[];
}

/** AI pre-fill response from BFF. */
export interface IAiPreFillResponse {
  matterTypeName?: string;
  practiceAreaName?: string;
  matterName?: string;
  summary?: string;
  confidence: number;
  preFilledFields: string[];
}

/** Progress callback for multi-file uploads. */
export interface IUploadProgress {
  current: number;
  total: number;
  currentFileName: string;
  status: 'uploading' | 'complete' | 'failed';
  error?: string;
}

// ---------------------------------------------------------------------------
// EntityCreationService
// ---------------------------------------------------------------------------

export class EntityCreationService {
  constructor(private readonly _webApi: IWebApi) {}

  /**
   * Upload files to SPE via the BFF OBO upload endpoint.
   *
   * Uses: PUT /api/obo/drives/{driveId}/upload?fileName={fileName}
   * Each file is uploaded individually with Bearer token auth.
   *
   * @param containerId SPE container/drive ID for the target storage
   * @param files Files to upload
   * @param onProgress Optional progress callback
   */
  async uploadFilesToSpe(
    containerId: string,
    files: IUploadedFile[],
    onProgress?: (progress: IUploadProgress) => void
  ): Promise<IFileUploadResult> {
    if (files.length === 0) {
      return { success: true, successCount: 0, failureCount: 0, uploadedFiles: [], errors: [] };
    }

    const bffBaseUrl = getBffBaseUrl();
    const uploadedFiles: ISpeFileMetadata[] = [];
    const errors: Array<{ fileName: string; error: string }> = [];

    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const fileName = encodeURIComponent(file.name);

      onProgress?.({
        current: i + 1,
        total: files.length,
        currentFileName: file.name,
        status: 'uploading',
      });

      try {
        const response = await authenticatedFetch(
          `${bffBaseUrl}/obo/drives/${containerId}/upload?fileName=${fileName}`,
          {
            method: 'PUT',
            body: file.file,
            headers: {
              'Content-Type': file.file.type || 'application/octet-stream',
            },
          }
        );

        if (!response.ok) {
          const errorText = await response.text().catch(() => response.statusText);
          errors.push({ fileName: file.name, error: `HTTP ${response.status}: ${errorText}` });
          onProgress?.({
            current: i + 1,
            total: files.length,
            currentFileName: file.name,
            status: 'failed',
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
          status: 'complete',
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Upload failed';
        errors.push({ fileName: file.name, error: message });
        onProgress?.({
          current: i + 1,
          total: files.length,
          currentFileName: file.name,
          status: 'failed',
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
   * Create a Dataverse entity record via Xrm.WebApi.
   *
   * @param entityName Dataverse logical name (e.g., 'sprk_matter', 'sprk_project')
   * @param entityData Entity payload with field values and @odata.bind lookups
   * @returns The GUID of the created record
   */
  async createEntityRecord(
    entityName: string,
    entityData: WebApiEntity
  ): Promise<string> {
    const result = await this._webApi.createRecord(entityName, entityData);
    return result.id;
  }

  /**
   * Create sprk_document records in Dataverse linking uploaded SPE files to a parent entity.
   *
   * Each document record contains:
   *   - sprk_name: file name
   *   - sprk_spedriveitemid: SPE drive item ID
   *   - Navigation property @odata.bind to parent entity
   *
   * @param parentEntityName Logical name of the parent entity set (e.g., 'sprk_matters')
   * @param parentEntityId GUID of the parent entity record
   * @param navigationProperty Navigation property name on sprk_document (e.g., 'sprk_matter')
   * @param uploadedFiles SPE file metadata from uploadFilesToSpe()
   */
  async createDocumentRecords(
    parentEntityName: string,
    parentEntityId: string,
    navigationProperty: string,
    uploadedFiles: ISpeFileMetadata[]
  ): Promise<IDocumentLinkResult> {
    const warnings: string[] = [];
    let linkedCount = 0;

    for (const file of uploadedFiles) {
      try {
        const documentEntity: WebApiEntity = {
          sprk_name: file.name,
          sprk_spedriveitemid: file.id,
        };

        // Add @odata.bind navigation property to link document to parent entity
        documentEntity[`${navigationProperty}@odata.bind`] =
          `/${parentEntityName}(${parentEntityId})`;

        if (file.webUrl) {
          documentEntity['sprk_weburl'] = file.webUrl;
        }

        await this._webApi.createRecord('sprk_document', documentEntity);
        linkedCount++;
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Unknown error';
        warnings.push(`Failed to create document record for "${file.name}": ${message}`);
      }
    }

    return {
      success: linkedCount > 0 || uploadedFiles.length === 0,
      linkedCount,
      warnings,
    };
  }

  /**
   * Request AI pre-fill from the BFF endpoint.
   *
   * Sends uploaded files to POST /api/workspace/matters/pre-fill as multipart/form-data.
   * Returns structured field values extracted by the AI playbook system.
   *
   * @param files Files to send for AI analysis
   * @param entityType Entity type hint (e.g., 'matter', 'project')
   */
  async requestAiPreFill(
    files: IUploadedFile[],
    entityType: string = 'matter'
  ): Promise<IAiPreFillResponse> {
    const bffBaseUrl = getBffBaseUrl();
    const formData = new FormData();

    for (const file of files) {
      formData.append('files', file.file, file.name);
    }

    try {
      const response = await authenticatedFetch(
        `${bffBaseUrl}/workspace/matters/pre-fill`,
        {
          method: 'POST',
          body: formData,
        }
      );

      if (!response.ok) {
        return {
          confidence: 0,
          preFilledFields: [],
        };
      }

      return await response.json();
    } catch {
      return {
        confidence: 0,
        preFilledFields: [],
      };
    }
  }
}
