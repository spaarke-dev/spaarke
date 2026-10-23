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
 *   PATCH /api/v1/external/todos/{id}                         → update to-do (NEW)
 *   GET   /api/v1/external/{projects|matters|workassignments|servicerequests}/{id}/documents
 *                                                              → a record's documents (task 031; service requests ONLY here)
 *   DELETE /api/v1/external/documents/{id}                    → delete own document (task 032 / 054)
 *   DELETE /api/v1/external/todos/{id}                        → delete own to-do (task 032 / 054)
 *   DELETE /api/v1/external/events/{id}                       → delete own event (task 032 / 054)
 *
 * Contract change (R3 task 007): the legacy event-as-todo boolean toggle was
 * removed; to-dos are now first-class `sprk_todo` records with
 * regarding-project resolver fields (ADR-024).
 * See: projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md
 * See: docs/architecture/external-access-architecture.md
 */

import { bffApiCall } from '../auth/bff-client';
import type { CreatorStamps } from './delete-own';

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
export interface ODataDocument extends CreatorStamps {
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
  /**
   * Whether the document has a file Download and Versions can serve (R3 ISS-011, #1886). `false` = a listed document
   * with no file: those actions are hidden. Absent (an older BFF) is treated as `true`.
   */
  hasFile?: boolean;
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
export interface ODataEvent extends CreatorStamps {
  /** Primary key — Dataverse GUID */
  sprk_eventid: string;
  /** Event display name — the BFF reads it from Dataverse `sprk_eventname` (sprk_event has no `sprk_name`; task 097). */
  sprk_name: string;
  /** Due date as a calendar date "yyyy-MM-dd" (Dataverse Date Only, task 098) — parse with parseDueDate, never new Date(). */
  sprk_duedate?: string | null;
  /**
   * Event status — the BFF reads it from the event's status of record, Dataverse `statuscode` (sprk_event has no
   * `sprk_status`; task 097 review F2): Draft 1, Open 659490001, Completed 659490002, Closed 659490003,
   * Cancelled 659490004, Transferred 659490005, On Hold 659490006, Reassigned 659490007, No Further Action 2.
   */
  sprk_status?: number | null;
  /** ISO date string — record created */
  createdon?: string | null;
  /**
   * Lookup ID of the owning project. `sprk_event` has no `sprk_projectid`
   * attribute — its project lookup is `sprk_regardingproject` — so the BFF
   * returns `_sprk_regardingproject_value`.
   */
  _sprk_regardingproject_value?: string | null;
}

/**
 * A To-Do record (sprk_todo EntitySet: sprk_todos).
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
export interface ODataTodo extends CreatorStamps {
  /** Primary key — Dataverse GUID */
  sprk_todoid: string;
  /** To-do display name (title) */
  sprk_name: string;
  /** Rich notes / description (memo up to 100000 chars) */
  sprk_notes?: string | null;
  /** Due date as a calendar date "yyyy-MM-dd" (Dataverse Date Only, task 106) — parse with parseDueDate, never new Date(). */
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
  /**
   * `true` when the BFF cut the list short — it reached its row cap, or a later page failed (unified-access-control-r2
   * task 105). Omitted for a complete list.
   */
  truncated?: boolean;
  /**
   * Whether the caller may create under the record the list is scoped to: the answer that record's create route gives.
   * Document lists always carry it (task 054 review: the upload route's Create). To-do lists and a project's event list
   * carry it only for a caller whose write access is decided per record (`/me` `writeAccessPerRecord`,
   * unified-access-control-r2 task 036 fix round 1); absent otherwise.
   */
  canCreate?: boolean;
}

/**
 * A list read from the BFF: the rows, and whether they are the WHOLE list.
 *
 * `truncated` is `true` when the BFF stopped at its row cap or lost a later page, so `items` is a prefix of the list.
 * A view MUST say so (`TruncatedListNotice`) and never present a truncated list as complete (NFR-03).
 */
