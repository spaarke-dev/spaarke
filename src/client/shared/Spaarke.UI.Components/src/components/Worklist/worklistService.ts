/**
 * worklistService - the ONE read the worklist makes: `GET /api/v1/signals/worklist?set=...` (task 038; D-15).
 *
 * The Signal read route is the only data source for Signals: it applies the caller's record access (the core-record check, the
 * Do lane's "mine" rule, the read-time witness rule) that a browser read of `sprk_signal` cannot, and it returns the order. This
 * module reads the answer and shapes it; it never sorts, filters or re-groups (the route's order is the list's order).
 *
 * The fetch is injected (ADR-012, ADR-028: the host passes `@spaarke/auth`'s `authenticatedFetch`; this package holds no token).
 * A failure reaches here two ways and both are handled: the injected fetch THROWS an `ApiError` or RETURNS a non-2xx `Response`.
 *
 * Task: spaarke-ontology-platform-r1, task 059.
 */

import type { ResponseFetchFn } from '../../utils/fetchTypes';
import { isApiError, problemOf } from '../../utils/thrownFetchError';
import type { ResolutionType } from '../ConsoleKit';
import type { PastDueWording, WorkType, WorklistCore, WorklistItem, WorklistLane } from './types';

/** The route's `set` values: the two lanes, or one of the three secondary sets. */
export type WorklistSetName = 'lanes' | 'closedToday' | 'suppressed' | 'closedEarlier';

/** One Signal as the route returns it: the row's display columns plus the columns the wizard and the disclosures read. */
export interface WorklistRouteItem extends WorklistItem {
  /** `Open`, `Acknowledged` or `Resolved`. */
  status?: string | null;
  /** The stored evidence lines (a JSON array as text) after the route's read-time witness rule. */
  evidenceRefs?: string | null;
  policyVersionId?: string | null;
  /** When it closed (ISO 8601); closed sets only. */
  resolvedOn?: string | null;
  /** How it closed; closed sets only. */
  resolutionType?: ResolutionType | null;
  /** Until when it is not raised again (ISO 8601); the suppressed set only. */
  suppressedUntil?: string | null;
}

/** One core record's items (or the "Not filed" items when `core` is null), in the route's order. */
export interface WorklistGroupData {
  core: WorklistCore | null;
  items: WorklistRouteItem[];
}

/** An answer: both lanes, each a list of groups in the route's order. */
export interface WorklistData {
  set: WorklistSetName;
  Decide: WorklistGroupData[];
  Do: WorklistGroupData[];
}

export interface WorklistService {
  read(set: WorklistSetName): Promise<WorklistData>;
}

export interface WorklistServiceOptions {
  /** The injected fetch. An `AuthenticatedFetchFn` is assignable. */
  fetch: ResponseFetchFn;
  /** The BFF base URL, with or without a trailing slash. */
  baseUrl: string;
}

/** A read that did not give a list. `httpStatus` is null when no answer arrived. */
export class WorklistRequestError extends Error {
  readonly httpStatus: number | null;
  constructor(message: string, httpStatus: number | null) {
    super(message);
    this.name = 'WorklistRequestError';
    this.httpStatus = httpStatus;
  }
}

const ROUTE = '/api/v1/signals/worklist';

const WORK_TYPES: ReadonlySet<string> = new Set<WorkType>([
  'askOutsideCounsel',
  'approveOrRebudget',
  'chaseReply',
  'finishOrReschedule',
  'comingDue',
  'chaseResponse',
]);

/** `sprk_signal.sprk_resolutiontype` option values (verified against the schema by SignalWriter). */
const RESOLUTION_TYPES: Readonly<Record<number, ResolutionType>> = {
  100000000: 'Acted',
  100000001: 'Dismissed',
  100000002: 'ConditionCleared',
  100000003: 'Superseded',
  100000004: 'PolicyRetired',
};

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const text = (v: unknown): string | null => (typeof v === 'string' && v !== '' ? v : null);

