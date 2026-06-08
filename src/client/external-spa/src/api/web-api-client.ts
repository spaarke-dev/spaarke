/**
 * BFF Data Client — Secure Project Workspace SPA
 *
 * Provides typed wrappers for querying Dataverse data through the BFF API.
 * All requests use `bffApiCall` from bff-client.ts which handles Azure AD B2B
 * authentication (MSAL) and Bearer token injection.
 *
 * BFF routes used:
 *   GET   /api/v1/external/projects                           → list accessible projects
 *   GET   /api/v1/external/projects/{id}                      → single project
 *   GET   /api/v1/external/projects/{id}/documents            → project documents
 *   GET   /api/v1/external/projects/{id}/events               → project events
 *   GET   /api/v1/external/projects/{id}/todos                → project to-dos (NEW; replaces /events?todoflag=true)
 *   GET   /api/v1/external/projects/{id}/contacts             → project contacts
 *   GET   /api/v1/external/projects/{id}/organizations        → project organizations
 *   POST  /api/v1/external/projects/{id}/events               → create event
 *   POST  /api/v1/external/projects/{id}/todos                → create to-do (NEW)
 *   PATCH /api/v1/external/events/{id}                        → update event
 *   PATCH /api/v1/external/todos/{id}                         → update to-do (NEW)
 *
 * Contract change (R3 task 007): the legacy event-as-todo boolean toggle was
 * removed; to-dos are now first-class `sprk_todo` records with
 * regarding-project resolver fields (ADR-024).
 * See: projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md
 * See: docs/architecture/external-access-architecture.md
 */

import { bffApiCall } from '../auth/bff-client';

// ---------------------------------------------------------------------------
// Re-export ApiError for module consumers
// ---------------------------------------------------------------------------

export { ApiError } from '../types';

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
  'createdby@OData.Community.Display.V1.FormattedValue'?: string | null;
}

/**
 * An Event record (sprk_event EntitySet: sprk_events).
 *
 * Events are now strictly calendar / project-timeline items. The legacy
 * event-as-todo boolean toggle was removed in R3 task 007 — to-dos are
 * first-class `sprk_todo` records (see `ODataTodo` below).
 *
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
  /** ISO date string — record created */
  createdon?: string | null;
  /** Lookup ID of the owning project */
  _sprk_projectid_value?: string | null;
}

/**
 * A To-Do record (sprk_todo EntitySet: sprk_todoes).
 *
 * NEW in R3 task 007 — replaces the legacy event-as-todo boolean-toggle
 * pattern. The BFF synthesises this DTO from `sprk_todo` records scoped to
 * the regarding project via `_sprk_regardingproject_value` plus ADR-024
 * polymorphic-resolver fields.
 *
 * Status values (FR-24):
 *   1         = Open
 *   659490001 = In Progress
 *   2         = Completed
 *   659490002 = Dismissed
 *
 * Column values (sprk_todocolumn):
 *   100000000 = Today
 *   100000001 = Tomorrow
 *   100000002 = Future
 */
export interface ODataTodo {
  /** Primary key — Dataverse GUID */
  sprk_todoid: string;
  /** To-do display name (title) */
  sprk_name: string;
  /** Rich notes / description (memo up to 100000 chars) */
  sprk_notes?: string | null;
  /** ISO date string — due date */
  sprk_duedate?: string | null;
  /** Priority score 0-100 */
  sprk_priorityscore?: number | null;
  /** Effort score 0-100 */
  sprk_effortscore?: number | null;
  /** Kanban column: 100000000=Today / 100000001=Tomorrow / 100000002=Future */
  sprk_todocolumn?: number | null;
  /** Whether the column assignment is pinned (locks against auto-shifting) */
  sprk_todopinned?: boolean | null;
  /** Entity state: 0=Active / 1=Inactive */
  statecode?: number | null;
  /** Status reason: 1=Open / 659490001=In Progress / 2=Completed / 659490002=Dismissed */
  statuscode?: number | null;
  /** ISO date string — record created */
  createdon?: string | null;
  /** Regarding-project lookup value (GUID) */
  _sprk_regardingproject_value?: string | null;
  /** ADR-024 resolver: regarding record GUID */
  sprk_regardingrecordid?: string | null;
  /** ADR-024 resolver: regarding record display name */
  sprk_regardingrecordname?: string | null;
  /** ADR-024 resolver: regarding record deep-link URL (e.g. main.aspx?...) */
  sprk_regardingrecordurl?: string | null;
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
  '@odata.context'?: string;
  value: T[];
  '@odata.nextLink'?: string;
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

