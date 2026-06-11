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

import type { IWebApiLike, IWebApiWithCreate } from '../types/WebApiLike';
import type { IUploadedFile } from '../components/FileUpload/fileUploadTypes';
import { SdapApiClient, type IndexFileRequest, type IndexFileResult } from '@spaarke/sdap-client';
// PolymorphicResolverService not needed — document records use canonical field set only

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Cascade defaults derived from the current user's owning Business Unit.
 *
 * Per spaarke-multi-container-multi-index-r1 spec (FR-WIZ-01..05) the 5 parent-record
 * wizards (Matter, Project, Invoice, WorkAssignment, Event) and DocumentUploadWizard
 * cascade two fields from `businessunit` onto the create payload:
 *
 *   - `sprk_containerid` — SPE container/drive ID
 *   - `sprk_searchindexname` — Azure AI Search index name
 *
 * Both fields are OPTIONAL on `businessunit`. When unset on the BU, the helpers
 * leave the corresponding payload field untouched and the BFF tenant-default
 * chain (or downstream backfill) takes over server-side.
 */
export interface IUserBuCascadeDefaults {
  /** `businessunit.sprk_containerid` for the current user's owning BU, or undefined if unset. */
  containerId?: string;
  /** `businessunit.sprk_searchindexname` for the current user's owning BU, or undefined if unset. */
  searchIndexName?: string;
  /** GUID of the user's owning Business Unit (always set when the lookup succeeded). */
  businessUnitId?: string;
}

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
  /**
   * SPE drive ID this item belongs to. Populated from `DriveItem.parentReference.driveId`
   * on the BFF upload response. Required to call `SdapApiClient.indexFile()` after upload.
   * Optional for backwards compatibility with response payloads that pre-date the field.
   */
  driveId?: string;
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
  bodyFormat?: 'HTML' | 'PlainText'; // matches server enum BodyFormat (Sprk.Bff.Api.Services.Communication.Models.BodyFormat); 'Text' was an incorrect alias and is rejected by the BFF (2026-05-25)
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
  status: 'uploading' | 'complete' | 'failed';
  error?: string;
}

/** Authenticated fetch function signature (injected by caller). */
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

// ---------------------------------------------------------------------------
// EntityCreationService
// ---------------------------------------------------------------------------

export class EntityCreationService {
  /** Lazily-constructed SDAP API client for BFF operations (currently: post-upload indexing). */
  private _sdapClient: SdapApiClient | null = null;

  constructor(
    private readonly _webApi: IWebApiWithCreate,
    private readonly _authenticatedFetch: AuthenticatedFetchFn,
    private readonly _bffBaseUrl: string
  ) {}

  /**
   * Lazy accessor for `SdapApiClient`. Constructed on first use with the same
   * `authenticatedFetch` + `bffBaseUrl` already injected into this service.
   */
  private _getSdapClient(): SdapApiClient {
    if (!this._sdapClient) {
      this._sdapClient = new SdapApiClient({
        baseUrl: this._bffBaseUrl,
        authenticatedFetch: this._authenticatedFetch,
      });
    }
    return this._sdapClient;
  }

