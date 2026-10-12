/**
 * delete-own — the client side of "delete what you created" on the external plane
 * (spaarke-SPA-external-access-platform-r3 task 054, FR-26; server: task 032).
 *
 * TWO JOBS, BOTH CONVENIENCE (the server is the authority):
 *
 *  1. WHICH ROWS TO OFFER Delete ON. The external list reads (documents, to-dos, events) return the two task-030 creator
 *     stamps on every row: `_sprk_createdbycontact_value` (a contact creator) and `_sprk_createdbyperson_value` (a
 *     systemuser creator). The server's own rule (`ExternalDataService.IsCreatedByCaller`) is mirrored here:
 *       - a contact principal matches `createdbycontact` = its contact id, and NEVER a person stamp;
 *       - a systemuser principal matches `createdbyperson` = its systemuser id, OR `createdbycontact` = its linked contact;
 *       - a row with neither stamp (older than task 030, or filed by staff) is nobody's, and an empty id never matches.
 *     Names are never compared. A caller whose identity is not known shows no Delete at all.
 *
 *  2. WHAT TO SAY WHEN THE SERVER REFUSES. `deleteFailureOf` maps the delete routes' answers to a user message:
 *       204 success; 403 uniform refusal (not yours / no Read on the parent / absent - one answer, no oracle);
 *       409 checked out (`document_locked`) or storage unverified; 503 nothing was deleted, retry; 500 the delete did not
 *       complete. Every problem body uses the `reasonCode` key (task 032 review fix). A 403 is an ERROR to show, not a
 *       reason to hide the row: the server decided, and nothing was deleted.
 */
import { accessFailureOf } from '../components/auth/BffAccessState';
import { messageProblemOf } from './messages-client';

// ---------------------------------------------------------------------------
// Who the caller is (for "created by me")
// ---------------------------------------------------------------------------

/** The two creator stamps a deletable row carries (task 030; returned by the task-032 list reads). */
export interface CreatorStamps {
  /** The systemuser creator stamp (`sprk_createdbyperson`). */
  _sprk_createdbyperson_value?: string | null;
  /** The contact creator stamp (`sprk_createdbycontact`). */
  _sprk_createdbycontact_value?: string | null;
}

/** The signed-in principal's ids, as `GET /api/v1/external/me` returns them. */
export interface CallerIdentity {
  /** The caller's contact id (a CIAM contact; for a workforce user, the linked contact, empty when none). */
  contactId: string | null;
  /**
   * The caller's systemuser id, from `/me` `systemUserId`: present only for a licensed workforce (systemuser) principal,
   * `null` for a CIAM partner or a contact-only caller, and never the empty GUID. `null` = no person-stamp match, so
   * Delete is withheld on person-stamped rows rather than guessed.
   */
  systemUserId: string | null;
}

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

function usableId(value: string | null | undefined): string | null {
  if (typeof value !== 'string') return null;
  const id = value.trim().toLowerCase();
  return id && id !== EMPTY_GUID ? id : null;
}

/** The identity out of the `/me` payload; null ids stay null (never an empty string or the empty GUID). */
export function callerIdentityOf(
  context: { contactId?: string | null; systemUserId?: string | null } | null | undefined
): CallerIdentity | null {
  if (!context) return null;
  const contactId = usableId(context.contactId);
  const systemUserId = usableId(context.systemUserId);
  if (!contactId && !systemUserId) return null;
  return { contactId, systemUserId };
}

/**
 * True when the row's creator stamp is the signed-in principal (the server's `IsCreatedByCaller`, mirrored). A convenience
 * for showing Delete: the server re-checks every delete and answers 403 when this guess is wrong.
 */
export function isCreatedByCaller(row: CreatorStamps, caller: CallerIdentity | null | undefined): boolean {
  if (!caller) return false;
  const byContact = usableId(row._sprk_createdbycontact_value);
  const byPerson = usableId(row._sprk_createdbyperson_value);
  if (caller.systemUserId && byPerson === caller.systemUserId) return true;
  return caller.contactId !== null && byContact === caller.contactId;
}

