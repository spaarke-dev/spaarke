/**
 * Dry-run client (ebilling-r1 task 084d-4): the read every wizard's Impact step makes, on task 084d-2a/2b's route, through
 * `authenticatedFetch` (ADR-028), one literal chain per core record:
 *
 *   - POST /api/finance/{matters|projects|workassignments}/{recordId}/procedures/dry-run?firmId=&date=   body ProcedureDryRunRequest
 *       -> 200 ProcedureDryRunView { record, date, firm, firmSource, proposal, now, after, afterLabel, disclosure, closes, affectedOpenInvoices }
 *
 * The shapes are `RecordProcedureReadService.cs`'s DTOs, camelCase on the wire. Nothing is written by this route: it answers "if saved now".
 * It is ADVISORY. The write routes still decide every write, so a refused or failed dry run never stops the user from going on to Confirm.
 *
 * Refusals (the route's doc comment): 404 (step 0, or the uniform record not found); 400 `procedures.invalid_query`, or the write's own
 * planning 400; 403 (the planned writes' own checks); 404 `assignment.override_not_found`; 409 (the write's planning conflicts); 503
 * `ebilling.disabled`; 503 `procedures.unavailable` (a row the answer depends on could not be read, or the caller cannot Read it: R6 says a
 * firm the request names is asked of the caller, so a firm the caller cannot Read is this 503). Every refusal is a {@link WriteFailure}
 * carrying the ProblemDetails title and detail, as the wizards' writes are.
 *
 * A call has a timeout ({@link READ_TIMEOUT_MS} unless the client is made with another) and an abort signal from the caller (the wizard closed
 * or moved to another row). A timeout is a failure with `CODE_TIMED_OUT`; an abandoned call is one with `CODE_ABANDONED`, which nobody is shown.
 */

import type { AuthenticatedFetchFn } from '@spaarke/auth';
import type { CoreRoute, FirmRef, ChainRecord, ProceduresQuery, RecordProceduresView, RecordRulesInForceView } from './recordProceduresApi';
import { readProceduresView, readRulesView } from './recordProceduresApi';
import { ABANDONED_FAILURE, READ_TIMEOUT_MS, TIMED_OUT_FAILURE, raceTimeout, toWriteFailure, type WriteFailure, type WriteResult } from './overridesApi';
import type { Plan, PlanStep } from '../components/wizards/plan';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types
// ─────────────────────────────────────────────────────────────────────────────

/** `ProcedureDryRunRequest.Kind*`: one of the four writes. */
export type DryRunKind = 'Assign' | 'ExcludeRule' | 'ChangeRule' | 'Remove';

/**
 * The request body (`ProcedureDryRunRequest`): the kind, the write's own body under its member, `overrideId` for a Remove, and
 * `replacesOverrideId` for a create that replaces this record's own override (the wizards' two-call plans: remove, then create).
 */
export interface DryRunRequest {
  kind: DryRunKind;
  overrideId?: string;
  replacesOverrideId?: string;
  assign?: { procedureId: string; mode: string; pinnedVersionId?: string; reason?: string };
  excludeRule?: { policyId: string; reason: string; firmId?: string };
  changeRule?: { policyId: string; reason: string; firmId?: string; ruleBody: string };
}

/** `ProcedureDryRunClosesView`: what the write would close. `kinds`: ChangedRuleRemoved | BaseRuleSuperseded. */
export interface DryRunCloses {
  d170: boolean;
  kinds: string[];
  warning: string | null;
  message: string | null;
}

/** `ProcedureDryRunAffectedView`. `count` null: it cannot be told (a fault or a bound), never a guess. `basis`: FindingsClosed | ResolutionChanged. */
export interface DryRunAffected {
  count: number | null;
  text: string | null;
  basis: string;
}

export interface DryRunDisclosure {
  predictsOwnerRefusal: boolean;
  readAtOneInstant: boolean;
  requestFirmAsked: boolean;
  notes: string[];
}

export interface DryRunSide {
  procedures: RecordProceduresView;
  rules: RecordRulesInForceView;
}

export interface DryRunView {
  record: ChainRecord;
  date: string;
  firm: FirmRef | null;
  firmSource: string;
  proposal: { kind: string; overrideKind: string; replacesOverrideId: string | null; overrideId: string | null; proposedRowIds: string[] };
  now: DryRunSide;
  after: DryRunSide;
  /** "If saved now": the After side is never a promise that saving will succeed. */
  afterLabel: string;
  disclosure: DryRunDisclosure;
  closes: DryRunCloses;
  affectedOpenInvoices: DryRunAffected;
}

/** The `errorCode` of the dry run's one 503 for a row it cannot read or the caller cannot Read. */
export const CODE_DRY_RUN_UNAVAILABLE = 'procedures.unavailable';

// ─────────────────────────────────────────────────────────────────────────────
// Plan -> request
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The dry-run request for a plan, or why there is none: `empty` (the plan changes nothing) or `unsupported` (a shape the route does not take).
 * The plans `plan.ts` makes are one call, or a removal of this record's own override followed by one create: the server expresses that
 * pair as the create's kind with `replacesOverrideId` (`RecordProcedureReadService.PlanAsync` removes it first, then plans the create over
 * the removal). Any other shape (two creates, a create then a removal, two removals) has no dry-run equivalent and is not sent.
 */
export type DryRunMapping = { ok: true; request: DryRunRequest } | { ok: false; reason: 'empty' | 'unsupported' };