export interface ListResult<T> {
  items: T[];
  truncated: boolean;
  /** The BFF's `canCreate` for the record the list is scoped to, when it wrote one (task 036 fix round 1); else absent. */
  canCreate?: boolean | null;
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
   * Not applied: the BFF ignores client query options and returns the whole list up to its own row cap, saying
   * `truncated` when it stops short (task 105). Kept only so existing call sites type-check.
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
 * Make a GET request to the BFF API and return the collection and whether it is complete.
 * Handles the standard `{ value: T[], truncated?: boolean }` envelope automatically.
 *
 * @param bffPath  BFF API path (e.g. "/api/v1/external/projects")
 * @param _options OData query options — reserved for future BFF support
 */
async function getCollection<T>(bffPath: string, _options: ODataQueryOptions = {}): Promise<ListResult<T>> {
  const response = await bffApiCall<ODataCollectionResponse<T>>(bffPath);
  return {
    items: response.value ?? [],
    truncated: response.truncated === true,
    // Only when the BFF said (task 036 fix round 1): a missing member stays absent, never read as "cannot".
    ...(typeof response.canCreate === 'boolean' ? { canCreate: response.canCreate } : {}),
  };
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
 * @param bffPath  BFF API path prefix (e.g. "/api/v1/external/todos")
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
export async function getProjects(options: ODataQueryOptions = {}): Promise<ListResult<ODataProject>> {
  const defaults: ODataQueryOptions = {
    $select:
      'sprk_projectid,sprk_name,sprk_referencenumber,sprk_description,sprk_issecure,sprk_status,createdon,modifiedon',
    $orderby: 'sprk_name asc',
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
export async function getDocuments(
  projectId: string,
  options: ODataQueryOptions = {}
): Promise<ListResult<ODataDocument>> {
  const defaults: ODataQueryOptions = {
    $select: 'sprk_documentid,sprk_name,sprk_documenttype,sprk_summary,_sprk_projectid_value,createdon',
    $filter: `_sprk_projectid_value eq '${projectId}'`,
    $orderby: 'createdon desc',
  };

  // Merge options — allow caller to override $filter / $orderby etc., but not silently lose defaults
  const merged: ODataQueryOptions = { ...defaults, ...options };

  // Preserve the project filter even when caller provides an additional filter
  if (options.$filter && options.$filter !== defaults.$filter) {
    merged.$filter = `(${defaults.$filter}) and (${options.$filter})`;
  }

  return getCollection<ODataDocument>(documentsPath('projects', projectId), merged);
}

/**
 * The record types whose documents the external plane serves (task 031): the `{root}` segment of
 * `/api/v1/external/{root}/{id}/documents`. A service request's documents are reachable ONLY through the
 * `servicerequests` route (the documents module's scope dimensions lack its lookup), never the module fetch. An invoice's
 * documents use the `invoices` route (owner decision 2026-10-11, option A: rights inherited from the invoice's matter and
 * project, the union of both; task 031 notes).
 */
export type DocumentRoot = 'projects' | 'matters' | 'workassignments' | 'servicerequests' | 'invoices';

/** `/api/v1/external/{root}/{id}/documents`: list (GET) and upload (POST). */
export function documentsPath(root: DocumentRoot, recordId: string): string {
  return `/api/v1/external/${root}/${encodeURIComponent(recordId)}/documents`;
}

/** The content (download) route of one document, scoped by the record it is filed to. */
export function documentContentPath(root: DocumentRoot, recordId: string, documentId: string): string {
  return `${documentsPath(root, recordId)}/${encodeURIComponent(documentId)}/content`;
}

/** The version-history route of one document, scoped by the record it is filed to. */
export function documentVersionsPath(root: DocumentRoot, recordId: string, documentId: string): string {
  return `${documentsPath(root, recordId)}/${encodeURIComponent(documentId)}/versions`;
}

/** A record's document list plus whether the caller may upload to it (`null` = the BFF did not say). */
export interface DocumentListResult extends ListResult<ODataDocument> {
  canCreate: boolean | null;
}

/**
 * The documents of a project, matter, work assignment, service request or invoice (task 031 routes), and the BFF's `canCreate`:
 * whether the caller holds Create on the record (so a ViewOnly reader is not offered an upload that can only fail).
 */
export async function getRecordDocuments(root: DocumentRoot, recordId: string): Promise<DocumentListResult> {
  const response = await bffApiCall<ODataCollectionResponse<ODataDocument>>(documentsPath(root, recordId));
  return {
    items: response.value ?? [],
    truncated: response.truncated === true,
    canCreate: typeof response.canCreate === 'boolean' ? response.canCreate : null,
  };
}

// ---------------------------------------------------------------------------
// Event queries
// ---------------------------------------------------------------------------

/**
 * The record kinds whose events and to-dos the external plane lists (task 039 / task 029): the `{root}` segment of
 * `/api/v1/external/{root}/{id}/events` and `.../todos`. Same row shape on all three; the server applies the record's own
 * rights (and the every-root rule) and the client does no filtering of its own.
 */
export type EventsTodosRoot = Extract<DocumentRoot, 'projects' | 'matters' | 'workassignments'>;

/** What an events/to-dos section calls the record it belongs to, in its sentences. */
export const EVENTS_TODOS_ROOT_NOUN: Readonly<Record<EventsTodosRoot, string>> = {
  projects: 'project',
  matters: 'matter',
  workassignments: 'work assignment',
};

/** `/api/v1/external/{root}/{id}/events`: the events regarding one record. */
export function rootEventsPath(root: EventsTodosRoot, recordId: string): string {
  return `/api/v1/external/${root}/${encodeURIComponent(recordId)}/events`;
}

/** `/api/v1/external/{root}/{id}/todos`: the to-dos regarding one record. */
export function rootTodosPath(root: EventsTodosRoot, recordId: string): string {
  return `/api/v1/external/${root}/${encodeURIComponent(recordId)}/todos`;
}

/**
 * Retrieve the Events regarding a project, matter or work assignment (task 057; the project route is the original).
 *
 * `sprk_event`'s project lookup is `sprk_regardingproject` (it has no `sprk_projectid` attribute); a matter or work
 * assignment event carries `null` there. Events no longer carry a to-do flag - see `getRootTodos`.
 *
 * NOTE: `getCollection` ignores its options entirely (:291) - select/filter/order/top are all applied server-side by
 * GET /api/v1/external/{root}/{id}/events - so the options are accepted only so existing call sites type-check.
 *
 * @param root      Which kind of record `recordId` is
 * @param recordId  Dataverse GUID of the record
 * @param options   Optional OData query overrides (currently ignored downstream)
 */
export async function getRootEvents(
  root: EventsTodosRoot,
  recordId: string,
  options: ODataQueryOptions = {}
): Promise<ListResult<ODataEvent>> {
  return getCollection<ODataEvent>(rootEventsPath(root, recordId), options);
}

/**
 * Retrieve all Events belonging to a Secure Project (`getRootEvents` for the project root).
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param options    Optional OData query overrides (currently ignored downstream)
 */
export async function getEvents(projectId: string, options: ODataQueryOptions = {}): Promise<ListResult<ODataEvent>> {
  return getRootEvents('projects', projectId, options);
}

// ---------------------------------------------------------------------------
// To-Do queries (NEW in R3 task 007 — replaces the legacy event-as-todo toggle)
// ---------------------------------------------------------------------------

/**
 * Retrieve the To-Dos regarding a project, matter or work assignment (task 057; the project route is the original).
 *
 * Calls the BFF route `/api/v1/external/{root}/{id}/todos`. The BFF resolves the regarding lookup server-side and returns
 * DTOs with the ADR-024 resolver fields populated. Note: the BFF computes the `$select` server-side.
 *
 * @param root      Which kind of record `recordId` is
 * @param recordId  Dataverse GUID of the record
 * @param options   Optional OData query overrides (currently ignored downstream)
 */
export async function getRootTodos(
  root: EventsTodosRoot,
  recordId: string,
  options: ODataQueryOptions = {}
): Promise<ListResult<ODataTodo>> {
  const defaults: ODataQueryOptions = {
    $orderby: 'sprk_duedate asc',
  };

  return getCollection<ODataTodo>(rootTodosPath(root, recordId), { ...defaults, ...options });
}

/**
 * Retrieve all To-Dos belonging to a Secure Project (`getRootTodos` for the project root).
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param options    Optional OData query overrides
 */
export async function getProjectTodos(
  projectId: string,
  options: ODataQueryOptions = {}
): Promise<ListResult<ODataTodo>> {
  return getRootTodos('projects', projectId, options);
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
): Promise<ListResult<ODataContact>> {
  const defaults: ODataQueryOptions = {
    $select: 'contactid,fullname,firstname,lastname,emailaddress1,telephone1,jobtitle,_parentcustomerid_value',
    // Filter contacts that have an active access record for this project.
    // The relationship is: contact → sprk_externalrecordaccess → sprk_project
    // We filter via the related entity navigation property.
    $filter: `sprk_externalrecordaccess_contact_contactid/any(a:a/_sprk_projectid_value eq '${projectId}' and a/statecode eq 0)`,
    $orderby: 'fullname asc',
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
): Promise<ListResult<ODataOrganization>> {
  const defaults: ODataQueryOptions = {
    $select: 'accountid,name,websiteurl,telephone1,address1_city,address1_country',
    // Filter accounts that have contacts with access to this project.
    $filter: `contact_customer_accounts/any(c:c/sprk_externalrecordaccess_contact_contactid/any(a:a/_sprk_projectid_value eq '${projectId}' and a/statecode eq 0))`,
    $orderby: 'name asc',
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
  /** Event display name — the BFF writes it to `sprk_eventname` (task 097). */
  sprk_name: string;
  /** Due date as a calendar date "yyyy-MM-dd" (Dataverse Date Only refuses a timestamp — task 098). */
  sprk_duedate?: string;
  /** Optional status — Draft (1) or Open (659490001) only, written to `statuscode`; omit it to create the event Open (task 097 review F9). */
  sprk_status?: number;
  /**
   * OData binding to associate the event with a project. The navigation property is the
   * PascalCase SchemaName sprk_RegardingProject (metadata-verified, R5 task 002 — sprk_event
   * has no "sprk_projectid" lookup; its project lookup is sprk_regardingproject).
   * Example: "/sprk_projects(b1c2d3e4-0000-0000-0000-000000000001)"
   */
  'sprk_RegardingProject@odata.bind'?: string;
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
    // Bind to the project using the OData navigation property (PascalCase SchemaName).
    'sprk_RegardingProject@odata.bind': `/sprk_projects(${projectId})`,
  };

  return createRecord<CreateEventPayload, ODataEvent>(`/api/v1/external/projects/${projectId}/events`, body);
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
  /** Due date as a calendar date "yyyy-MM-dd" (Dataverse Date Only refuses a timestamp — task 106). */
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
  /** Due date as a calendar date "yyyy-MM-dd" (Dataverse Date Only refuses a timestamp — task 106). */
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
// Delete-own (task 054, FR-26; server: task 032)
// ---------------------------------------------------------------------------
//
// SERVICE REQUESTS (owner decision 2026-10-11): the requester may delete their own request while it has no related record.
// A request with activity answers 409 `has_related_records`, and the user can then ask legal to recall it through the
// existing message post (`sendMessage`, messages-client.ts) - there is no recall endpoint.
//
// Each call resolves on 204. The server decides (stamp + Read on the parent) and answers a uniform 403, 409 (checked out /
// storage unverified), 503 (nothing deleted, retry) or 500, all as ProblemDetails with a `reasonCode`; see
// `deleteFailureOf` (delete-own.ts) for what the user is told. The delete is by id alone: the server finds the parent.

/** Hard-deletes a document the caller created (and its stored file). */
export async function deleteOwnDocument(documentId: string): Promise<void> {
  await bffApiCall<void>(`/api/v1/external/documents/${encodeURIComponent(documentId)}`, { method: 'DELETE' });
}

/** Hard-deletes a to-do the caller created. */
export async function deleteOwnTodo(todoId: string): Promise<void> {
  await bffApiCall<void>(`/api/v1/external/todos/${encodeURIComponent(todoId)}`, { method: 'DELETE' });
}

/**
 * Hard-deletes a service request the caller requested, when it has no related record. 204 on success; 403 (uniform: not the
 * requester / absent), 409 `has_related_records` (the request has activity: offer a recall), 503 (nothing deleted, retry).
 */
export async function deleteOwnServiceRequest(serviceRequestId: string): Promise<void> {
  await bffApiCall<void>(`/api/v1/external/servicerequests/${encodeURIComponent(serviceRequestId)}`, {
    method: 'DELETE',
  });
}

/** Hard-deletes an event the caller created. */
export async function deleteOwnEvent(eventId: string): Promise<void> {
  await bffApiCall<void>(`/api/v1/external/events/${encodeURIComponent(eventId)}`, { method: 'DELETE' });
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
  getRecordDocuments,
  deleteOwnDocument,
  // Events
  getEvents,
  getRootEvents,
  createEvent,
  // To-Dos (NEW)
  getProjectTodos,
  getRootTodos,
  createTodo,
  updateTodo,
  deleteOwnTodo,
  deleteOwnEvent,
  deleteOwnServiceRequest,
  // Contacts
  getContacts,
  // Organizations
  getOrganizations,
  // Query utilities (for advanced usage)
  buildQueryString,
} as const;
