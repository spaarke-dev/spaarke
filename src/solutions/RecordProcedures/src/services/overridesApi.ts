/**
 * Overrides API client (ebilling-r1 task 084b): the writes the Procedure and rule wizards make, through `authenticatedFetch` (ADR-028),
 * on task 089's routes (`ProcedureAssignmentEndpoints.cs`), one literal chain per core record:
 *
 *   - POST   /api/finance/{core}/{recordId}/procedure-assignments          body { procedureId, mode, pinnedVersionId?, reason? }  -> 201 OverrideCreated
 *   - POST   /api/finance/{core}/{recordId}/rule-overrides/exclude         body { policyId, reason, firmId? }                     -> 201 OverrideCreated
 *   - GET    /api/finance/{core}/{recordId}/overrides/{overrideId}/removal-preview                                               -> 200 RemovalPreview
 *   - DELETE /api/finance/{core}/{recordId}/overrides/{overrideId}                                                               -> 200 Removed
 *
 * Task 084c adds the change of a rule for the record:
 *
 *   - POST   /api/finance/{core}/{recordId}/rule-overrides/change          body { policyId, reason, firmId?, ruleBody }          -> 201 RuleChanged
 *
 * `ruleBody` is the new rule body as JSON TEXT (the route's `RuleChangeRequest.RuleBody` is a string): the type's whole body, which replaces
 * the rule's. The server validates it (RuleBodySchemaValidator, PolicyVersionValidator) and answers a refusal 400 with `errors`
 * (`{ policyId, rule, field, message, reason }`); {@link WriteFailure.fieldErrors} keeps them by field.
 *
 * One existing read is added: task 081's GET /api/finance/procedures/{procedureId} (the Pin picker's versions: task 084's view carries the
 * version in force and the newer one, not the list a pin picks from). It is not a new route. (084c's read of Rule Review's rule set for the
 * Change form's values was removed in its fix round 1: the rules-in-force rows now carry `ruleBody`.)
 *
 * The page decides nothing about authorization: every refusal (400, 403, 404, 409, 422, 503 ...) comes back as a {@link WriteFailure} carrying
 * the ProblemDetails `title` and `detail`, and the wizard shows them. The dry-run read is not here: it is `dryRunApi.ts` (task 084d-4).
 *
 * Mirrors `recordProceduresApi.ts`: `authenticatedFetch` THROWS on any non-2xx (`ApiError`) and on an exhausted 401 (`AuthError`).
 */

