import type { ConflictBehaviorOption } from '@spaarke/sdap-client';

/**
 * Document Upload Service Types
 *
 * Shared type definitions for document upload services extracted from
 * UniversalQuickCreate PCF control. These types support both PCF (context.webAPI)
 * and Code Page (OData fetch with MSAL) contexts.
 *
 * @version 1.0.0
 */

// ---------------------------------------------------------------------------
// Token Provider
// ---------------------------------------------------------------------------

/**
 * Token provider function type.
 *
 * Returns a bearer token for authenticating API requests.
 * Works with both PCF (MSAL via MsalAuthProvider) and Code Page (MSAL via @azure/msal-browser) contexts.
 *
 * @returns Promise resolving to a JWT access token string
 */
export type ITokenProvider = () => Promise<string>;

// ---------------------------------------------------------------------------
// Dataverse Client Interface (Strategy Pattern)
// ---------------------------------------------------------------------------

/**
 * Dataverse record creation/update abstraction.
 *
 * Two implementations:
 * - PcfDataverseClient: wraps ComponentFramework.WebApi (PCF controls)
 * - ODataDataverseClient: direct OData fetch calls with token auth (Code Pages)
 */
export interface IDataverseClient {
  /**
   * Create a record in Dataverse.
   *
   * @param entityLogicalName - Entity logical name (e.g., "sprk_document")
   * @param data - Record payload object
   * @returns Created record reference with id
   */
  createRecord(entityLogicalName: string, data: Record<string, unknown>): Promise<DataverseRecordRef>;

  /**
   * Update a record in Dataverse.
   *
   * @param entityLogicalName - Entity logical name
   * @param id - Record GUID
   * @param data - Fields to update
   */
  updateRecord(entityLogicalName: string, id: string, data: Record<string, unknown>): Promise<void>;
}

/**
 * Reference to a Dataverse record (returned from create operations).
 */
export interface DataverseRecordRef {
  /** Record GUID */
  id: string;
}

// ---------------------------------------------------------------------------
// SPE File Metadata (from SDAP BFF API)
// ---------------------------------------------------------------------------

/**
 * SPE File Metadata returned from SDAP API (matches FileHandleDto from Sprk.Bff.Api).
 *
 * Maps to Dataverse fields:
 * - id        -> sprk_graphitemid / sprk_driveitemid
 * - name      -> sprk_filename
 * - size      -> sprk_filesize
 * - webUrl    -> sprk_filepath / sprk_sharepointurl
 */
export interface SpeFileMetadata {
  /** Graph API Item ID */
  id: string;

  /** File name */
  name: string;

  /** Parent folder ID (optional) */
  parentId?: string;

  /**
   * The SPE drive the bytes ACTUALLY landed in, per the server.
   *
   * Added 2026-09-03 (task 076). Under the record-keyed contract the client no longer chooses the
   * destination, so this is the ONLY trustworthy source for `sprk_document.sprk_graphdriveid` — any
   * client-side container is at best a guess and, for a secure record, provably wrong (the bytes go
   * to the record's own container while the column would point at the shared BU one, 404-ing every
   * later download on exactly the records that matter most).
   *
   * The server always populates it: `UploadSessionManager` maps
   * `uploadedItem.ParentReference?.DriveId ?? containerId`, so it is non-null on every successful
   * upload. Declared optional because `SpeFileMetadata` is also constructed by callers describing
   * files they did not upload through this service.
   */
  driveId?: string;

  /** File size in bytes */
  size: number;

  /** Created date/time (ISO 8601) */
  createdDateTime: string;

  /** Last modified date/time (ISO 8601) */
  lastModifiedDateTime: string;

  /** Version identifier (ETag) */
  eTag?: string;

  /** Is this a folder */
  isFolder: boolean;

  /** SharePoint web URL (may not be available in all responses) */
  webUrl?: string;

  // Convenience aliases (populated by FileUploadService after upload)

  /** Alias for id */
  driveItemId?: string;

  /** Alias for name */
  fileName?: string;

  /** Alias for webUrl */
  sharePointUrl?: string;

  /** Alias for size */
  fileSize?: number;
}

// ---------------------------------------------------------------------------
// Service Result
// ---------------------------------------------------------------------------

/**
 * Generic service operation result wrapper.
 */
export interface ServiceResult<T = void> {
  success: boolean;
  data?: T;
  error?: string;

  /**
   * Present when the operation was blocked by a NAME COLLISION rather than failing.
   *
   * Distinct from `error` because it is recoverable and the recovery needs a user decision:
   * nothing was written, the existing file is intact, and retrying the same upload with
   * `conflictBehavior: 'rename' | 'replace'` will succeed. A caller that only reads `error` still
   * behaves correctly (it shows the message) — this field is additive so existing consumers are
   * unaffected.
   */
  nameConflict?: { fileName: string };
}

