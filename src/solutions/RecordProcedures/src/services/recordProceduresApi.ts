/**
 * Record Procedures API client (ebilling-r1 task 084a): the two BFF reads the panel makes, through `authenticatedFetch` (ADR-028).
 *
 *   - GET /api/finance/{matters|projects|workassignments}/{recordId}/procedures?firmId=&date=       -> RecordProceduresView
 *   - GET /api/finance/{matters|projects|workassignments}/{recordId}/rules-in-force?firmId=&date=   -> RecordRulesInForceView
 *
 * The shapes below are task 084's DTOs (`RecordProcedureReadService.cs`, notes/084-procedure-review-ui.md), camelCase on the wire. The page
 * resolves nothing itself: every list is the server's answer for one firm and one work date, and the server already refuses (404 / 503)
 * unless the caller can Read every row it names, so a response is either whole or absent. `authenticatedFetch` THROWS on any non-2xx
 * (`ApiError` with the parsed ProblemDetails) and on an exhausted 401 (`AuthError`); every outcome is decided in the `catch` through the
 * `@spaarke/auth` guards. Mirrors `src/solutions/InvoiceFiles/src/services/invoiceFilesApi.ts`.
 *
 * Read-only by construction: this module has no write method and no route of task 089 (procedure assignments, rule overrides).
 */

