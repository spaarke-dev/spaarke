/**
 * communicationThreadListApi.ts
 *
 * Typed client wrapper around the FR-16 thread-list read endpoint
 * (`GET /api/communications/threads`, R3 task 003) — the workspace/code-page
 * left-pane's "list ALL of the caller's threads, incl. record-less Direct
 * threads" surface (Success Criterion 5) — plus a record-scoped listing helper
 * built on the already-shipped `by-regarding` endpoint (R2 task 010).
 *
 * Consumed by `<ConversationWorkspace />` (task 012, FR-01/10). This file is
 * a NEW, separate service module — it intentionally does NOT extend
 * `communicationTimelineApi.ts` (owned by a concurrent task in this project;
 * see that file's header for the polling-timeline read/send wrappers this
 * module does not duplicate).
 *
 * Read-model shapes below mirror the BFF's `ThreadListItem` / `ThreadListResult`
 * records (`Sprk.Bff.Api/Api/CommunicationEndpoints.cs` +
 * `Sprk.Bff.Api/Services/Communication/CommunicationThreadReadModels.cs`)
 * EXACTLY, field-for-field, camelCase (ASP.NET default STJ casing). This file
 * is a pass-through, not a translation layer.
 *
 * --- FR-16 does NOT support a regarding filter (documented gap, task 012) ---
 * `GET /api/communications/threads` is DELIBERATELY not scoped to any
 * `sprk_regarding{type}` lookup (see the BFF route doc-comment: "NOT scoped to
 * any regarding lookup (so Direct/record-less threads are included)") — this
 * is what lets record-less Direct threads appear in the all-threads list. It
 * has no `entityType`/`id`/`regarding` query parameter at all. The task 012
 * POML's constraint text ("In record mode, pass the regarding filter so only
 * that record's threads return") therefore cannot be satisfied by adding a
 * parameter to THIS endpoint — task 003 (same project) shipped it without one,
 * on purpose.
 *
 * Resolution (documented deviation, NOT a client-side access filter — NFR-01
 * is fully preserved): record mode routes to the EXISTING, already-shipped,
 * server-access-filtered `GET /api/communications/by-regarding/{entityType}/{id}`
 * endpoint (R2 task 010) instead. That endpoint returns ALL of a regarding
 * record's threads (with full messages, since it also backs the regarding-mode
 * Timeline) — {@link listThreadsByRegarding} below adapts its `threads[]` into
 * the SAME `IThreadListItemDto`-shaped rows the all-mode list uses, so
 * `<ConversationWorkspace />` can render both modes through one row shape. No
 * new backend endpoint, no client-side membership computation — both paths are
 * 100% server-filtered; this file only chooses WHICH existing server-filtered
 * endpoint to call based on whether a `regarding` scope was supplied.
 *
 * Escalation trigger note (task 012 POML `<escalation>`): this is the
 * documented resolution, not a silent bypass — see
 * `projects/messaging-communication-app-r3/notes/task-012-notes.md` for the
 * full ADR-Conflict-Resolution-Protocol-style writeup (CLAUDE.md §6.5, Path A
 * — project-scoped exception; the FR-16 contract truly cannot express the
 * filter, and a clean, already-access-filtered alternative already exists).
 *
 * Constraints (NFR-01 / ADR-028):
 *   - `authenticatedFetch` is injected (never imports `@spaarke/auth`).
 *   - No client-side membership union or inference of threads the server did
 *     not return. The word-filter narrows only the rows a server call already
 *     returned (see {@link IThreadListApiClientOptions} callers) — it never
 *     widens or reconstructs visibility.
 */

import type { AuthenticatedFetchFn } from './EntityCreationService';

// ---------------------------------------------------------------------------
// Read-model DTOs (mirror the BFF records field-for-field)
// ---------------------------------------------------------------------------

/** Mirrors `ThreadListItem` (`GET /api/communications/threads` row shape). */
export interface IThreadListItemDto {
  threadId: string;
  name: string | null;
  /** `sprk_threadtype`: Record-Anchored = 100000000, Direct 1:1 = 100000001. */
  threadType: number | null;
  createdOn: string | null;
}

/** Mirrors `ThreadListResult` (`GET /api/communications/threads` response). */
export interface IThreadListResultDto {
  threads: IThreadListItemDto[];
  count: number;
  /** Opaque composite keyset cursor; null when there is no further page. */
  nextPageToken: string | null;
  hasMore: boolean;
}

