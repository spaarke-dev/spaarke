/**
 * TrackingFieldTrio — the record's access status (unified-access-control-r2 task 153, owner round 83 item 11).
 *
 * Pure functions, kept out of the component so the fail-closed rules are pinned by tests without rendering (the
 * split `AccessGrantModal/noAccess.ts` uses):
 *  - {@link parseAccessStatusResponse} reads the two signals of task 064's per-record read,
 *    `GET /api/v1/records/{table}/{id}/no-access` (frozen contract:
 *    `projects/unified-access-control-r2/notes/phase4-access-report-contract.md`). Every answer it cannot trust — a
 *    body that is not an object, a `recordId` that does not name THIS record, a missing signal or a state value this
 *    client does not know — is `unknown`, never "does not apply".
 *  - {@link resolveAccessIndicator} turns the two signals into what the indicator shows. ANY `unknown` shows
 *    "Access status unavailable" (never silence, which would read as "not restricted"); nothing is shown only when
 *    BOTH signals are `doesNotApply`.
 *
 * Task 174 (owner rounds 82 and 84): the Secure signal of a filed work assignment or project should be the EFFECTIVE,
 * parent-derived value. Until the server exposes it, `secure` is the record's own flag (064's contract). This parser is
 * the ONE place this client reads it, so when 174 lands only this function (and the form banner's twin,
 * `Spaarke.AccessStatus.signalsOf` in `sprk_accessstatus_banner.js`) changes, if 174 does not simply make `secure`
 * the effective value itself.
 */

import { cleanGuid } from '../../utils/guid';
import { buildNoAccessPath } from '../AccessGrantModal/noAccess';
import type { ExternalGrantRootType } from '../AccessGrantModal/types';

/** One signal's state, exactly as 064's route spells it. */
export type AccessSignalState = 'applies' | 'doesNotApply' | 'unknown';

/** The two signals 064's route answers for a project, matter or work assignment. */
export interface ITrackingAccessStatus {
  /** `sprk_issecure`: `unknown` when it was unreadable, unreturned or empty. */
  secure: AccessSignalState;
  /** An in-force No Access entry covers the record (its own, an organization it references, a secure parent). */
  noAccess: AccessSignalState;
}

/** The answer a host passes when it could not obtain one: a non-200, a thrown call, no auth, an unparseable body. */
export const ACCESS_STATUS_UNAVAILABLE: Readonly<ITrackingAccessStatus> = Object.freeze({
  secure: 'unknown',
  noAccess: 'unknown',
});

function signal(value: unknown): AccessSignalState {
  return value === 'applies' || value === 'doesNotApply' || value === 'unknown' ? value : 'unknown';
}

/**
 * Reads a 200 body of 064's route for the record the host is bound to. The `recordId` echo is compared canonically
 * (ADR-044: the host may hold a braced id while the server echoes the bare one); an answer about another record, or
 * one that names no record, is {@link ACCESS_STATUS_UNAVAILABLE}.
 */
export function parseAccessStatusResponse(body: unknown, recordId: string): ITrackingAccessStatus {
  if (!body || typeof body !== 'object') return { ...ACCESS_STATUS_UNAVAILABLE };
  const b = body as Record<string, unknown>;
  if (typeof b.recordId !== 'string' || !recordId || cleanGuid(b.recordId) !== cleanGuid(recordId)) {
    return { ...ACCESS_STATUS_UNAVAILABLE };
  }
  return { secure: signal(b.secure), noAccess: signal(b.noAccess) };
}

/** What the indicator shows. */
export type AccessIndicatorView =
  /** Both signals `doesNotApply`: nothing at all (no "not secure" / "not restricted" text anywhere). */
  | { kind: 'none' }
  /** At least one signal is `unknown`: the neutral "Access status unavailable", never clickable. */
  | { kind: 'unavailable' }
  /** At least one signal `applies` and none is `unknown`: red. */
  | { kind: 'restricted'; secure: boolean; noAccess: boolean };

export function resolveAccessIndicator(status: ITrackingAccessStatus): AccessIndicatorView {
  const secure = signal(status.secure);
  const noAccess = signal(status.noAccess);
  if (secure === 'unknown' || noAccess === 'unknown') return { kind: 'unavailable' };
  if (secure === 'doesNotApply' && noAccess === 'doesNotApply') return { kind: 'none' };
  return { kind: 'restricted', secure: secure === 'applies', noAccess: noAccess === 'applies' };
}

/**
 * Asks task 064's route about one record through the host's authenticated fetch (relative BFF path, as every Manage
 * Access call is made) and parses the answer. Never rejects: a non-200 (the route's uniform 404 included), a thrown
 * call (auth not initialised, network), an unparseable body or an answer about another record is
 * {@link ACCESS_STATUS_UNAVAILABLE}. The entries a Write caller also receives are never read.
 */
export async function readAccessStatus(
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>,
  recordType: ExternalGrantRootType,
  recordId: string
): Promise<ITrackingAccessStatus> {
  try {
    const res = await authenticatedFetch(buildNoAccessPath(recordType, recordId), { method: 'GET' });
    if (!res || !res.ok) {
      console.info(
        `[TrackingFieldTrio] Access status unavailable for ${recordType} ${recordId}: ${res ? res.status : 'no response'}.`
      );
      return { ...ACCESS_STATUS_UNAVAILABLE };
    }
    return parseAccessStatusResponse(await res.json(), recordId);
  } catch (err) {
    console.warn(`[TrackingFieldTrio] Access status unavailable for ${recordType} ${recordId}.`, err);
    return { ...ACCESS_STATUS_UNAVAILABLE };
  }
}
