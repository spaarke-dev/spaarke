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
import type { AuthenticatedFetchFn } from '../../utils/fetchTypes';
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

/**
 * Whether the indicator shows the Secure signal at all. OWNER CHANGE POINT (b): `false` since owner round 85
 * (2026-10-08) — the access-permission pill already reads "Secure" in red, so the indicator shows only No Access and
 * draws nothing for a record that is only secure. An unknown Secure signal still shows "Access status unavailable".
 * Flipping it back to `true` restores "Secure" / "Secure · No Access"; the banner's SECURE text must then be
 * re-checked (sprk_accessstatus_banner.js ManageAccessFrom* constants).
 */
export const INDICATOR_SHOWS_SECURE = false;

/** A request that has not answered by then is "unavailable" (a hung call must not leave the indicator absent). */
export const ACCESS_STATUS_TIMEOUT_MS = 20000;

/** What the indicator shows. */
export type AccessIndicatorView =
  /** Both signals `doesNotApply`: nothing at all (no "not secure" / "not restricted" text anywhere). */
  | { kind: 'none' }
  /** At least one signal is `unknown`: the neutral "Access status unavailable", never clickable. */
  | { kind: 'unavailable' }
  /** At least one signal `applies` and none is `unknown`: red. */
  | { kind: 'restricted'; secure: boolean; noAccess: boolean };

export function resolveAccessIndicator(
  status: ITrackingAccessStatus,
  showSecure: boolean = INDICATOR_SHOWS_SECURE
): AccessIndicatorView {
  const secure = signal(status.secure);
  const noAccess = signal(status.noAccess);
  if (secure === 'unknown' || noAccess === 'unknown') return { kind: 'unavailable' };
  const showsSecure = showSecure && secure === 'applies';
  const showsNoAccess = noAccess === 'applies';
  if (!showsSecure && !showsNoAccess) return { kind: 'none' };
  return { kind: 'restricted', secure: showsSecure, noAccess: showsNoAccess };
}

/**
 * Asks task 064's route about one record through the host's authenticated fetch (relative BFF path, as every Manage
 * Access call is made) and parses the answer. Never rejects: a non-200 (the route's uniform 404 included), a thrown
 * call (auth not initialised, network), an unparseable body or an answer about another record is
 * {@link ACCESS_STATUS_UNAVAILABLE}. The entries a Write caller also receives are never read.
 */
export async function readAccessStatus(
  authenticatedFetch: AuthenticatedFetchFn,
  recordType: ExternalGrantRootType,
  recordId: string,
  timeoutMs: number = ACCESS_STATUS_TIMEOUT_MS
): Promise<ITrackingAccessStatus> {
  const controller = typeof AbortController === 'function' ? new AbortController() : undefined;
  let timer: ReturnType<typeof setTimeout> | undefined;
  // Bounds the whole call (token acquisition included), and aborts the request when it fires.
  const timedOut = new Promise<'timeout'>(resolve => {
    timer = setTimeout(() => {
      controller?.abort();
      resolve('timeout');
    }, timeoutMs);
  });
  try {
    const answer = await Promise.race([
      authenticatedFetch(buildNoAccessPath(recordType, recordId), { method: 'GET', signal: controller?.signal }),
      timedOut,
    ]);
    if (answer === 'timeout') {
      console.warn(`[TrackingFieldTrio] Access status unavailable for ${recordType} ${recordId}: no answer in time.`);
      return { ...ACCESS_STATUS_UNAVAILABLE };
    }
    const res = answer;
    return parseAccessStatusResponse(await res.json(), recordId);
  } catch (err) {
    console.warn(`[TrackingFieldTrio] Access status unavailable for ${recordType} ${recordId}.`, err);
    return { ...ACCESS_STATUS_UNAVAILABLE };
  } finally {
    if (timer !== undefined) clearTimeout(timer);
  }
}
