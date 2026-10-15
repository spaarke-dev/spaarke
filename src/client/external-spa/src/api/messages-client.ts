/**
 * messages-client — the record message thread over the external plane
 * (spaarke-SPA-external-access-platform-r3 task 051, FR-04 read / FR-11 post).
 *
 *   GET  /api/v1/external/{root}/{id}/messages   the thread, oldest first (task 033)
 *   POST /api/v1/external/{root}/{id}/messages   post as the caller (task 034)
 *
 * Both go through `bffApiCall` (active backend + active token acquirer), so no BFF URL or scope ever comes from a link.
 *
 * CONTRACT (read from `ExternalMessageEndpoints.cs` / `ExternalMessageDtos.cs`, not from docs):
 *   - `{root}` is one of `MESSAGE_ROOTS` (the endpoint's `Roots` table). Any other root — documents included — is a 404
 *     (`EXT_MESSAGES_ROOT_NOT_SUPPORTED`), so the UI never offers a thread for it.
 *   - The POST body is `{ sprk_body }` only: plain text, 1..10,000 characters. A property that names an attachment is
 *     refused 400 (`EXT_MESSAGES_ATTACHMENTS_NOT_SUPPORTED`), so this client has no attachment parameter at all.
 *   - The sender is the resolved caller, never client input; the 201 body has a null `sendername` and a server-time
 *     `createdon`, so a caller re-reads the thread instead of appending it (review 034 K3).
 *   - Errors are ProblemDetails: `errorCode` on 400/404/500/502, `reasonCode` on 403; 429 is the per-user write throttle.
 */
import { bffApiCall } from '../auth/bff-client';
import { ApiError } from '../types';

/** Dataverse entity → the `{root}` route segment (the endpoint's `Roots` table; `ExternalMessageEndpoints.cs`). */
export const MESSAGE_ROOTS: Readonly<Record<string, string>> = {
  sprk_project: 'projects',
  sprk_matter: 'matters',
  sprk_workassignment: 'workassignments',
  sprk_invoice: 'invoices',
  sprk_servicerequest: 'servicerequests',
};

/** The route segment for an entity, or `undefined` when the entity has no message thread (e.g. `sprk_document`). */
export function messageRootFor(entity: string | undefined): string | undefined {
  if (!entity) return undefined;
  return Object.prototype.hasOwnProperty.call(MESSAGE_ROOTS, entity) ? MESSAGE_ROOTS[entity] : undefined;
}

/** `CommunicationService.MaxThreadPostBodyLength`: a longer body is refused 400. Counted in UTF-16 units, as the server does. */
export const MAX_MESSAGE_BODY_LENGTH = 10_000;

/** `sprk_bodyformat` option values. */
export const BODY_FORMAT_PLAIN_TEXT = 100000000;
export const BODY_FORMAT_HTML = 100000001;

/** One message, as `ExternalMessageDto` returns it. */
export interface ExternalMessage {
  sprk_communicationid: string;
  sprk_subject?: string | null;
  sprk_body?: string | null;
  /** 100000000 plain text, 100000001 HTML; null when unset. */
  sprk_bodyformat?: number | null;
  /** 100000000 incoming, 100000001 outgoing; null when unset. */
  sprk_direction?: number | null;
  /** The sender's display name; null when none is recorded. */
  sendername?: string | null;
  /** ISO-8601 UTC. */
  createdon?: string | null;
  /** The CALLER authored this message (server-decided; never inferred client-side from names or direction). */
  isMine: boolean;
}

/** `ExternalCollectionResponse<ExternalMessageDto>`. */
export interface ExternalMessagesResponse {
  value: ExternalMessage[];
  /** The read stopped at its cap, or a later page failed: the list is incomplete. Absent = complete. */
  truncated?: boolean;
}

function messagesPath(root: string, id: string): string {
  return `/api/v1/external/${encodeURIComponent(root)}/${encodeURIComponent(id)}/messages`;
}

/** GET the record's thread, oldest first. */
export function listMessages(root: string, id: string): Promise<ExternalMessagesResponse> {
  return bffApiCall<ExternalMessagesResponse>(messagesPath(root, id));
}

/**
 * POST a plain-text message to the record's thread. Resolves with the created DTO, which a caller must NOT append
 * (null `sendername`, server-time `createdon`): it re-reads the thread. There is no idempotency guard on the server,
 * so the caller must not issue a second POST while one is in flight.
 */
export function sendMessage(root: string, id: string, body: string): Promise<ExternalMessage> {
  return bffApiCall<ExternalMessage>(messagesPath(root, id), {
    method: 'POST',
    body: JSON.stringify({ sprk_body: body }),
  });
}

/** The ProblemDetails fields of a failed call that this surface reads. */
export interface MessageProblem {
  status: number | null;
  detail: string | null;
  errorCode: string | null;
  reasonCode: string | null;
}

/** Parses an `ApiError` (whose message is the raw response body) into its ProblemDetails fields; nulls when it is not one. */
export function messageProblemOf(err: unknown): MessageProblem {
  const problem: MessageProblem = { status: null, detail: null, errorCode: null, reasonCode: null };
  if (!(err instanceof ApiError)) return problem;
  problem.status = err.statusCode;
  try {
    const body = JSON.parse(err.message) as { detail?: unknown; errorCode?: unknown; reasonCode?: unknown };
    if (typeof body.detail === 'string' && body.detail.trim()) problem.detail = body.detail;
    if (typeof body.errorCode === 'string') problem.errorCode = body.errorCode;
    if (typeof body.reasonCode === 'string') problem.reasonCode = body.reasonCode;
  } catch {
    /* not a ProblemDetails body */
  }
  return problem;
}
