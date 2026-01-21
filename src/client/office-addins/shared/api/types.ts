/**
 * Office API Client Types
 *
 * TypeScript interfaces matching the server DTOs for the /office/* endpoints.
 * These types ensure type safety when communicating with the Spaarke BFF API.
 *
 * @see projects/sdap-office-integration/spec.md for API contracts
 */

// ============================================
// Source Types
// ============================================

/**
 * Types of content that can be saved to Spaarke.
 */
export type SourceType = 'OutlookEmail' | 'OutlookAttachment' | 'WordDocument';

/**
 * Types of entities that documents can be associated with.
 */
export type AssociationType = 'Matter' | 'Project' | 'Invoice' | 'Account' | 'Contact';

// ============================================
// Save Request/Response
// ============================================

/**
 * Content details for the save request.
 */
export interface SaveContent {
  /** Outlook message ID (for OutlookEmail/OutlookAttachment) */
  emailId?: string;
  /** Whether to include email body */
  includeBody?: boolean;
  /** IDs of attachments to save */
  attachmentIds?: string[];
  /** Attachment content (base64 encoded) for client-side retrieval */
  attachments?: AttachmentContent[];
  /** Word document URL (for WordDocument) */
  documentUrl?: string;
  /** Document name */
  documentName?: string;
  /** Document content (base64 encoded) for Word documents */
  documentContent?: string;
}

/**
 * Attachment content for client-side upload.
 */
export interface AttachmentContent {
  /** Attachment ID from Outlook */
  id: string;
  /** Filename */
  name: string;
  /** MIME content type */
  contentType: string;
  /** Size in bytes */
  size: number;
  /** Base64-encoded content */
  content: string;
}

/**
 * Processing options for saved documents.
 */
export interface ProcessingOptions {
  /** Generate AI profile summary */
  profileSummary?: boolean;
  /** Index for RAG search */
  ragIndex?: boolean;
  /** Perform deep AI analysis */
  deepAnalysis?: boolean;
}

/**
 * Metadata for saved documents.
 */
export interface SaveMetadata {
  /** Optional description */
  description?: string;
  /** Optional tags */
  tags?: string[];
}

/**
 * Request to save email or document to Spaarke.
 * POST /office/save
 */
export interface SaveRequest {
  /** Type of content being saved */
  sourceType: SourceType;
  /** Entity type to associate with (required) */
  associationType: AssociationType;
  /** Entity ID to associate with (required) */
  associationId: string;
  /** Content details */
  content: SaveContent;
  /** Processing options */
  processing?: ProcessingOptions;
  /** Document metadata */
  metadata?: SaveMetadata;
}

/**
 * Response from save endpoint.
 * 202 Accepted for new jobs, 200 OK for duplicates.
 */
export interface SaveResponse {
  /** Job ID for tracking */
  jobId: string;
  /** Document ID (may be stub for new, final for duplicate) */
  documentId: string;
  /** URL to poll for status */
  statusUrl: string;
  /** URL for SSE streaming */
  streamUrl: string;
  /** Current job status */
  status: JobStatus;
  /** Whether this was a duplicate detection */
  duplicate: boolean;
  /** Message for duplicates */
  message?: string;
  /** Correlation ID for tracing */
  correlationId: string;
}

// ============================================
// Job Status Types
// ============================================

/**
 * Overall job status.
 */
export type JobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'PartialSuccess' | 'NeedsAttention';

/**
 * Individual stage status.
 */
export type StageStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Skipped';

/**
 * Processing stage information.
 */
export interface JobStage {
  /** Stage name */
  name: string;
  /** Stage status */
  status: StageStatus;
  /** Timestamp when stage completed (if applicable) */
  timestamp?: string;
}

/**
 * Job status response.
 * GET /office/jobs/{jobId}
 */
export interface JobStatusResponse {
  /** Job ID */
  jobId: string;
  /** Overall job status */
  status: JobStatus;
  /** Individual stage statuses */
  stages: JobStage[];
  /** Document ID (populated when created) */
  documentId?: string;
  /** URL to the document in Spaarke */
  documentUrl?: string;
  /** URL to the association target */
  associationUrl?: string;
  /** Error code if failed */
  errorCode?: string;
  /** Error message if failed */
  errorMessage?: string;
}

// ============================================
// Search Types
// ============================================

/**
 * Entity search result.
 */
export interface EntitySearchResult {
  /** Entity ID */
  id: string;
  /** Entity type */
  entityType: AssociationType;
  /** Dataverse logical name */
  logicalName: string;
  /** Display name */
  name: string;
  /** Additional display info (e.g., "Client: Acme | Status: Active") */
  displayInfo?: string;
  /** Icon URL */
  iconUrl?: string;
}

