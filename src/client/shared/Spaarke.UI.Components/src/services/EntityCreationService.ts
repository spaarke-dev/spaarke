/**
 * EntityCreationService.ts
 * Generic service for entity creation workflows (Matter, Project, Event, etc.).
 *
 * Responsibilities:
 *   1. Upload files to SPE via BFF OBO endpoint
 *   2. Create Dataverse entity records: a table the BFF creates (`BFF_CHILD_CREATE_TABLES` — the child tables, work
 *      assignment, matter and project) through `POST /api/v1/child-records/{table}`; any other table via WebApi
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
 *
 * // Create the record FIRST — the upload is keyed on it, and the server resolves the storage
 * // container from that same record (task 076). There is no container parameter. A matter is created through the
 * // BFF (POST /api/v1/child-records/sprk_matter): the server decides its owner and applies field mapping.
 * const matterId = await service.createEntityRecord('sprk_matter', matterData);
 * const uploadResult = await service.uploadFilesToSpe('sprk_matter', matterId, files);
 * await service.createDocumentRecords('sprk_matters', matterId, 'sprk_Matter', uploadResult.uploadedFiles);
 * ```
 */

import { createChildRecordViaBff, isBffChildCreateTable } from '../utils/adapters/bffChildWriteAdapter';
import type { IWebApiLike, IWebApiWithCreate } from '../types/WebApiLike';
import type { IUploadedFile } from '../components/FileUpload/fileUploadTypes';
import { SdapApiClient, type DriveItem, type IndexFileRequest, type IndexFileResult } from './sdap';
import { cleanGuid } from '../utils/guid';
import type { ResponseFetchFn } from '../utils/fetchTypes';
// PolymorphicResolverService not needed — document records use canonical field set only

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Cascade defaults derived from the current user's owning Business Unit.
 *
 * 🔴 **No search index is read from the business unit or written by any client** (field-mapping-server-write-path-r1
 * FR-40, task 035). The index follows the record's content type and is the server's to set; `sprk_searchindexname`
 * and the `sprk_ai_search_index` bind are neither selected here nor sent on a create or an index-file call, and the
 * server logs a warning (`client_search_index_dropped`) if one arrives.
 *
 * 🔴 **`sprk_containerid` is NO LONGER cascaded onto any create payload** (task 076, 2026-09-03).
 * A storage location is not the client's to choose; the server derives it from the record. The
 * `containerId` field below is still RETURNED by `resolveUserBuDefaults` because callers outside
 * the create-payload path read it (notably Compose's `resolveContainer`) — but nothing writes it
 * to `sprk_containerid` any more, and nothing should.
 *
 * The container field is OPTIONAL on `businessunit`; when unset it is `undefined`.
 */
export interface IUserBuCascadeDefaults {
  /** `businessunit.sprk_containerid` for the current user's owning BU, or undefined if unset. */
  containerId?: string;
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

/**
 * One extra `@odata.bind` to emit on a `sprk_document` create payload, alongside
 * the primary bind. `sprk_document` exposes separate typed lookups per parent
 * type (`sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_event`, …), so one
 * document row can natively bind to multiple parents (design.md §5.8) — e.g. the
 * host record AND the newly-created child record.
 */
export interface IAdditionalBind {
  /** Target entity SET name (plural, e.g. `'sprk_matters'`). */
  entitySet: string;
  /** GUID of the target record. Brace-wrapped GUIDs are normalized automatically. */
  id: string;
  /** Navigation property name on `sprk_document` (e.g. `'sprk_Matter'`). */
  navProp: string;
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

/**
 * Authenticated fetch function signature (injected by caller).
 *
 * Despite the name, this is the EITHER-shape fetch ({@link ResponseFetchFn}), not `@spaarke/auth`'s
 * throwing `AuthenticatedFetchFn`: the send path is also reached from the Outlook add-in, whose fetch
 * returns failures (`spaarke-send-email-pane.d.ts`). The name is kept because it is public. Surfaces
 * that are only ever given `@spaarke/auth`'s `authenticatedFetch` should take the throwing type from
 * `utils/fetchTypes` instead.
 */
export type AuthenticatedFetchFn = ResponseFetchFn;

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
   * via `SdapApiClient.indexFile()` (`services/sdap`). Indexing runs under
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
   * @returns Warnings array describing per-file failures (empty when all succeed).
   */
  async indexUploadedFiles(
    uploadedFiles: ISpeFileMetadata[],
    tenantId: string,
    parentEntity?: { entityType: string; entityId: string; entityName: string },
    createdDocumentIds?: string[]
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
   * Normalize a GUID for use in an `@odata.bind` URL: strip surrounding curly
   * braces (as returned by `Xrm.Utility.lookupObjects` / some Xrm context APIs)
   * and lowercase. Mirrors the normalization in
   * `PolymorphicResolverService.applyResolverFields` — the OData bind syntax
   * rejects brace-wrapped GUIDs with HTTP 400 "Error in query syntax".
   */
  private static _cleanGuid(id: string): string {
    return cleanGuid(id);
  }

  /**
   * 🔴 **`applyDefaultContainerId` was DELETED 2026-09-03 by unified-access-control-r2 task 076
   * (harmful write "W1"). Do not reintroduce it.**
   *
   * It stamped the ACTING USER's business-unit container onto every newly-created row — including
   * secure projects, unconditionally and with no suppression. `CreateProjectWizard/projectService.ts`
   * set `sprk_issecure` and then called `applyUserBuDefaults` regardless, so a project marked secure
   * was born pointing at the SHARED container. Because SharePoint Embedded permissions are
   * additive-only, nothing later retracts content placed there.
   *
   * `sprk_containerid` is now written by exactly one authority: the server. For a secure project it
   * is stamped by provisioning (BFF task 021); for everything else the container is DERIVED at use
   * time from the record's own business unit, so there is nothing for a create payload to default.
   * A client that writes this column is asserting a storage location it has no authority to choose —
   * which is the shape this whole task exists to remove.
   */

  /**
   * 🔴 **`applyDefaultSearchIndexName` and `applyUserBuDefaults` were DELETED by field-mapping-server-write-path-r1
   * task 035 (FR-40). Do not reintroduce them.** They stamped the acting user's business-unit search index onto the
   * create payload. The index follows the record's content type and is the server's to set; the server drops a client
   * value and logs a warning when one arrives.
   */

  /**
   * Resolve the current user's owning Business Unit and its container (`sprk_containerid`). No search index column is read.
   *
   * Chain (matches the SemanticSearchControl NavigationService + SummarizeFilesWizard pattern):
   *   systemuser(userId) → `_businessunitid_value` → businessunit(buId)
   *     → `sprk_containerid`
   *
   * Caller responsibilities:
   *   - Determine `userId` upstream (e.g. via `Xrm.Utility.getUserId()` in a Code Page / PCF host).
   *     This helper stays Xrm-host-agnostic per ADR-012 so it can be unit-tested without
   *     a real Xrm global.
   *
   * Behavior:
   *   - Returns `{ containerId: undefined, businessUnitId: undefined }`
   *     if the user has no `_businessunitid_value` (rare).
   *   - Returns `containerId` as `undefined` when the BU exists but the field is unset
   *     (Spaarke Dev 1 / Test 1 case per Phase A.5 — operator setup may lag).
   *   - Returns trimmed-empty strings as `undefined` (treated as unset).
   *   - Never throws on Dataverse "field exists but is null" — only network / 4xx errors propagate.
   *
   * @param webApi Xrm.WebApi-compatible interface (PCF context.webAPI or wrapped Xrm.WebApi).
   * @param userId GUID of the current user (with or without surrounding braces).
   * @returns The user's BU id and container; `containerId` may be `undefined` if unset on the BU.
   *
   * @see spec.md FR-WIZ-01..05
   * @see design.md §5.0 (BU cascade source)
   */
  static async resolveUserBuDefaults(webApi: IWebApiLike, userId: string): Promise<IUserBuCascadeDefaults> {
    const cleanUserId = cleanGuid(userId);

    // Step 1: user → BU id
    const userRecord = await webApi.retrieveRecord('systemuser', cleanUserId, '?$select=_businessunitid_value');
    const rawBuId = userRecord['_businessunitid_value'];
    const buId = typeof rawBuId === 'string' && rawBuId.trim() !== '' ? rawBuId : undefined;
    if (!buId) {
      return { containerId: undefined, businessUnitId: undefined };
    }

    // Step 2: BU → cascade fields
    const buRecord = await webApi.retrieveRecord('businessunit', buId, '?$select=sprk_containerid');

    const normalize = (v: unknown): string | undefined => {
      if (typeof v !== 'string') return undefined;
      const trimmed = v.trim();
      return trimmed === '' ? undefined : trimmed;
    };

    return {
      businessUnitId: buId,
      containerId: normalize(buRecord['sprk_containerid']),
    };
  }

  /**
   * Upload files to SPE against their OWNING RECORD (task 076 contract).
   *
   * Uses: `PUT /api/obo/records/{entityLogicalName}/{recordId}/files/{path}`
   *
   * 🔴 **The container is NOT a parameter, and that is the point.** The server resolves the
   * container from the same record it authorizes the caller against, so the authorization key and
   * the storage destination are one value and cannot disagree. Before task 076 the caller named a
   * container resolved at wizard-OPEN time, which meant a secure record created with files put those
   * files in the shared business-unit container — and SPE permissions are additive-only, so nothing
   * retracts that afterwards.
   *
   * **Signature changed 2026-09-03** from `(containerId, files, onProgress)`. The arity change is
   * deliberate: it makes every un-migrated call site a COMPILE ERROR rather than a silent
   * pass-through of a string that happens to still fit.
   *
   * Post-upload RAG indexing is the caller's responsibility — invoke `indexUploadedFiles`, which
   * already reads the SERVER's `driveId` off each result rather than any client-side container.
   *
   * @param entityLogicalName Dataverse logical name of the owning record (e.g. `'sprk_matter'`)
   * @param recordId GUID of the owning record — must already exist when this is called
   * @param files Files to upload
   * @param onProgress Optional progress callback
   */
  async uploadFilesToSpe(
    entityLogicalName: string,
    recordId: string,
    files: IUploadedFile[],
    onProgress?: (progress: IUploadProgress) => void
  ): Promise<IFileUploadResult> {
    // The server route is constrained `{recordId:guid}`, so a brace-wrapped or upper-cased GUID
    // would MISS THE ROUTE and 404 — surfacing to the user as a plain "upload failed" with no clue
    // that the id shape was the cause. Brace-wrapped GUIDs demonstrably reach this service (that is
    // why `createDocumentRecords` already normalizes its `parentEntityId` the same way), and the
    // ids flowing in here come from a variety of wizard hosts and Xrm lookups.
    const cleanRecordId = EntityCreationService._cleanGuid(recordId);

    return await this._uploadEach(
      files,
      // `conflictBehavior` is deliberately OMITTED so the BFF applies its non-destructive `fail`
      // default: a same-named file returns 409 with the stored file untouched, rather than
      // silently overwriting it.
      file => this._getSdapClient().uploadFileForRecord(entityLogicalName, cleanRecordId, file),
      onProgress
    );
  }

  /**
   * Upload files that genuinely have NO owning record yet; the server resolves the container from
   * the ACTING USER's business unit.
   *
   * Uses: `PUT /api/obo/me/files/{path}`
   *
   * ⚠️ **Only for the flows that cannot create the record first.** If a record exists, use
   * {@link uploadFilesToSpe} — sending the bytes here stores them in the caller's business-unit
   * container instead of the record's, which for a secure record is the wrong container and is not
   * reversible. At HEAD there are exactly TWO such flows — `createXrmEmailComposeHandlers.ts`
   * (EmailComposer local attachment) and `CreateAnalysisWizardWidget.tsx` (Analysis wizard standalone
   * document) — and task 076's classification (`notes/task-076-client-cutover-and-supplier-
   * classification.md:54-55`) reads both as **parentless**: an email attachment on an unsent draft
   * and a standalone analysis document genuinely have no record to belong to when the bytes move.
   * (A third flow, DocumentUploadWizard's "skip associate", was cut over to {@link uploadFilesToSpe}
   * by task 076 and is no longer a caller here.)
   *
   * Note this is still NOT a client-named container: the client supplies nothing, and a caller can
   * only ever write into their own BU's container, which they are entitled to anyway.
   */
  async uploadFilesWithoutRecord(
    files: IUploadedFile[],
    onProgress?: (progress: IUploadProgress) => void
  ): Promise<IFileUploadResult> {
    return await this._uploadEach(files, file => this._getSdapClient().uploadFileWithoutRecord(file), onProgress);
  }

  /**
   * Shared per-file loop for both upload contracts.
   *
   * Extracted with the record-keyed cutover so the two cannot drift in progress reporting, error
   * collection, or DriveItem→ISpeFileMetadata mapping. Divergence between two copies of one upload
   * path is a failure this project has already paid for more than once.
   */
  private async _uploadEach(
    files: IUploadedFile[],
    uploadOne: (file: File) => Promise<DriveItem>,
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
      onProgress?.({
        current: i + 1,
        total: files.length,
        currentFileName: file.name,
        status: 'uploading',
      });

      try {
        // The shared client owns URL construction (including encoding the file name) and failure
        // translation.
        const item = await uploadOne(file.file);

        // DriveItem -> ISpeFileMetadata. `size` is nullable on the shared type (folders); an
        // uploaded file always has one, and 0 is the honest fallback for a zero-byte upload.
        //
        // `driveId` is where the bytes ACTUALLY landed, per the server. Under the record-keyed
        // contract that is the only trustworthy value — the caller no longer chooses, so any
        // client-side container is at best a guess and at worst (a secure record) simply wrong.
        // The server always populates it (`ParentReference?.DriveId ?? containerId`).
        uploadedFiles.push({
          id: item.id,
          name: item.name,
          size: item.size ?? 0,
          webUrl: item.webUrl,
          driveId: item.driveId,
        });

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
   * Create a Dataverse entity record: through the BFF for a table it creates (`isBffChildCreateTable`), else via WebApi.
   *
   * @param entityName Dataverse logical name (e.g., 'sprk_matter', 'sprk_project')
   * @param entityData Entity payload with field values and @odata.bind lookups
   * @returns The GUID of the created record
   */
  async createEntityRecord(entityName: string, entityData: Record<string, unknown>): Promise<string> {
    // UAC-r2 task 147 r1 (owner round 28 item 1): a CHILD record is created through the BFF (G5) — the server decides its
    // owner. So are the roots in the same set (work assignment; matter and project since field-mapping-server-write-path-r1
    // task 020): the server decides the owner, applies field mapping and creates a project under a secure record secure.
    if (isBffChildCreateTable(entityName)) {
      return createChildRecordViaBff(this._authenticatedFetch, this._bffBaseUrl, entityName, entityData);
    }
    const result = await this._webApi.createRecord(entityName, entityData);
    return result.id;
  }

  /**
   * Create sprk_document records in Dataverse linking uploaded SPE files to a parent entity.
   *
   * Each document record is created with:
   *   - sprk_documentname / sprk_filename: file name
   *   - sprk_filesize: file size in bytes
   *   - Navigation property @odata.bind to parent entity
   * and then its file is ATTACHED by the BFF ({@link attachUploadedFile}), which stamps the SPE pointer
   * (`sprk_graphdriveid` / `sprk_graphitemid`), `sprk_hasfile` and `sprk_filepath` server-side
   * (unified-access-control-r2 task 166 f1). A row whose file the BFF refuses is removed and reported.
   *
   * @param parentEntityName Logical name of the parent entity set (e.g., 'sprk_matters')
   * @param parentEntityId GUID of the parent entity record
   * @param navigationProperty Navigation property name on sprk_document (e.g., 'sprk_Matter')
   * @param uploadedFiles SPE file metadata from uploadFilesToSpe()
   * @param options Additional context for the document records. `additionalBinds`
   *   (added by visual-host-create-button-r1 task 013 per design.md §5.8) lets a
   *   caller bind the SAME document to one or more extra parents — e.g. the
   *   Visual Host launch's host record — alongside the primary bind, since
   *   `sprk_document` exposes separate typed lookups per parent type. Omitting
   *   it preserves the historical single-bind behavior exactly (backward
   *   compatible — this service is shared by multiple wizards).
   */
  async createDocumentRecords(
    parentEntityName: string,
    parentEntityId: string,
    navigationProperty: string,
    uploadedFiles: ISpeFileMetadata[],
    options?: {
      /**
       * FALLBACK SPE drive id, used only when an uploaded file carries no `driveId` of its own.
       *
       * ⚠️ Corrected 2026-09-03: this was documented as *"stored as sprk_graphdriveid and
       * sprk_containerid"*. The second half was never true — this method has never written
       * `sprk_containerid`, and `sprk_document` deliberately keeps that column NULL (a fact
       * `DocumentRecordService.payload.test.ts` pins as a NEGATIVE assertion). A doc comment
       * describing a write that does not happen is how this project has repeatedly turned a comment
       * into a believed constraint — see FAILURE-MODES AP-12.
       *
       * Prefer leaving this unset: `createDocumentRecords` now reads `file.driveId`, the drive the
       * SERVER reported on the upload, which is the only value that says where the bytes actually
       * landed. Pass it only when building document rows for files not uploaded via
       * `uploadFilesToSpe`.
       */
      containerId?: string;
      /** Parent record display name — stored in sprk_regardingrecordname for resolver fields */
      parentRecordName?: string;
      /** Parent entity logical name (e.g., 'sprk_matter'). If omitted, derived from parentEntityName by removing trailing 's'. */
      parentEntityLogicalName?: string;
      /**
       * Extra `@odata.bind` entries to emit on EVERY created document, alongside
       * the primary `navigationProperty` bind (e.g. bind to both the host record
       * and the newly-created child record). Each entry's nav-prop must differ
       * from the primary bind and from every other entry — a duplicate nav-prop
       * is skipped (primary always wins) and reported as a warning rather than
       * silently overwriting a bind.
       */
      additionalBinds?: IAdditionalBind[];
    }
  ): Promise<IDocumentLinkResult> {
    const warnings: string[] = [];
    let linkedCount = 0;
    const createdDocumentIds: string[] = [];

    const containerId = options?.containerId;

    for (const file of uploadedFiles) {
      try {
        // 🔴 `sprk_graphdriveid` MUST record where the bytes ACTUALLY landed, which is the drive the
        // SERVER chose and reported back on the upload response — not a container the caller picked.
        //
        // Before task 076's cutover this read `containerId ?? null`, i.e. the container the wizard
        // resolved when it OPENED. That was survivable only while the client also named the upload
        // destination, so the two happened to agree. Under the record-keyed contract the server
        // resolves the container from the owning record, so for a SECURE record they provably
        // DISAGREE: the bytes go to the record's own container while this column would have pointed
        // at the shared business-unit one — a dangling pointer that 404s on every subsequent
        // download, on exactly the records that matter most.
        //
        // `driveId` is always populated server-side (`ParentReference?.DriveId ?? containerId`), so
        // the `options.containerId` fallback below is retained only for callers that build document
        // rows for files they did not upload through `uploadFilesToSpe`.
        const driveId = file.driveId ?? containerId ?? undefined;

        // Payload aligned with canonical DocumentRecordService fields.
        //
        // 🔴 NO SPE POINTER IN THE CREATE PAYLOAD (unified-access-control-r2 task 166 f1; owner round 21 item 1 (i)).
        // `sprk_graphitemid` / `sprk_graphdriveid` are the pointer the BFF follows AS THE APPLICATION on every
        // download, so they are field-secured and writable by the BFF identity only. The row is created without them
        // (and without `sprk_hasfile` / `sprk_filepath`, which the BFF writes WITH the pointer so "has a file" and
        // "points at a file" never disagree), then {@link attachUploadedFile} asks the BFF to verify and stamp them.
        const documentEntity: Record<string, unknown> = {
          sprk_documentname: file.name,
          sprk_filename: file.name,
          sprk_filesize: file.size ?? null,
        };

        // Add @odata.bind navigation property to link document to parent entity.
        // GUID normalization (strip braces, lowercase) mirrors the convention used
        // by PolymorphicResolverService.applyResolverFields — defensive against a
        // caller passing a brace-wrapped GUID (e.g. sourced from an Xrm lookup
        // control) straight through to the OData bind URL.
        const usedNavProps = new Set<string>();
        if (navigationProperty) {
          const cleanParentId = EntityCreationService._cleanGuid(parentEntityId);
          documentEntity[`${navigationProperty}@odata.bind`] = `/${parentEntityName}(${cleanParentId})`;
          usedNavProps.add(navigationProperty);
        }

        // Additional binds (design.md §5.8 multi-parent dual-bind — e.g. bind the
        // same document to both the Visual Host launch's host record and the
        // newly-created child record). Backward compatible: omitting
        // `options.additionalBinds` leaves this loop a no-op and the payload
        // identical to the pre-existing single-bind shape. A duplicate nav-prop
        // (incl. one colliding with the primary bind) is skipped + warned rather
        // than silently overwriting an already-set bind.
        for (const bind of options?.additionalBinds ?? []) {
          if (usedNavProps.has(bind.navProp)) {
            const msg = `Skipped duplicate additionalBinds nav-prop "${bind.navProp}" for "${file.name}" — a bind for this nav-prop was already set on the payload.`;
            console.warn(`[EntityCreationService] ${msg}`);
            warnings.push(msg);
            continue;
          }
          const cleanBindId = EntityCreationService._cleanGuid(bind.id);
          documentEntity[`${bind.navProp}@odata.bind`] = `/${bind.entitySet}(${cleanBindId})`;
          usedNavProps.add(bind.navProp);
        }

        console.info('[EntityCreationService] createDocumentRecord payload:', JSON.stringify(documentEntity, null, 2));
        // UAC-r2 task 147 r1 (owner round 28 item 1): the document row is created through the BFF (G5) — the server
        // decides its owner (the Secure Record Owners team when its parent is secure) and records the caller as its
        // creator person; a refusal is this file's warning.
        const documentId = await createChildRecordViaBff(
          this._authenticatedFetch,
          this._bffBaseUrl,
          'sprk_document',
          documentEntity
        );

        // The BFF stamps the pointer after verifying the file (task 166 f1). A row whose file could not be attached
        // is removed rather than left looking like a document with no file.
        try {
          await this.attachUploadedFile(documentId, { id: file.id, driveId });
        } catch (attachErr) {
          await this._deleteUnattachedDocument(documentId);
          throw attachErr;
        }

        createdDocumentIds.push(documentId);
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
   * Ask the BFF to attach a file this caller uploaded to the `sprk_document` this caller created
   * (`POST /api/v1/documents/{id}/file`, unified-access-control-r2 task 166 f1).
   *
   * The ONLY way the client gives a document its file: the pointer columns are field-secured and
   * writable by the BFF identity only. The BFF checks that the caller created the row and uploaded
   * the file and that the file sits in the container derived for the row, then stamps the pointer,
   * `sprk_hasfile` and `sprk_filepath` server-side.
   *
   * @param documentId GUID of the `sprk_document` just created
   * @param file the upload response (`id` = drive item id, `driveId` = where the bytes landed)
   * @throws the server's refusal (403 / 409) — the caller decides what to do with the row
   */
  async attachUploadedFile(documentId: string, file: { id: string; driveId?: string }): Promise<void> {
    await this._getSdapClient().attachDocumentFile(documentId, file);
  }

  /**
   * Best-effort removal of a document row whose file could not be attached, so the user never sees a
   * "document" that has no file. Uses `deleteRecord` when the host WebApi offers it (Xrm.WebApi does);
   * never throws — the attach failure is what the caller reports.
   */
  private async _deleteUnattachedDocument(documentId: string): Promise<void> {
    if (typeof this._webApi.deleteRecord !== 'function') {
      return;
    }

    try {
      await this._webApi.deleteRecord('sprk_document', documentId);
    } catch (err) {
      console.warn(`[EntityCreationService] Could not remove document ${documentId} whose file was not attached:`, err);
    }
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