import { isApiError, isAuthFailure, problemOf, type AuthenticatedFetchFn } from '@spaarke/auth';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types (task 084's JSON). Enum-like members are strings; a value this page does not know is rendered, never dropped.
// ─────────────────────────────────────────────────────────────────────────────

/** A record on the chain: the route's record (distance 0) or a direct parent (distance 1). `table` is the logical name. */
export interface ChainRecord {
  table: string;
  id: string;
  name: string | null;
  distance: number;
}

export interface FirmRef {
  id: string;
  name: string | null;
}

/** `Query` (the firmId asked for), `Record` (the record's billing firm) or `None` (only Procedures with no Firm apply). */
export type FirmSource = 'Query' | 'Record' | 'None' | (string & {});

/** `status` is Draft / Active / Superseded / Retired; the window is `[from, to)`, `to` null = open-ended. */
export interface VersionView {
  id: string;
  number: number;
  status: string;
  from: string | null;
  to: string | null;
}

/** `byName` can be null (the creator's name is read separately). */
export interface Stamp {
  byId: string | null;
  byName: string | null;
  on: string | null;
}

/** A Procedure-level assignment row on the chain. `mode`: FollowLatest | Pinned | Excluded. */
export interface OverrideView {
  rowId: string;
  mode: string;
  decisive: boolean;
  distance: number;
  chainRecord: ChainRecord;
  reason: string | null;
  pinnedVersion: VersionView | null;
  newerVersion: VersionView | null;
  stamp: Stamp;
}

/** How a Procedure reaches the record. `kind`: AllRecords | Firm | ManualOwn | ManualParent; `mode` (manual only): FollowLatest | Pinned. */
export interface SourceView {
  kind: string;
  winning: boolean;
  distance: number;
  via: ChainRecord | null;
  mode: string | null;
  rowId: string | null;
}

/** `status`: InForce | Excluded | Gap | NotYetInForce. `level`: the highest source (AllRecords | Firm | ManualParent | ManualOwn). */
export interface ProcedureItem {
  id: string;
  name: string;
  area: string;
  type: string | null;
  appliesTo: string;
  firm: FirmRef | null;
  status: string;
  level: string;
  sources: SourceView[];
  versionInForce: VersionView | null;
  pinned: boolean;
  newerVersion: VersionView | null;
  overrides: OverrideView[];
}

/** `kind`: HoleBetweenVersions | AfterLastVersion | PinnedVersionRetired | PinnedVersionNotInForce | ConflictingParents. */
export interface GapView {
  procedureId: string;
  procedureName: string;
  kind: string;
  workDate: string;
  text: string;
  overrides: OverrideView[];
}

export interface RecordProceduresView {
  record: ChainRecord;
  date: string;
  firm: FirmRef | null;
  firmSource: FirmSource;
  procedures: ProcedureItem[];
  gaps: GapView[];
}

export interface VersionRef {
  id: string;
  number: number;
}

/**
 * 084e: one rule that outranks another on the record (`OutrankingRuleView`). `source`: Procedure | Legacy. `level` and `distance` are its rank;
 * `code` is the reader code.
 */
export interface OutrankingRule {
  policyId: string;
  policyVersionId: string;
  code: string;
  name: string | null;
  versionNumber: number;
  source: string;
  procedureId: string | null;
  level: string;
  distance: number;
}

/**
 * One mark on a rule. The array is OPEN: `kind` is Excluded | Changed | Stale | BaseRetired, and (task 084e) Outranked | CapOutranked, so a
 * reader renders a kind it does not know (neutral) rather than dropping it. Members a kind does not use are null.
 * Outranked / CapOutranked carry `text` ("Outranked by X" / "Cap outranked by X"), `note` (when the rule can still run) and `by` (every
 * outranking rule at the top rank). Neither claims the rule does not run: it stays in force.
 */
export interface RuleMark {
  kind: string;
  reason?: string | null;
  firm?: FirmRef | null;
  distance?: number | null;
  chainRecord?: ChainRecord | null;
  stamp?: Stamp | null;
  overrideRowId?: string | null;
  recordRuleId?: string | null;
  basedOn?: VersionRef | null;
  currentBase?: VersionRef | null;
  baseCode?: string | null;
  baseParameterSummary?: string | null;
  text?: string | null;
  note?: string | null;
  by?: OutrankingRule[] | null;
}

export interface RuleProcedure {
  id: string;
  name: string;
  version: VersionView | null;
  pinned: boolean;
}

export interface RuleItem {
  policyId: string;
  policyVersionId: string | null;
  code: string;
  name: string | null;
  ruleType: string | null;
  parameterSummary: string | null;
  versionNumber: number | null;
  inForceFrom: string | null;
  inForce: boolean;
  recordRule: boolean;
  ruleSetId: string | null;
  procedure: RuleProcedure | null;
  level: string | null;
  distance: number | null;
  firmScope: FirmRef | null;
  marks: RuleMark[];
}

export interface RecordRulesInForceView {
  record: ChainRecord;
  date: string;
  firm: FirmRef | null;
  firmSource: FirmSource;
  rules: RuleItem[];
  gaps: GapView[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Failures
// ─────────────────────────────────────────────────────────────────────────────

/** Everything the page needs of a failed call, whatever raised it. */
export interface ApiFailure {
  /** HTTP status; 0 when no response arrived (network, or a token that could not be acquired). */
  status: number;
  /** The stable `errorCode` of ProblemDetails (ADR-019), when there is one. */
  errorCode: string | null;
  detail: string | null;
  /** True when sign-in failed (an exhausted 401 or no token). */
  signInFailed: boolean;
}

export type ApiResult<T> = { ok: true; value: T } | { ok: false; failure: ApiFailure };

export const CODE_EBILLING_DISABLED = 'ebilling.disabled';
export const CODE_PROCEDURES_UNAVAILABLE = 'procedures.unavailable';
export const CODE_INVALID_QUERY = 'procedures.invalid_query';

function str(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

/** Turn whatever `authenticatedFetch` threw into an {@link ApiFailure}. Exported for the tests. */
export function toFailure(err: unknown): ApiFailure {
  if (isApiError(err)) {
    const p = problemOf(err) as Record<string, unknown> | null;
    return {
      status: err.status,
      errorCode: str(p?.errorCode),
      detail: str(p?.detail) ?? str(err.message),
      signInFailed: isAuthFailure(err),
    };
  }
  return {
    status: 0,
    errorCode: null,
    detail: err instanceof Error ? err.message : null,
    signInFailed: isAuthFailure(err),
  };
}

/** What the panel shows when it cannot show the lists. Every kind shows NO rows. */
export type PanelFailureKind = 'signin' | 'notAvailable' | 'ebillingOff' | 'unavailable' | 'invalidQuery' | 'network' | 'other';

export function failureKind(f: ApiFailure): PanelFailureKind {
  if (f.signInFailed) return 'signin';
  // 404 is the uniform answer: an unknown record and one the caller cannot Read are the same body, and so the same message.
  if (f.status === 404) return 'notAvailable';
  if (f.errorCode === CODE_EBILLING_DISABLED) return 'ebillingOff';
  // Any 503 (procedures.unavailable included) is the one "cannot be shown right now" answer.
  if (f.status === 503) return 'unavailable';
  if (f.errorCode === CODE_INVALID_QUERY || f.status === 400) return 'invalidQuery';
  if (f.status === 0) return 'network';
  return 'other';
}

/** The order a failure of either route wins when both fail: the most definite answer first. */
const FAILURE_PRIORITY: PanelFailureKind[] = ['signin', 'notAvailable', 'ebillingOff', 'unavailable', 'invalidQuery', 'network', 'other'];

/** When one or both routes fail, the one failure the panel reports (never a partial list). */
export function pickFailure(a: ApiFailure, b: ApiFailure | null): ApiFailure {
  if (!b) return a;
  return FAILURE_PRIORITY.indexOf(failureKind(a)) <= FAILURE_PRIORITY.indexOf(failureKind(b)) ? a : b;
}

export const NOT_AVAILABLE_TITLE = 'Procedures are not available for this record';

/** The sentences the panel shows for a failed read. The 404 is the uniform one (no Read, or no such record): it must not say which. */
export function failureMessage(kind: PanelFailureKind): { title: string; body: string } {
  switch (kind) {
    case 'signin':
      return { title: 'Unable to load', body: 'Sign-in could not be verified for this session. Retry, or reload the form.' };
    case 'notAvailable':
      return { title: NOT_AVAILABLE_TITLE, body: 'The record may not exist, or you may not have access to it.' };
    case 'ebillingOff':
      return { title: 'eBilling is not enabled', body: 'eBilling is not enabled in this environment, so Procedures cannot be shown.' };
    case 'unavailable':
      return { title: 'Procedures cannot be shown right now', body: 'Nothing is shown rather than a partial list. Retry in a moment.' };
    case 'invalidQuery':
      return { title: 'Procedures cannot be shown', body: 'The firm or date was not accepted. Choose them again.' };
    case 'network':
      return { title: 'Procedures could not be loaded', body: 'Check your connection and retry.' };
    default:
      return { title: 'Procedures could not be loaded', body: 'Retry in a moment.' };
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

/** The core-record kinds, as the route segment: one literal chain per kind on the server. */
export type CoreRoute = 'matters' | 'projects' | 'workassignments';

export interface ProceduresQuery {
  /** A firm id. Null / absent: the record's billing firm (the server's default). */
  firmId?: string | null;
  /** yyyy-MM-dd. */
  date: string;
}

export interface RecordProceduresApi {
  procedures(core: CoreRoute, recordId: string, query: ProceduresQuery): Promise<ApiResult<RecordProceduresView>>;
  rulesInForce(core: CoreRoute, recordId: string, query: ProceduresQuery): Promise<ApiResult<RecordRulesInForceView>>;
}

function trimBase(bffBaseUrl: string): string {
  return bffBaseUrl.replace(/\/+$/, '');
}

function recordUrl(bffBaseUrl: string, core: CoreRoute, recordId: string, leaf: 'procedures' | 'rules-in-force', q: ProceduresQuery): string {
  const params: string[] = [];
  if (q.firmId) params.push(`firmId=${encodeURIComponent(q.firmId.toLowerCase())}`);
  params.push(`date=${encodeURIComponent(q.date)}`);
  return `${trimBase(bffBaseUrl)}/api/finance/${core}/${encodeURIComponent(recordId.toLowerCase())}/${leaf}?${params.join('&')}`;
}

/** The Procedures route. `bffBaseUrl` is the BFF host without `/api` (runtime config). */
export function proceduresUrl(bffBaseUrl: string, core: CoreRoute, recordId: string, q: ProceduresQuery): string {
  return recordUrl(bffBaseUrl, core, recordId, 'procedures', q);
}

/** The rules-in-force route. */
export function rulesInForceUrl(bffBaseUrl: string, core: CoreRoute, recordId: string, q: ProceduresQuery): string {
  return recordUrl(bffBaseUrl, core, recordId, 'rules-in-force', q);
}

const BAD_BODY: ApiFailure = { status: 200, errorCode: null, detail: 'The server answered with an unexpected body.', signInFailed: false };

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const list = <T>(v: unknown): T[] => (Array.isArray(v) ? (v as T[]) : []);

/** A Procedures body that is the view, with the arrays a reader walks always arrays. Null when it is not the view. */
export function readProceduresView(body: unknown): RecordProceduresView | null {
  if (!isObject(body) || !Array.isArray(body.procedures)) return null;
  const view = body as unknown as RecordProceduresView;
  return {
    ...view,
    gaps: list(body.gaps),
    procedures: (body.procedures as ProcedureItem[]).map(p => ({ ...p, sources: list(p.sources), overrides: list(p.overrides) })),
  };
}

/** A rules body that is the view; every rule's `marks` is an array. Null when it is not the view. */
export function readRulesView(body: unknown): RecordRulesInForceView | null {
  if (!isObject(body) || !Array.isArray(body.rules)) return null;
  const view = body as unknown as RecordRulesInForceView;
  return {
    ...view,
    gaps: list(body.gaps),
    rules: (body.rules as RuleItem[]).map(r => ({ ...r, marks: list(r.marks) })),
  };
}

export function createRecordProceduresApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string): RecordProceduresApi {
  return {
    async procedures(core, recordId, query) {
      try {
        const res = await fetchFn(proceduresUrl(bffBaseUrl, core, recordId, query), { method: 'GET' });
        // A body that is not the view is a failure, never an empty list (an empty list means "no Procedures apply").
        const view = readProceduresView((await res.json()) as unknown);
        return view ? { ok: true, value: view } : { ok: false, failure: BAD_BODY };
      } catch (err) {
        return { ok: false, failure: toFailure(err) };
      }
    },

    async rulesInForce(core, recordId, query) {
      try {
        const res = await fetchFn(rulesInForceUrl(bffBaseUrl, core, recordId, query), { method: 'GET' });
        const view = readRulesView((await res.json()) as unknown);
        return view ? { ok: true, value: view } : { ok: false, failure: BAD_BODY };
      } catch (err) {
        return { ok: false, failure: toFailure(err) };
      }
    },
  };
}