/** Mirrors `UnreadCountResult` (`GET /threads/{threadId}/unread-count`). */
export interface IThreadListUnreadCountDto {
  threadId: string;
  since: string | null;
  unreadCount: number;
}

// ---------------------------------------------------------------------------
// Client options + shared error type
// ---------------------------------------------------------------------------

export interface IThreadListApiClientOptions {
  /** Authenticated fetch (Bearer-attached). Required. */
  authenticatedFetch: AuthenticatedFetchFn;
  /**
   * Optional base URL. When omitted, the URL is sent as a relative path and
   * the supplied fetch is expected to resolve it (as `@spaarke/auth`'s
   * `authenticatedFetch` does).
   */
  bffBaseUrl?: string;
}

interface IProblemDetailsBody {
  type?: string;
  title?: string;
  detail?: string;
  status?: number;
  instance?: string;
  errorCode?: string;
  code?: string;
  correlationId?: string;
  traceId?: string;
}

/**
 * Typed error thrown by every export in this module on any non-2xx response.
 * Parses the BFF's RFC 7807 ProblemDetails body (ADR-019), mirroring the
 * shape/parse behavior of the timeline's own read error class so callers can
 * branch on `status`/`code` uniformly.
 */
export class CommunicationThreadListError extends Error {
  readonly status: number;
  readonly code: string;
  readonly detail: string;
  readonly correlationId?: string;

  constructor(status: number, code: string, detail: string, correlationId?: string) {
    super(`communicationThreadListApi read failed (${status}): ${detail}`);
    this.name = 'CommunicationThreadListError';
    this.status = status;
    this.code = code;
    this.detail = detail;
    this.correlationId = correlationId;
    Object.setPrototypeOf(this, CommunicationThreadListError.prototype);
  }

