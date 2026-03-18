/**
 * TypeScript type definitions for the SPE Admin App.
 *
 * All interfaces mirror the shapes returned by the BFF API (/api/spe/*) endpoints
 * and the Dataverse table schemas defined in spec.md.
 *
 * Graph API role values (ContainerRole) match the Microsoft Graph API naming:
 * "reader" | "writer" | "manager" | "owner"
 */

// ---------------------------------------------------------------------------
// Core / Shared
// ---------------------------------------------------------------------------

/** Dataverse status option set values */
export type ActiveStatus = "active" | "inactive";

/** Billing classification option set values for container types */
export type BillingClassification = "trial" | "standard" | "directToCustomer";

/** Sharing capability option set values */
export type SharingCapability =
  | "disabled"
  | "externalUserSharingOnly"
  | "existingExternalUserSharingOnly"
  | "externalUserAndGuestSharing";

// ---------------------------------------------------------------------------
// Business Unit
// ---------------------------------------------------------------------------

/**
 * Dataverse Business Unit — returned by GET /api/spe/businessunits.
 * Used by the BU picker to scope all SPE operations.
 */
export interface BusinessUnit {
  /** GUID of the Business Unit record */
  businessUnitId: string;
  /** Display name of the Business Unit */
  name: string;
  /** Whether this is the root/default Business Unit */
  isRootUnit: boolean;
  /** Parent Business Unit ID (null for root) */
  parentBusinessUnitId: string | null;
}

// ---------------------------------------------------------------------------
// SPE Environment (sprk_speenvironment)
// ---------------------------------------------------------------------------

/**
 * SPE Environment configuration record from the sprk_speenvironment Dataverse table.
 * Returned by GET /api/spe/environments.
 */
export interface SpeEnvironment {
  /** Primary key GUID (sprk_speenvironmentid) */
  id: string;
  /** Display name, e.g. "Production", "Dev" (sprk_name) */
  name: string;
  /** Azure AD tenant ID (sprk_tenantid) */
  tenantId: string;
  /** Tenant display name (sprk_tenantname) */
  tenantName: string;
  /** SharePoint root site URL (sprk_rootsiteurl) */
  rootSiteUrl: string;
  /** Microsoft Graph API endpoint (sprk_graphendpoint) */
  graphEndpoint: string;
  /** Whether this is the default environment (sprk_isdefault) */
  isDefault: boolean;
  /** Active / inactive status (sprk_status) */
  status: ActiveStatus;
}

/** Payload for creating or updating a SpeEnvironment */
export interface SpeEnvironmentUpsert {
  name: string;
  tenantId: string;
  tenantName: string;
  rootSiteUrl: string;
  graphEndpoint: string;
  isDefault: boolean;
  status: ActiveStatus;
}

// ---------------------------------------------------------------------------
// Container Type Config (sprk_specontainertypeconfig)
// ---------------------------------------------------------------------------

/**
 * Container Type Configuration record from the sprk_specontainertypeconfig Dataverse table.
 * Links a Business Unit to a specific SPE container type with auth credentials.
 * Returned by GET /api/spe/configs.
 */