import { isApiError, isAuthFailure, problemOf, type AuthenticatedFetchFn } from '@spaarke/auth';
import type { CoreRoute, VersionView } from './recordProceduresApi';
import { ABANDONED_FAILURE, READ_TIMEOUT_MS, TIMED_OUT_FAILURE, raceTimeout } from './readRace';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types (task 089's JSON, camelCase)
// ─────────────────────────────────────────────────────────────────────────────

export type AssignMode = 'FollowLatest' | 'Pinned' | 'Excluded';

/** The body of `procedure-assignments`. `pinnedVersionId` only with `Pinned`; `reason` is optional on the route (the wizard requires it for a pin and an exclude). */
export interface AssignRequest {
  procedureId: string;
  mode: AssignMode;
  pinnedVersionId?: string;
  reason?: string;
  /** Task 084h-4: the optional note for the change's Decision Record. Blank is sent as absent. */
  note?: string;
}

/** The body of `rule-overrides/exclude`: the reason is required; `firmId` limits the exclusion to one firm's invoices. */
export interface ExcludeRuleRequest {
  policyId: string;
  reason: string;
  firmId?: string;
  /** Task 084h-4: the optional note for the change's Decision Record. Blank is sent as absent. */
  note?: string;
}

/**
 * The body of `rule-overrides/change`: the reason is required; `firmId` limits the change to one firm's invoices (and must be the rule's own
 * firm when the rule names one); `ruleBody` is the new body of the rule's type as JSON text.
 */
export interface ChangeRuleRequest {
  policyId: string;
  reason: string;
  firmId?: string;
  ruleBody: string;
  /** Task 084h-4: the optional note for the change's Decision Record. Blank is sent as absent. */
  note?: string;
}

/**
 * Task 084h-4 (design 7.1): what became of a committed change's Decision Record, as the server answers it (`decisionRecord`). The member is
 * absent from an older server's answer. `Recorded` and `Queued` (the server retries a queued record, task 084h-3) need no word from this page;
 * `NotRecorded` means the change stands but its record could not be written or queued: the page warns.
 */
export interface DecisionRecordStatus {
  status: string;
  recordId?: string | null;
  errorCode?: string | null;
}

/** The status a change's Decision Record has when it could not be written or queued (`OverrideDecisionRecord.NotRecorded`). */
export const DECISION_RECORD_NOT_RECORDED = 'NotRecorded';

/** What a created override row looks like (`OverrideView` on the server). */
export interface OverrideCreated {
  id: string;
  kind: string;
  mode: string;
  procedureId: string | null;
  pinnedVersionId: string | null;
  policyId: string | null;
  firmId: string | null;
  reason: string | null;
  /** Task 084h-4: absent when the server predates the member. */
  decisionRecord?: DecisionRecordStatus | null;
}

/** The read-only confirmation before a removal (`OverrideRemovalPreview`). A plain removal has no warning and no count. */
export interface RemovalPreview {
  overrideId: string;
  kind: string;
  closesRule: boolean;
  warning: string | null;
  affectedText: string | null;
  visibleOpenInvoices: number | null;
}

/** What a removal did (`OverrideRemoved`). */
export interface Removed {
  overrideId: string;
  kind: string;
  ruleSwitchedOff: boolean;
  closeComplete: boolean;
  affectedText: string | null;
  visibleOpenInvoices: number | null;
  message: string | null;
  /** Task 084h-4: absent when the server predates the member. */
  decisionRecord?: DecisionRecordStatus | null;
}

/** What a change wrote (`RuleChangedView`): the Exclude row, the record rule that takes the rule's place, and what the close of the base rule's findings did. */
export interface RuleChanged {
  override: OverrideCreated;
  recordRule: { ruleSetId: string; policyId: string; code: string; versionId: string; versionNumber: number; basedOnVersionId: string; scope: string; firmId: string | null };
  closeComplete: boolean;
  affectedText: string | null;
  visibleOpenInvoices: number | null;
  message: string | null;
}

/** A version of a Procedure, for the Pin picker (from task 081's read). `status` is Draft | Active | Superseded | Retired. */
export type PinnableVersion = VersionView;

// ─────────────────────────────────────────────────────────────────────────────
// Failures
// ─────────────────────────────────────────────────────────────────────────────

/** One problem of a 400 refusal, with the request member it names (`field`: ruleBody, firmId, reason, consequence ...). */
export interface FieldError {
  field: string;
  message: string;
  reason: string | null;
}

/** Everything the wizard shows of a refused or failed call. The server's words are kept as they came. */
export interface WriteFailure {
  /** HTTP status; 0 when no response arrived (network, or a token that could not be acquired). */
  status: number;
  /** The stable `errorCode` of ProblemDetails (ADR-019), when there is one. */
  errorCode: string | null;
  /** The ProblemDetails `title`: what the wizard leads with. */
  title: string | null;
  detail: string | null;
  /** The field problems of a 400 (`errors`), as sentences. */
  problems: string[];
  /** The same problems with the member each names, for a form to put them on its fields (task 084c). */
  fieldErrors: FieldError[];
  /** True when sign-in failed (an exhausted 401 or no token). */
  signInFailed: boolean;
}

export type WriteResult<T> = { ok: true; value: T } | { ok: false; failure: WriteFailure };

const str = (v: unknown): string | null => (typeof v === 'string' && v.trim().length > 0 ? v : null);

/** The 400's `errors`: an array of `{ message }` (override validation) or a `{ field: string[] }` dictionary (ValidationProblem). */
function problemsOf(errors: unknown): string[] {
  if (Array.isArray(errors)) return errors.flatMap(e => (typeof e === 'object' && e !== null ? [str((e as { message?: unknown }).message)] : [])).filter((m): m is string => m !== null);
  if (typeof errors === 'object' && errors !== null) {
    return Object.values(errors as Record<string, unknown>).flatMap(v => (Array.isArray(v) ? v.filter((m): m is string => typeof m === 'string') : []));
  }
  return [];
}

/** The 400's `errors` with the request member each names: `{ field, message, reason }` items, or a `{ field: string[] }` dictionary. */
function fieldErrorsOf(errors: unknown): FieldError[] {
  if (Array.isArray(errors)) {
    return errors.flatMap(e => {
      if (typeof e !== 'object' || e === null) return [];
      const o = e as { field?: unknown; message?: unknown; reason?: unknown };
      const message = str(o.message);
      return message === null ? [] : [{ field: str(o.field) ?? '', message, reason: str(o.reason) }];
    });
  }
  if (typeof errors === 'object' && errors !== null) {
    return Object.entries(errors as Record<string, unknown>).flatMap(([field, v]) =>
      Array.isArray(v) ? v.filter((m): m is string => typeof m === 'string').map(message => ({ field, message, reason: null })) : []
    );
  }
  return [];
}

/** Turn whatever `authenticatedFetch` threw into a {@link WriteFailure}. Exported for the tests. */
export function toWriteFailure(err: unknown): WriteFailure {
  if (isApiError(err)) {
    const p = problemOf(err) as Record<string, unknown> | null;
    return {
      status: err.status,
      errorCode: str(p?.errorCode),
      title: str(p?.title),
      detail: str(p?.detail),
      problems: problemsOf(p?.errors),
      fieldErrors: fieldErrorsOf(p?.errors),
      signInFailed: isAuthFailure(err),
    };
  }
  return { status: 0, errorCode: null, title: null, detail: err instanceof Error ? err.message : null, problems: [], fieldErrors: [], signInFailed: isAuthFailure(err) };
}

/**
 * The sentence the wizard shows for a refused or failed call: the ProblemDetails title first (the server's own words), then its detail and the
 * field problems. Only a failure with no title at all (no response, or a body that was not ProblemDetails) gets a sentence of ours.
 */
export function failureText(f: WriteFailure): string {
  if (f.signInFailed) return 'Sign-in could not be verified for this session. Try again, or reload the form.';
  const lead = f.title ?? (f.status === 0 ? 'The change could not be sent. Check your connection and try again.' : 'The change was not saved.');
  const rest = [f.detail && f.detail !== f.title ? f.detail : null, ...f.problems].filter((x): x is string => !!x);
  return rest.length > 0 ? `${lead}. ${rest.join(' ')}` : lead;
}

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

export interface OverridesApi {
  assignProcedure(core: CoreRoute, recordId: string, body: AssignRequest): Promise<WriteResult<OverrideCreated>>;
  excludeRule(core: CoreRoute, recordId: string, body: ExcludeRuleRequest): Promise<WriteResult<OverrideCreated>>;
  /** Task 084c: change a rule for the record (and one firm's invoices when `firmId` is given). */
  changeRule(core: CoreRoute, recordId: string, body: ChangeRuleRequest): Promise<WriteResult<RuleChanged>>;
  /** `signal` abandons the read (the wizard closed); a read that takes longer than the client's timeout is a failure with {@link CODE_TIMED_OUT} (readRace.ts). */
  removalPreview(core: CoreRoute, recordId: string, overrideId: string, signal?: AbortSignal): Promise<WriteResult<RemovalPreview>>;
  /** Task 084h-4: `note` (optional) travels as a JSON body `{ "note" }`; with none the DELETE has no body at all. */
  removeOverride(core: CoreRoute, recordId: string, overrideId: string, note?: string): Promise<WriteResult<Removed>>;
  /** The versions of a Procedure, for the Pin picker (task 081's read). */
  procedureVersions(procedureId: string): Promise<WriteResult<PinnableVersion[]>>;
}

const trimBase = (bffBaseUrl: string): string => bffBaseUrl.replace(/\/+$/, '');
const seg = (id: string): string => encodeURIComponent(id.toLowerCase());

function recordBase(bffBaseUrl: string, core: CoreRoute, recordId: string): string {
  return `${trimBase(bffBaseUrl)}/api/finance/${core}/${seg(recordId)}`;
}

export const assignmentsUrl = (bff: string, core: CoreRoute, recordId: string): string => `${recordBase(bff, core, recordId)}/procedure-assignments`;
export const excludeRuleUrl = (bff: string, core: CoreRoute, recordId: string): string => `${recordBase(bff, core, recordId)}/rule-overrides/exclude`;
export const changeRuleUrl = (bff: string, core: CoreRoute, recordId: string): string => `${recordBase(bff, core, recordId)}/rule-overrides/change`;
export const overrideUrl =(bff: string, core: CoreRoute, recordId: string, overrideId: string): string => `${recordBase(bff, core, recordId)}/overrides/${seg(overrideId)}`;
export const removalPreviewUrl = (bff: string, core: CoreRoute, recordId: string, overrideId: string): string => `${overrideUrl(bff, core, recordId, overrideId)}/removal-preview`;
export const procedureUrl = (bff: string, procedureId: string): string => `${trimBase(bff)}/api/finance/procedures/${seg(procedureId)}`;

const BAD_BODY: WriteFailure = { status: 200, errorCode: null, title: 'The server answered with an unexpected body.', detail: null, problems: [], fieldErrors: [], signInFailed: false };
const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);