  static async fromResponse(response: Response): Promise<CommunicationThreadListError> {
    const status = response.status;
    try {
      const ct = response.headers.get('content-type') ?? '';
      if (ct.includes('application/problem+json') || ct.includes('application/json')) {
        const body = (await response.json()) as IProblemDetailsBody;
        const code = body.errorCode ?? body.code ?? `HTTP_${status}`;
        const detail = body.detail ?? body.title ?? `HTTP ${status}`;
        const correlationId = body.correlationId ?? body.traceId ?? body.instance;
        return new CommunicationThreadListError(status, code, detail, correlationId);
      }
      const text = await response.text();
      return new CommunicationThreadListError(status, `HTTP_${status}`, text || `HTTP ${status}`);
    } catch {
      return new CommunicationThreadListError(status, `HTTP_${status}`, `HTTP ${status}`);
    }
  }
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

function resolveUrl(base: string | undefined, path: string, params?: URLSearchParams): string {
  const query = params?.toString();
  const fullPath = query ? `${path}?${query}` : path;
  if (!base) return fullPath;
  return base.replace(/\/+$/, '') + fullPath;
}

// ---------------------------------------------------------------------------
// listThreads — GET /api/communications/threads (FR-16, all-mode)
// ---------------------------------------------------------------------------

export interface IListThreadsOptions {
  /** Name filter — `contains(sprk_name, …)` server-side. */
  search?: string;
  /** Page size. Server default 50, cap 200. */
  top?: number;
  /** Opaque composite keyset cursor from a previous page's `nextPageToken`. */
  pageToken?: string;
}

/**
 * Lists ALL threads the caller may see, including record-less Direct threads
 * (FR-16 / Success Criterion 5). Impersonated + access-filtered server-side —
 * this wrapper performs no client-side filtering of the result set.
 */
export async function listThreads(
  options: IListThreadsOptions,
  client: IThreadListApiClientOptions
): Promise<IThreadListResultDto> {
  if (!client.authenticatedFetch) {
    throw new Error('listThreads: authenticatedFetch is required.');
  }

  const params = new URLSearchParams();
  if (options.search) params.set('search', options.search);
  if (options.top) params.set('top', String(options.top));
  if (options.pageToken) params.set('pageToken', options.pageToken);

  const url = resolveUrl(client.bffBaseUrl, '/api/communications/threads', params);
  const response = await client.authenticatedFetch(url, { method: 'GET' });

  if (!response.ok) {
    throw await CommunicationThreadListError.fromResponse(response);
  }

  return (await response.json()) as IThreadListResultDto;
}

// ---------------------------------------------------------------------------
// listThreadsByRegarding — adapts GET /api/communications/by-regarding/{entityType}/{id}
// (R2 task 010) into the SAME IThreadListItemDto[] row shape as listThreads,
// for record mode (see module header "FR-16 does NOT support a regarding
// filter" note above for why this endpoint is used instead of a `regarding`
// query param on /threads).
// ---------------------------------------------------------------------------

/** Minimal shape this module reads off the `by-regarding` response — a subset of `RegardingReadResult`. */
interface IRegardingReadResultForListDto {
  entityType: string;
  recordId: string;
  threads: Array<{ threadId: string; name: string | null }>;
  threadCount: number;
}

function resolveByRegardingUrl(base: string | undefined, entityType: string, id: string): string {
  const path = `/api/communications/by-regarding/${encodeURIComponent(entityType)}/${encodeURIComponent(id)}`;
  return resolveUrl(base, path);
}

/**
 * Lists ONLY the given regarding record's threads (record mode). Delegates to
 * the existing, already-access-filtered `by-regarding` endpoint and maps its
 * `threads[]` into `IThreadListItemDto` rows (`threadType`/`createdOn` are not
 * projected by that endpoint, so both are `null` here — the left-pane list
 * only renders name + unread indicator, so neither field is needed for
 * display; ordering follows whatever order the server returned).
 */
export async function listThreadsByRegarding(
  entityType: string,
  id: string,
  client: IThreadListApiClientOptions
): Promise<IThreadListResultDto> {
  if (!entityType) {
    throw new Error('listThreadsByRegarding: entityType is required.');
  }
  if (!id) {
    throw new Error('listThreadsByRegarding: id is required.');
  }
  if (!client.authenticatedFetch) {
    throw new Error('listThreadsByRegarding: authenticatedFetch is required.');
  }

  const url = resolveByRegardingUrl(client.bffBaseUrl, entityType, id);
  const response = await client.authenticatedFetch(url, { method: 'GET' });

  if (!response.ok) {
    throw await CommunicationThreadListError.fromResponse(response);
  }

  const body = (await response.json()) as IRegardingReadResultForListDto;
  const threads: IThreadListItemDto[] = body.threads.map(t => ({
    threadId: t.threadId,
    name: t.name,
    threadType: null,
    createdOn: null,
  }));

  return {
    threads,
    count: body.threadCount,
    nextPageToken: null,
    hasMore: false,
  };
}

// ---------------------------------------------------------------------------
// getThreadUnreadCount — GET /threads/{threadId}/unread-count
// ---------------------------------------------------------------------------
//
// The list-pane per-row unread signal (task 012 constraint: "rows = name +
// UnreadIndicator ONLY"). KNOWN LIMITATION: `ThreadListItem`/`by-regarding`
// project no unread signal of their own, and there is no persisted per-user
// "last seen this thread" watermark endpoint — `since` is a purely client-side,
// per-mount concept owned by the open-thread timeline (see
// `communicationTimelineApi.ts` / `CommunicationTimeline`'s `ADVANCE_LAST_SEEN`
// reducer action). Calling this endpoint with `since` omitted therefore
// returns the READABLE MESSAGE COUNT for the whole thread (server doc: "omitted
// = all"), not a true post-last-visit delta. This is still a fully
// server-computed, access-filtered signal (never a client guess) — just a
// coarser one than a persisted watermark would give. Logged as a follow-up in
// `notes/defer-issues.md` (persisted last-seen watermark is out of scope here).
// ---------------------------------------------------------------------------

function resolveThreadUnreadCountUrl(base: string | undefined, threadId: string): string {
  const path = `/api/communications/threads/${encodeURIComponent(threadId)}/unread-count`;
  return resolveUrl(base, path);
}

export async function getThreadUnreadCount(
  threadId: string,
  client: IThreadListApiClientOptions
): Promise<IThreadListUnreadCountDto> {
  if (!threadId) {
    throw new Error('getThreadUnreadCount: threadId is required.');
  }
  if (!client.authenticatedFetch) {
    throw new Error('getThreadUnreadCount: authenticatedFetch is required.');
  }

  const url = resolveThreadUnreadCountUrl(client.bffBaseUrl, threadId);
  const response = await client.authenticatedFetch(url, { method: 'GET' });

  if (!response.ok) {
    throw await CommunicationThreadListError.fromResponse(response);
  }

  return (await response.json()) as IThreadListUnreadCountDto;
}