export interface SpeContainerTypeConfig {
  /** Primary key GUID (sprk_specontainertypeconfigid) */
  id: string;
  /** Config display name (sprk_name) */
  name: string;
  /** Owning Business Unit ID (sprk_businessunitid) */
  businessUnitId: string;
  /** Business Unit display name (denormalized for display) */
  businessUnitName: string;
  /** Parent environment ID (sprk_environmentid) */
  environmentId: string;
  /** Environment display name (denormalized for display) */
  environmentName: string;
  /** SPE Container Type ID from Graph API (sprk_containertypeid) */
  containerTypeId: string;
  /** Container type display name (sprk_containertypename) */
  containerTypeName: string;
  /** Billing classification (sprk_billingclassification) */
  billingClassification: BillingClassification;
  /** Azure App Registration Client ID — owning app (sprk_owningappid) */
  owningAppId: string;
  /** Owning app display name (sprk_owningappdisplayname) */
  owningAppDisplayName: string;
  /** Key Vault secret name reference — NEVER the actual secret (sprk_keyvaultsecretname) */
  keyVaultSecretName: string;
  /** Optional consuming/guest app Client ID (sprk_consumingappid) */
  consumingAppId?: string;
  /** Optional consuming app Key Vault secret reference (sprk_consumingappkeyvaultsecret) */
  consumingAppKeyVaultSecret?: string;
  /** Whether the container type is registered on the consuming tenant (sprk_isregistered) */
  isRegistered: boolean;
  /** Registration date ISO string (sprk_registeredon) */
  registeredOn?: string;
  /** Comma-separated delegated permissions (sprk_delegatedpermissions) */
  delegatedPermissions: string;
  /** Comma-separated application permissions (sprk_applicationpermissions) */
  applicationPermissions: string;
  /** Optional default container ID for this config (sprk_defaultcontainerid) */
  defaultContainerId?: string;
  /** Max storage per container in bytes (sprk_maxstorageperbytes) */
  maxStoragePerBytes: number;
  /** Sharing capability setting (sprk_sharingcapability) */
  sharingCapability: SharingCapability;
  /** Whether item versioning is enabled (sprk_isitemversioningenabled) */
  isItemVersioningEnabled: boolean;
  /** Max major versions per item (sprk_itemmajorsversionlimit) */
  itemMajorVersionLimit: number;
  /** Active / inactive status (sprk_status) */
  status: ActiveStatus;
  /** Optional admin notes (sprk_notes) */
  notes?: string;
}

/** Payload for creating or updating a SpeContainerTypeConfig */
export interface SpeContainerTypeConfigUpsert {
  name: string;
  businessUnitId: string;
  environmentId: string;
  containerTypeId: string;
  containerTypeName: string;
  billingClassification: BillingClassification;
  owningAppId: string;
  owningAppDisplayName: string;
  keyVaultSecretName: string;
  consumingAppId?: string;
  consumingAppKeyVaultSecret?: string;
  delegatedPermissions: string;
  applicationPermissions: string;
  defaultContainerId?: string;
  maxStoragePerBytes: number;
  sharingCapability: SharingCapability;
  isItemVersioningEnabled: boolean;
  itemMajorVersionLimit: number;
  status: ActiveStatus;
  notes?: string;
}

// ---------------------------------------------------------------------------
// Container Type (Graph API)
// ---------------------------------------------------------------------------

/** Container type status from Graph API */
export type ContainerTypeStatus = "trial" | "standard" | "directToCustomer";

/**
 * SPE Container Type — returned by the Graph API and proxied through
 * GET /api/spe/containertypes?configId={id}.
 */
export interface ContainerType {
  /** Container Type ID (GUID from Graph) */
  containerTypeId: string;
  /** Display name */
  displayName: string;
  /** Owning Azure App Registration Client ID */
  owningAppId: string;
  /** Billing classification (trial / standard / directToCustomer) */
  billingClassification: ContainerTypeStatus;
  /** Azure AD tenant ID of the owning tenant */
  azureTenantId: string;
  /** Whether the container type is registered on the consuming tenant */
  isRegistered?: boolean;
  /** Creation date ISO string */
  createdDateTime?: string;
  /** Expiry date for trial container types */
  expiryDateTime?: string;
}

/** Application permissions entry for a container type registration */
export interface ContainerTypePermission {
  /** Registering app client ID */
  appId: string;
  /** Display name of the registering app */
  appDisplayName?: string;
  /** Delegated permissions granted */
  delegatedPermissions: string[];
  /** Application permissions granted */
  applicationPermissions: string[];
}

// ---------------------------------------------------------------------------
// Consuming Tenant Management (SPE-082)
// ---------------------------------------------------------------------------

/**
 * Represents a consuming application registration for an SPE container type.
 * In multi-tenant scenarios, a single container type can be consumed by multiple
 * applications from different tenants.
 *
 * Returned by GET /api/spe/containertypes/{typeId}/consumers
 */
export interface ConsumingTenant {
  /** Azure AD application (client) ID of the consuming application */
  appId: string;
  /** Optional display name of the consuming application */
  displayName?: string;
  /** Home tenant ID of the consuming application */
  tenantId?: string;
  /** Delegated permission scopes granted to the consuming application */
  delegatedPermissions: string[];
  /** Application permission scopes granted to the consuming application */
  applicationPermissions: string[];
}

