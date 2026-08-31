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
 * Billing standing of a container type, from Graph's `fileStorageContainerBillingStatus` enum.
 *
 * Graph declares exactly `invalid | valid | unknownFutureValue` on both v1.0 and beta (verified
 * against `https://graph.microsoft.com/v1.0/$metadata`). `unknownFutureValue` is Graph's forward-
 * compatibility sentinel, never a real value, so it is deliberately not modelled here — an actual
 * future member would arrive as an unrecognised string and must land in the UNKNOWN branch rather
 * than being silently typed away.
 */
export type BillingStatus = "valid" | "invalid";

/**
 * SPE Container Type — returned by the Graph API and proxied through
 * GET /api/spe/containertypes?configId={id}.
 */
/**
 * A person who owns (administers) a container type — spec FR-C09, task 027.
 *
 * 🔑 NOT the same thing as `ContainerTypePermission`. That describes which APPLICATIONS may access
 * containers of a type (Graph `applicationPermissions`); this describes which PEOPLE administer the
 * type itself (Graph `fileStorageContainerType.permissions`). They share a Graph word and nothing
 * else — neither supersedes the other, and they are served by different routes (`/owners` vs
 * `/permissions`) precisely so the distinction survives a glance at the network tab.
 */
export interface ContainerTypeOwner {
  /** Graph permission id — the handle needed to revoke this grant. */
  permissionId: string;
  /**
   * Display name, or undefined when Graph did not report one.
   * `undefined` means UNKNOWN — render it as such, never as a blank that reads as "no name".
   */
  displayName?: string;
  /** Email / UPN, or undefined when Graph did not report one. */
  email?: string;
  /** Directory object id, or undefined when Graph did not report one. */
  userId?: string;
  /** Roles carried by the grant (e.g. "owner"). Empty means Graph reported none. */
  roles: string[];
}

export interface ContainerType {
  /** Container Type ID (GUID from Graph) */
  containerTypeId: string;
  /** Display name */
  displayName: string;
  /**
   * Owning Azure App Registration Client ID.
   *
   * Optional because Graph may not return it — and `undefined` means UNKNOWN, not "none". Rendering
   * an absent owning app as a blank cell (as this screen did until 2026-08-23) reads as "there
   * isn't one", which is a different and wrong claim.
   */
  owningAppId?: string;
  /**
   * Billing classification (trial / standard / directToCustomer).
   *
   * Optional since 2026-08-24 (task 029). The BFF has always sent this nullable (`string?`), but
   * declaring it required here made the client type assert something the wire never guaranteed — and
   * because responses are cast rather than parsed, TypeScript could not catch the difference. It
   * mattered: the value was null for **every** container type between the Graph 6 upgrade
   * (2026-08-13) and task 030's fix (2026-08-23), during which the grid rendered an empty badge
   * rather than saying "unknown". `undefined` means UNKNOWN, never "standard".
   */
  billingClassification?: ContainerTypeStatus;
  /**
   * Whether billing for this container type is in good standing.
   *
   * `undefined` means NOT REPORTED and MUST NOT be rendered as valid (NFR-06). Read together with
   * `billingClassification`: only a `standard` type requires a billing profile in the developer
   * tenant, so an `invalid` status is actionable there and not necessarily elsewhere
   * (`knowledge/sharepoint-embedded/docs/learn-containertypes.md` :61, :79-:80).
   *
   * READ-ONLY — attaching a billing profile belongs to provisioning (spec §4.2d).
   */
  billingStatus?: BillingStatus;
  /** Azure AD tenant ID of the owning tenant. Not currently returned by the BFF. */
  azureTenantId?: string;
  /**
   * Whether the container type is registered on the consuming tenant.
   *
   * Sourced from the containerTypeRegistrations endpoint, NOT from the container-type list — so on
   * the list screen it is `undefined`, meaning **not yet determined**. Treating that as `false`
   * makes the grid state "No" for every row, which is an assertion the data does not support.
   */
  isRegistered?: boolean;
  /** Creation date ISO string */
  createdDateTime?: string;
  /** Expiry date for trial container types */
  expiryDateTime?: string;
  /**
   * The container type's settings, or undefined when Graph did not return them.
   * Added by task 025 — before it, no settings value reached the client at all.
   */
  settings?: ContainerTypeSettings;
}