// FileUploadApiRequest / FileDownloadRequest / FileDeleteRequest / FileReplaceRequest were DELETED
// 2026-09-03 with `./SdapApiClient.ts`, the parallel upload client whose method signatures they
// described. They had no other consumer — not the barrel's re-export, not a test, not a solution.
// The surviving client (`@spaarke/sdap-client`) takes positional arguments instead, so there is no
// replacement type to point at. `FileUploadRequest` below is a DIFFERENT, still-live type: it is
// this package's own service-layer input, not the wire shape.

// ---------------------------------------------------------------------------
// FileUploadService Request
// ---------------------------------------------------------------------------

/**
 * WHERE an upload is filed — and deliberately NOT a container id.
 *
 * Task 076: the client names the OWNING RECORD (or states that there is none) and the SERVER
 * resolves the container from it. The authorization key and the storage destination are then the
 * same value by construction and cannot disagree. Before this, the caller passed a `driveId`
 * resolved at wizard-OPEN time from the acting user's business unit — so files attached to a
 * SECURE record landed in the shared BU container, and SPE permissions are additive-only, so
 * nothing retracts that afterwards.
 *
 * A discriminated union rather than an optional pair: `{ entityLogicalName?, recordId? }` would let
 * a caller supply neither and silently get the record-less contract, which for a record-bearing
 * flow is the wrong container. Here the caller must SAY which contract it is on.
 */
export type UploadTarget =
  /** The content has an owning record that already exists. `PUT /api/obo/records/{entity}/{id}/files/{path}` */
  | { kind: 'record'; entityLogicalName: string; recordId: string }
  /**
   * The content genuinely has NO owning record yet — the server files it in the ACTING USER's
   * business-unit container. `PUT /api/obo/me/files/{path}`
   *
   * ⚠️ Not an escape hatch. If a record exists, use `kind: 'record'`.
   */
  | { kind: 'no-record' };

/**
 * Request for uploading a single file via FileUploadService.
 */
export interface FileUploadRequest {
  /** File to upload */
  file: File;

  /**
   * Where the file is filed. Replaced `driveId: string` on 2026-09-03 (task 076) — see
   * {@link UploadTarget}. The shape change is deliberate: it makes every un-migrated call site a
   * COMPILE ERROR rather than a container string that silently still fits.
   */
  target: UploadTarget;

  // `fileName?: string` ("optional override for file name") was REMOVED 2026-09-03 with the move to
  // `@spaarke/sdap-client`, which names the stored file from `file.name`. No caller ever passed it.
  // Removed rather than accepted-and-ignored: a field that is read from the type but dropped on the
  // way to the server is the same failure class as a comment that outlives its mechanism
  // (FAILURE-MODES AP-12). Renaming on upload needs a real parameter on the shared client.

  /**
   * Name-collision behaviour. OMIT on the first attempt (server defaults to `fail`, so a collision
   * returns `nameConflict` with the existing file untouched). Set only when RETRYING after the user
   * has chosen rename or replace.
   */
  conflictBehavior?: ConflictBehaviorOption;
}

// ---------------------------------------------------------------------------
// Multi-File Upload Types
// ---------------------------------------------------------------------------

/**
 * Request for uploading multiple files.
 */
export interface UploadFilesRequest {
  /** Files to upload */
  files: File[];

  /**
   * Where the batch is filed — see {@link UploadTarget}. Replaced `containerId: string` on
   * 2026-09-03 (task 076). One target for the whole batch, because a batch is one wizard step
   * against one parent.
   */
  target: UploadTarget;

  /**
   * Name-collision behaviour applied to EVERY file in this batch. Omit on a first attempt.
   *
   * Batch-wide rather than per-file because a retry carries exactly one user decision: the UI
   * re-invokes the pipeline with the single file the user just chose for. A batch of mixed
   * decisions is therefore a batch of single-file retries, not one call with a map.
   */
  conflictBehavior?: ConflictBehaviorOption;
}

/**
 * Progress update for multi-file upload.
 */
export interface UploadProgress {
  /** 1-based index of current file */
  current: number;

  /** Total number of files */
  total: number;

  /** Name of file currently being processed */
  currentFileName: string;

  /** Current status */
  status: 'uploading' | 'complete' | 'failed';

  /** Error message (when status is 'failed') */
  error?: string;

  /**
   * Present when `status === 'failed'` because of a NAME COLLISION rather than a real failure.
   *
   * Carried alongside `error` (not instead of it) so a consumer that only reads `error` still shows
   * something sensible. Consumers that understand this field offer the rename / new-version choice.
   */
  nameConflict?: { fileName: string };
}

/**
 * Result of multi-file upload operation.
 *
 * Returns uploaded file metadata only -- NO record creation.
 * Caller is responsible for creating Dataverse records via DocumentRecordService.
 */
export interface UploadFilesResult {
  /** Overall success flag (true if at least one file uploaded) */
  success: boolean;

  /** Total files attempted */
  totalFiles: number;

  /** Number of successful uploads */
  successCount: number;

  /** Number of failed uploads */
  failureCount: number;

  /** SPE metadata for successfully uploaded files */
  uploadedFiles: SpeFileMetadata[];