const JSON_HEADERS = { 'Content-Type': 'application/json' };

/**
 * Task 084h-4: the server's limit on a note, after trimming (`InvoiceCorrectionNote.MaxLength`; 400 `assignment.note_too_long` above it).
 * The one source of the limit in this page: a reason's `MAX_REASON_CHARS` (plan.ts) is this number. The Note box does not cut a longer text
 * (a paste is never silently truncated): `noteProblem` holds Finish and says so, and the box shows a count against this limit.
 * Pinned to the server's source by `__tests__/serverWordsPin.test.ts`.
 */
export const MAX_NOTE_CHARS = 2000;

/** The stable codes of the server's 400 for a refused note (`ProcedureAssignmentService.CodeNoteTooLong` / `CodeNoteInvalidCharacters`). */
export const NOTE_ERROR_CODES: readonly string[] = ['assignment.note_too_long', 'assignment.note_invalid_characters'];

/** What the server refuses in a note (`InvoiceCorrectionNote`): a control character other than newline, carriage return and tab. */
// eslint-disable-next-line no-control-regex
const REFUSED_CONTROL = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/;

/** The note to send: trimmed, or undefined when it is blank (a blank note is absent, never null or ""). */
export function noteToSend(note: string | null | undefined): string | undefined {
  const t = (note ?? '').trim();
  return t.length > 0 ? t : undefined;
}