/** Response envelope for GET /api/spe/containertypes/{typeId}/consumers */
export interface ConsumingTenantListResponse {
  items: ConsumingTenant[];
  count: number;
}

/** Request body for POST /api/spe/containertypes/{typeId}/consumers */
export interface RegisterConsumingTenantRequest {
  /** Azure AD application (client) ID of the consuming application to register */
  appId: string;
  /** Optional display name for admin labeling */
  displayName?: string;
  /** Optional home tenant ID of the consuming application */
  tenantId?: string;
  /** Delegated permissions to grant */
  delegatedPermissions: string[];
  /** Application permissions to grant */
  applicationPermissions: string[];
}

/** Request body for PUT /api/spe/containertypes/{typeId}/consumers/{appId} */
export interface UpdateConsumingTenantRequest {
  /** Replacement delegated permissions */
  delegatedPermissions: string[];
  /** Replacement application permissions */
  applicationPermissions: string[];
}

// ---------------------------------------------------------------------------
// Container (Graph API)
// ---------------------------------------------------------------------------

/** Container status values from Graph API */
export type ContainerStatus = "active" | "inactive" | "deleted";

/**
 * SPE Container — returned by the Graph API and proxied through
 * GET /api/spe/containers?configId={id}.
 */
export interface Container {
  /** Container ID (GUID from Graph) */
  id: string;
  /** Container display name */
  displayName: string;
  /** Optional description */
  description?: string;
  /** Container type ID this container belongs to */
  containerTypeId: string;
  /** Current status */
  status: ContainerStatus;
  /** Whether the container is locked (read-only) */
  isItemVersioningEnabled?: boolean;
  /** Creation date ISO string */
  createdDateTime: string;
  /** Last modified date ISO string */
  lastModifiedDateTime?: string;
  /** Storage used in bytes */
  storageUsedInBytes?: number;
  /** Custom properties (key-value pairs) */
  customProperties?: Record<string, ContainerCustomProperty>;
  /** Storage settings */
  settings?: ContainerSettings;
}

/** Storage settings for a container */
export interface ContainerSettings {
  /** Whether item versioning is enabled */
  isVersioningEnabled?: boolean;
  /** Maximum number of major versions */
  majorVersionLimit?: number;
}

/** Custom property value for a container */
export interface ContainerCustomProperty {
  /** The property value (string) */
  value: string;
  /** Whether this property is searchable */
  isSearchable?: boolean;
}

// ---------------------------------------------------------------------------
// Container Permission (Graph API)
// ---------------------------------------------------------------------------

/**
 * Role values for container permissions.
 * Matches Graph API role names exactly per FR-07:
 * reader, writer, manager, owner
 */
export type ContainerRole = "reader" | "writer" | "manager" | "owner";

/** Identity info for a user or group in a permission entry */
export interface ContainerPermissionIdentity {
  /** User or group display name */
  displayName?: string;
  /** User principal name (email) */
  userPrincipalName?: string;
  /** Azure AD Object ID */
  id?: string;
  /** Type of identity: "user" | "group" | "device" | "application" */
  type?: string;
}

/**
 * Container permission entry — returned by
 * GET /api/spe/containers/{containerId}/permissions?configId={id}.
 */
export interface ContainerPermission {
  /** Permission entry ID from Graph */
  id: string;
  /** Assigned role */
  roles: ContainerRole[];
  /** Grantee identity */
  grantedToV2?: {
    user?: ContainerPermissionIdentity;
    group?: ContainerPermissionIdentity;
    siteUser?: ContainerPermissionIdentity;
  };
}

/** Payload for adding or updating a container permission */
export interface ContainerPermissionUpsert {
  /** Email or UPN of the user/group */
  userPrincipalName: string;
  /** Role to assign */
  role: ContainerRole;
}

// ---------------------------------------------------------------------------
// Container Column Definition (Graph API)
// ---------------------------------------------------------------------------

/** Column data types supported by SPE Graph API */
export type ColumnType =
  | "text"
  | "number"
  | "boolean"
  | "dateTime"
  | "choice"
  | "lookup"
  | "personOrGroup"
  | "currency"
  | "hyperlink";

