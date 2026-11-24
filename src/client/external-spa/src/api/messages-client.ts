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
  /**
   * Whether the caller may post to this thread: the POST's own decision (R4 task 002, FR-06; `CanPostAsync`). Absent
   * (a BFF that predates it) is "not said", never "can".
   */
  canCreate?: boolean;
}

function messagesPath(root: string, id: string): string {
  return `/api/v1/external/${encodeURIComponent(root)}/${encodeURIComponent(id)}/messages`;
}

/** How a read acquires its token (`bffApiCall`'s `auth` argument). */
export interface ListMessagesOptions {
  /**
   * A TIMER-driven background read (R4 task 034, FR-21: the new-message check): if the token cannot be had silently the call
   * rejects with `SignInRequiredError` instead of starting a sign-in redirect or popup. Never set for a read the user asked for.
   */
  noInteraction?: boolean;
}

/** GET the record's thread, oldest first. */
export function listMessages(
  root: string,
  id: string,
  options: ListMessagesOptions = {}
): Promise<ExternalMessagesResponse> {
  // The third argument is passed only when set, so every existing call keeps its shape (`bffApiCall(path)`).
  return options.noInteraction
    ? bffApiCall<ExternalMessagesResponse>(messagesPath(root, id), {}, { noInteraction: true })
    : bffApiCall<ExternalMessagesResponse>(messagesPath(root, id));
}

function sentAtMs(message: ExternalMessage): number | null {
  if (!message.createdon) return null;
  const ms = Date.parse(message.createdon);
  return Number.isNaN(ms) ? null : ms;
}

/**
 * Whether a later read of the thread (`fetched`) holds a message that is newer than the thread on screen (`shown`) (R4 task 034,
 * FR-21). A message counts when its id is not on screen AND it was created after the newest message on screen; with nothing
 * dated on screen (an empty thread) any message not on screen counts. A message with no usable `createdon` can only count
 * against a thread with nothing dated, so a missing date never raises the indicator over a thread that has dates. Only
 * ADDITIONS count: a message that disappeared, or an older one that was already shown, does not.
 */
export function hasNewerMessages(shown: readonly ExternalMessage[], fetched: readonly ExternalMessage[]): boolean {
  const shownIds = new Set(shown.map(m => m.sprk_communicationid).filter(id => !!id));
  let newest: number | null = null;
  for (const m of shown) {
    const t = sentAtMs(m);
    if (t !== null && (newest === null || t > newest)) newest = t;
  }
  return fetched.some(m => {
    if (m.sprk_communicationid && shownIds.has(m.sprk_communicationid)) return false;
    if (newest === null) return true;
    const t = sentAtMs(m);
    // `>=`: `createdon` has second precision, so a message created in the same second as the newest one on screen is not
    // older than it. The id check above already excludes every message that is on screen.
    return t !== null && t >= newest;
  });
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