/** Why a note cannot be sent (the server's 400 `assignment.note_too_long` / `assignment.note_invalid_characters`, said before the call), or null. */
export function noteProblem(note: string | null | undefined): string | null {
  const t = noteToSend(note);
  if (t === undefined) return null;
  if (t.length > MAX_NOTE_CHARS) return `The note may be at most ${MAX_NOTE_CHARS} characters.`;
  if (REFUSED_CONTROL.test(t)) return 'The note holds a control character. Only line breaks and tabs are allowed.';
  return null;
}

/** True when a change's answer says its Decision Record was neither written nor queued. Absent or any other status: false. */
export const notRecorded = (v: { decisionRecord?: DecisionRecordStatus | null } | null | undefined): boolean => v?.decisionRecord?.status === DECISION_RECORD_NOT_RECORDED;

/** Drop the members that are undefined or blank, so the body is exactly the route's request shape. */
function compact<T extends object>(body: T): Record<string, unknown> {
  return Object.fromEntries(Object.entries(body).filter(([, v]) => v !== undefined && v !== null && !(typeof v === 'string' && v.trim() === '')));
}

export function createOverridesApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string, options: { timeoutMs?: number } = {}): OverridesApi {
  const timeoutMs = options.timeoutMs ?? READ_TIMEOUT_MS;
  async function run<T>(url: string, init: RequestInit, read: (body: unknown) => T | null): Promise<WriteResult<T>> {
    try {
      const res = await fetchFn(url, init);
      const value = read((await res.json()) as unknown);
      // A 2xx whose body is not what the route returns is a failure: the wizard must not report a change it cannot name.
      return value === null ? { ok: false, failure: BAD_BODY } : { ok: true, value };
    } catch (err) {
      return { ok: false, failure: toWriteFailure(err) };
    }
  }

  const created = (body: unknown): OverrideCreated | null => (isObject(body) && typeof body.id === 'string' ? (body as unknown as OverrideCreated) : null);

  return {
    assignProcedure: (core, recordId, body) =>
      run(
        assignmentsUrl(bffBaseUrl, core, recordId),
        {
          method: 'POST',
          headers: JSON_HEADERS,
          body: JSON.stringify(compact({ procedureId: body.procedureId, mode: body.mode, pinnedVersionId: body.pinnedVersionId, reason: body.reason?.trim(), note: noteToSend(body.note) })),
        },
        created
      ),

    excludeRule: (core, recordId, body) =>
      run(
        excludeRuleUrl(bffBaseUrl, core, recordId),
        { method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(compact({ policyId: body.policyId, reason: body.reason.trim(), firmId: body.firmId, note: noteToSend(body.note) })) },
        created
      ),

    changeRule: (core, recordId, body) =>
      run(
        changeRuleUrl(bffBaseUrl, core, recordId),
        { method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(compact({ policyId: body.policyId, reason: body.reason.trim(), firmId: body.firmId, ruleBody: body.ruleBody, note: noteToSend(body.note) })) },
        b => (isObject(b) && isObject(b.override) && typeof b.override.id === 'string' && isObject(b.recordRule) ? (b as unknown as RuleChanged) : null)
      ),

    removalPreview: async (core, recordId, overrideId, signal) => {
      try {
        const raced = await raceTimeout(timeoutMs, signal, async s => {
          const res = await fetchFn(removalPreviewUrl(bffBaseUrl, core, recordId, overrideId), { method: 'GET', signal: s });
          const b = (await res.json()) as unknown;
          return isObject(b) && typeof b.overrideId === 'string' ? (b as unknown as RemovalPreview) : null;
        });
        if (raced.kind === 'timeout') return { ok: false, failure: TIMED_OUT_FAILURE };
        if (raced.kind === 'aborted') return { ok: false, failure: ABANDONED_FAILURE };
        return raced.value === null ? { ok: false, failure: BAD_BODY } : { ok: true, value: raced.value };
      } catch (err) {
        return { ok: false, failure: toWriteFailure(err) };
      }
    },

    removeOverride: (core, recordId, overrideId, note) => {
      // A DELETE has a body only when a note is given (EB-D-239 Q8); otherwise it is exactly the request task 089 always took.
      const sent = noteToSend(note);
      const init: RequestInit = sent === undefined ? { method: 'DELETE' } : { method: 'DELETE', headers: JSON_HEADERS, body: JSON.stringify({ note: sent }) };
      return run(overrideUrl(bffBaseUrl, core, recordId, overrideId), init, b =>
        isObject(b) && typeof b.overrideId === 'string' ? (b as unknown as Removed) : null
      );
    },

    procedureVersions: procedureId =>
      run(procedureUrl(bffBaseUrl, procedureId), { method: 'GET' }, b => {
        if (!isObject(b) || !Array.isArray(b.versions)) return null;
        return (b.versions as Array<Record<string, unknown>>).map(v => ({
          id: String(v.id),
          number: Number(v.versionNumber),
          status: String(v.status),
          from: str(v.effectiveFrom),
          to: str(v.effectiveTo),
        }));
      }),
  };
}