function createOf(step: Exclude<PlanStep, { kind: 'remove' }>): DryRunRequest {
  switch (step.kind) {
    case 'assign':
      return { kind: 'Assign', assign: { procedureId: step.procedureId, mode: step.mode, pinnedVersionId: step.pinnedVersionId, reason: step.reason } };
    case 'excludeRule':
      return { kind: 'ExcludeRule', excludeRule: { policyId: step.policyId, reason: step.reason, firmId: step.firmId } };
    case 'changeRule':
      return { kind: 'ChangeRule', changeRule: { policyId: step.policyId, reason: step.reason, firmId: step.firmId, ruleBody: step.ruleBody } };
  }
}

export function dryRunRequestOf(plan: Plan): DryRunMapping {
  const [first, second, ...rest] = plan.steps;
  if (!first) return { ok: false, reason: 'empty' };
  if (rest.length > 0) return { ok: false, reason: 'unsupported' };
  if (!second) return first.kind === 'remove' ? { ok: true, request: { kind: 'Remove', overrideId: first.overrideId } } : { ok: true, request: createOf(first) };
  if (first.kind !== 'remove' || second.kind === 'remove') return { ok: false, reason: 'unsupported' };
  return { ok: true, request: { ...createOf(second), replacesOverrideId: first.overrideId } };
}

/** The firm the request itself names (a rule exclude's or a change's `firmId`): R6 asks it of the caller as a query firm. */
export function requestFirmOf(request: DryRunRequest): string | null {
  return request.excludeRule?.firmId || request.changeRule?.firmId || null;
}

/** Drop undefined and blank members, recursively one level, so the body is exactly the route's request shape. */
function compact(value: Record<string, unknown>): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(value)
      .filter(([, v]) => v !== undefined && v !== null && !(typeof v === 'string' && v.trim() === ''))
      .map(([k, v]) => [k, typeof v === 'object' && v !== null && !Array.isArray(v) ? compact(v as Record<string, unknown>) : v])
  );
}

/** The JSON body sent for a request. Exported for the tests and for the Impact step's "same proposal" key. */
export const dryRunBody = (request: DryRunRequest): string => JSON.stringify(compact(request as unknown as Record<string, unknown>));

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

export interface DryRunApi {
  dryRun(core: CoreRoute, recordId: string, query: ProceduresQuery, request: DryRunRequest, signal?: AbortSignal): Promise<WriteResult<DryRunView>>;
}

const trimBase = (bffBaseUrl: string): string => bffBaseUrl.replace(/\/+$/, '');

/** The route: the same prefix and query as the two list reads (`recordProceduresApi.ts`). */
export function dryRunUrl(bffBaseUrl: string, core: CoreRoute, recordId: string, q: ProceduresQuery): string {
  const params: string[] = [];
  if (q.firmId) params.push(`firmId=${encodeURIComponent(q.firmId.toLowerCase())}`);
  params.push(`date=${encodeURIComponent(q.date)}`);
  return `${trimBase(bffBaseUrl)}/api/finance/${core}/${encodeURIComponent(recordId.toLowerCase())}/procedures/dry-run?${params.join('&')}`;
}

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | null => (typeof v === 'string' && v.length > 0 ? v : null);

function readSide(v: unknown): DryRunSide | null {
  if (!isObject(v)) return null;
  const procedures = readProceduresView(v.procedures);
  const rules = readRulesView(v.rules);
  return procedures && rules ? { procedures, rules } : null;
}

/** A body that is the view, with the members the Impact step reads normalised. Null when it is not the view. */
export function readDryRunView(body: unknown): DryRunView | null {
  if (!isObject(body)) return null;
  const now = readSide(body.now);
  const after = readSide(body.after);
  if (!now || !after) return null;
  const closes = isObject(body.closes) ? body.closes : {};
  const affected = isObject(body.affectedOpenInvoices) ? body.affectedOpenInvoices : {};
  const disclosure = isObject(body.disclosure) ? body.disclosure : {};
  return {
    ...(body as unknown as DryRunView),
    now,
    after,
    afterLabel: str(body.afterLabel) ?? 'If saved now',
    closes: {
      d170: closes.d170 === true,
      kinds: Array.isArray(closes.kinds) ? closes.kinds.filter((k): k is string => typeof k === 'string') : [],
      warning: str(closes.warning),
      message: str(closes.message),
    },
    // A count that is not a number is "can't be counted" (O-K1b), never 0.
    affectedOpenInvoices: {
      count: typeof affected.count === 'number' ? affected.count : null,
      text: str(affected.text),
      basis: str(affected.basis) ?? '',
    },
    disclosure: {
      predictsOwnerRefusal: disclosure.predictsOwnerRefusal === true,
      readAtOneInstant: disclosure.readAtOneInstant === true,
      requestFirmAsked: disclosure.requestFirmAsked === true,
      notes: Array.isArray(disclosure.notes) ? disclosure.notes.filter((n): n is string => typeof n === 'string') : [],
    },
  };
}

const BAD_BODY: WriteFailure = { status: 200, errorCode: null, title: 'The server answered with an unexpected body.', detail: null, problems: [], fieldErrors: [], signInFailed: false };

export function createDryRunApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string, options: { timeoutMs?: number } = {}): DryRunApi {
  const timeoutMs = options.timeoutMs ?? READ_TIMEOUT_MS;
  return {
    async dryRun(core, recordId, query, request, signal) {
      try {
        const raced = await raceTimeout(timeoutMs, signal, async s => {
          const res = await fetchFn(dryRunUrl(bffBaseUrl, core, recordId, query), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: dryRunBody(request),
            signal: s,
          });
          return readDryRunView((await res.json()) as unknown);
        });
        if (raced.kind === 'timeout') return { ok: false, failure: TIMED_OUT_FAILURE };
        if (raced.kind === 'aborted') return { ok: false, failure: ABANDONED_FAILURE };
        return raced.value ? { ok: true, value: raced.value } : { ok: false, failure: BAD_BODY };
      } catch (err) {
        return { ok: false, failure: toWriteFailure(err) };
      }
    },
  };
}