  return params.length > 0 ? `?${params.join('&')}` : '';
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
async function getCollection<T>(bffPath: string, _options: ODataQueryOptions = {}): Promise<T[]> {
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
async function getById<T>(bffPath: string, id: string, _options: ODataQueryOptions = {}): Promise<T> {
  return bffApiCall<T>(`${bffPath}/${id}`);
}

/**
 * Make a POST (create) request to the BFF API.
 *
 * @param bffPath  BFF API path (e.g. "/api/v1/external/projects/{id}/events")
 * @param body     Record payload to create
 * @returns        The created record
 */
async function createRecord<TBody, TResult = TBody>(bffPath: string, body: TBody): Promise<TResult> {
  return bffApiCall<TResult>(bffPath, {
    method: 'POST',
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
async function updateRecord<TBody>(bffPath: string, id: string, body: Partial<TBody>): Promise<void> {
  await bffApiCall<void>(`${bffPath}/${id}`, {
    method: 'PATCH',
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
export async function getProjects(options: ODataQueryOptions = {}): Promise<ODataProject[]> {
  const defaults: ODataQueryOptions = {
    $select:
      'sprk_projectid,sprk_name,sprk_referencenumber,sprk_description,sprk_issecure,sprk_status,createdon,modifiedon',
    $orderby: 'sprk_name asc',
    $top: 100,
  };

  return getCollection<ODataProject>('/api/v1/external/projects', { ...defaults, ...options });
}

/**
 * Retrieve a single Secure Project by its record ID.
 *
 * @param projectId  Dataverse GUID of the sprk_project record
 * @param options    Optional OData query overrides ($select, $expand)
 */
export async function getProjectById(projectId: string, options: ODataQueryOptions = {}): Promise<ODataProject> {
  const defaults: ODataQueryOptions = {
    $select:
      'sprk_projectid,sprk_name,sprk_referencenumber,sprk_description,sprk_issecure,sprk_status,createdon,modifiedon',
  };

  return getById<ODataProject>('/api/v1/external/projects', projectId, { ...defaults, ...options });
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
export async function getDocuments(projectId: string, options: ODataQueryOptions = {}): Promise<ODataDocument[]> {
  const defaults: ODataQueryOptions = {
    $select: 'sprk_documentid,sprk_name,sprk_documenttype,sprk_summary,_sprk_projectid_value,createdon',
    $filter: `_sprk_projectid_value eq '${projectId}'`,
    $orderby: 'createdon desc',
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
 * Filters by the `_sprk_projectid_value` lookup column. Events no longer
 * carry a to-do flag — see `getProjectTodos` for project to-do retrieval.
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param options    Optional OData query overrides
 */
export async function getEvents(projectId: string, options: ODataQueryOptions = {}): Promise<ODataEvent[]> {
  const defaults: ODataQueryOptions = {
    $select: 'sprk_eventid,sprk_name,sprk_duedate,sprk_status,_sprk_projectid_value,createdon',
    $filter: `_sprk_projectid_value eq '${projectId}'`,
    $orderby: 'sprk_duedate asc',
    $top: 200,
  };

  const merged: ODataQueryOptions = { ...defaults, ...options };

  if (options.$filter && options.$filter !== defaults.$filter) {
    merged.$filter = `(${defaults.$filter}) and (${options.$filter})`;
  }

  return getCollection<ODataEvent>(`/api/v1/external/projects/${projectId}/events`, merged);
}

// ---------------------------------------------------------------------------
// To-Do queries (NEW in R3 task 007 — replaces the legacy event-as-todo toggle)
// ---------------------------------------------------------------------------

/**
 * Retrieve all To-Dos belonging to a Secure Project.
 *
 * Calls the BFF route `/api/v1/external/projects/{id}/todos`. The BFF
 * resolves the regarding-project lookup server-side and returns DTOs
 * with the ADR-024 resolver fields populated.
 *
 * Note: the BFF computes the `$select` server-side; client `$select`
 * overrides are honoured but the field list is constrained to
 * `ODataTodo` shape.
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param options    Optional OData query overrides
 */
export async function getProjectTodos(projectId: string, options: ODataQueryOptions = {}): Promise<ODataTodo[]> {
  const defaults: ODataQueryOptions = {
    $orderby: 'sprk_duedate asc',
    $top: 200,
  };

  const merged: ODataQueryOptions = { ...defaults, ...options };

  return getCollection<ODataTodo>(`/api/v1/external/projects/${projectId}/todos`, merged);
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
export async function getContacts(projectId: string, options: ODataQueryOptions = {}): Promise<ODataContact[]> {
  const defaults: ODataQueryOptions = {
    $select: 'contactid,fullname,firstname,lastname,emailaddress1,telephone1,jobtitle,_parentcustomerid_value',
    // Filter contacts that have an active access record for this project.
    // The relationship is: contact → sprk_externalrecordaccess → sprk_project
    // We filter via the related entity navigation property.
    $filter: `sprk_externalrecordaccess_contact_contactid/any(a:a/_sprk_projectid_value eq '${projectId}' and a/statecode eq 0)`,
    $orderby: 'fullname asc',
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
    $select: 'accountid,name,websiteurl,telephone1,address1_city,address1_country',
    // Filter accounts that have contacts with access to this project.
    $filter: `contact_customer_accounts/any(c:c/sprk_externalrecordaccess_contact_contactid/any(a:a/_sprk_projectid_value eq '${projectId}' and a/statecode eq 0))`,
    $orderby: 'name asc',
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
  /**
   * OData binding syntax to associate the event with a project.
   * Example: "sprk_projects(b1c2d3e4-0000-0000-0000-000000000001)"
   */
  'sprk_projectid@odata.bind'?: string;
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
export async function createEvent(projectId: string, payload: CreateEventPayload): Promise<ODataEvent> {
  const body: CreateEventPayload = {
    ...payload,
    // Bind to the project using OData navigation property syntax
    'sprk_projectid@odata.bind': `sprk_projects(${projectId})`,
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
export async function updateEvent(eventId: string, payload: UpdateEventPayload): Promise<void> {
  return updateRecord<UpdateEventPayload>('/api/v1/external/events', eventId, payload);
}

// ---------------------------------------------------------------------------
// To-Do write operations (NEW in R3 task 007)
// ---------------------------------------------------------------------------

/**
 * Payload for creating a new To-Do record via the BFF.
 *
 * NOTE: regarding context (project lookup + ADR-024 resolver fields) is NOT
 * in the request body. The BFF applies the regarding context server-side
 * using the project id from the route — prevents external callers from
 * regarding-ing a to-do to an arbitrary project they don't have access to.
 */
export interface CreateTodoPayload {
  /** To-do title (required) */
  sprk_name: string;
  /** Rich notes / description */
  sprk_notes?: string | null;
  /** ISO date string for the due date */
  sprk_duedate?: string | null;
  /** Priority score 0-100 */
  sprk_priorityscore?: number;
  /** Effort score 0-100 */
  sprk_effortscore?: number;
  /** Kanban column: 100000000=Today / 100000001=Tomorrow / 100000002=Future */
  sprk_todocolumn?: number;
  /** Whether the column assignment is pinned */
  sprk_todopinned?: boolean;
}

/**
 * Payload for updating an existing To-Do record via the BFF.
 *
 * PATCH semantics — only provided fields are written. Regarding context
 * cannot be changed via this surface (re-parent through the model-driven
 * app form to keep resolver-field invariants intact per ADR-024).
 */
export interface UpdateTodoPayload {
  /** To-do title */
  sprk_name?: string | null;
  /** Rich notes / description */
  sprk_notes?: string | null;
  /** ISO date string for the due date */
  sprk_duedate?: string | null;
  /** Priority score 0-100 */
  sprk_priorityscore?: number;
  /** Effort score 0-100 */
  sprk_effortscore?: number;
  /** Kanban column: 100000000=Today / 100000001=Tomorrow / 100000002=Future */
  sprk_todocolumn?: number;
  /** Whether the column assignment is pinned */
  sprk_todopinned?: boolean;
  /** Status reason: 1=Open / 659490001=In Progress / 2=Completed / 659490002=Dismissed */
  statuscode?: number;
}

/**
 * Create a new To-Do record via the BFF.
 *
 * The BFF applies the regarding-project lookup + 4 ADR-024 resolver fields
 * server-side using `{projectId}` from the route.
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param payload    To-Do fields to set on creation
 * @returns          The created ODataTodo record (with system + resolver fields populated)
 */
export async function createTodo(projectId: string, payload: CreateTodoPayload): Promise<ODataTodo> {
  return createRecord<CreateTodoPayload, ODataTodo>(`/api/v1/external/projects/${projectId}/todos`, payload);
}

/**
 * Update an existing To-Do record via the BFF.
 *
 * PATCH semantics — only the fields included in `payload` are modified.
 *
 * @param todoId   Dataverse GUID of the sprk_todo record to update
 * @param payload  Partial to-do fields to update
 */
export async function updateTodo(todoId: string, payload: UpdateTodoPayload): Promise<void> {
  return updateRecord<UpdateTodoPayload>('/api/v1/external/todos', todoId, payload);
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
 * import { getProjects, getEvents, getProjectTodos } from "../api/web-api-client";
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
  // To-Dos (NEW)
  getProjectTodos,
  createTodo,
  updateTodo,
  // Contacts
  getContacts,
  // Organizations
  getOrganizations,
  // Query utilities (for advanced usage)
  buildQueryString,
} as const;