/**
 * Column definition for a container — returned by
 * GET /api/spe/containers/{containerId}/columns?configId={id}.
 */
export interface ColumnDefinition {
  /** Column ID from Graph */
  id: string;
  /** Internal column name */
  name: string;
  /** Display name shown in the UI */
  displayName: string;
  /** Data type of the column */
  columnGroup?: string;
  /** Whether the column is required */
  required?: boolean;
  /** Whether the column is indexed (hidden from UI) */
  hidden?: boolean;
  /** Whether the column is read-only */
  readOnly?: boolean;
  /** Description / tooltip */
  description?: string;
  /** Data type category */
  text?: Record<string, unknown>;
  number?: Record<string, unknown>;
  boolean?: Record<string, unknown>;
  dateTime?: Record<string, unknown>;
  choice?: {
    allowTextEntry?: boolean;
    choices?: string[];
    displayAs?: string;
  };
}

/** Payload for creating or updating a column definition */
export interface ColumnDefinitionUpsert {
  name: string;
  displayName: string;
  description?: string;
  required?: boolean;
  hidden?: boolean;
  text?: Record<string, unknown>;
  number?: Record<string, unknown>;
  boolean?: Record<string, unknown>;
  dateTime?: Record<string, unknown>;
  choice?: {
    allowTextEntry?: boolean;
    choices?: string[];
    displayAs?: string;
  };
}

// ---------------------------------------------------------------------------
// Drive Items / Files (Graph API)
// ---------------------------------------------------------------------------

/** File/folder item from Graph API — proxied through file browser endpoints */
export interface DriveItem {
  /** Item ID from Graph */
  id: string;
  /** Item name (filename or folder name) */
  name: string;
  /** Size in bytes */
  size?: number;
  /** Creation date ISO string */
  createdDateTime: string;
  /** Last modified date ISO string */
  lastModifiedDateTime: string;
  /** Etag for concurrency */
  eTag?: string;
  /** Parent folder reference */
  parentReference?: {
    driveId?: string;
    id?: string;
    path?: string;
  };
  /** Present if item is a file */
  file?: {
    mimeType?: string;
    hashes?: {
      quickXorHash?: string;
      sha256Hash?: string;
    };
  };
  /** Present if item is a folder */
  folder?: {
    childCount?: number;
  };
  /** Download URL (ephemeral, Graph-signed) */
  "@microsoft.graph.downloadUrl"?: string;
  /** Web URL for browser access */
  webUrl?: string;
  /** Created-by identity */
  createdBy?: {
    user?: { displayName?: string; email?: string; id?: string };
  };
  /** Last modified-by identity */
  lastModifiedBy?: {
    user?: { displayName?: string; email?: string; id?: string };
  };
}

/** Version entry for a drive item */
export interface DriveItemVersion {
  /** Version ID from Graph */
  id: string;
  /** Last modified date ISO string */
  lastModifiedDateTime: string;
  /** Version size in bytes */
  size?: number;
  /** Modified-by identity */
  lastModifiedBy?: {
    user?: { displayName?: string; email?: string; id?: string };
  };
}

/** Thumbnail set for a drive item */
export interface Thumbnail {
  /** Thumbnail set ID */
  id: string;
  /** Small thumbnail */
  small?: ThumbnailSize;
  /** Medium thumbnail */
  medium?: ThumbnailSize;
  /** Large thumbnail */
  large?: ThumbnailSize;
  /** Custom-size thumbnail */
  source?: ThumbnailSize;
}

/** Dimensions and URL for a single thumbnail size */
export interface ThumbnailSize {
  /** Width in pixels */
  width?: number;
  /** Height in pixels */
  height?: number;
  /** Thumbnail URL */
  url?: string;
}

/** Sharing link type values */
export type SharingLinkType = "view" | "edit" | "embed";

/** Sharing link scope values */
export type SharingLinkScope = "anonymous" | "organization" | "users";

