/**
 * BFF Data Client — Secure Project Workspace SPA
 *
 * Provides typed wrappers for querying Dataverse data through the BFF API.
 * All requests use `bffApiCall` from bff-client.ts which handles Azure AD B2B
 * authentication (MSAL) and Bearer token injection.
 *
 * BFF routes used (planned — require BFF implementation):
 *   GET  /api/v1/external/projects                           → list accessible projects
 *   GET  /api/v1/external/projects/{id}                     → single project
 *   GET  /api/v1/external/projects/{id}/documents           → project documents
 *   GET  /api/v1/external/projects/{id}/events              → project events
 *   GET  /api/v1/external/projects/{id}/contacts            → project contacts
 *   GET  /api/v1/external/projects/{id}/organizations       → project organizations
 *   POST /api/v1/external/projects/{id}/events              → create event
 *   PATCH /api/v1/external/events/{id}                      → update event
 *
 * See: docs/architecture/external-access-architecture.md
 */

// TODO: Data-read endpoints are pending BFF implementation.
// These functions call planned BFF routes under /api/v1/external/*
// that serve project data using managed-identity Dataverse access.
// Once BFF GET endpoints are added, no changes are needed here.
import { bffApiCall } from "../auth/bff-client";

// ---------------------------------------------------------------------------
// Re-export ApiError for module consumers
// ---------------------------------------------------------------------------

export { ApiError } from "../types";

// ---------------------------------------------------------------------------
// OData entity type interfaces
// ---------------------------------------------------------------------------

/**
 * A Secure Project record (sprk_project EntitySet: sprk_projects).
 * Fields must be listed in the `Webapi/sprk_project/fields` site setting.
 */
export interface ODataProject {
  /** Primary key — Dataverse GUID */
  sprk_projectid: string;
  /** Project display name */
  sprk_name: string;
  /** Human-readable reference number */
  sprk_referencenumber: string;
  /** Project description */
  sprk_description?: string | null;
  /** Whether this project has external access enabled */
  sprk_issecure?: boolean | null;
  /** Project status option set value */
  sprk_status?: number | null;
  /** ISO date string — record created */
  createdon?: string | null;
  /** ISO date string — last modified */
  modifiedon?: string | null;
}

/**
 * A Document record (sprk_document EntitySet: sprk_documents).
 * Fields must be listed in the `Webapi/sprk_document/fields` site setting.
 */
export interface ODataDocument {
  /** Primary key — Dataverse GUID */
  sprk_documentid: string;
  /** Document display name */
  sprk_name: string;
  /** Document type option set label or value */
  sprk_documenttype?: string | null;
  /** AI-generated document summary */
  sprk_summary?: string | null;
  /** Lookup ID of the owning project (sprk_projectid) */
  _sprk_projectid_value?: string | null;
  /** ISO date string — record created */
  createdon?: string | null;
  /** Display name of the record creator */
  "createdby@OData.Community.Display.V1.FormattedValue"?: string | null;
}

/**
 * An Event record (sprk_event EntitySet: sprk_events).
 * Fields must be listed in the `Webapi/sprk_event/fields` site setting.
 */
export interface ODataEvent {
  /** Primary key — Dataverse GUID */
  sprk_eventid: string;
  /** Event display name */
  sprk_name: string;
  /** ISO date string — due date */
  sprk_duedate?: string | null;
  /** Event status option set value */
  sprk_status?: number | null;
  /** Whether this event is flagged as a To-Do item */
  sprk_todoflag?: boolean | null;
  /** ISO date string — record created */
  createdon?: string | null;
  /** Lookup ID of the owning project */
  _sprk_projectid_value?: string | null;
}

/**
 * A Contact record (EntitySet: contacts) as returned through the Web API.
 * Fields must be listed in the `Webapi/contact/fields` site setting.
 */
export interface ODataContact {
  /** Primary key — Dataverse GUID */
  contactid: string;
  /** Full name */
  fullname?: string | null;
  /** First name */
  firstname?: string | null;
  /** Last name */
  lastname?: string | null;
  /** Primary email */
  emailaddress1?: string | null;
  /** Business phone */
  telephone1?: string | null;
  /** Job title */
  jobtitle?: string | null;
  /** Lookup ID of the parent account */
  _parentcustomerid_value?: string | null;
}

/**
 * An Account (Organisation) record (EntitySet: accounts) as returned through
 * the Web API.
 * Fields must be listed in the `Webapi/account/fields` site setting.
 */
export interface ODataOrganization {
  /** Primary key — Dataverse GUID */
  accountid: string;
  /** Organisation display name */
  name: string;
  /** Website URL */
  websiteurl?: string | null;
  /** Primary phone */
  telephone1?: string | null;
  /** City (address) */
  address1_city?: string | null;
  /** Country/region (address) */
  address1_country?: string | null;
}