function parseItem(raw: unknown, lane: WorklistLane): WorklistRouteItem | null {
  if (!isObject(raw)) return null;
  const signalId = text(raw.signalId);
  if (!signalId) return null;
  const workType = text(raw.workType);
  const resolution = typeof raw.resolutionType === 'number' ? RESOLUTION_TYPES[raw.resolutionType] : undefined;
  return {
    signalId,
    lane: raw.lane === 'Do' || raw.lane === 'Decide' ? raw.lane : lane,
    status: text(raw.status),
    title: text(raw.title),
    subjectName: text(raw.subjectName),
    ruleShortName: text(raw.ruleShortName),
    workType: workType && WORK_TYPES.has(workType) ? (workType as WorkType) : null,
    pastDueWording: (raw.pastDueWording === 'late' ? 'late' : 'overdue') as PastDueWording,
    raisedOn: text(raw.raisedOn),
    dueDate: text(raw.dueDate),
    evidenceRefs: text(raw.evidenceRefs),
    policyVersionId: text(raw.policyVersionId),
    resolvedOn: text(raw.resolvedOn),
    resolutionType: resolution ?? null,
    suppressedUntil: text(raw.suppressedUntil),
  };
}

function parseCore(raw: unknown): WorklistCore | null {
  if (!isObject(raw)) return null;
  const recordType = text(raw.recordType);
  const recordId = text(raw.recordId);
  if (!recordType || !recordId) return null;
  return {
    recordType,
    recordId,
    typeLabel: text(raw.typeLabel),
    name: text(raw.name),
    number: text(raw.number),
  };
}

function parseGroups(raw: unknown, lane: WorklistLane): WorklistGroupData[] {
  if (!Array.isArray(raw)) return [];
  const groups: WorklistGroupData[] = [];
  for (const g of raw) {
    if (!isObject(g) || !Array.isArray(g.items)) continue;
    const items = g.items.map(i => parseItem(i, lane)).filter((i): i is WorklistRouteItem => i !== null);
    if (items.length === 0) continue;
    // A group the route marks Not filed (or one with an unreadable core) is the null-core group: it is never dropped.
    groups.push({ core: g.notFiled === true ? null : parseCore(g.core), items });
  }
  return groups;
}

/**
 * Shape the route's JSON body. A body that is not an object with a `lanes` array, or that lacks the Decide or the Do lane, is not
 * an answer: it throws, so the widget shows its error state instead of "Nothing to do." (a false all-clear). Inside a lane, a
 * group or item that cannot be read is skipped, never made up.
 */
export function parseWorklistResponse(
  body: unknown,
  set: WorklistSetName,
  httpStatus: number | null = null
): WorklistData {
  const unreadable = () => new WorklistRequestError('The worklist answer could not be read.', httpStatus);
  if (!isObject(body) || !Array.isArray(body.lanes)) throw unreadable();
  const data: WorklistData = { set, Decide: [], Do: [] };
  let seenDecide = false;
  let seenDo = false;
  for (const l of body.lanes) {
    if (!isObject(l)) continue;
    if (l.lane === 'Decide') {
      data.Decide = parseGroups(l.groups, 'Decide');
      seenDecide = true;
    } else if (l.lane === 'Do') {
      data.Do = parseGroups(l.groups, 'Do');
      seenDo = true;
    }
  }
  if (!seenDecide || !seenDo) throw unreadable();
  return data;
}

export function createWorklistService({ fetch, baseUrl }: WorklistServiceOptions): WorklistService {
  const root = baseUrl.replace(/\/+$/, '');

  return {
    async read(set: WorklistSetName): Promise<WorklistData> {
      const address = `${root}${ROUTE}?set=${encodeURIComponent(set)}`;
      let response: Response;
      try {
        response = await fetch(address, { method: 'GET', headers: { Accept: 'application/json' } });
      } catch (err) {
        if (isApiError(err)) {
          const detail = problemOf(err)?.detail;
          throw new WorklistRequestError(
            typeof detail === 'string' && detail ? detail : `The worklist could not be read (${err.status}).`,
            err.status
          );
        }
        throw new WorklistRequestError('The worklist could not be read.', null);
      }
      if (!response.ok) {
        let detail: unknown;
        try {
          const body: unknown = await response.json();
          detail = isObject(body) ? body.detail : undefined;
        } catch {
          detail = undefined;
        }
        throw new WorklistRequestError(
          typeof detail === 'string' && detail ? detail : `The worklist could not be read (${response.status}).`,
          response.status
        );
      }
      let body: unknown;
      try {
        body = await response.json();
      } catch {
        throw new WorklistRequestError('The worklist answer could not be read.', response.status);
      }
      return parseWorklistResponse(body, set, response.status);
    },
  };
}