  /**
   * Trigger RAG indexing for files uploaded via {@link uploadFilesToSpe}.
   *
   * Iterates `uploadedFiles` and posts one `POST /api/ai/rag/index-file` per file
   * via `@spaarke/sdap-client.SdapApiClient.indexFile()`. Indexing runs under
   * the caller's OBO identity inside the BFF request — same canonical path as
   * DocumentUploadWizard's `triggerRagIndexing` and the "Send to Index" ribbon
   * command. Pattern 4 compliant (writer-identity matching).
   *
   * Each call is **non-fatal**: an individual failure is logged + collected in
   * the returned warnings array; remaining files continue. The wizard contract
   * with the user is "files uploaded successfully" — searchability is best-effort.
   *
   * @param uploadedFiles Result of `uploadFilesToSpe` (must include `id` and `driveId`)
   * @param tenantId AAD tenant GUID — required for multi-tenant index routing
   * @param parentEntity Optional parent entity context for entity-scoped search
   *   (e.g. `{ entityType: 'sprk_matter', entityId: matterId, entityName: matterName }`)
   * @param createdDocumentIds Optional Dataverse document GUIDs in the same order as
   *   `uploadedFiles` (returned by `createDocumentRecords`). When provided, the BFF
   *   updates `sprk_searchindexed` + tracking fields on each document.
   * @param searchIndexName Optional explicit index name (e.g. from BU cascade). When
   *   omitted, the BFF resolver chain (parent → BU → tenant default) decides.
   * @returns Warnings array describing per-file failures (empty when all succeed).
   */
  async indexUploadedFiles(
    uploadedFiles: ISpeFileMetadata[],
    tenantId: string,
    parentEntity?: { entityType: string; entityId: string; entityName: string },
    createdDocumentIds?: string[],
    searchIndexName?: string
  ): Promise<string[]> {
    const warnings: string[] = [];
    if (uploadedFiles.length === 0) return warnings;

    if (!tenantId || tenantId.trim() === '') {
      warnings.push('RAG indexing skipped: tenantId is required.');
      return warnings;
    }

    const client = this._getSdapClient();

    for (let i = 0; i < uploadedFiles.length; i++) {
      const file = uploadedFiles[i];

      if (!file.driveId) {
        warnings.push(`RAG indexing skipped for ${file.name}: missing driveId on upload response.`);
        continue;
      }

      // multi-container-multi-index-r1 UAT 2026-06-09: the BFF entity-scope
      // filter (`SearchFilterBuilder.BuildFilter`) compares chunks'
      // `parentEntityType` against the LOWERCASED, UN-PREFIXED form
      // ("matter" / "project" / "invoice" / etc.). DocumentUploadWizard's
      // `triggerRagIndexing` strips `sprk_` before sending; our wizards
      // were passing the Dataverse logical name ("sprk_matter") directly,
      // which got indexed verbatim → entity-scoped search returned 0 results
      // because filter expected "matter" but the chunks said "sprk_matter".
      // Normalize at the single seam so all 4 Create wizards + future
      // callers automatically conform.
      const normalizedParentEntity = parentEntity
        ? {
            entityType: parentEntity.entityType.startsWith('sprk_')
              ? parentEntity.entityType.substring('sprk_'.length)
              : parentEntity.entityType,
            entityId: parentEntity.entityId,
            entityName: parentEntity.entityName,
          }
        : undefined;

      const request: IndexFileRequest = {
        driveId: file.driveId,
        itemId: file.id,
        fileName: file.name,
        tenantId,
        documentId: createdDocumentIds?.[i],
        parentEntity: normalizedParentEntity,
        searchIndexName,
      };

      try {
        const result: IndexFileResult = await client.indexFile(request);
        if (!result.success) {
          warnings.push(`RAG indexing failed for ${file.name}: ${result.errorMessage ?? 'unknown error'}`);
        } else {
          console.info(`[EntityCreationService] Indexed ${file.name} — ${result.chunksIndexed ?? '?'} chunks`);
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        warnings.push(`RAG indexing threw for ${file.name}: ${message}`);
      }
    }

    return warnings;
  }

  // -------------------------------------------------------------------------
  // INV-5-safe cascade helpers (spaarke-multi-container-multi-index-r1 / FR-WIZ-01..08)
  // -------------------------------------------------------------------------

  /**
   * Returns true when the entity payload has a non-empty value for the given field.
   *
   * "Non-empty" means: not `undefined`, not `null`, and not an empty/whitespace-only string.
   * Booleans and numbers (including `false` / `0`) count as non-empty since they are explicit values.
   *
   * Per design.md INV-5: explicit override values are sacred — never overwrite.
   */
  private static _hasExplicitValue(entity: Record<string, unknown>, fieldLogicalName: string): boolean {
    if (!(fieldLogicalName in entity)) return false;
    const v = entity[fieldLogicalName];
    if (v === null || v === undefined) return false;
    if (typeof v === 'string' && v.trim() === '') return false;
    return true;
  }

  /**
   * Apply a default `sprk_containerid` to the create payload IF AND ONLY IF the payload
   * does not already have an explicit value for that field (INV-5).
   *
   * Mirrors the canonical assignment shape from
   * `CreateMatterWizard/matterService.ts:216` — `entity['sprk_containerid'] = containerId` —
   * adding the INV-5 guard so each wizard does not duplicate it.
   *
   * @param entity Create payload (mutated in place).
   * @param containerId BU-derived container ID; passing `undefined` / `null` / `''` is a no-op.
   * @returns `true` if the value was set, `false` if it was skipped (already present, or input empty).
   *
   * @see design.md INV-5
   * @see spec.md FR-WIZ-01..05 (parent-record wizards)
   * @see spec.md FR-WIZ-08 (INV-5 preservation across all wizards)
   */
  static applyDefaultContainerId(entity: Record<string, unknown>, containerId: string | null | undefined): boolean {
    if (containerId === null || containerId === undefined || containerId === '') return false;
    if (this._hasExplicitValue(entity, 'sprk_containerid')) return false;
    entity['sprk_containerid'] = containerId;
    return true;
  }

  /**
   * Apply a default `sprk_searchindexname` to the create payload IF AND ONLY IF the payload
   * does not already have an explicit value for that field (INV-5).
   *
   * Mirrors `applyDefaultContainerId` — both fields cascade from the current user's owning
   * Business Unit per FR-WIZ-01..05. When the BU has no configured index name, callers should
   * pass `undefined` here and the field will be left unset on the payload (the BFF tenant-default
   * chain handles the fallback server-side).
   *
   * @param entity Create payload (mutated in place).
   * @param searchIndexName BU-derived index name; passing `undefined` / `null` / `''` is a no-op.
   * @returns `true` if the value was set, `false` if it was skipped (already present, or input empty).
   *
   * @see design.md INV-5
   * @see spec.md FR-WIZ-01..05 (parent-record wizards)
   * @see spec.md FR-WIZ-07 (DocumentUploadWizard payload)
   * @see spec.md FR-WIZ-08 (INV-5 preservation across all wizards)
   */
  static applyDefaultSearchIndexName(
    entity: Record<string, unknown>,
    searchIndexName: string | null | undefined
  ): boolean {
    if (searchIndexName === null || searchIndexName === undefined || searchIndexName === '') return false;
    if (this._hasExplicitValue(entity, 'sprk_searchindexname')) return false;
    entity['sprk_searchindexname'] = searchIndexName;
    return true;
  }

  /**
   * Apply both BU-derived defaults to the create payload, each guarded independently by INV-5.
   * Convenience wrapper around `applyDefaultContainerId` + `applyDefaultSearchIndexName` for the
   * 5 parent-record wizards (Matter, Project, Invoice, WorkAssignment, Event).
   *
   * @param entity Create payload (mutated in place).
   * @param defaults BU-derived defaults; either or both may be undefined.
   * @returns Object reporting which fields were actually set (vs. skipped by INV-5 or empty input).
   *
   * @see spec.md FR-WIZ-01..05
   * @see spec.md FR-WIZ-08 (INV-5)
   */
  static applyUserBuDefaults(
    entity: Record<string, unknown>,
    defaults: IUserBuCascadeDefaults | null | undefined
  ): { containerIdSet: boolean; searchIndexNameSet: boolean } {
    if (!defaults) {
      return { containerIdSet: false, searchIndexNameSet: false };
    }
    return {
      containerIdSet: EntityCreationService.applyDefaultContainerId(entity, defaults.containerId),
      searchIndexNameSet: EntityCreationService.applyDefaultSearchIndexName(entity, defaults.searchIndexName),
    };
  }

  /**
   * Resolve the current user's owning Business Unit defaults for cascade fields
   * (`sprk_containerid`, `sprk_searchindexname`).
   *
   * Chain (matches the SemanticSearchControl NavigationService + SummarizeFilesWizard pattern):
   *   systemuser(userId) → `_businessunitid_value` → businessunit(buId)
   *     → `sprk_containerid`, `sprk_searchindexname`
   *
   * Caller responsibilities:
   *   - Determine `userId` upstream (e.g. via `Xrm.Utility.getUserId()` in a Code Page / PCF host).
   *     This helper stays Xrm-host-agnostic per ADR-012 so it can be unit-tested without
   *     a real Xrm global.
   *
   * Behavior:
   *   - Returns `{ containerId: undefined, searchIndexName: undefined, businessUnitId: undefined }`
   *     if the user has no `_businessunitid_value` (rare).
   *   - Returns either field as `undefined` when the BU exists but the field is unset
   *     (Spaarke Dev 1 / Test 1 case per Phase A.5 — operator setup may lag).
   *   - Returns trimmed-empty strings as `undefined` (treated as unset).
   *   - Never throws on Dataverse "field exists but is null" — only network / 4xx errors propagate.
   *
   * @param webApi Xrm.WebApi-compatible interface (PCF context.webAPI or wrapped Xrm.WebApi).
   * @param userId GUID of the current user (with or without surrounding braces).
   * @returns Cascade defaults from the user's BU; fields may be `undefined` if unset on the BU.
   *
   * @see spec.md FR-WIZ-01..05
   * @see design.md §5.0 (BU cascade source)
   */
  static async resolveUserBuDefaults(webApi: IWebApiLike, userId: string): Promise<IUserBuCascadeDefaults> {
    const cleanUserId = userId.replace(/^\{|\}$/g, '');

    // Step 1: user → BU id
    const userRecord = await webApi.retrieveRecord('systemuser', cleanUserId, '?$select=_businessunitid_value');
    const rawBuId = userRecord['_businessunitid_value'];
    const buId = typeof rawBuId === 'string' && rawBuId.trim() !== '' ? rawBuId : undefined;
    if (!buId) {
      return { containerId: undefined, searchIndexName: undefined, businessUnitId: undefined };
    }

    // Step 2: BU → cascade fields
    const buRecord = await webApi.retrieveRecord(
      'businessunit',
      buId,
      '?$select=sprk_containerid,sprk_searchindexname'
    );

    const normalize = (v: unknown): string | undefined => {
      if (typeof v !== 'string') return undefined;
      const trimmed = v.trim();
      return trimmed === '' ? undefined : trimmed;
    };

    return {
      businessUnitId: buId,
      containerId: normalize(buRecord['sprk_containerid']),
      searchIndexName: normalize(buRecord['sprk_searchindexname']),
    };
  }

  /**
   * Upload files to SPE via the BFF OBO upload endpoint.
   *
   * Uses: PUT /api/obo/containers/{containerId}/files/{path}
   * Each file is uploaded individually with Bearer token auth.
   *
   * Post-upload RAG indexing is the caller's responsibility — invoke
   * `SdapApiClient.indexFile()` from `@spaarke/sdap-client` for each
   * returned `ISpeFileMetadata`. See project `sdap-client-shared-library-fix-r1`
   * for the planned migration of this method to `sdap-client.UploadOperation`.
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
        status: 'uploading',
      });

      try {
        const response = await this._authenticatedFetch(
          `${this._bffBaseUrl}/api/obo/containers/${containerId}/files/${fileName}`,
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
          errors.push({
            fileName: file.name,
            error: `HTTP ${response.status}: ${errorText}`,
          });
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
  async createEntityRecord(entityName: string, entityData: Record<string, unknown>): Promise<string> {
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
          // Upload to SPE succeeded by the time we reach here — mark the file flag.
          // BFF treats DriveId/ItemId as authoritative, but downstream consumers
          // (RAG indexing filter, scheduled jobs, form ribbon visibility) read this flag.
          sprk_hasfile: true,
        };

        // Add @odata.bind navigation property to link document to parent entity
        if (navigationProperty) {
          documentEntity[`${navigationProperty}@odata.bind`] = `/${parentEntityName}(${parentEntityId})`;
        }

        console.info('[EntityCreationService] createDocumentRecord payload:', JSON.stringify(documentEntity, null, 2));
        const result = await this._webApi.createRecord('sprk_document', documentEntity);
        createdDocumentIds.push(result.id);
        linkedCount++;
      } catch (err) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const errObj = err as any;
        const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
        console.error('[EntityCreationService] createDocumentRecord failed:', message, err);
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
  private async _triggerDocumentAnalysis(documentIds: string[], _warnings: string[]): Promise<void> {
    for (const docId of documentIds) {
      try {
        const response = await this._authenticatedFetch(`${this._bffBaseUrl}/api/documents/${docId}/analyze`, {
          method: 'POST',
        });
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
        return arr.map(a => a.trim()).filter(Boolean);
      };

      const to = normalize(input.to);
      if (to.length === 0) {
        return { success: true };
      }

      const cc = normalize(input.cc);
      const response = await this._authenticatedFetch(`${this._bffBaseUrl}/api/communications/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          to,
          cc: cc.length > 0 ? cc : undefined,
          subject: input.subject,
          body: input.body,
          bodyFormat: input.bodyFormat ?? 'HTML',
          associations: input.associations,
        }),
      });

      if (!response.ok) {
        const errorText = await response.text().catch(() => 'Unknown error');
        console.warn('[EntityCreationService] Email send failed:', response.status, errorText);
        return {
          success: false,
          warning: `Could not send email (${response.status}). Please send manually.`,
        };
      }

      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not send email (${message}). Please send manually.`,
      };
    }
  }
}