// ---------------------------------------------------------------------------
// OData response envelope
// ---------------------------------------------------------------------------

/**
 * Standard OData collection response envelope.
 * The Web API always wraps collection results in `{ "@odata.context": "...", "value": [...] }`.
 */
interface ODataCollectionResponse<T> {
  "@odata.context"?: string;
  value: T[];
  "@odata.nextLink"?: string;
}

// ---------------------------------------------------------------------------
// OData query builder
// ---------------------------------------------------------------------------

/**
 * Options for building an OData query string.
 * All options are optional; only those provided are appended to the URL.
 */
export interface ODataQueryOptions {
  /**
   * Comma-separated list of columns to return.
   * Example: "sprk_name,sprk_referencenumber,createdon"
   */
  $select?: string;
  /**
   * OData filter expression.
   * Example: "_sprk_projectid_value eq 'b1c2d3e4-...'"
   */
  $filter?: string;
  /**
   * Navigation property to expand inline.
   * Example: "sprk_project_contacts($select=fullname,emailaddress1)"
   */
  $expand?: string;
  /**
   * Sort order expression.
   * Example: "createdon desc"
   */
  $orderby?: string;
  /**
   * Maximum number of records to return (server-side page size).
   */
  $top?: number;
}

/**
 * Build an OData query string from the provided options.
 * Returns an empty string if no options are provided.
 *
 * Example output: "?$select=sprk_name,createdon&$filter=sprk_issecure eq true&$top=50"
 */
function buildQueryString(options: ODataQueryOptions): string {
  const params: string[] = [];

  if (options.$select) {
    params.push(`$select=${encodeURIComponent(options.$select)}`);
  }
  if (options.$filter) {
    params.push(`$filter=${encodeURIComponent(options.$filter)}`);
  }
  if (options.$expand) {
    params.push(`$expand=${encodeURIComponent(options.$expand)}`);
  }
  if (options.$orderby) {
    params.push(`$orderby=${encodeURIComponent(options.$orderby)}`);
  }
  if (options.$top !== undefined && options.$top > 0) {
    params.push(`$top=${options.$top}`);
  }

  return params.length > 0 ? `?${params.join("&")}` : "";
}

// ---------------------------------------------------------------------------
// Internal fetch helpers
// ---------------------------------------------------------------------------

/**
 * Make a GET request to the BFF API and return the collection value.
 * Handles the standard `{ value: T[] }` envelope automatically.
 *
 * @param bffPath  BFF API path (e.g. "/api/v1/external/projects")
 * @param _options OData query options — reserved for future BFF support
 */
async function getCollection<T>(
  bffPath: string,
  _options: ODataQueryOptions = {}
): Promise<T[]> {
  const response = await bffApiCall<ODataCollectionResponse<T>>(bffPath);
  return response.value ?? [];
}

/**
 * Make a GET request to the BFF API for a single entity by ID.
 *
 * @param bffPath  BFF API path prefix (e.g. "/api/v1/external/projects")
 * @param id       Record GUID
 * @param _options OData query options — reserved for future BFF support
 */
async function getById<T>(
  bffPath: string,
  id: string,
  _options: ODataQueryOptions = {}
): Promise<T> {
  return bffApiCall<T>(`${bffPath}/${id}`);
}

/**
 * Make a POST (create) request to the BFF API.
 *
 * @param bffPath  BFF API path (e.g. "/api/v1/external/projects/{id}/events")
 * @param body     Record payload to create
 * @returns        The created record
 */
