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
 *   GET   /api/v1/external/projects/{id}/contacts             → project contacts
 *   GET   /api/v1/external/projects/{id}/organizations        → project organizations
 *   POST  /api/v1/external/projects/{id}/events               → create event
 *   GET   /api/v1/external/events/{id}                        → one event (R4 task 015; the `?record=event:{id}` deep link)
 *   PATCH /api/v1/external/events/{id}                        → edit / change status / reassign (R4 task 015; 204)
 *   GET   /api/v1/external/{projects|matters|workassignments}/{id}/assignable-people → who an event may be assigned to
 *   GET   /api/v1/external/{projects|matters|workassignments|servicerequests}/{id}/documents
 *                                                              → a record's documents (task 031; service requests ONLY here)
 *   GET   /api/v1/external/invoices/{id}/files[/{fileId}/content] → an invoice's files, list and download only (R4 task 021)
 *   DELETE /api/v1/external/documents/{id}                    → delete own document (task 032 / 054)
 *   DELETE /api/v1/external/events/{id}                       → delete own event (task 032 / 054)
 *   POST  /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions → submit an invoice PDF (ebilling-r1 051)
 *   GET   /api/v1/external/invoice-submissions[/{id}]         → the caller's submissions / one submission (ebilling-r1 051)
 *
 * External users work only with `sprk_event` (owner 2026-11-12, R4 task 009): the SPA calls no other work-item
 * route (a Task is an event type).
 * See: docs/architecture/external-access-architecture.md
 */

import { bffApiBlob, bffApiCall, getExternalUserContext } from '../auth/bff-client';
import { ApiError } from '../types';
import { gridDataverseClient } from '../services/gridDataverseClient';
import { InvoiceSubmissionError, isEbillingDisabledError } from '@spaarke/ui-components/components/InvoiceSubmission';
import type {
  InvoiceCoreRecordKind,
  InvoiceCoreRecordList,
  InvoiceCoreRecordOption,
  InvoiceFirmOption,
  InvoiceRecordQuery,
  InvoiceSubmissionAccepted,
  InvoiceSubmissionInput,
  InvoiceSubmissionStatusView,
} from '@spaarke/ui-components/components/InvoiceSubmission';
import type { CreatorStamps } from './delete-own';

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

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
 * Events are calendar / project-timeline items. External users work only
 * with events (owner 2026-11-12, R4 task 009).
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
  /**
   * `true` when the event is of the Task type (R4 task 008): the BFF reads the type server-side and sends only this
   * boolean. The Events list marks these with a Task badge; every other event is a plain event. Absent (an older BFF) is "not a task".
   */
  isTask?: boolean;
  // R4 task 014b/c, 015 (FR-35 / FR-36): the event's own fields and the rights the BFF decided for THIS caller. Names are the
  // Dataverse FormattedValues of the lookups; the SPA never receives a contact id except from `assignable-people`.
  /** The event type's row id (a type row, never a person); null when the event has no type. */
  eventTypeId?: string | null;
  /** The event type's name; null when the event has no type (shown as "—"). */
  eventTypeName?: string | null;
  /** Plain text, up to 2,000 characters. */
  sprk_description?: string | null;
  /** Low 100000000, Normal 100000001, High 100000002, Urgent 100000003 (`events/eventFields.ts`). */
  sprk_priority?: number | null;
  /** The assignee's name; never the assignee's id or email. */
  assignedToName?: string | null;
  /** The creator's name. */
  createdByName?: string | null;
  /** `PATCH /events/{id}` would admit an edit by this caller (the same check). Absent = not said = hidden. */
  canEdit?: boolean;
  /** `PATCH /events/{id}` would admit a status change by this caller. Absent = not said = hidden. */
  canChangeStatus?: boolean;
  /** The statuscodes a status change may set (every status but the current one), when `canChangeStatus`. */
  allowedStatuses?: number[];
  /** The PATCH would admit a reassign (`assignedToContactId`): `canEdit`, open work, and a role that reads contacts. */
  canReassign?: boolean;
}

