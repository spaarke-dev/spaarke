/**
 * communicationTimelineApi.ts
 *
 * Typed client wrapper around the polling-timeline read endpoints shipped by
 * task 050 (`GET /api/communications/threads/{threadId}/messages` and
 * `GET /api/communications/threads/{threadId}/unread-count`) plus a thin
 * `sendTimelineMessage` delegate over the existing `sendCommunication()`
 * send path (task 051). Consumed by `<CommunicationTimeline />` (task 060).
 * R2 task 020 (FR-03) adds `readByRegarding` — the `GET /api/communications/
 * by-regarding/{entityType}/{id}` wrapper backing regarding mode.
 *
 * Read-model shapes below mirror the BFF's `ThreadReadResult` /
 * `ThreadMessageDto` / `ThreadAttachmentRef` / `UnreadCountResult` /
 * `RegardingReadResult` records
 * (`Sprk.Bff.Api/Services/Communication/CommunicationThreadReadModels.cs`)
 * EXACTLY, field-for-field, camelCase (ASP.NET default STJ casing). Do not
 * add fields the BFF does not return — this file is a pass-through, not a
 * translation layer (mapping into the timeline's own domain shape happens in
 * `CommunicationTimeline.buildTimeline.ts`, not here).
 *
 * `sprk_communicationtype` / `sprk_bodyformat` are Dataverse choice ints —
 * confirmed against `Sprk.Bff.Api/Services/Communication/Models/{CommunicationType,BodyFormat}.cs`:
 *   - CommunicationType: Email = 100000000, TeamsMessage = 100000001,
 *     SMS = 100000002, Notification = 100000003, Message = 100000004.
 *   - BodyFormat: PlainText = 100000000, HTML = 100000001.
 * NOTE these are plain `int?` on `ThreadMessageDto` (NOT the
 * `JsonStringEnumConverter`-serialized enum used elsewhere in the BFF, e.g.
 * `SendCommunicationRequest.BodyFormat`) — the wire value is a raw number,
 * not a string like `"HTML"`.
 *
 * Constraints (NFR-04 / ADR-028):
 *   - NO `@azure/communication-*` import — reads persisted `sprk_communication`
 *     rows via the BFF only, never ACS directly.
 *   - NO `@spaarke/auth` import — `authenticatedFetch` is injected exactly as
 *     `communicationApi.ts` does.
 */

import type { AuthenticatedFetchFn } from './EntityCreationService';
import {
  sendCommunication,
  type SendCommunicationOptions,
  type SendCommunicationResult,
  type ICommunicationApiClientOptions,
} from './communicationApi';

// Re-export so callers of this module don't need a second import from
// `communicationApi` just to get the send-side error type.
export { SendCommunicationError } from './communicationApi';

// ---------------------------------------------------------------------------
// Read-model DTOs (mirror the BFF records field-for-field)
// ---------------------------------------------------------------------------

/** Mirrors `ThreadAttachmentRef` — points at the governed `sprk_document`, never the binary. */
export interface IThreadAttachmentRefDto {
  communicationAttachmentId: string;
  documentId: string | null;
  fileName: string | null;
  attachmentType: number | null;
}

/** Mirrors `ThreadMessageDto` — one `sprk_communication` row projected for the timeline. */
export interface IThreadMessageDto {
  messageId: string;
  body: string | null;
  bodyFormat: number | null;
  communicationType: number | null;
  from: string | null;
  sentAt: string | null;
  createdOn: string | null;
  inReplyTo: string | null;
  privilege: number;
  attachments: IThreadAttachmentRefDto[];
}

/**
 * Mirrors `ThreadReadResult` — the access-filtered, ordered message page.
 * `name` (`sprk_name`) is populated by the by-regarding read (R2 task 020,
 * FR-03 — the collapsible group header label) and `null` on the R1 per-thread
 * read (`readThread` below never needed it — that surface is placed directly
 * on the thread form, which already shows the name via the record header).
 */
export interface IThreadReadResultDto {
  threadId: string;
  name: string | null;
  messages: IThreadMessageDto[];
  count: number;
}

/** Mirrors `UnreadCountResult`. */
export interface IUnreadCountResultDto {
  threadId: string;
  since: string | null;
  unreadCount: number;
}