/**
 * The contact id a service request record names as its requester (`sprk_requestedby`), or null when it names none. The
 * module fetch serialises a lookup as an entity reference (`{ id, logicalName, name }`); a bare id string and the Web API
 * `_sprk_requestedby_value` shape are accepted too. Only an id is read, never a name.
 */
export function requestedByIdOf(record: Record<string, unknown> | null | undefined): string | null {
  if (!record) return null;
  const raw = record['sprk_requestedby'] ?? record['_sprk_requestedby_value'];
  if (typeof raw === 'string') return usableId(raw);
  if (raw && typeof raw === 'object') {
    const ref = raw as { id?: unknown; Id?: unknown };
    const id = ref.id ?? ref.Id;
    return typeof id === 'string' ? usableId(id) : null;
  }
  return null;
}

/**
 * True when the signed-in caller is the service request's requester: the request names a requester and it is the caller's
 * contact (for a workforce user, the linked contact). A convenience for showing Delete; the server's requester gate
 * (`RightsForDocumentParentAsync`) re-checks the DELETE and answers a uniform 403 when this guess is wrong.
 */
export function isRequestedByCaller(
  record: Record<string, unknown> | null | undefined,
  caller: CallerIdentity | null | undefined
): boolean {
  if (!caller || !caller.contactId) return false;
  return requestedByIdOf(record) === caller.contactId;
}

// ---------------------------------------------------------------------------
// What to say when a delete does not happen
// ---------------------------------------------------------------------------

/** What a failed delete is shown as: the sign-in state (401), or a plain error message. */
export type DeleteFailure =
  | { kind: 'signin' }
  | {
      kind: 'error';
      status: number | null;
      message: string;
      /** The server's `reasonCode` (null when none); lets a host offer a follow-up such as a recall. */
      reasonCode: string | null;
    };

/** `reasonCode`s the delete routes answer with (`ExternalProjectDataEndpoints`, tasks 032 and the 2026-10-11 owner decisions). */
const REASON_DOCUMENT_LOCKED = 'document_locked';
/** 409 on a service request that has documents, messages or other records: it cannot be deleted, but legal can be asked to recall it. */
export const REASON_HAS_RELATED_RECORDS = 'has_related_records';

/**
 * Maps a failed delete to what the user sees. `noun` is the row type ("document", "task", "event", "service request").
 *
 * The messages are the client's own: a server `detail` is not echoed (a 403 is deliberately uniform and a 500 may name
 * internals). Anything not listed gets the generic retry sentence. 401, a failed Teams token acquisition and a token
 * redirect that already failed all become the shared sign-in state (`accessFailureOf`'s 'signin').
 */
export function deleteFailureOf(err: unknown, noun: string): DeleteFailure {
  // Only the sign-in half of accessFailureOf applies: its 'denied' (a 403) is an ERROR to show here, not a page state.
  if (accessFailureOf(err) === 'signin') return { kind: 'signin' };

  const problem = messageProblemOf(err);
  const status = problem.status;
  const message = ((): string => {
    switch (status) {
      case 403:
        return noun === 'service request'
          ? 'Only the person who submitted this request can delete it. Nothing was deleted.'
          : `You cannot delete this ${noun}: only the person who created it can, and only while they can still see the record it belongs to. Nothing was deleted.`;
      case 409:
        if (problem.reasonCode === REASON_HAS_RELATED_RECORDS) {
          return `This ${noun} already has activity (documents, messages or other records), so it can't be deleted. You can ask legal to recall it. Nothing was deleted.`;
        }
        return problem.reasonCode === REASON_DOCUMENT_LOCKED
          ? 'This document is checked out, so it cannot be deleted right now. Ask for it to be checked back in, then try again. Nothing was deleted.'
          : `This ${noun} could not be deleted because its stored file could not be verified. Nothing was deleted. Try again later, or contact the record team.`;
      case 503:
        return `The ${noun} could not be deleted just now, and nothing was deleted. Please try again in a moment.`;
      case 500:
        return `The ${noun} could not be deleted. It may have been only partly removed - refresh the list, and contact the record team if it is still there.`;
      default:
        return `The ${noun} could not be deleted. Check your connection and try again.`;
    }
  })();
  return { kind: 'error', status, message, reasonCode: problem.reasonCode };
}