/** Sharing link returned by POST /api/spe/containers/{id}/items/{itemId}/sharing */
export interface SharingLink {
  /** The sharing link URL */
  link?: {
    type?: SharingLinkType;
    scope?: SharingLinkScope;
    webUrl?: string;
    webHtml?: string;
    application?: {
      id?: string;
      displayName?: string;
    };
  };
  /** When this link expires */
  expirationDateTime?: string;
  /** Granted roles */
  roles?: string[];
  /** Share ID */
  id?: string;
}

// ---------------------------------------------------------------------------
// Audit Log (sprk_speauditlog)
// ---------------------------------------------------------------------------

/**
 * Audit category option set values.
 * Matches sprk_category values in sprk_speauditlog:
 * ContainerType | Container | Permission | File | Search | Security
 */
export type AuditCategory =
  | "ContainerType"
  | "Container"
  | "Permission"
  | "File"
  | "Search"
  | "Security";

/**
 * Audit log entry from the sprk_speauditlog Dataverse table.
 * Returned by GET /api/spe/audit.
 */
export interface AuditLogEntry {
  /** Primary key GUID (sprk_speauditlogid) */
  id: string;
  /** Operation name, e.g. "CreateContainer" (sprk_operation) */
  operation: string;
  /** Category of the operation (sprk_category) */
  category: AuditCategory;
  /** ID of the affected resource (sprk_targetresourceid) */
  targetResourceId: string;
  /** Name of the affected resource (sprk_targetresourcename) */
  targetResourceName: string;
  /** HTTP status code of the operation response (sprk_responsestatus) */
  responseStatus: number;
  /** Response summary or error message (sprk_responsesummary) */
  responseSummary: string;
  /** Environment context ID (sprk_environmentid) */
  environmentId: string;
  /** Environment display name (denormalized) */
  environmentName?: string;
  /** Container type config context ID (sprk_containertypeconfigid) */
  containerTypeConfigId: string;
  /** Config display name (denormalized) */
  containerTypeConfigName?: string;
  /** Business Unit context ID (sprk_businessunitid) */
  businessUnitId: string;
  /** Business Unit display name (denormalized) */
  businessUnitName?: string;
  /** User who performed the operation (sprk_performedby) */
  performedBy: string;
  /** Timestamp ISO string (sprk_performedon) */
  performedOn: string;
}

// ---------------------------------------------------------------------------
// Dashboard Metrics
// ---------------------------------------------------------------------------

/**
 * Metrics for a single container (used within DashboardMetrics.containers).
 */
export interface ContainerMetrics {
  /** Container ID */
  containerId: string;
  /** Container display name */
  displayName: string;
  /** Storage used in bytes */
  storageUsedInBytes: number;
  /** Number of items in the container */
  itemCount: number;
  /** Container status */
  status: ContainerStatus;
  /** Last activity date ISO string */
  lastActivityDateTime?: string;
}

/**
 * Dashboard metrics returned by GET /api/spe/dashboard/metrics?configId={id}.
 * Data is served from background-sync cache (SpeDashboardSyncService).
 *
 * Shape matches SpeDashboardSyncService.DashboardMetrics (server-side record).
 */
export interface DashboardMetrics {
  /** Total number of containers across all registered container types */
  totalContainerCount: number;
  /** Total storage used in bytes across all containers */
  totalStorageUsedInBytes: number;
  /** Container count keyed by container type config ID (Guid string) */
  containerCountByConfig: Record<string, number>;
  /** UTC timestamp when these metrics were last synced from Graph (ISO string) */
  lastSyncedAt: string;
  /** True if the most recent sync completed without errors */
  syncSucceeded: boolean;
  /** Human-readable sync status message */
  syncStatus: string;
}

// ---------------------------------------------------------------------------
// Recycle Bin
// ---------------------------------------------------------------------------

/**
 * Deleted container in the recycle bin.
 * Returned by GET /api/spe/recyclebin?configId={id}.
 *
 * Distinct from Container — only contains the fields returned by the
 * DeletedContainerDto (id, displayName, deletedDateTime, containerTypeId).
 */
export interface DeletedContainer {
  /** Container ID (Graph FileStorageContainer ID) */
  id: string;
  /** Display name of the container as it appeared before deletion */
  displayName: string;
  /** UTC timestamp when the container was soft-deleted (null if unknown) */
  deletedDateTime: string | null;
  /** The container type GUID this container belongs to */
  containerTypeId: string;
}