/**
 * Entity search response.
 * GET /office/search/entities
 */
export interface EntitySearchResponse {
  /** Search results */
  results: EntitySearchResult[];
  /** Total count (for pagination) */
  totalCount: number;
  /** Whether more results exist */
  hasMore: boolean;
}

/**
 * Entity search request parameters.
 */
export interface EntitySearchParams {
  /** Search query (min 2 chars) */
  q: string;
  /** Filter by entity types (comma-separated or array) */
  type?: AssociationType[] | string;
  /** Maximum results (default: 20, max: 50) */
  limit?: number;
}

/**
 * Document search result.
 */
export interface DocumentSearchResult {
  /** Document ID */
  id: string;
  /** Document name */
  name: string;
  /** File name */
  fileName: string;
  /** Content type */
  contentType: string;
  /** File size in bytes */
  size: number;
  /** Created date */
  createdOn: string;
  /** Modified date */
  modifiedOn: string;
  /** Associated entity type */
  associationType?: AssociationType;
  /** Associated entity name */
  associationName?: string;
  /** URL to the document */
  documentUrl?: string;
}

/**
 * Document search response.
 * GET /office/search/documents
 */
export interface DocumentSearchResponse {
  /** Search results */
  results: DocumentSearchResult[];
  /** Total count */
  totalCount: number;
  /** Whether more results exist */
  hasMore: boolean;
}

/**
 * Document search request parameters.
 */
export interface DocumentSearchParams {
  /** Search query */
  q: string;
  /** Filter by content types */
  contentTypes?: string[];
  /** Filter by association type */
  associationType?: AssociationType;
  /** Filter by association ID */
  associationId?: string;
  /** Maximum results */
  limit?: number;
}

// ============================================
// Quick Create Types
// ============================================

/**
 * Valid entity types for quick create.
 */
export type QuickCreateEntityType = 'matter' | 'project' | 'invoice' | 'account' | 'contact';

/**
 * Base quick create request.
 */
export interface QuickCreateRequestBase {
  /** Entity name (required) */
  name: string;
  /** Description (optional) */
  description?: string;
}

/**
 * Matter/Project/Invoice quick create request.
 */
export interface QuickCreateMatterRequest extends QuickCreateRequestBase {
  /** Client/Account ID (optional) */
  clientId?: string;
}

/**
 * Account quick create request.
 */
export interface QuickCreateAccountRequest extends QuickCreateRequestBase {
  /** Industry (optional) */
  industry?: string;
  /** City (optional) */
  city?: string;
}

/**
 * Contact quick create request.
 */
export interface QuickCreateContactRequest {
  /** First name (required) */
  firstName: string;
  /** Last name (required) */
  lastName: string;
  /** Email (optional) */
  email?: string;
  /** Account ID (optional) */
  accountId?: string;
}

/**
 * Union type for all quick create requests.
 */
export type QuickCreateRequest =
  | QuickCreateMatterRequest
  | QuickCreateAccountRequest
  | QuickCreateContactRequest;

/**
 * Quick create response.
 * 201 Created
 */
export interface QuickCreateResponse {
  /** Created entity ID */
  id: string;
  /** Entity type */
  entityType: AssociationType;
  /** Dataverse logical name */
  logicalName: string;
  /** Display name */
  name: string;
  /** URL to entity in Dataverse */
  url?: string;
}

// ============================================
// Share Types
// ============================================

/**
 * Share role for document access.
 */
export type ShareRole = 'ViewOnly' | 'Download' | 'Edit';

/**
 * Share links request.
 * POST /office/share/links
 */
export interface ShareLinksRequest {
  /** Document IDs to share */
  documentIds: string[];
  /** Recipient email addresses */
  recipients?: string[];
  /** Whether to grant access to external recipients */
  grantAccess?: boolean;
  /** Access role */
  role?: ShareRole;
}

/**
 * Share link info.
 */
export interface ShareLink {
  /** Document ID */
  documentId: string;
  /** Share URL */
  url: string;
  /** Document title */
  title: string;
}

/**
 * External invitation info.
 */
export interface ExternalInvitation {
  /** Recipient email */
  email: string;
  /** Invitation status */
  status: 'Created' | 'Pending' | 'Sent' | 'Accepted' | 'Declined';
  /** Invitation ID */
  invitationId?: string;
}

/**
 * Share links response.
 */
export interface ShareLinksResponse {
  /** Generated share links */
  links: ShareLink[];
  /** External invitations created */
  invitations?: ExternalInvitation[];
}

/**
 * Share attach request.
 * POST /office/share/attach
 */
export interface ShareAttachRequest {
  /** Document IDs to attach */
  documentIds: string[];
}