/**
 * Container-type settings as returned by the BFF — the nine v1.0 properties plus the beta-only
 * `isOfficeRestricted`.
 *
 * Verified against Graph's own OData metadata (notes/task-025-schema-verification.md), not docs prose.
 * FR-C07 named `agent.chatEmbedAllowedHosts`, which exists in neither API version, and omitted
 * `sharingCapability`, which does.
 *
 * Every member is optional and `undefined` means NOT REPORTED, never a default. A settings block that
 * could not be read must not present as "search is off".
 */
export interface ContainerTypeSettings {
  /** Which external sharing is permitted (Graph SharingCapabilities). */
  sharingCapability?: SharingCapability;
  isItemVersioningEnabled?: boolean;
  itemMajorVersionLimit?: number;
  /** Per-container CEILING in bytes — a limit, never a usage figure (task 023's split). */
  maxStoragePerContainerInBytes?: number;
  /** Whether container content is indexed for search. */
  isSearchEnabled?: boolean;
  /** Whether containers of this type are discoverable. */
  isDiscoverabilityEnabled?: boolean;
  /** Distinct from `sharingCapability` — a separate restriction flag. */
  isSharingRestricted?: boolean;
  urlTemplate?: string;
  /**
   * Which settings a consuming tenant may override, as the raw comma-delimited flag string
   * (e.g. "sharingCapability,itemMajorVersionLimit,isOfficeRestricted").
   *
   * Override METADATA, not a value. Kept as a string because the live tenant uses flags that are not
   * members of the SDK's typed enum. Task 026 renders its meaning.
   */
  consumingTenantOverridables?: string;
  /** Beta-only and READ-ONLY — absent from the v1.0 schema and the SDK's typed model. */
  isOfficeRestricted?: boolean;
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
 * Archive state, from Graph's `archivalDetails.archiveStatus` (FR-E01, task 050).
 *
 * ⚠️ There is deliberately no `"notArchived"` member — Graph's `siteArchiveStatus` enum has none on
 * either API version, and a container that is not archived simply omits `archivalDetails`. So the
 * absence of a value here is the ONLY way "not archived" is expressed, and it is indistinguishable
 * from "Graph did not report". Never render `undefined` as a positive claim that content is online.
 *
 * All three values are transitional or terminal states of an ASYNCHRONOUS operation:
 *   recentlyArchived → fullyArchived   (archiving, in progress → done)
 *   reactivating     → (field absent)  (unarchiving, in progress → done)
 */
export type ContainerArchiveStatus =
  | "recentlyArchived"
  | "fullyArchived"
  | "reactivating";

/**
 * Per-container storage quota, from the container drive's `quota` facet (FR-E02, task 051).
 *
 * 🔑 **`total` is a container-TYPE setting.** It is the type's `maxStoragePerContainerInBytes` as it
 * applies to this container, so it is the SAME for every container of that type. Do not label it in a
 * way that implies this one container can be capped differently — Graph has no per-container ceiling:
 * `fileStorageContainerSettings` carries no storage property on either API version, and a
 * container-scope PATCH returns 200 while discarding the value (measured 2026-08-27).
 *
 * `used` is the only consumption figure available on a single-container fetch — `storageUsedInBytes`
 * is LIST-only (tasks 020/024).
 *
 * Every field is nullable: null means **NOT REPORTED**, never zero (spec NFR-06).
 */
export interface ContainerQuota {
  /** The ceiling in bytes — sourced from the container TYPE. */
  total?: number | null;
  /** Bytes consumed. */
  used?: number | null;
  /** Bytes remaining, as Graph computes it — NOT `total - used` (deleted items still count). */
  remaining?: number | null;
  /** Bytes held by deleted items that still count against the quota. */
  deleted?: number | null;
  /** Graph's own assessment, e.g. `normal`, `nearing`, `critical`, `exceeded`. */
  state?: string | null;
}

/**
 * Body of the 202 returned by the archive / unarchive endpoints (FR-E01).
 *
 * ⚠️ The shape exists to make "accepted ≠ done" impossible to miss. `pending` is always true today —
 * Graph has no synchronous path for these actions — and `expectedNextState` names the state the
 * container is moving into, so the UI can say what is happening rather than implying it finished.
 */
export interface ArchivalActionAccepted {
  message: string;
  /** Always true: Graph performs archival asynchronously. */
  pending: boolean;
  /** The state the container transitions into — `recentlyArchived` or `reactivating`. */
  expectedNextState: ContainerArchiveStatus;
}

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
  /**
   * Current status — or `null` meaning **NOT REPORTED**.
   *
   * 🔑 Always `null` on list rows: Graph drops `status` from container collection rows even when
   * `$select` asks for it (measured 2026-08-27, task 050). Populated on the detail fetch.
   *
   * This was a required `ContainerStatus` until 2026-08-27, and both the server mapper and the grid
   * cell defaulted it to `"active"`. The server's fallback fired for 100% of responses because it
   * searched the wrong place for the field, so the Status column asserted "Active" for every
   * container regardless of truth. **Do not reintroduce a `?? "active"` anywhere.** Render the absent
   * case explicitly (spec NFR-06).
   */
  status: ContainerStatus | null;
  /**
   * Archive state (FR-E01), or absent when there is no archive state to show.
   *
   * ⚠️ A **separate dimension** from {@link status} — a container can be `active` and
   * `fullyArchived` at once. Do not merge them into a single badge value.
   */
  archiveStatus?: ContainerArchiveStatus;
  /**
   * Per-container storage quota (FR-E02). **Detail responses only** — Graph reports it on the
   * expanded drive, which a list cannot carry.
   */
  quota?: ContainerQuota;
  /** Whether the container is locked (read-only) */
  isItemVersioningEnabled?: boolean;
  /** Creation date ISO string */
  createdDateTime: string;
  /** Last modified date ISO string */
  lastModifiedDateTime?: string;
  /** Storage used in bytes */
  storageUsedInBytes?: number;
  /**
   * The container's SharePoint URL — the scoping key for a Purview eDiscovery search (FR-C10).
   *
   * 🔑 Present on a DETAIL response only. The BFF omits the key entirely from LIST rows, because
   * Graph structurally cannot supply it there: the containers collection accepts
   * `$expand=drive($select=webUrl)`, answers 200, echoes it in `@odata.context`, and returns no
   * `drive` on any row (measured 2026-08-24 on both API versions — notes/task-028-findings.md §1).
   *
   * So `undefined` here means one of two things depending on WHERE the object came from, and only
   * the detail case is renderable:
   *   • from `containers.list(...)` → NOT ASKED. Never render an absent state from a list row.
   *   • from `containers.get(...)`  → asked, and Graph reported none → render the explicit absent
   *     state (NFR-06), never a blank.
   */
  webUrl?: string;
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
 * Returned inside the `items` array of GET /api/spe/audit.
 *
 * Corrected 2026-08-25. This interface used to declare thirteen required fields, seven of which the
 * endpoint has never sent — it described the Dataverse table rather than the response. The required
 * markers were doing real harm: they told every reader that `businessUnitId` would be there, so a
 * consumer could reach for it and get `undefined` with no type error anywhere. Optional now means
 * "this endpoint does not return it", which is a fact about the wire, not a wish about the schema.
 */
export interface AuditLogEntry {
  /** Primary key GUID (sprk_speauditlogid) */
  id: string;
  /** Operation name, e.g. "CreateContainer" (sprk_operation) */
  operation: string;
  /**
   * Human-readable category LABEL resolved server-side from the `sprk_category` option set —
   * e.g. "Container type", not the `AuditCategory` filter value "ContainerType". The two are
   * deliberately different: this one is for display, `AuditCategory` is what the filter sends.
   */
  category: string;
  /** ID of the affected resource (sprk_targetresourceid) */
  targetResourceId: string;
  /** Name of the affected resource (sprk_targetresourcename) */
  targetResourceName: string;
  /** HTTP status code of the operation response (sprk_responsestatus) */
  responseStatus: number;
  /**
   * Response summary or error message (sprk_responsesummary).
   * NOT currently returned — the column is absent from the endpoint's `$select` because it has not
   * been verified against the live Dataverse schema, and naming a column that does not exist 400s
   * the entire query (task 005 found exactly that with `sprk_targetresource`).
   */
  responseSummary?: string;
  /** Environment context ID (sprk_environmentid). Not returned by GET /api/spe/audit. */
  environmentId?: string;
  /** Environment display name (denormalized). Not returned by GET /api/spe/audit. */
  environmentName?: string;
  /** Container type config context ID. Not returned — it is the query's input, not its output. */
  containerTypeConfigId?: string;
  /** Config display name (denormalized). Not returned by GET /api/spe/audit. */
  containerTypeConfigName?: string;
  /** Business Unit context ID (sprk_businessunitid). Not returned by GET /api/spe/audit. */
  businessUnitId?: string;
  /** Business Unit display name (denormalized). Not returned by GET /api/spe/audit. */
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
  /**
   * How many containers actually reported a storage figure, out of totalContainerCount.
   *
   * Graph returns consumption only on the beta LIST surface, so coverage can be partial. When this
   * is below the total, totalStorageUsedInBytes is a FLOOR, not a total — present it as such.
   * Optional so an older cached metrics payload still deserializes.
   */
  storageReportingContainerCount?: number;
  /** Container count keyed by container type config ID (Guid string) */
  containerCountByConfig: Record<string, number>;
  /** UTC timestamp when these metrics were last synced from Graph (ISO string) */
  lastSyncedAt: string;
  /** True if the most recent sync completed without errors. Mirrors `syncHealth === "Healthy"`. */
  syncSucceeded: boolean;
  /** Human-readable sync status message — names the failing concern(s) when any failed */
  syncStatus: string;
  /** Overall sync health, derived server-side from `concerns`. Never optimistic. */
  syncHealth: SyncHealth;
  /** Per-concern outcome for every concern the sync pass attempted */
  concerns: ConcernOutcome[];
}

/** Overall dashboard sync health (server: SpeDashboardSyncService.SyncHealth). */
export type SyncHealth = "Healthy" | "Degraded" | "Failed";

/**
 * The outcome of one concern in a dashboard sync pass.
 * Lets the dashboard NAME what failed instead of showing an opaque "Partial".
 */
export interface ConcernOutcome {
  /** What was attempted — e.g. "Dataverse container-type configs" */
  concern: string;
  succeeded: boolean;
  /** Redacted failure reason; null/absent when succeeded */
  reason?: string | null;
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

/**
 * A deleted FILE or FOLDER inside one container's recycle bin.
 *
 * Returned by GET /api/spe/containers/{containerId}/recyclebin/items?configId={id}.
 *
 * ⚠️ Distinct from {@link DeletedContainer}, which is a whole deleted CONTAINER. Spec decision D3
 * keeps both bins: a container-level restore cannot recover one deleted file, and an item-level
 * restore cannot recover a deleted container.
 *
 * Added by sdap-SPE-admin-app-r2 task 052 (spec FR-E03).
 */
export interface RecycleBinItem {
  /** Graph recycleBinItem ID */
  id: string;
  /** File or folder name */
  name: string;
  /** Size in bytes. `null` means Graph did not report a size — render as "—", never as 0. */
  size: number | null;
  /** UTC timestamp when the item was deleted. `null` means not reported. */
  deletedDateTime: string | null;
  /** Where it was deleted from, e.g. "contentstorage/CSP_.../Document Library". */
  deletedFromLocation: string | null;
  /** Who deleted it. `null` means Graph did not report it — NOT "nobody". */
  deletedByDisplayName: string | null;
}

/** What happened to one requested item in a restore or permanent delete. */
export interface RecycleBinItemOutcome {
  id: string;
  /** The item's name where known — an outcome list of bare ids is unreadable. */
  name: string | null;
  succeeded: boolean;
  /** What actually happened, in terms an admin can act on. */
  detail: string;
}

/**
 * Per-item result of a recycle-bin restore or permanent delete.
 *
 * ⚠️ The `outcomes` array is the contract. Graph reports these two operations in ways that hide
 * failure — restore returns 207 listing only the ids that SUCCEEDED, and permanent delete returns
 * 204 whether it purged everything, some, or nothing. Rendering only `summary`, or reducing this to
 * a single success banner, reintroduces exactly the defect the BFF works to remove.
 */
export interface RecycleBinItemActionResult {
  /** One entry per requested id. Always render this. */
  outcomes: RecycleBinItemOutcome[];
  requestedCount: number;
  succeededCount: number;
  /**
   * Whether the outcomes were confirmed against the bin's actual state. Only ever `false` on a
   * permanent delete whose post-delete re-read failed — in which case the items may or may not
   * have been destroyed, and the UI must say so rather than pick a side.
   */
  verified: boolean;
  /** Human-readable roll-up. Supplements the per-item list; never replaces it. */
  summary: string;
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
/**
 * A container that matched a search.
 *
 * Corrected 2026-08-25. `container` used to be typed as a full `Container`, which was never true:
 * Graph Search returns a projection, and the endpoint's `SearchContainerDto` carries only id,
 * displayName, description and containerTypeId. `status`, `createdDateTime` and
 * `storageUsedInBytes` are NOT available on a search result — the grid renders them as "—" rather
 * than inventing an "active"/epoch default, because a fabricated status on a security-admin screen
 * is worse than a visible blank.
 */
export interface ContainerSearchResult {
  /** Container that matched the search — a PROJECTION, not a full container record. */
  container: Partial<Container> & Pick<Container, "id" | "displayName">;
  /** Relevance score. Not currently reported by the endpoint. */
  score?: number;
}

/**
 * A drive item that matched a search.
 *
 * Same correction as {@link ContainerSearchResult}: the endpoint's `SearchItemDto` returns id, name,
 * size, lastModifiedDateTime, containerId, containerName, webUrl and mimeType — so `createdDateTime`
 * and `lastModifiedBy` are absent here even though a fully-read `DriveItem` has them.
 */
export interface DriveItemSearchResult {
  /** Drive item that matched — a PROJECTION, not a full drive item. */
  item: Partial<DriveItem> & Pick<DriveItem, "id" | "name">;
  /** Container the item belongs to */
  containerId: string;
  /** Display name of the owning container, when search reported one. */
  containerName?: string;
  /** Relevance score. Not currently reported by the endpoint. */
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
  /** Score ID. NOT returned by GET /api/spe/security/score. */
  id?: string;
  /** Current score */
  currentScore: number;
  /** Maximum possible score */
  maxScore: number;
  /**
   * Percentage (currentScore / maxScore * 100).
   * NOT returned by the endpoint — `SecureScoreDto` carries only currentScore, maxScore and
   * averageComparativeScores. Marked optional 2026-08-25 after the card rendered "NaN%" for reading
   * a field that was never on the wire; the card now derives it from the two scores.
   */
  percentage?: number;
  /** Date of this score snapshot. NOT returned by the endpoint. */
  createdDateTime?: string;
  /** Individual control scores */
  controlScores?: Array<{
    controlName: string;
    score: number;
    maxScore: number;
    description?: string;
  }>;
}