// ---------------------------------------------------------------------------
// Search
// ---------------------------------------------------------------------------

/** Search request payload for POST /api/spe/search/containers or /api/spe/search/items */
export interface SearchRequest {
  /** The search query string */
  query: string;
  /** Optional container ID scope (for item search within a specific container) */
  containerId?: string;
  /** Maximum results to return */
  top?: number;
  /** Skip for pagination */
  skip?: number;
}

/** Search result item for container search */
export interface ContainerSearchResult {
  /** Container that matched the search */
  container: Container;
  /** Relevance score */
  score?: number;
}

/** Search result item for drive item search */
export interface DriveItemSearchResult {
  /** Drive item that matched */
  item: DriveItem;
  /** Container the item belongs to */
  containerId: string;
  /** Relevance score */
  score?: number;
  /** Search result hit highlights */
  hitHighlightedSummary?: string;
}

// ---------------------------------------------------------------------------
// Security
// ---------------------------------------------------------------------------

/** Security alert severity levels */
export type AlertSeverity = "unknown" | "informational" | "low" | "medium" | "high";

/** Security alert status values */
export type AlertStatus = "unknown" | "newAlert" | "inProgress" | "resolved";

/** Security alert from GET /api/spe/security/alerts */
export interface SecurityAlert {
  /** Alert ID */
  id: string;
  /** Alert title */
  title: string;
  /** Alert description */
  description?: string;
  /** Severity level */
  severity: AlertSeverity;
  /** Current status */
  status: AlertStatus;
  /** Category of the alert */
  category?: string;
  /** Created date ISO string */
  createdDateTime: string;
  /** Last updated date ISO string */
  lastModifiedDateTime?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Bulk Operations (SPE-083)
// ─────────────────────────────────────────────────────────────────────────────

/** Operation type for a bulk job */
export type BulkOperationType = "Delete" | "AssignPermissions";

/**
 * Lightweight acknowledgement returned immediately after enqueueing a bulk operation.
 * Poll the statusUrl to track progress.
 */
export interface BulkOperationAccepted {
  /** Unique ID of the bulk operation — use to poll status. */
  operationId: string;
  /** Relative URL to poll: /api/spe/bulk/{operationId}/status */
  statusUrl: string;
}

/** Per-item error in a bulk operation */
export interface BulkOperationItemError {
  /** Container ID that failed */
  containerId: string;
  /** Human-readable description of what went wrong */
  errorMessage: string;
}

/** Live progress snapshot for a bulk operation */
export interface BulkOperationStatus {
  /** Matches the ID returned by the enqueue endpoint */
  operationId: string;
  /** Type of operation */
  operationType: BulkOperationType;
  /** Total number of items to process */
  total: number;
  /** Number of items successfully processed */
  completed: number;
  /** Number of items that failed */
  failed: number;
  /** true when all items have been processed (success or error) */
  isFinished: boolean;
  /** Per-item error details — empty when no failures */
  errors: BulkOperationItemError[];
  /** ISO timestamp when the operation was enqueued */
  startedAt: string;
  /** ISO timestamp when the operation finished, or null if still running */
  completedAt: string | null;
}

/** Request body for POST /api/spe/bulk/delete */
export interface BulkDeleteRequest {
  containerIds: string[];
  configId: string;
}

/** Request body for POST /api/spe/bulk/permissions */
export interface BulkPermissionsRequest {
  containerIds: string[];
  configId: string;
  /** Azure AD user object ID. Mutually exclusive with groupId. */
  userId?: string;
  /** Azure AD group object ID. Mutually exclusive with userId. */
  groupId?: string;
  /** SPE role: reader, writer, manager, or owner */
  role: string;
}

/** Secure score from GET /api/spe/security/score */
export interface SecureScore {
  /** Score ID */
  id: string;
  /** Current score */
  currentScore: number;
  /** Maximum possible score */
  maxScore: number;
  /** Percentage (currentScore / maxScore * 100) */
  percentage: number;
  /** Date of this score snapshot */
  createdDateTime: string;
  /** Individual control scores */
  controlScores?: Array<{
    controlName: string;
    score: number;
    maxScore: number;
    description?: string;
  }>;
}
