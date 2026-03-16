/**
 * Power Pages Web API Client — Secure Project Workspace SPA
 *
 * Provides a typed OData 4.0 client for querying and mutating Dataverse tables
 * through the Power Pages Web API proxy at `/_api/`.
 *
 * All requests use `portalApiCall` from portal-auth.ts which:
 *   - Injects `OData-Version: 4.0` and `OData-MaxVersion: 4.0` headers
 *   - Injects the anti-forgery (CSRF) token for write operations
 *   - Handles 401/403 by redirecting to the portal sign-in page
 *   - Throws ApiError on non-2xx responses
 *
 * Base URL: `/_api/` (proxied via Vite dev server to the Power Pages portal)
 *
 * Tables enabled via Power Pages site settings:
 *   - sprk_projects          (EntitySetName)
 *   - sprk_documents         (EntitySetName)
 *   - sprk_events            (EntitySetName)
 *   - contacts               (EntitySetName)
 *   - accounts               (EntitySetName)
 *
 * Relationship navigation properties used for project-scoped queries:
 *   - sprk_project_documents  (1:N, sprk_document → sprk_project)
 *   - sprk_project_events     (1:N, sprk_event → sprk_project)
 *   - sprk_project_contacts   (N:N or via junction, contacts → sprk_project)
 *
 * See: docs/architecture/power-pages-spa-guide.md — Power Pages Web API section
 */

import { portalApiCall } from "../auth/portal-auth";

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
 * Make a GET request to the Power Pages Web API and return the collection value.
 * Handles the standard `{ value: T[] }` envelope automatically.
 *
 * @param entitySet  OData EntitySet name (e.g. "sprk_projects")
 * @param options    OData query options ($select, $filter, etc.)
 */
async function getCollection<T>(
  entitySet: string,
  options: ODataQueryOptions = {}
): Promise<T[]> {
  const qs = buildQueryString(options);
  const url = `/_api/${entitySet}${qs}`;
  const response = await portalApiCall<ODataCollectionResponse<T>>(url);
  return response.value ?? [];
}

/**
 * Make a GET request to the Power Pages Web API for a single entity by ID.
 *
 * @param entitySet  OData EntitySet name (e.g. "sprk_projects")
 * @param id         Record GUID
 * @param options    OData query options ($select, $expand)
 */
async function getById<T>(
  entitySet: string,
  id: string,
  options: ODataQueryOptions = {}
): Promise<T> {
  const qs = buildQueryString(options);
  const url = `/_api/${entitySet}(${id})${qs}`;
  return portalApiCall<T>(url);
}

/**
 * Make a POST (create) request to the Power Pages Web API.
 * The anti-forgery token is injected automatically by `portalApiCall`.
 *
 * @param entitySet  OData EntitySet name (e.g. "sprk_events")
 * @param body       Record payload to create
 * @returns          The created record (OData returns it with 201 Created)
 */
async function createRecord<TBody, TResult = TBody>(
  entitySet: string,
  body: TBody
): Promise<TResult> {
  const url = `/_api/${entitySet}`;
  return portalApiCall<TResult>(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Prefer": "return=representation",
    },
    body: JSON.stringify(body),
  });
}

/**
 * Make a PATCH (update) request to the Power Pages Web API.
 * The anti-forgery token is injected automatically by `portalApiCall`.
 *
 * @param entitySet  OData EntitySet name (e.g. "sprk_events")
 * @param id         Record GUID to update
 * @param body       Partial record payload with fields to update
 */
async function updateRecord<TBody>(
  entitySet: string,
  id: string,
  body: Partial<TBody>
): Promise<void> {
  const url = `/_api/${entitySet}(${id})`;
  await portalApiCall<void>(url, {
    method: "PATCH",
    headers: {
      "Content-Type": "application/json",
    },
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

  return getCollection<ODataProject>("sprk_projects", { ...defaults, ...options });
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

  return getById<ODataProject>("sprk_projects", projectId, { ...defaults, ...options });
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

  return getCollection<ODataDocument>("sprk_documents", merged);
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

  return getCollection<ODataEvent>("sprk_events", merged);
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

  return getCollection<ODataContact>("contacts", merged);
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

  return getCollection<ODataOrganization>("accounts", merged);
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
 * The CSRF anti-forgery token is automatically included by `portalApiCall`.
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

  return createRecord<CreateEventPayload, ODataEvent>("sprk_events", body);
}

/**
 * Update an existing Event record in Dataverse via the Power Pages Web API.
 *
 * Uses PATCH semantics — only the fields included in `payload` are modified.
 * The CSRF anti-forgery token is automatically included by `portalApiCall`.
 *
 * @param eventId  Dataverse GUID of the sprk_event record to update
 * @param payload  Partial event fields to update
 */
export async function updateEvent(
  eventId: string,
  payload: UpdateEventPayload
): Promise<void> {
  return updateRecord<UpdateEventPayload>("sprk_events", eventId, payload);
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
