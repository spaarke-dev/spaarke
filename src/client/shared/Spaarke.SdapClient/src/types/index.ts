/**
 * SDAP Client configuration options.
 */
export interface SdapClientConfig {
  /** Base URL of SDAP BFF API (e.g., 'https://spe-bff-api.azurewebsites.net') */
  baseUrl: string;

  /** Request timeout in milliseconds (default: 300000 = 5 minutes) */
  timeout?: number;
}

/**
 * SharePoint Drive Item metadata.
 */
export interface DriveItem {
  /** Unique item ID */
  id: string;

  /** File/folder name */
  name: string;

  /** File size in bytes (null for folders) */
  size: number | null;

  /** Drive ID containing this item */
  driveId: string;

  /** Parent folder reference ID */
  parentReferenceId?: string;

  /** Created date/time */
  createdDateTime: string;

  /** Last modified date/time */
  lastModifiedDateTime: string;

  /** ETag for versioning */
  eTag?: string;

  /** Whether this is a folder */
  isFolder: boolean;

  /** MIME type (files only) */
  mimeType?: string;
}

/**
 * Upload session for chunked uploads.
 */
export interface UploadSession {
  /** Upload URL for PUT requests */
  uploadUrl: string;

  /** Session expiration date/time */
  expirationDateTime: string;

  /** Next expected byte ranges (for resumption) */
  nextExpectedRanges?: string[];
}

/**
 * File metadata.
 */
export interface FileMetadata extends DriveItem {
  /** Download URL */
  downloadUrl?: string;

  /** Web URL for browser viewing */
  webUrl?: string;
}

/**
 * Upload progress callback.
 */
export type UploadProgressCallback = (percent: number) => void;

/**
 * SDAP API error response.
 */
export interface SdapApiError {
  /** Error status code */
  status: number;

  /** Error title */
  title: string;

  /** Detailed error message */
  detail: string;

  /** Trace ID for correlation */
  traceId?: string;
}

/**
 * Container information.
 */
export interface Container {
  /** Container ID */
  id: string;

  /** Container display name */
  displayName: string;

  /** Drive ID */
  driveId: string;
}

/**
 * Authenticated fetch function signature. Matches `@spaarke/auth.authenticatedFetch`.
 * Consumers inject this so `sdap-client` operations can call BFF endpoints with
 * the canonical Spaarke Auth v2 contract (ADR-028) without taking a direct
 * dependency on `@spaarke/auth`. The function MUST attach a valid Bearer token
 * for the BFF API audience (`api://{bff-client-id}/SDAP.Access`).
 */
export type AuthenticatedFetchFn = (url: string, options?: RequestInit) => Promise<Response>;

/**
 * Parent entity context attached to indexed file chunks for entity-scoped search.
 * Mirrors the BFF's `ParentEntityContext` record so chunks are filterable by
 * parent record in `spaarke-files-index`.
 */
export interface ParentEntityContext {
  /** Logical entity name — e.g., 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_event'. */
  entityType: string;

  /** GUID of the parent record. */
  entityId: string;

  /** Display name of the parent record (e.g., matter name) — informational, no routing impact. */
  entityName: string;
}

/**
 * Request payload for `SdapApiClient.indexFile()` / `IndexFileOperation`.
 * Triggers sync OBO indexing of a SPE-resident file into Azure AI Search.
 *
 * Pattern: writer-identity matching (Pattern 4) — the same user who wrote the
 * file via OBO MUST be the caller indexing it via OBO. Used by wizards after
 * a successful PUT to `/api/obo/containers/{id}/files/{path}`.
 */
export interface IndexFileRequest {
  /** SPE drive ID (NOT the container ID — these are different). */
  driveId: string;

  /** SPE drive item ID (returned from a prior upload). */
  itemId: string;

  /** File name with extension — used for format detection. */
  fileName: string;

  /** Tenant ID for multi-tenant index routing. */
  tenantId: string;

  /** Optional Dataverse `sprk_document` record GUID linked to this file. */
  documentId?: string;

  /** Optional parent entity context for entity-scoped search. */
  parentEntity?: ParentEntityContext;

  /**
   * Optional explicit Azure AI Search index name. When provided, routes to that
   * index after validating against the BFF's `AiSearchOptions.AllowedIndexes`.
   * When omitted, the BFF resolver falls through the standard chain
   * (parent record → BU cascade → tenant default).
   */
  searchIndexName?: string;
}

/**
 * Result returned by `SdapApiClient.indexFile()`.
 */
export interface IndexFileResult {
  /** True when indexing succeeded end-to-end (chunks written to AI Search). */
  success: boolean;

  /** Number of chunks indexed (when `success` is true). */
  chunksIndexed?: number;

  /** Error message when `success` is false. */
  errorMessage?: string;
}