/**
 * Mirrors `RegardingReadResult` (R2 task 010 / FR-01) — ALL of a regarding
 * record's threads, each in the `IThreadReadResultDto` shape above (entity-
 * set-agnostic across the 11 ADR-024 families; see
 * `Sprk.Bff.Api/Services/Communication/CommunicationThreadReadModels.cs`).
 */
export interface IRegardingReadResultDto {
  entityType: string;
  recordId: string;
  threads: IThreadReadResultDto[];
  threadCount: number;
  messageCount: number;
}

// ---------------------------------------------------------------------------
// Client options + shared error type
// ---------------------------------------------------------------------------

/** Dependencies for the wrapper (kept explicit for testability — mirrors `communicationApi.ts`). */
export interface ICommunicationTimelineApiClientOptions {
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
 * Typed error thrown by {@link readThread} / {@link getUnreadCount} on any
 * non-2xx response. Parses the BFF's RFC 7807 ProblemDetails body (ADR-019),
 * mirroring `SendCommunicationError`'s shape/parse behavior so callers can
 * branch on `status`/`code` uniformly across the timeline's read + send paths.
 */
export class CommunicationTimelineReadError extends Error {
  readonly status: number;
  readonly code: string;
  readonly detail: string;
  readonly correlationId?: string;

  constructor(status: number, code: string, detail: string, correlationId?: string) {
    super(`communicationTimelineApi read failed (${status}): ${detail}`);
    this.name = 'CommunicationTimelineReadError';
    this.status = status;
    this.code = code;
    this.detail = detail;
    this.correlationId = correlationId;
    Object.setPrototypeOf(this, CommunicationTimelineReadError.prototype);
  }