/** The record an event is regarding, as `GET /events/{id}` names it. */
export interface ODataEventRegarding {
  kind: 'project' | 'matter' | 'workassignment';
  id: string;
  name?: string | null;
}

/** `GET /events/{id}`: the list row's fields plus the regarding record. */
export interface ODataEventDetail extends ODataEvent {
  regarding?: ODataEventRegarding | null;
}

/** One event type the caller may pick on create (`creatableEventTypes` of the events list). */
export interface CreatableEventType {
  id: string;
  name: string;
  /** An event of this type needs a due date. */
  requiresDueDate: boolean;
}

/** One person an event may be assigned to (`GET …/assignable-people`). The only place a contact id reaches the SPA. */
export interface AssignablePerson {
  contactId: string;
  name: string;
  isMe: boolean;
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
   * Document lists always carry it (task 054 review: the upload route's Create). Since R4 task 002 (FR-06) the event
   * lists of a project, matter or work assignment carry it for every caller too (it was only for a caller whose
   * write access is decided per record before, unified-access-control-r2 task 036 fix round 1). A BFF that predates that
   * may still omit it: absent is "not said", never "can".
   */
  canCreate?: boolean;
  /**
   * The event lists only (R4 task 014b): every event type the caller may pick on create. An empty array means "none": the
   * SPA offers no Add event (014 K3). Omitted by a BFF that predates it: absent is "not said", never "any".
   */
  creatableEventTypes?: unknown;
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
  /**
   * The event lists' `creatableEventTypes` (task 014b), only when the BFF sent an array (an empty one included: "no type may
   * be picked"); else absent.
   */
  creatableEventTypes?: CreatableEventType[];
}

/** The well-formed entries of a `creatableEventTypes` array (an entry with no id or name is dropped, never guessed). */
function creatableEventTypesOf(raw: unknown[]): CreatableEventType[] {
  const types: CreatableEventType[] = [];
  for (const entry of raw) {
    if (!entry || typeof entry !== 'object') continue;
    const { id, name, requiresDueDate } = entry as Record<string, unknown>;
    if (typeof id !== 'string' || !id || typeof name !== 'string' || !name.trim()) continue;
    types.push({ id, name, requiresDueDate: requiresDueDate === true });
  }
  return types;
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
    ...(Array.isArray(response.creatableEventTypes)
      ? { creatableEventTypes: creatableEventTypesOf(response.creatableEventTypes) }
      : {}),
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
 * `servicerequests` route (the documents module's scope dimensions lack its lookup), never the module fetch. An invoice has
 * NO document root: its files are ebilling's read-only `/invoices/{id}/files` routes (`getInvoiceFiles` below; R4 task 021).
 */
export type DocumentRoot = 'projects' | 'matters' | 'workassignments' | 'servicerequests';

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
 * The documents of a project, matter, work assignment or service request (task 031 routes), and the BFF's `canCreate`:
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
// Invoice files (R4 task 021, FR-13): ebilling-r1 task 093's read-only firm routes
// ---------------------------------------------------------------------------
//
// Contract read at the ebilling PR #2839 head (ref pr/2839):
//   ExternalInvoiceSubmissionEndpoints.cs:106 GET /api/v1/external/invoices/{id}/files          -> 200 { value: ExternalInvoiceFileItem[] }
//   ExternalInvoiceSubmissionEndpoints.cs:122 GET /api/v1/external/invoices/{id}/files/{fileId}/content -> the bytes (no Content-Disposition name)
//   StoredFileContracts.cs:628 `ExternalInvoiceFileItem` (Id, FileName, ContentType?, SizeBytes?, CreatedOn?, ScanStatus)
// Content for a Checking file answers 409 `errorCode: file_scan_pending` (StoredFileEndpoints.cs:479). A file the caller may not
// see (an invisible invoice, an unknown or out-of-set file, a Malicious one) is one uniform 404; a fault or the eBilling kill
// switch is 503 (`file_store_unavailable`, `ebilling.disabled`); the workforce role ceiling is 403. There is no upload route
// and no versions route: the SPA offers list and download only.

/** `scanStatus`: Clean and NotScanned are served; Checking has no malware verdict yet (its content answers 409). */
export type InvoiceFileScanStatus = 'Clean' | 'Checking' | 'NotScanned';

/** One row of `GET /api/v1/external/invoices/{id}/files`. `contentType`, `sizeBytes` and `createdOn` (UTC ISO) may be null. */
export interface InvoiceFileItem {
  id: string;
  fileName: string;
  contentType: string | null;
  sizeBytes: number | null;
  createdOn: string | null;
  /** Typed as the contract's values; the reader treats any other string as not served (never offers Download for it). */
  scanStatus: InvoiceFileScanStatus;
}

/** The `errorCode` of the content route's 409 while a file's malware scan has no verdict. */
export const FILE_SCAN_PENDING_ERROR_CODE = 'file_scan_pending';

/** `/api/v1/external/invoices/{id}/files`: the invoice's firm-visible files (list). */
export function invoiceFilesPath(invoiceId: string): string {
  return `/api/v1/external/invoices/${encodeURIComponent(invoiceId)}/files`;
}

/** The content (download) route of one invoice file. */
export function invoiceFileContentPath(invoiceId: string, fileId: string): string {
  return `${invoiceFilesPath(invoiceId)}/${encodeURIComponent(fileId)}/content`;
}

/** The invoice's files, as the BFF lists them (a Malicious file is omitted by the server). */
export async function getInvoiceFiles(invoiceId: string): Promise<InvoiceFileItem[]> {
  const response = await bffApiCall<{ value?: InvoiceFileItem[] }>(invoiceFilesPath(invoiceId));
  return response.value ?? [];
}

/** The bytes of one invoice file, through the authenticated blob read (no signed URL, no Graph, no SPE pointer). */
export async function getInvoiceFileContent(invoiceId: string, fileId: string): Promise<Blob> {
  return bffApiBlob(invoiceFileContentPath(invoiceId, fileId));
}

/** Whether a failed content read is the 409 `file_scan_pending` (the file is still being checked). */
export function isFileScanPendingError(err: unknown): boolean {
  if (!(err instanceof ApiError) || err.statusCode !== 409) return false;
  try {
    return (JSON.parse(err.message) as { errorCode?: unknown }).errorCode === FILE_SCAN_PENDING_ERROR_CODE;
  } catch {
    return false;
  }
}

/**
 * Whether a failed files read is the eBilling kill switch (503 `ebilling.disabled`; both files routes sit behind it). It
 * reuses the shared detector, which answers true for that code only: an outage 503 (`file_store_unavailable`) is not it.
 */
export function isInvoiceFilesDisabledError(err: unknown): boolean {
  return isEbillingDisabledError(toInvoiceSubmissionError(err));
}

/** The name an invoice file is shown under when the BFF sends none (it sends `Name ?? ""`). */
export const INVOICE_FILE_FALLBACK_NAME = 'Invoice file';

/** The name shown for a file: its `fileName`, else {@link INVOICE_FILE_FALLBACK_NAME}. */
export function invoiceFileDisplayName(file: Pick<InvoiceFileItem, 'fileName'>): string {
  return file.fileName.trim() ? file.fileName : INVOICE_FILE_FALLBACK_NAME;
}

const FILE_EXTENSIONS: Readonly<Record<string, string>> = {
  'application/pdf': 'pdf',
  'image/png': 'png',
  'image/jpeg': 'jpg',
  'image/gif': 'gif',
  'image/webp': 'webp',
};

/** The name a file is saved under: its `fileName`, else "invoice-file" plus an extension from its `contentType` (pdf for application/pdf). */
export function invoiceFileSaveName(file: Pick<InvoiceFileItem, 'fileName' | 'contentType'>): string {
  if (file.fileName.trim()) return file.fileName;
  const type = (file.contentType ?? '').split(';')[0].trim().toLowerCase();
  const extension = FILE_EXTENSIONS[type];
  return extension ? `invoice-file.${extension}` : 'invoice-file';
}

// ---------------------------------------------------------------------------
// Event queries
// ---------------------------------------------------------------------------

/**
 * The record kinds whose events the external plane lists (task 039 / task 029): the `{root}` segment of
 * `/api/v1/external/{root}/{id}/events`. Same row shape on all three; the server applies the record's own rights (and the
 * every-root rule) and the client does no filtering of its own.
 */
export type EventsRoot = Extract<DocumentRoot, 'projects' | 'matters' | 'workassignments'>;

/** What an events section calls the record it belongs to, in its sentences. */
export const EVENTS_ROOT_NOUN: Readonly<Record<EventsRoot, string>> = {
  projects: 'project',
  matters: 'matter',
  workassignments: 'work assignment',
};

/** `/api/v1/external/{root}/{id}/events`: the events regarding one record. */
export function rootEventsPath(root: EventsRoot, recordId: string): string {
  return `/api/v1/external/${root}/${encodeURIComponent(recordId)}/events`;
}

/**
 * Retrieve the Events regarding a project, matter or work assignment (task 057; the project route is the original).
 *
 * `sprk_event`'s project lookup is `sprk_regardingproject` (it has no `sprk_projectid` attribute); a matter or work
 * assignment event carries `null` there.
 *
 * NOTE: `getCollection` ignores its options entirely (:291) - select/filter/order/top are all applied server-side by
 * GET /api/v1/external/{root}/{id}/events - so the options are accepted only so existing call sites type-check.
 *
 * @param root      Which kind of record `recordId` is
 * @param recordId  Dataverse GUID of the record
 * @param options   Optional OData query overrides (currently ignored downstream)
 */
export async function getRootEvents(
  root: EventsRoot,
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

/**
 * `GET /api/v1/external/events/{id}` (R4 task 014b, owner Q-7): one event with the rights the BFF decided for this caller and
 * the record it is regarding. Not readable and not found answer the same 404 (`ApiError`). The deep link `?record=event:{id}`
 * reads through this; a row opened from a list does NOT (a multi-parent event can be in a readable list and 404 here).
 */
export async function getEvent(eventId: string): Promise<ODataEventDetail> {
  return bffApiCall<ODataEventDetail>(`/api/v1/external/events/${encodeURIComponent(eventId)}`);
}

/**
 * `GET /api/v1/external/{root}/{id}/assignable-people` (R4 task 014b): the people an event on this record may be assigned to,
 * "Me" flagged. 403 when the caller may neither create nor edit an event there; 503 when unknown (`ApiError`).
 */
export async function getAssignablePeople(root: EventsRoot, recordId: string): Promise<AssignablePerson[]> {
  const response = await bffApiCall<{ value?: AssignablePerson[] }>(
    `/api/v1/external/${root}/${encodeURIComponent(recordId)}/assignable-people`
  );
  return (response.value ?? []).filter(p => typeof p.contactId === 'string' && p.contactId && !!p.name);
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
   * The event's type: REQUIRED, one of the list's `creatableEventTypes` ids (R4 task 014b / 015, owner Q-2). The legacy
   * `eventType: "task"` is no longer sent.
   */
  eventTypeId: string;
  /** Low 100000000, Normal 100000001, High 100000002, Urgent 100000003 (`events/eventFields.ts`). */
  sprk_priority?: number;
  /** Plain text, up to 2,000 characters. */
  sprk_description?: string;
  /** A contact id from `getAssignablePeople` for this record; anyone else is refused (400). */
  assignedToContactId?: string;
  /**
   * OData binding to associate the event with a project. The navigation property is the
   * PascalCase SchemaName sprk_RegardingProject (metadata-verified, R5 task 002 — sprk_event
   * has no "sprk_projectid" lookup; its project lookup is sprk_regardingproject).
   * Example: "/sprk_projects(b1c2d3e4-0000-0000-0000-000000000001)"
   */
  'sprk_RegardingProject@odata.bind'?: string;
}

/**
 * Create an Event regarding a project, matter or work assignment: `POST /api/v1/external/{root}/{id}/events`
 * (spaarke-SPA-external-access-platform-r4 task 006, FR-06; the matter and work-assignment routes are task 002's).
 *
 * The server takes the parent from the ROUTE only (`CreateExternalEventRequest` has no regarding member), so a matter or
 * work-assignment event sends the payload as given. The project route keeps the body it has always been sent, the project
 * bind included (the server skips that member), byte for byte.
 *
 * The returned row has NO formatted names (type, assignee, creator) and no rights, so a list must re-read after a create
 * rather than insert it.
 *
 * @param root      Which kind of record `recordId` is
 * @param recordId  Dataverse GUID of the record
 * @param payload   Event fields to set on creation
 * @returns         The created ODataEvent record (system fields only)
 */
export async function createRootEvent(
  root: EventsRoot,
  recordId: string,
  payload: CreateEventPayload
): Promise<ODataEvent> {
  const body: CreateEventPayload =
    root === 'projects'
      ? {
          ...payload,
          // Bind to the project using the OData navigation property (PascalCase SchemaName).
          'sprk_RegardingProject@odata.bind': `/sprk_projects(${recordId})`,
        }
      : payload;
  return createRecord<CreateEventPayload, ODataEvent>(rootEventsPath(root, recordId), body);
}

/**
 * Create a new Event regarding a Secure Project (`createRootEvent` for the project root).
 *
 * @param projectId  Dataverse GUID of the parent sprk_project record
 * @param payload    Event fields to set on creation
 * @returns          The created ODataEvent record (with system fields populated)
 */
export async function createEvent(projectId: string, payload: CreateEventPayload): Promise<ODataEvent> {
  return createRootEvent('projects', projectId, payload);
}

/**
 * The partial body of `PATCH /api/v1/external/events/{id}` (R4 task 014b / 014c): a member that is not sent is not changed.
 * `eventTypeId` is never sent (the type is immutable: 400 `type_immutable`).
 */
export interface UpdateEventPatch {
  sprk_name?: string;
  /** "yyyy-MM-dd", or `null` to clear it (400 when the event's type requires one). */
  sprk_duedate?: string | null;
  sprk_priority?: number;
  /** `null` or "" clears it. */
  sprk_description?: string | null;
  /** Any event statuscode (`EVENT_STATUS_LABELS`). */
  sprk_status?: number;
  /** A reassign to this contact (from `getAssignablePeople`): sets status Reassigned unless `sprk_status` is also sent. */
  assignedToContactId?: string;
}

/**
 * Edits an event / changes its status: `PATCH /api/v1/external/events/{id}`, 204 with no body. A refusal is an `ApiError`
 * whose message is the ProblemDetails body (`problemMessage`): 400 with a stable `errorCode` per field, 403, 404, 503.
 */
export async function updateEvent(eventId: string, patch: UpdateEventPatch): Promise<void> {
  await bffApiCall<void>(`/api/v1/external/events/${encodeURIComponent(eventId)}`, {
    method: 'PATCH',
    body: JSON.stringify(patch),
  });
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
// Invoice submission (ebilling-r1 task 051, FR-18 / FR-19 / FR-45) - the BFF's task 044 external routes
// ---------------------------------------------------------------------------
//
//   POST /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions
//        multipart: file, firmInvoiceNumber, poNumber; optional ?firmId=   -> 202 { submissionId, jobId, trackingNumber, statusUrl }
//   GET  /api/v1/external/invoice-submissions/{id}                          -> the status view
//   GET  /api/v1/external/invoice-submissions                               -> the caller's firms' submissions (flag probe fallback)
//   GET  /api/v1/external/invoice-submissions/firm-records?firmId=          -> the caller's readable records the firm is linked to (D-106)
//
// The server decides who may submit and for which firm (ADR-002 / FR-17). A caller with one firm names none (the server
// takes it); a caller with several picks one in the wizard's "Choose firm" step (owner, 2026-10-17) and it goes as the
// `firmId` QUERY parameter task 044 reads, which the server still checks against the caller's own firms. The list of
// firms is `firms` on GET /api/v1/external/me. This client creates nothing in Dataverse and shows the server's refusals by code. A refusal is an
// ApiError whose message is the ProblemDetails body; `toInvoiceSubmissionError` adapts it to the shared wizard's error.

/** The external route segment of each core record kind an invoice can be submitted against. */
const INVOICE_SUBMISSION_SEGMENT: Readonly<Record<InvoiceCoreRecordKind, string>> = {
  matter: 'matters',
  project: 'projects',
  workassignment: 'workassignments',
};

/** Adapts a failed BFF call (an ApiError carrying a ProblemDetails body, or anything else) for the shared wizard. */
export function toInvoiceSubmissionError(err: unknown): InvoiceSubmissionError {
  if (err instanceof InvoiceSubmissionError) return err;
  if (err instanceof ApiError) {
    let detail: string | undefined;
    let errorCode: string | undefined;
    try {
      const body = JSON.parse(err.message) as { detail?: unknown; errorCode?: unknown };
      if (typeof body.detail === 'string' && body.detail.trim()) detail = body.detail;
      if (typeof body.errorCode === 'string' && body.errorCode) errorCode = body.errorCode;
    } catch {
      /* not a ProblemDetails body: the status alone is used */
    }
    return new InvoiceSubmissionError({ statusCode: err.statusCode, errorCode, detail });
  }
  // No response (a network failure, a sign-in that could not start): the request may not have reached the server.
  return new InvoiceSubmissionError({ message: err instanceof Error ? err.message : undefined });
}

/** Submits one PDF invoice against a core record. Rejects with an {@link InvoiceSubmissionError}. */
export async function submitInvoice(input: InvoiceSubmissionInput): Promise<InvoiceSubmissionAccepted> {
  if (!GUID_RE.test(input.record.recordId)) {
    throw new InvoiceSubmissionError({ message: 'Invalid record id.' });
  }
  if (input.firmId !== undefined && !GUID_RE.test(input.firmId)) {
    throw new InvoiceSubmissionError({ message: 'Invalid firm id.' });
  }
  const form = new FormData();
  form.append('file', input.file);
  form.append('firmInvoiceNumber', input.firmInvoiceNumber);
  if (input.poNumber) form.append('poNumber', input.poNumber);
  // Only a firm the user chose is named (a several-firm caller); a single-firm caller sends nothing and the server uses it.
  const query = input.firmId ? `?firmId=${encodeURIComponent(input.firmId)}` : '';
  try {
    // No Content-Type: the browser sets multipart/form-data with its boundary (executeFetch drops the JSON default).
    return await bffApiCall<InvoiceSubmissionAccepted>(
      `/api/v1/external/${INVOICE_SUBMISSION_SEGMENT[input.record.kind]}/${input.record.recordId}/invoice-submissions${query}`,
      { method: 'POST', body: form, headers: {} }
    );
  } catch (err) {
    throw toInvoiceSubmissionError(err);
  }
}

/**
 * The firms the signed-in user can submit invoices for: `firms` on GET /api/v1/external/me (only the caller's own).
 * `null` is UNKNOWN (an older BFF, eBilling off, a failed firm read), which is not "no firm" (`[]`): the wizard shows no
 * firm step for either, but on a `firm_required` answer it asks again and inserts the step when the caller now has several.
 */
export async function listInvoiceFirms(): Promise<InvoiceFirmOption[] | null> {
  const me = await getExternalUserContext();
  if (me.firms == null) return null;
  return me.firms.filter(f => GUID_RE.test(f.firmId)).map(f => ({ firmId: f.firmId, name: f.name }));
}

/** Reads one submission's current status. Rejects with an {@link InvoiceSubmissionError}. */
export async function getInvoiceSubmission(submissionId: string): Promise<InvoiceSubmissionStatusView> {
  try {
    return await bffApiCall<InvoiceSubmissionStatusView>(
      `/api/v1/external/invoice-submissions/${encodeURIComponent(submissionId)}`
    );
  } catch (err) {
    throw toInvoiceSubmissionError(err);
  }
}

/**
 * Whether invoice submission is on. `firms` on GET /api/v1/external/me is the cheap signal: the BFF sends it only when
 * eBilling is on (absent or null otherwise, and for an older BFF), so a list is `available` without another call. Only
 * when `firms` is absent does this fall back to the one route every caller may read, the caller's own submissions list,
 * to tell an off flag apart from an unknown state: 503 with the `ebilling.disabled` code is the only "off" answer (FR-45). Any other failure
 * is `unknown`: an outage or a refusal must not read as "off", so the entry stays and the server answers when it is used.
 */
export async function probeInvoiceSubmission(): Promise<'available' | 'disabled' | 'unknown'> {
  try {
    const me = await getExternalUserContext();
    if (me.firms != null) return 'available';
  } catch {
    /* /me failed: the list read below decides (and is 'unknown' when it fails too) */
  }
  try {
    await bffApiCall<InvoiceSubmissionStatusView[]>('/api/v1/external/invoice-submissions');
    return 'available';
  } catch (err) {
    return isEbillingDisabledError(toInvoiceSubmissionError(err)) ? 'disabled' : 'unknown';
  }
}

/** Rows asked of each core-record read; a longer list is flagged truncated so the picker says so. */
const INVOICE_RECORD_PAGE = 200;

/**
 * The columns each Tier-2 module read admits for these roots (ExternalAccessModule.cs ReadableColumns): id, name and
 * number only. A column outside that list is refused with a 400 before the query runs.
 */
const INVOICE_RECORD_READS = [
  { kind: 'matter', entity: 'sprk_matter', id: 'sprk_matterid', name: 'sprk_mattername', number: 'sprk_matternumber' },
  {
    kind: 'workassignment',
    entity: 'sprk_workassignment',
    id: 'sprk_workassignmentid',
    name: 'sprk_name',
    number: 'sprk_workassignmentnumber',
  },
] as const;

/** The body of GET /api/v1/external/invoice-submissions/firm-records (task 051 / D-106): the caller's readable records the firm is linked to. */
export interface InvoiceFirmRecordsResponse {
  firmId: string;
  matters?: string[];
  projects?: string[];
  workAssignments?: string[];
  partial?: boolean;
  /** The BFF cut the list at its bound (too many readable records to examine, or more linked records than it returns). */
  truncated?: boolean;
}

const sameId = (a: string, b: string): boolean => a.toLowerCase() === b.toLowerCase();

/**
 * One firm's firm-records answer: the caller's readable records the firm is linked to (a single read, not paged; the BFF
 * flags a cut-short answer `truncated` and a partly failed one `partial`). Rejects with an {@link InvoiceSubmissionError}.
 * Used by {@link listInvoiceCoreRecords} (the wizard's Record step) and by the record view's "Submit invoice" rule
 * (R4 task 007, FR-10).
 */
export async function readInvoiceFirmRecords(firmId: string): Promise<InvoiceFirmRecordsResponse> {
  if (!GUID_RE.test(firmId)) {
    throw new InvoiceSubmissionError({ message: 'Invalid firm id.' });
  }
  try {
    return await bffApiCall<InvoiceFirmRecordsResponse>(
      `/api/v1/external/invoice-submissions/firm-records?firmId=${encodeURIComponent(firmId)}`
    );
  } catch (err) {
    throw toInvoiceSubmissionError(err);
  }
}

/**
 * The core records the caller can pick for an invoice: their accessible projects (the external projects route) plus
 * matters and work assignments (the Tier-2-scoped module read). The server re-checks Read and Create on the chosen
 * record, and that the firm is linked to it (D-106), when the invoice is submitted.
 *
 * With `query.firmId` the list is narrowed to the records linked to that firm: the BFF's firm-records route names the
 * caller's own readable records the firm is linked to, and the three reads are filtered to them (the Tier-2 module read
 * is allow-listed, so the link cannot be asked for there). A failed firm-records read throws (the picker then shows the
 * failure, never an unfiltered list the server would refuse). If one record source fails the others are still offered,
 * flagged `partial`; `truncated` is the row cap (200), `moreRecords`, or the firm-records route's own bound, any of which can hide a linked record. If every source
 * fails the first failure is thrown.
 */
export async function listInvoiceCoreRecords(query: InvoiceRecordQuery = {}): Promise<InvoiceCoreRecordList> {
  const firmId = query.firmId;
  if (firmId !== undefined && !GUID_RE.test(firmId)) {
    throw new InvoiceSubmissionError({ message: 'Invalid firm id.' });
  }
  const linkedRead: Promise<InvoiceFirmRecordsResponse | null> = firmId
    ? readInvoiceFirmRecords(firmId)
    : Promise.resolve(null);

  const [linked, reads] = await Promise.all([
    linkedRead,
    Promise.allSettled<InvoiceCoreRecordList>([
      getProjects().then(list => ({
        truncated: list.truncated,
        records: list.items.map<InvoiceCoreRecordOption>(p => ({
          kind: 'project',
          recordId: p.sprk_projectid,
          name: p.sprk_name,
          number: p.sprk_referencenumber,
        })),
      })),
      ...INVOICE_RECORD_READS.map(async read => {
        const fetchXml =
          `<fetch count="${INVOICE_RECORD_PAGE}"><entity name="${read.entity}">` +
          `<attribute name="${read.id}"/><attribute name="${read.name}"/><attribute name="${read.number}"/>` +
          `<order attribute="${read.number}"/></entity></fetch>`;
        const result = await gridDataverseClient.retrieveMultipleRecords<Record<string, unknown>>(
          read.entity,
          fetchXml
        );
        return {
          truncated: result.moreRecords,
          records: result.entities
            .filter(row => typeof row[read.id] === 'string')
            .map<InvoiceCoreRecordOption>(row => ({
              kind: read.kind,
              recordId: String(row[read.id]),
              name: String(row[read.name] ?? row[read.number] ?? ''),
              number: row[read.number] == null ? null : String(row[read.number]),
            })),
        };
      }),
    ]),
  ]);

  const fulfilled = reads.filter((r): r is PromiseFulfilledResult<InvoiceCoreRecordList> => r.status === 'fulfilled');
  if (fulfilled.length === 0) {
    const first = reads.find((r): r is PromiseRejectedResult => r.status === 'rejected');
    throw toInvoiceSubmissionError(first?.reason);
  }

  const linkedIds: Readonly<Record<InvoiceCoreRecordKind, readonly string[]>> | null = linked
    ? { matter: linked.matters ?? [], project: linked.projects ?? [], workassignment: linked.workAssignments ?? [] }
    : null;
  const all = fulfilled.flatMap(r => r.value.records);
  const records = linkedIds ? all.filter(r => linkedIds[r.kind].some(id => sameId(id, r.recordId))) : all;

  return {
    records: records.sort((a, b) => a.name.localeCompare(b.name)),
    truncated: fulfilled.some(r => r.value.truncated === true) || linked?.truncated === true,
    partial: fulfilled.length < reads.length || linked?.partial === true,
  };
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
  getRecordDocuments,
  deleteOwnDocument,
  // Events
  getEvents,
  getRootEvents,
  createEvent,
  getEvent,
  updateEvent,
  getAssignablePeople,
  deleteOwnEvent,
  deleteOwnServiceRequest,
  // Contacts
  getContacts,
  // Organizations
  getOrganizations,
  // Query utilities (for advanced usage)
  buildQueryString,
} as const;
