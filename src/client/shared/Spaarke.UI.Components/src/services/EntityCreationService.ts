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

import type { IWebApiWithCreate } from '../types/WebApiLike';
import type { IUploadedFile } from '../components/FileUpload/fileUploadTypes';
import { resolveRecordType, buildRecordUrl, findNavProp, type INavPropEntry } from './PolymorphicResolverService';

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

/** Progress callback for multi-file uploads. */
export interface IUploadProgress {
  current: number;
  total: number;
  currentFileName: string;
  status: 'uploading' | 'complete' | 'failed';
  error?: string;
}

/** Authenticated fetch function signature (injected by caller). */
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

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
    onProgress?: (progress: IUploadProgress) => void
  ): Promise<IFileUploadResult> {
    if (files.length === 0) {
      return { success: true, successCount: 0, failureCount: 0, uploadedFiles: [], errors: [] };
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
        status: 'uploading',
      });

      try {
        const response = await this._authenticatedFetch(
          `${this._bffBaseUrl}/obo/containers/${containerId}/files/${fileName}`,
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
   * Create a Dataverse entity record via WebApi.
   *
   * @param entityName Dataverse logical name (e.g., 'sprk_matter', 'sprk_project')
   * @param entityData Entity payload with field values and @odata.bind lookups
   * @returns The GUID of the created record
   */
  async createEntityRecord(
    entityName: string,
    entityData: Record<string, unknown>
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
    }
  ): Promise<IDocumentLinkResult> {
    const warnings: string[] = [];
    let linkedCount = 0;
    const createdDocumentIds: string[] = [];

    const containerId = options?.containerId;
    // Derive entity logical name: 'sprk_matters' → 'sprk_matter'
    const entityLogicalName =
      options?.parentEntityLogicalName ||
      parentEntityName.replace(/e?s$/, '');

    // Resolve record type ref + nav-prop once for all documents in this batch
    let recordTypeRefBind: string | undefined;
    let rtNavPropName: string | undefined;
    try {
      const recordType = await resolveRecordType(this._webApi, entityLogicalName);
      if (recordType) {
        // Discover nav-prop for sprk_document → sprk_recordtype_ref (regardingrecordtype column)
        const metaQuery =
          `EntityDefinitions(LogicalName='sprk_document')/ManyToOneRelationships` +
          `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntityNavigationPropertyName,ReferencedEntity` +
          `&$filter=ReferencedEntity eq 'sprk_recordtype_ref'`;
        const metaResult = await this._webApi.retrieveMultipleRecords('', metaQuery);
        const docNavProps: INavPropEntry[] = (metaResult.entities ?? []).map(
          (r: Record<string, unknown>) => ({
            columnName: r['ReferencingAttribute'] as string,
            navPropName: r['ReferencingEntityNavigationPropertyName'] as string,
            referencedEntity: r['ReferencedEntity'] as string,
          })
        );
        rtNavPropName = findNavProp(docNavProps, 'sprk_recordtype_ref', 'regardingrecordtype');
        if (rtNavPropName) {
          recordTypeRefBind = `/sprk_recordtype_refs(${recordType.id})`;
        }
      }
    } catch {
      console.warn(`[EntityCreationService] Could not resolve record type for ${entityLogicalName}`);
    }

    for (const file of uploadedFiles) {
      try {
        const documentEntity: Record<string, unknown> = {
          sprk_documentname: file.name,
          sprk_filename: file.name,
          sprk_driveitemid: file.id,
          sprk_graphitemid: file.id,
          // Source type: User Upload (659490000)
          sprk_sourcetype: 659490000,
          // Mark for Document Profile processing: Pending (100000001)
          sprk_filesummarystatus: 100000001,
          // Polymorphic resolver fields (ADR-024)
          sprk_regardingrecordid: parentEntityId.replace(/[{}]/g, '').toLowerCase(),
          sprk_regardingrecordname: options?.parentRecordName ?? '',
          sprk_regardingrecordurl: buildRecordUrl(entityLogicalName, parentEntityId),
        };

        // Bind sprk_regardingrecordtype lookup to sprk_recordtype_ref
        if (recordTypeRefBind && rtNavPropName) {
          documentEntity[`${rtNavPropName}@odata.bind`] = recordTypeRefBind;
        }

        // Add @odata.bind navigation property to link document to parent entity
        if (navigationProperty) {
          documentEntity[`${navigationProperty}@odata.bind`] =
            `/${parentEntityName}(${parentEntityId})`;
        }

        if (file.webUrl) {
          documentEntity['sprk_filepath'] = file.webUrl;
        }
        if (file.size) {
          documentEntity['sprk_filesize'] = file.size;
        }
        if (containerId) {
          documentEntity['sprk_graphdriveid'] = containerId;
          documentEntity['sprk_containerid'] = containerId;
        }

        const result = await this._webApi.createRecord('sprk_document', documentEntity);
        createdDocumentIds.push(result.id);
        linkedCount++;
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Unknown error';
        warnings.push(`Failed to create document record for "${file.name}": ${message}`);
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
    warnings: string[]
  ): Promise<void> {
    for (const docId of documentIds) {
      try {
        const response = await this._authenticatedFetch(
          `${this._bffBaseUrl}/documents/${docId}/analyze`,
          { method: 'POST' }
        );
        if (response.ok) {
          console.info(`[EntityCreationService] Document analysis queued for ${docId}`);
        } else {
          console.warn(`[EntityCreationService] Failed to queue analysis for ${docId}: HTTP ${response.status}`);
        }
      } catch (err) {
        console.warn(`[EntityCreationService] Could not queue analysis for ${docId}:`, err);
      }
    }
  }
}