async function createRecord<TBody, TResult = TBody>(
  bffPath: string,
  body: TBody
): Promise<TResult> {
  return bffApiCall<TResult>(bffPath, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

/**
 * Make a PATCH (update) request to the BFF API.
 *
 * @param bffPath  BFF API path prefix (e.g. "/api/v1/external/events")
 * @param id       Record GUID to update
 * @param body     Partial record payload with fields to update
 */
async function updateRecord<TBody>(
  bffPath: string,
  id: string,
  body: Partial<TBody>
): Promise<void> {
  await bffApiCall<void>(`${bffPath}/${id}`, {
    method: "PATCH",
    body: JSON.stringify(body),
  });
}

// ---------------------------------------------------------------------------
// Project queries
// ---------------------------------------------------------------------------

/**
 * Retrieve all Secure Projects the current portal user can access.
 *
 * Table permissions on the Power Pages portal scope this query to records
 * that the authenticated contact is granted access to via the
 * `sprk_externalrecordaccess` junction table and the associated Web Role.
 *
 * @param options  Optional OData query overrides ($filter, $top, etc.)
 */
export async function getProjects(
  options: ODataQueryOptions = {}
): Promise<ODataProject[]> {
  const defaults: ODataQueryOptions = {
    $select:
      "sprk_projectid,sprk_name,sprk_referencenumber,sprk_description,sprk_issecure,sprk_status,createdon,modifiedon",
    $orderby: "sprk_name asc",
    $top: 100,
  };

  return getCollection<ODataProject>("/api/v1/external/projects", { ...defaults, ...options });
}

/**
 * Retrieve a single Secure Project by its record ID.
 *
 * @param projectId  Dataverse GUID of the sprk_project record
 * @param options    Optional OData query overrides ($select, $expand)
 */
export async function getProjectById(
  projectId: string,
  options: ODataQueryOptions = {}
): Promise<ODataProject> {
  const defaults: ODataQueryOptions = {
    $select:
      "sprk_projectid,sprk_name,sprk_referencenumber,sprk_description,sprk_issecure,sprk_status,createdon,modifiedon",
  };

  return getById<ODataProject>("/api/v1/external/projects", projectId, { ...defaults, ...options });
}

// ---------------------------------------------------------------------------
// Document queries
// ---------------------------------------------------------------------------

/**
 * Retrieve all Documents belonging to a Secure Project.
 *
 * Filters by the `_sprk_projectid_value` lookup column to scope results
 * to the specified project.
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param options    Optional OData query overrides
 */
export async function getDocuments(
  projectId: string,
  options: ODataQueryOptions = {}
): Promise<ODataDocument[]> {
  const defaults: ODataQueryOptions = {
    $select:
      "sprk_documentid,sprk_name,sprk_documenttype,sprk_summary,_sprk_projectid_value,createdon",
    $filter: `_sprk_projectid_value eq '${projectId}'`,
    $orderby: "createdon desc",
    $top: 200,
  };

  // Merge options — allow caller to override $filter / $orderby etc., but not silently lose defaults
  const merged: ODataQueryOptions = { ...defaults, ...options };

  // Preserve the project filter even when caller provides an additional filter
  if (options.$filter && options.$filter !== defaults.$filter) {
    merged.$filter = `(${defaults.$filter}) and (${options.$filter})`;
  }

  return getCollection<ODataDocument>(`/api/v1/external/projects/${projectId}/documents`, merged);
}

// ---------------------------------------------------------------------------
// Event queries
// ---------------------------------------------------------------------------

/**
 * Retrieve all Events belonging to a Secure Project.
 *
 * Filters by the `_sprk_projectid_value` lookup column.
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param options    Optional OData query overrides
 */
export async function getEvents(
  projectId: string,
  options: ODataQueryOptions = {}
): Promise<ODataEvent[]> {
  const defaults: ODataQueryOptions = {
    $select:
      "sprk_eventid,sprk_name,sprk_duedate,sprk_status,sprk_todoflag,_sprk_projectid_value,createdon",
    $filter: `_sprk_projectid_value eq '${projectId}'`,
    $orderby: "sprk_duedate asc",
    $top: 200,
  };

  const merged: ODataQueryOptions = { ...defaults, ...options };

  if (options.$filter && options.$filter !== defaults.$filter) {
    merged.$filter = `(${defaults.$filter}) and (${options.$filter})`;
  }

  return getCollection<ODataEvent>(`/api/v1/external/projects/${projectId}/events`, merged);
}

// ---------------------------------------------------------------------------
// Contact queries
// ---------------------------------------------------------------------------

/**
 * Retrieve Contacts associated with a Secure Project.
 *
 * Power Pages table permissions restrict this to contacts whose related
 * external access record links them to the project. The OData filter uses
 * the navigation property on the Contact to find project-linked records.
 *
 * Note: The exact filter expression depends on the relationship configured
 * in Dataverse. Adjust the filter property name if the schema differs.
 *
 * @param projectId  Dataverse GUID of the sprk_project record
 * @param options    Optional OData query overrides
 */
export async function getContacts(
  projectId: string,
  options: ODataQueryOptions = {}
): Promise<ODataContact[]> {
  const defaults: ODataQueryOptions = {
    $select:
      "contactid,fullname,firstname,lastname,emailaddress1,telephone1,jobtitle,_parentcustomerid_value",
    // Filter contacts that have an active access record for this project.
    // The relationship is: contact → sprk_externalrecordaccess → sprk_project
    // We filter via the related entity navigation property.
    $filter: `sprk_externalrecordaccess_contact_contactid/any(a:a/_sprk_projectid_value eq '${projectId}' and a/statecode eq 0)`,
    $orderby: "fullname asc",
    $top: 100,
  };

  const merged: ODataQueryOptions = { ...defaults, ...options };

  if (options.$filter && options.$filter !== defaults.$filter) {
    merged.$filter = `(${defaults.$filter}) and (${options.$filter})`;
  }

  return getCollection<ODataContact>(`/api/v1/external/projects/${projectId}/contacts`, merged);
}

// ---------------------------------------------------------------------------
// Organization queries
// ---------------------------------------------------------------------------

/**
 * Retrieve Organisations (Accounts) associated with contacts on a Secure Project.
 *
 * Because organisations are parent accounts of contacts, we look for accounts
 * that have at least one contact linked to the given project.
 *
 * @param projectId  Dataverse GUID of the sprk_project record
 * @param options    Optional OData query overrides
 */
export async function getOrganizations(
  projectId: string,
  options: ODataQueryOptions = {}
): Promise<ODataOrganization[]> {
  const defaults: ODataQueryOptions = {
    $select:
      "accountid,name,websiteurl,telephone1,address1_city,address1_country",
    // Filter accounts that have contacts with access to this project.
    $filter: `contact_customer_accounts/any(c:c/sprk_externalrecordaccess_contact_contactid/any(a:a/_sprk_projectid_value eq '${projectId}' and a/statecode eq 0))`,
    $orderby: "name asc",
    $top: 100,
  };

  const merged: ODataQueryOptions = { ...defaults, ...options };

  if (options.$filter && options.$filter !== defaults.$filter) {
    merged.$filter = `(${defaults.$filter}) and (${options.$filter})`;
  }

  return getCollection<ODataOrganization>(`/api/v1/external/projects/${projectId}/organizations`, merged);
}

// ---------------------------------------------------------------------------
// Event write operations
// ---------------------------------------------------------------------------

/**
 * Payload for creating a new Event record via the Web API.
 * Omits system-generated fields (primary key, createdon, etc.).
 */
export interface CreateEventPayload {
  /** Event display name */
  sprk_name: string;
  /** ISO date string for the due date */
  sprk_duedate?: string;
  /** Event status option set value */
  sprk_status?: number;
  /** Whether this is a To-Do item */
  sprk_todoflag?: boolean;
  /**
   * OData binding syntax to associate the event with a project.
   * Example: "sprk_projects(b1c2d3e4-0000-0000-0000-000000000001)"
   */
  "sprk_projectid@odata.bind"?: string;
}

/**
 * Payload for updating an existing Event record via the Web API.
 * All fields are optional — only provided fields are updated (PATCH semantics).
 */
export interface UpdateEventPayload {
  /** Event display name */
  sprk_name?: string;
  /** ISO date string for the due date */
  sprk_duedate?: string;
  /** Event status option set value */
  sprk_status?: number;
  /** Whether this is a To-Do item */
  sprk_todoflag?: boolean;
}

/**
 * Create a new Event record in Dataverse via the Power Pages Web API.
 *
 * Requires the authenticated user to have Create table permission on
 * sprk_event for their web role.
 *
 * The Bearer token is automatically included by `bffApiCall`.
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param payload    Event fields to set on creation
 * @returns          The created ODataEvent record (with system fields populated)
 */
export async function createEvent(
  projectId: string,
  payload: CreateEventPayload
): Promise<ODataEvent> {
  const body: CreateEventPayload = {
    ...payload,
    // Bind to the project using OData navigation property syntax
    "sprk_projectid@odata.bind": `sprk_projects(${projectId})`,
  };

  return createRecord<CreateEventPayload, ODataEvent>(`/api/v1/external/projects/${projectId}/events`, body);
}

/**
 * Update an existing Event record in Dataverse via the Power Pages Web API.
 *
 * Uses PATCH semantics — only the fields included in `payload` are modified.
 * The Bearer token is automatically included by `bffApiCall`.
 *
 * @param eventId  Dataverse GUID of the sprk_event record to update
 * @param payload  Partial event fields to update
 */
export async function updateEvent(
  eventId: string,
  payload: UpdateEventPayload
): Promise<void> {
  return updateRecord<UpdateEventPayload>("/api/v1/external/events", eventId, payload);
}

// ---------------------------------------------------------------------------
// Singleton client object (convenience re-export)
// ---------------------------------------------------------------------------

/**
 * Named export of all Web API client functions as a single object.
 *
 * Usage:
 * ```typescript
 * import { webApiClient } from "../api/web-api-client";
 * const projects = await webApiClient.getProjects();
 * ```
 *
 * Or import individual functions directly for better tree-shaking:
 * ```typescript
 * import { getProjects, getEvents } from "../api/web-api-client";
 * ```
 */
export const webApiClient = {
  // Projects
  getProjects,
  getProjectById,
  // Documents
  getDocuments,
  // Events
  getEvents,
  createEvent,
  updateEvent,
  // Contacts
  getContacts,
  // Organizations
  getOrganizations,
  // Query utilities (for advanced usage)
  buildQueryString,
} as const;