/**
 * Attachment info for Outlook compose.
 */
export interface AttachmentInfo {
  /** Document ID */
  documentId: string;
  /** Filename */
  filename: string;
  /** MIME content type */
  contentType: string;
  /** File size in bytes */
  size: number;
  /** Pre-signed download URL */
  downloadUrl: string;
  /** URL expiration time */
  urlExpiry: string;
}

/**
 * Share attach response.
 */
export interface ShareAttachResponse {
  /** Attachment information */
  attachments: AttachmentInfo[];
}

// ============================================
// Recent Items Types
// ============================================

/**
 * Recent association item.
 */
export interface RecentAssociation {
  /** Entity ID */
  id: string;
  /** Entity type */
  entityType: AssociationType;
  /** Display name */
  name: string;
  /** When last used */
  lastUsed: string;
  /** Additional display info */
  displayInfo?: string;
}

/**
 * Recent document item.
 */
export interface RecentDocument {
  /** Document ID */
  id: string;
  /** Document name */
  name: string;
  /** File name */
  fileName: string;
  /** When last accessed */
  lastAccessed: string;
  /** Associated entity type */
  associationType?: AssociationType;
  /** Associated entity name */
  associationName?: string;
}

/**
 * Favorite item.
 */
export interface FavoriteItem {
  /** Item ID */
  id: string;
  /** Item type (entity or document) */
  itemType: 'entity' | 'document';
  /** Entity type (if entity) */
  entityType?: AssociationType;
  /** Display name */
  name: string;
  /** When added to favorites */
  addedOn: string;
}

/**
 * Recent items response.
 * GET /office/recent
 */
export interface RecentItemsResponse {
  /** Recently used association targets */
  recentAssociations: RecentAssociation[];
  /** Recently accessed documents */
  recentDocuments: RecentDocument[];
  /** User's favorites */
  favorites: FavoriteItem[];
}

// ============================================
// Error Types
// ============================================

/**
 * ProblemDetails error response (RFC 7807).
 */
export interface ProblemDetails {
  /** Error type URI */
  type: string;
  /** Short error title */
  title: string;
  /** HTTP status code */
  status: number;
  /** Detailed error message */
  detail?: string;
  /** Request path */
  instance?: string;
  /** Correlation ID for tracing */
  correlationId?: string;
  /** Stable error code (e.g., OFFICE_001) */
  errorCode?: string;
  /** Field-level validation errors */
  errors?: Record<string, string[]>;
}

/**
 * Office API error codes.
 */
export enum OfficeApiErrorCode {
  // Validation errors (400)
  INVALID_SOURCE_TYPE = 'OFFICE_001',
  INVALID_ASSOCIATION_TYPE = 'OFFICE_002',
  ASSOCIATION_REQUIRED = 'OFFICE_003',
  ATTACHMENT_TOO_LARGE = 'OFFICE_004',
  TOTAL_SIZE_EXCEEDED = 'OFFICE_005',
  BLOCKED_FILE_TYPE = 'OFFICE_006',
  // Not found errors (404)
  ASSOCIATION_NOT_FOUND = 'OFFICE_007',
  JOB_NOT_FOUND = 'OFFICE_008',
  // Forbidden errors (403)
  ACCESS_DENIED = 'OFFICE_009',
  CANNOT_CREATE_ENTITY = 'OFFICE_010',
  // Conflict errors (409)
  DOCUMENT_EXISTS = 'OFFICE_011',
  // Service errors (502/503)
  SPE_UPLOAD_FAILED = 'OFFICE_012',
  GRAPH_API_ERROR = 'OFFICE_013',
  DATAVERSE_ERROR = 'OFFICE_014',
  PROCESSING_UNAVAILABLE = 'OFFICE_015',
}

// ============================================
// SSE Event Types
// ============================================

/**
 * SSE stage update event data.
 */
export interface SseStageUpdateEvent {
  /** Stage name */
  stage: string;
  /** Stage status */
  status: StageStatus;
  /** Event timestamp */
  timestamp: string;
}

/**
 * SSE job complete event data.
 */
export interface SseJobCompleteEvent {
  /** Final job status */
  status: 'Completed' | 'Failed' | 'PartialSuccess';
  /** Document ID */
  documentId?: string;
  /** Document URL */
  documentUrl?: string;
  /** Error code (if failed) */
  errorCode?: string;
  /** Error message (if failed) */
  errorMessage?: string;
}

/**
 * SSE event types.
 */
export type SseEventType = 'stage-update' | 'job-complete' | 'error' | 'heartbeat';

/**
 * SSE event data union.
 */
export type SseEventData = SseStageUpdateEvent | SseJobCompleteEvent | ProblemDetails | null;
