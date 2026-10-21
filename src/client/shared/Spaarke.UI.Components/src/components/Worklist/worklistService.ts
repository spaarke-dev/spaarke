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
import type { DecisionOutcome, RecordClass, ResolutionType } from '../ConsoleKit';
import type { PastDueWording, WorkType, WorklistCore, WorklistItem, WorklistLane } from './types';
import { acceptNarrative } from './worklistNarrative';
import type { WorklistNarrativeRequest, WorklistNarrativeText } from './worklistNarrative';

/** The worklist's `set` values: the two lanes, or one of the three secondary sets. */
export type WorklistSetName = 'lanes' | 'closedToday' | 'suppressed' | 'closedEarlier';

/** Every `set` the route answers: the worklist's, and `decided` (the Decision Record tab, task 045; not a worklist set). */
export type WorklistRouteSetName = WorklistSetName | 'decided';

/** One offered action of a Decision Record and what became of it (`values` is what was sent; null for a skipped step). */
export interface WorklistDecisionStep {
  code: string;
  taken: boolean;
  values: Record<string, unknown> | null;
  status: string | null;
}

/** A Next step the decision created. */
export interface WorklistDecisionFollowOn {
  code: string;
  entity: string | null;
  id: string | null;
}

/**
 * The Decision Record that closed an item, as the route returns it (task 045, #1959 item 2): present only when the caller's own read
 * returned the record (a record the caller cannot read is omitted, never withheld by name). `steps` is null when the stored envelope
 * was unreadable: the wizard then opens the item read-only with no replay.
 */
export interface WorklistDecision {
  recordId: string;
  /** The autonumber, for example `DR-00042`. */
  number: string | null;
  recordClass: RecordClass | null;
  outcome: DecisionOutcome | null;
  /** ISO 8601. */
  decidedOn: string | null;
  /** From the record's own confirming-user field, never the owner (D-129). */
  decidedByName: string | null;
  reason: string | null;
  steps: WorklistDecisionStep[] | null;
  followOns: WorklistDecisionFollowOn[];
}

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
  /** The rule's sentence as stored on the Signal (task 154); null when the route returns none. The wizard quotes it (D-23). */
  sentence?: string | null;
  /** Do lane rows only (task 154): the subject's table (logical name). Null on a Decide row. */
  subjectRecordType?: string | null;
  /** Do lane rows only (task 154): the subject's id, with `subjectRecordType`. Null on a Decide row. */
  subjectRecordId?: string | null;
  /** Closed today, Closed earlier and Decided sets (task 045): the Decision Record the caller can read; null when there is none to show. */
  decision?: WorklistDecision | null;
}

/** One core record's items (or the "Not filed" items when `core` is null), in the route's order. */
export interface WorklistGroupData {
  core: WorklistCore | null;
  items: WorklistRouteItem[];
}

/** An answer: both lanes, each a list of groups in the route's order. */
export interface WorklistData {
  set: WorklistRouteSetName;
  Decide: WorklistGroupData[];
  Do: WorklistGroupData[];
}

/** One incoming reply to an inquiry, as `GET /api/v1/inquiries/for-todo/{todoId}` returns it (newest first). */
export interface InquiryReply {
  communicationId: string;
  subject: string | null;
  /** ISO 8601. */
  receivedOn: string | null;
}

/** The inquiry an inquiry-outcome To Do records the outcome of, with the replies the caller can read. */
export interface InquiryForTodo {
  serviceRequestId: string;
  replies: InquiryReply[];
}

/** The body of `POST /api/v1/inquiries/{serviceRequestId}/disposition`. */
export interface InquiryDispositionRequest {
  replyCommunicationId: string;
  /** The `sprk_servicerequest.sprk_disposition` option value (100000000..100000003). */
  disposition: number;
}

/** The 200 body of `POST .../disposition`. */
export interface RecordDispositionResult {
  /** The "Record the outcome" To Do was completed server-side (false when none was found or it could not be completed). */
  todoCompleted: boolean;
}