  static async fromResponse(response: Response): Promise<CommunicationTimelineReadError> {
    const status = response.status;
    try {
      const ct = response.headers.get('content-type') ?? '';
      if (ct.includes('application/problem+json') || ct.includes('application/json')) {
        const body = (await response.json()) as IProblemDetailsBody;
        const code = body.errorCode ?? body.code ?? `HTTP_${status}`;
        const detail = body.detail ?? body.title ?? `HTTP ${status}`;
        const correlationId = body.correlationId ?? body.traceId ?? body.instance;
        return new CommunicationTimelineReadError(status, code, detail, correlationId);
      }
      const text = await response.text();
      return new CommunicationTimelineReadError(status, `HTTP_${status}`, text || `HTTP ${status}`);
    } catch {
      return new CommunicationTimelineReadError(status, `HTTP_${status}`, `HTTP ${status}`);
    }
  }
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

function resolveThreadUrl(base: string | undefined, threadId: string, suffix: string, params: URLSearchParams): string {
  const path = `/api/communications/threads/${encodeURIComponent(threadId)}/${suffix}`;
  const query = params.toString();
  const fullPath = query ? `${path}?${query}` : path;
  if (!base) return fullPath;
  return base.replace(/\/+$/, '') + fullPath;
}

// ---------------------------------------------------------------------------
// readThread — GET /api/communications/threads/{threadId}/messages
// ---------------------------------------------------------------------------

export interface IReadThreadOptions {
  /** ISO 8601. Omit for a full load; pass the newest rendered `createdOn` for incremental polls. */
  since?: string;
  /** Page size. Server default 100, cap 200. */
  top?: number;
}

export async function readThread(
  threadId: string,
  options: IReadThreadOptions,
  client: ICommunicationTimelineApiClientOptions
): Promise<IThreadReadResultDto> {
  if (!threadId) {
    throw new Error('readThread: threadId is required.');
  }
  if (!client.authenticatedFetch) {
    throw new Error('readThread: authenticatedFetch is required.');
  }

  const params = new URLSearchParams();
  if (options.since) params.set('since', options.since);
  if (options.top) params.set('top', String(options.top));

  const url = resolveThreadUrl(client.bffBaseUrl, threadId, 'messages', params);
  const response = await client.authenticatedFetch(url, { method: 'GET' });

  if (!response.ok) {
    throw await CommunicationTimelineReadError.fromResponse(response);
  }

  return (await response.json()) as IThreadReadResultDto;
}

// ---------------------------------------------------------------------------
// getUnreadCount — GET /api/communications/threads/{threadId}/unread-count
// ---------------------------------------------------------------------------

export interface IGetUnreadCountOptions {
  /** ISO 8601 last-seen marker. Omitted = count against the whole thread. */
  since?: string;
}

export async function getUnreadCount(
  threadId: string,
  options: IGetUnreadCountOptions,
  client: ICommunicationTimelineApiClientOptions
): Promise<IUnreadCountResultDto> {
  if (!threadId) {
    throw new Error('getUnreadCount: threadId is required.');
  }
  if (!client.authenticatedFetch) {
    throw new Error('getUnreadCount: authenticatedFetch is required.');
  }

  const params = new URLSearchParams();
  if (options.since) params.set('since', options.since);

  const url = resolveThreadUrl(client.bffBaseUrl, threadId, 'unread-count', params);
  const response = await client.authenticatedFetch(url, { method: 'GET' });

  if (!response.ok) {
    throw await CommunicationTimelineReadError.fromResponse(response);
  }

  return (await response.json()) as IUnreadCountResultDto;
}

// ---------------------------------------------------------------------------
// readByRegarding — GET /api/communications/by-regarding/{entityType}/{id}
// (R2 task 020 / FR-03 — the regarding-mode Timeline's read wrapper)
// ---------------------------------------------------------------------------

function resolveByRegardingUrl(base: string | undefined, entityType: string, id: string): string {
  const path = `/api/communications/by-regarding/${encodeURIComponent(entityType)}/${encodeURIComponent(id)}`;
  if (!base) return path;
  return base.replace(/\/+$/, '') + path;
}

/**
 * Reads ALL of a regarding record's threads + their access-filtered messages
 * (task 010's `by-regarding` endpoint) — the read model behind the regarding-
 * mode `<CommunicationTimeline mode="regarding" />` (task 020). Same
 * typed-error contract as {@link readThread}/{@link getUnreadCount}: throws
 * {@link CommunicationTimelineReadError} on any non-2xx response.
 *
 * NFR-03: the response is already access-filtered server-side (impersonation
 * + the shared internal-only/privilege filter) — this wrapper is a thin
 * pass-through, exactly like {@link readThread}. It performs NO client-side
 * filtering of the result.
 */
export async function readByRegarding(
  entityType: string,
  id: string,
  client: ICommunicationTimelineApiClientOptions
): Promise<IRegardingReadResultDto> {
  if (!entityType) {
    throw new Error('readByRegarding: entityType is required.');
  }
  if (!id) {
    throw new Error('readByRegarding: id is required.');
  }
  if (!client.authenticatedFetch) {
    throw new Error('readByRegarding: authenticatedFetch is required.');
  }

  const url = resolveByRegardingUrl(client.bffBaseUrl, entityType, id);
  const response = await client.authenticatedFetch(url, { method: 'GET' });

  if (!response.ok) {
    throw await CommunicationTimelineReadError.fromResponse(response);
  }

  return (await response.json()) as IRegardingReadResultDto;
}

// ---------------------------------------------------------------------------
// sendTimelineMessage — reuses sendCommunication() (task 051's POST /send)
// ---------------------------------------------------------------------------

/**
 * Sends a new message into the thread via the SAME `POST /api/communications/send`
 * path `<EmailComposer/>` uses — this is a pass-through delegate, not a new send
 * contract (task 060 constraint: "Do NOT invent a new send contract"). Throws
 * {@link SendCommunicationError} on failure exactly as `sendCommunication()` does.
 *
 * GAP CLOSED (task 062, FR-12 prerequisite): `SendCommunicationOptions` now carries
 * `communicationType` / `threadId` / `inReplyToMessageId` (additive — email send is
 * unchanged when omitted). Callers that want a THREADED Message send (the
 * send/respond accessory PCF's "respond into the current thread" action) pass
 * `communicationType: 'message'` + `threadId: <target sprk_communicationthread id>`.
 * This timeline component's OWN compose box (`<CommunicationTimeline/>` / task 060)
 * still calls `sendTimelineMessage` without those fields, so its replies remain
 * Email-typed/unthreaded by default — extending the timeline's own reply UX to
 * default to a threaded Message send is a follow-up, not part of this closure.
 */
export async function sendTimelineMessage(
  options: SendCommunicationOptions,
  client: ICommunicationApiClientOptions
): Promise<SendCommunicationResult> {
  return sendCommunication(options, client);
}