  /**
   * Errors for failed uploads.
   *
   * `nameConflict` marks the recoverable subset: nothing was written for that file, and retrying it
   * with `conflictBehavior` will succeed. Without this the collision reached the UI as an
   * indistinguishable error string, which is how it previously surfaced as "Unknown error occurred".
   */
  errors: { fileName: string; error: string; nameConflict?: { fileName: string } }[];
}

// ---------------------------------------------------------------------------
// Document Record Types (Dataverse)
// ---------------------------------------------------------------------------

/**
 * Parent entity context for document creation.
 */
export interface ParentContext {
  /** Parent entity logical name (e.g., "sprk_matter") */
  parentEntityName: string;

  /** Parent record GUID */
  parentRecordId: string;

  // `containerId: string` was DELETED here 2026-09-03 (task 076).
  //
  // It fed `sprk_document.sprk_graphdriveid` — the pointer used by every later download and by RAG
  // indexing — from a container the CLIENT resolved at wizard-OPEN time. That was survivable only
  // while the client also NAMED the upload destination, so the two agreed by construction. Under
  // the record-keyed contract the server picks the container, so for a secure record they provably
  // disagree: the bytes land in the record's own container while the column points at the shared
  // business-unit one. `sprk_graphdriveid` now comes from `SpeFileMetadata.driveId`, which is where
  // the server says the bytes went.
  //
  // Removed rather than left unread: a field that is still populated but no longer consulted is the
  // failure class this project keeps paying for (FAILURE-MODES AP-12). Deleting it turns every
  // remaining supplier into a compile error.

  /** Parent record display name (e.g., "MAT-2024-001") */
  parentDisplayName: string;
}

/**
 * Form data collected from user input.
 */
export interface DocumentFormData {
  /** Document name/title */
  documentName: string;

  /** Optional document description */
  description?: string;
}

/**
 * Result of creating a single Document record in Dataverse.
 */
export interface CreateResult {
  /** Success flag */
  success: boolean;

  /** File name that was processed */
  fileName: string;

  /** Created record ID (if successful) */
  recordId?: string;

  /** Document ID (Dataverse GUID, if successful) */
  documentId?: string;

  /** SharePoint Embedded drive ID (if successful) */
  driveId?: string;

  /** SharePoint Embedded item ID (if successful) */
  itemId?: string;

  /** Error message (if failed) */
  error?: string;
}

// ---------------------------------------------------------------------------
// Entity Configuration
// ---------------------------------------------------------------------------

/**
 * Configuration for a parent entity that supports document uploads.
 */
export interface EntityDocumentConfig {
  /** Entity logical name (e.g., "sprk_matter") */
  entityName: string;

  /** Lookup field name on Document entity (e.g., "sprk_matter") */
  lookupFieldName: string;

  /** Relationship schema name for metadata queries (e.g., "sprk_matter_document") */
  relationshipSchemaName: string;

  /**
   * Hardcoded navigation property name for @odata.bind fallback.
   * Used when NavMap API is unavailable. CASE-SENSITIVE.
   * Example: "sprk_Matter" (capital M)
   */
  navigationPropertyName?: string;

  /** Container ID field name on parent entity */
  containerIdField: string;

  /** Display name field on parent entity */
  displayNameField: string;

  /** Entity set name for OData (e.g., "sprk_matters") */
  entitySetName: string;
}

// ---------------------------------------------------------------------------
// NavMap Types (Navigation Property Metadata)
// ---------------------------------------------------------------------------

/**
 * Lookup Navigation Response from BFF NavMap API.
 * Contains the case-sensitive navigation property name required for @odata.bind.
 */
export interface LookupNavigationResponse {
  /** Child entity logical name */
  childEntity: string;

  /** Relationship schema name */
  relationship: string;

  /** Lookup attribute logical name (lowercase) */
  logicalName: string;

  /** Lookup attribute schema name */
  schemaName: string;

  /**
   * Navigation property name for @odata.bind (CASE-SENSITIVE).
   * Example: "sprk_Matter" (capital M)
   */
  navigationPropertyName: string;

  /** Target entity logical name (parent) */
  targetEntity: string;

  /** Data source: "dataverse", "cache", or "hardcoded" */
  source: string;
}

// ---------------------------------------------------------------------------
// Logger Interface
// ---------------------------------------------------------------------------

/**
 * Minimal logger interface used by document upload services.
 * Consumers must provide an implementation (e.g., wrapping console, PCF logger, etc.).
 */
export interface ILogger {
  info(source: string, message: string, data?: unknown): void;
  warn(source: string, message: string, data?: unknown): void;
  error(source: string, message: string, error?: unknown): void;
  debug(source: string, message: string, data?: unknown): void;
}

/**
 * Default console logger implementation.
 */
export const consoleLogger: ILogger = {
  info: (source, message, data) => console.log(`[${source}] ${message}`, data ?? ''),
  warn: (source, message, data) => console.warn(`[${source}] ${message}`, data ?? ''),
  error: (source, message, error) => console.error(`[${source}] ${message}`, error ?? ''),
  debug: (source, message, data) => console.debug(`[${source}] ${message}`, data ?? ''),
};