export interface WorklistService {
  read(set: WorklistRouteSetName): Promise<WorklistData>;
  /**
   * The inquiry an inquiry-outcome To Do (D-111) records the outcome of, and its replies; `null` when the route answers 404 (the
   * To Do is not readable, or no open outbound inquiry matches it: there is no picker). Any other failure throws `InquiryRequestError`.
   */
  findInquiryForTodo(todoId: string): Promise<InquiryForTodo | null>;
  /** Records the disposition. Resolves on 200 with whether the To Do was completed; throws `InquiryRequestError` (status and reason code) otherwise. */
  recordDisposition(serviceRequestId: string, request: InquiryDispositionRequest): Promise<RecordDispositionResult>;
  /**
   * The Briefing narrative over the list (task 062): prose the Assistant writes from the request's counts, or `null` when none can be
   * shown (the call failed, the answer had no summary, or its entity-name scrub removed a term). Never throws: the narrative is an
   * extra, and the list does not wait on it. Optional so a host or test double without one simply shows no narrative.
   */
  narrate?(request: WorklistNarrativeRequest): Promise<WorklistNarrativeText | null>;
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

/** A call to the inquiry routes that did not succeed. `httpStatus` is null when no answer arrived; `reasonCode` is the problem's `reasonCode`. */
export class InquiryRequestError extends Error {
  readonly httpStatus: number | null;
  readonly reasonCode: string | null;
  constructor(message: string, httpStatus: number | null, reasonCode: string | null) {
    super(message);
    this.name = 'InquiryRequestError';
    this.httpStatus = httpStatus;
    this.reasonCode = reasonCode;
  }
}

const ROUTE = '/api/v1/signals/worklist';
const INQUIRY_ROUTE = '/api/v1/inquiries';
const NARRATE_ROUTE = '/api/ai/daily-briefing/narrate';

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

const RECORD_CLASSES: ReadonlySet<string> = new Set<RecordClass>(['Judgement', 'Routine', 'Dismissal']);
const DECISION_OUTCOMES: ReadonlySet<string> = new Set<DecisionOutcome>(['Authorized', 'Denied', 'Dismissed']);

function parseDecisionSteps(raw: unknown): WorklistDecisionStep[] | null {
  if (!Array.isArray(raw)) return null;
  const steps: WorklistDecisionStep[] = [];
  for (const s of raw) {
    if (!isObject(s)) continue;
    const code = text(s.code);
    if (!code) continue;
    steps.push({ code, taken: s.taken === true, values: isObject(s.values) ? s.values : null, status: text(s.status) });
  }
  return steps;
}

/** A Decision Record that cannot be read is no record (null): the item then opens read-only without a replay, never with a made-up one. */
function parseDecision(raw: unknown): WorklistDecision | null {
  if (!isObject(raw)) return null;
  const recordId = text(raw.recordId);
  if (!recordId) return null;
  const recordClass = text(raw.recordClass);
  const outcome = text(raw.outcome);
  return {
    recordId,
    number: text(raw.number),
    recordClass: recordClass && RECORD_CLASSES.has(recordClass) ? (recordClass as RecordClass) : null,
    outcome: outcome && DECISION_OUTCOMES.has(outcome) ? (outcome as DecisionOutcome) : null,
    decidedOn: text(raw.decidedOn),
    decidedByName: text(raw.decidedByName),
    reason: text(raw.reason),
    steps: parseDecisionSteps(raw.steps),
    followOns: Array.isArray(raw.followOns)
      ? raw.followOns.filter(isObject).flatMap(f => {
          const code = text(f.code);
          return code ? [{ code, entity: text(f.entity), id: text(f.id) }] : [];
        })
      : [],
  };
}

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
    sentence: text(raw.sentence),
    subjectRecordType: text(raw.subjectRecordType),
    subjectRecordId: text(raw.subjectRecordId),
    decision: parseDecision(raw.decision),
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
  set: WorklistRouteSetName,
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

function parseReply(raw: unknown): InquiryReply | null {
  if (!isObject(raw)) return null;
  const communicationId = text(raw.communicationId);
  if (!communicationId) return null;
  return { communicationId, subject: text(raw.subject), receivedOn: text(raw.receivedOn) };
}

/** The for-todo answer. Not an object with a `serviceRequestId` is not an answer (it throws): the picker must not guess an inquiry. */
export function parseInquiryForTodo(body: unknown, httpStatus: number | null = null): InquiryForTodo {
  const serviceRequestId = isObject(body) ? text(body.serviceRequestId) : null;
  if (!isObject(body) || !serviceRequestId) {
    throw new InquiryRequestError('The inquiry answer could not be read.', httpStatus, null);
  }
  const replies = Array.isArray(body.replies)
    ? body.replies.map(parseReply).filter((r): r is InquiryReply => r !== null)
    : [];
  return { serviceRequestId, replies };
}

export function createWorklistService({ fetch, baseUrl }: WorklistServiceOptions): WorklistService {
  const root = baseUrl.replace(/\/+$/, '');

  /** One inquiry-route call: the 2xx response, or an `InquiryRequestError` (a thrown `ApiError` and a returned non-2xx are both handled). */
  async function inquiryCall(address: string, init: RequestInit): Promise<Response> {
    let response: Response;
    try {
      response = await fetch(address, init);
    } catch (err) {
      if (isApiError(err)) {
        const problem = problemOf(err);
        throw new InquiryRequestError(
          text(problem?.detail) ?? `The request failed (${err.status}).`,
          err.status,
          text(problem?.reasonCode)
        );
      }
      throw new InquiryRequestError('The request could not be sent.', null, null);
    }
    if (!response.ok) {
      let problem: unknown;
      try {
        problem = await response.json();
      } catch {
        problem = undefined;
      }
      const detail = isObject(problem) ? text(problem.detail) : null;
      const reasonCode = isObject(problem) ? text(problem.reasonCode) : null;
      throw new InquiryRequestError(detail ?? `The request failed (${response.status}).`, response.status, reasonCode);
    }
    return response;
  }

  return {
    async read(set: WorklistRouteSetName): Promise<WorklistData> {
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

    async findInquiryForTodo(todoId: string): Promise<InquiryForTodo | null> {
      let response: Response;
      try {
        response = await inquiryCall(`${root}${INQUIRY_ROUTE}/for-todo/${encodeURIComponent(todoId)}`, {
          method: 'GET',
          headers: { Accept: 'application/json' },
        });
      } catch (err) {
        if (err instanceof InquiryRequestError && err.httpStatus === 404) return null;
        throw err;
      }
      let body: unknown;
      try {
        body = await response.json();
      } catch {
        throw new InquiryRequestError('The inquiry answer could not be read.', response.status, null);
      }
      return parseInquiryForTodo(body, response.status);
    },

    async recordDisposition(
      serviceRequestId: string,
      request: InquiryDispositionRequest
    ): Promise<RecordDispositionResult> {
      const response = await inquiryCall(
        `${root}${INQUIRY_ROUTE}/${encodeURIComponent(serviceRequestId)}/disposition`,
        {
          method: 'POST',
          headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
          body: JSON.stringify({
            replyCommunicationId: request.replyCommunicationId,
            disposition: request.disposition,
          }),
        }
      );
      // The outcome is recorded (200). A body that cannot be read only means we cannot say the To Do was completed.
      let body: unknown;
      try {
        body = await response.json();
      } catch {
        return { todoCompleted: false };
      }
      const flag = (body as { todoCompleted?: unknown } | null)?.todoCompleted;
      return { todoCompleted: flag === true };
    },

    async narrate(request: WorklistNarrativeRequest): Promise<WorklistNarrativeText | null> {
      try {
        const response = await fetch(`${root}${NARRATE_ROUTE}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify(request),
        });
        if (!response.ok) return null;
        return acceptNarrative(await response.json());
      } catch {
        return null;
      }
    },
  };
}
