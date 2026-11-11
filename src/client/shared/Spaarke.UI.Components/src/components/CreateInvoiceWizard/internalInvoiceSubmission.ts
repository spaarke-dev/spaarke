/**
 * internalInvoiceSubmission.ts — the internal host of the shared InvoiceSubmission wizard (ebilling-r1 task 052).
 *
 * The internal CreateInvoiceWizard no longer creates anything itself (ADR-002 / FR-17 / D-19): it submits the PDF to the BFF's
 * internal routes (task 044) and shows the server's answer. This module is the transport adapter between those routes and the
 * three callbacks `InvoiceSubmissionWizard` takes (task 051), plus the two calls of the blocked-submission panel (task 105):
 *
 *   POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions?firmId=   (multipart; 202 {submissionId, ...})
 *   GET  /api/finance/invoice-submissions/{id}                                              (status view)
 *   GET  /api/finance/core-records/{Matter|Project|WorkAssignment}/{id}/firms               (the active firms linked to the record)
 *   GET  /api/finance/submissions/{id}/blocked                                              (state, findings, canOverride, canReturn)
 *   POST /api/finance/submissions/{id}/override | /return                                   ({ reason })
 *
 * Reads of the records the user may choose are Dataverse reads through the injected `IDataService` (the same reads the pickers of
 * the other wizards make); no write to Dataverse is made from here. The server decides everything that matters: the firm link
 * (D-106), authorization, the override privilege (`canOverride`), the reason, the invoice state. A refusal is shown by its code and
 * nothing changes on the client (FR-19).
 *
 * The injected fetch may THROW an `ApiError` (what `@spaarke/auth`'s `authenticatedFetch` does for every non-2xx answer) or RETURN a
 * non-2xx `Response`; both are handled (`utils/thrownFetchError.ts`, `utils/fetchTypes.ts`).
 *
 * ADR-012: no `@spaarke/auth` import; the host injects the fetch and the base URL.
 */
import type { IDataService } from '../../types/serviceInterfaces';
import type { ResponseFetchFn } from '../../utils/fetchTypes';
import { isApiError, problemOf } from '../../utils/thrownFetchError';
import {
  FIRM_REQUIRED_ERROR_CODE,
  InvoiceSubmissionError,
  type InvoiceCoreRecordKind,
  type InvoiceCoreRecordList,
  type InvoiceCoreRecordOption,
  type InvoiceFirmOption,
  type InvoiceRecordQuery,
  type InvoiceSubmissionAccepted,
  type InvoiceSubmissionInput,
  type InvoiceSubmissionStatusView,
} from '../InvoiceSubmission';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** The submission's `outcomeReasonCode` while it is held for Override / Return (task 105, D-101). */
export const BLOCKED_REASON_CODE = 'ebilling.submission.blocked';
/** Rejected after a Return to firm. */
export const RETURNED_REASON_CODE = 'ebilling.submission.returned';
/** Accepted after an Override. */
export const OVERRIDDEN_REASON_CODE = 'ebilling.submission.overridden';

/** A record has no firm linked to it, so no invoice can be submitted for it (client-side wording only; the server would answer `firm_invalid`). */
export const NO_FIRM_LINKED_ERROR_CODE = 'ebilling.submission.no_firm_linked';

/** The longest reason the server accepts (`SubmissionOverrideService`). */
export const MAX_DECISION_REASON_LENGTH = 2000;

/** Rows asked of each record source. A source that returns this many is flagged as possibly cut off. */
export const RECORD_PAGE_SIZE = 200;

const FINANCE = '/api/finance';

/** The three core-record kinds: Dataverse entity, route segment, firms-route type name and the columns read for the picker. */
const CORE_KINDS: Readonly<
  Record<
    InvoiceCoreRecordKind,
    { entity: string; segment: string; routeType: string; id: string; name: string; number: string }
  >
> = {
  matter: {
    entity: 'sprk_matter',
    segment: 'matters',
    routeType: 'Matter',
    id: 'sprk_matterid',
    name: 'sprk_mattername',
    number: 'sprk_matternumber',
  },
  project: {
    entity: 'sprk_project',
    segment: 'projects',
    routeType: 'Project',
    id: 'sprk_projectid',
    name: 'sprk_name',
    number: 'sprk_referencenumber',
  },
  workassignment: {
    entity: 'sprk_workassignment',
    segment: 'workassignments',
    routeType: 'WorkAssignment',
    id: 'sprk_workassignmentid',
    name: 'sprk_name',
    number: 'sprk_workassignmentnumber',
  },
};

const KIND_ORDER: readonly InvoiceCoreRecordKind[] = ['matter', 'project', 'workassignment'];

// ---------------------------------------------------------------------------
// Pure helpers (exported for unit testing)
// ---------------------------------------------------------------------------

/** The association a host launches the wizard with (the shape `useWizardPageBootstrap` provides). */
export interface IInitialInvoiceAssociation {
  entityType: string;
  recordId: string;
  recordName: string;
}

/**
 * The core record an `initialAssociation` names, or undefined when it names something an invoice cannot be submitted against
 * (an invoice is submitted against a matter, a project or a work assignment: the core-record set).
 */
export function resolveInitialInvoiceRecord(
  association: IInitialInvoiceAssociation | undefined
): InvoiceCoreRecordOption | undefined {
  if (!association?.recordId) return undefined;
  const kind = KIND_ORDER.find(k => CORE_KINDS[k].entity === association.entityType);
  if (!kind) return undefined;
  return { kind, recordId: stripBraces(association.recordId), name: association.recordName || association.recordId };
}

const stripBraces = (id: string): string => id.replace(/[{}]/g, '').toLowerCase();

const recordKey = (record: Pick<InvoiceCoreRecordOption, 'kind' | 'recordId'>): string =>
  `${record.kind}:${record.recordId.toLowerCase()}`;

/** The submission is held for a decision: Rejected with the blocked code (the one state Override / Return act on). */
export function isBlockedStatus(status: Pick<InvoiceSubmissionStatusView, 'status' | 'outcomeReasonCode'>): boolean {
  return status.status === 'Rejected' && status.outcomeReasonCode === BLOCKED_REASON_CODE;
}

// ---------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------

/** What the server's ProblemDetails (a thrown `ApiError` body or a returned response body) says, as the screen's error type. */
function fromProblem(status: number | undefined, problem: Record<string, unknown> | null): InvoiceSubmissionError {
  const detail = typeof problem?.detail === 'string' && problem.detail.trim() ? problem.detail : undefined;
  const errorCode = typeof problem?.errorCode === 'string' && problem.errorCode ? problem.errorCode : undefined;
  return new InvoiceSubmissionError({ statusCode: status, errorCode, detail });
}

/** A thrown failure of the injected fetch as the screen's error type. No answer at all (a network failure) carries no status. */
export function toInvoiceSubmissionError(err: unknown): InvoiceSubmissionError {
  if (err instanceof InvoiceSubmissionError) return err;
  if (isApiError(err)) return fromProblem(err.status, problemOf(err));
  return new InvoiceSubmissionError({ message: err instanceof Error ? err.message : undefined });
}

async function errorFromResponse(response: Response): Promise<InvoiceSubmissionError> {
  // A body that is not ProblemDetails JSON leaves the status alone to speak.
  const body: unknown = await response.json().catch(() => null);
  const problem = typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : null;
  return fromProblem(response.status, problem);
}

// ---------------------------------------------------------------------------
// Blocked-submission contract (task 105)
// ---------------------------------------------------------------------------

/** `BlockedSubmissionStates` of the server: what the read says the submission is. */
export type BlockedSubmissionState = 'Blocked' | 'Overridden' | 'Returned' | 'NotBlocked';

/** One finding of the stored evaluation (the fields the panel shows). */
export interface BlockedFinding {
  index: number;
  ruleType?: string | null;
  policyCode?: string | null;
  clause?: string | null;
  clauseLocation?: string | null;
  subject?: string | null;
  line?: number | null;
  reasonCode?: string | null;
  detailWithheld?: boolean;
  consequence?: string | null;
  /** True for a finding that made the rejection rule reject. */
  blocking: boolean;
  heldBecause?: string | null;
  overridden?: boolean;
}

/** Who decided, when and why (internal reviewers only). */
export interface BlockedDecisionStamp {
  recordId: string;
  bySystemUserId: string;
  on: string;
  reason: string;
}

/** The `GET /api/finance/submissions/{id}/blocked` body. `canOverride` / `canReturn` are the server's own answer: the client reads no role. */
export interface BlockedSubmissionView {
  submissionId: string;
  invoiceId?: string | null;
  trackingNumber?: string | null;
  firmInvoiceNumber?: string | null;
  status?: string | null;
  outcomeReasonCode?: string | null;
  state: BlockedSubmissionState | string;
  canOverride: boolean;
  canReturn: boolean;
  findings: BlockedFinding[];
  override?: BlockedDecisionStamp | null;
  return?: BlockedDecisionStamp | null;
}

export type BlockedDecisionVerb = 'override' | 'return';

/** The 200 body of an override or a return. */
export interface BlockedDecisionResponse {
  submissionId: string;
  invoiceId?: string | null;
  outcome: string;
  recordId?: string | null;
  alreadyDecided: boolean;
}

/** Stable codes of task 105 the panel words itself (the server's own `detail` wins when it wrote one). */
const DECISION_MESSAGE_BY_CODE: Readonly<Record<string, string>> = {
  'ebilling.override.not_permitted':
    'You do not hold the eBilling Override privilege, so this invoice cannot be overridden. Nothing was changed.',
  'ebilling.reason_required': 'Enter a reason.',
  'ebilling.reason_too_long': `The reason is too long (at most ${MAX_DECISION_REASON_LENGTH} characters).`,
  'ebilling.blocked.unavailable':
    'The decision could not be completed right now. Nothing was lost; try the same action again.',
  'ebilling.blocked.record_unavailable':
    'The decision could not be recorded right now. Nothing was changed; try again shortly.',
  'ebilling.blocked.in_progress': 'Another decision on this invoice is in progress. Try again in a moment.',
  'ebilling.blocked.concurrent_change':
    'This submission changed while you were deciding. Reload it and check its state.',
  'ebilling.blocked.already_overridden': 'This submission was already accepted by an override.',
  'ebilling.blocked.already_returned': 'This submission was already returned to the firm.',
};

const DECISION_MESSAGE_BY_STATUS: Readonly<Record<number, string>> = {
  401: 'Your sign-in is no longer valid. Reload the page to sign in again.',
  403: 'You are not allowed to decide this submission. Nothing was changed.',
  404: 'That submission was not found, or you do not have access to it.',
  409: 'The submission cannot be decided in its current state. Nothing was changed.',
};

/** The message the screen shows for a refused Override or Return (or a failed read of the held submission). */
export function blockedDecisionErrorMessage(err: unknown): string {
  if (err instanceof InvoiceSubmissionError) {
    const detail = err.detail?.trim();
    if (detail) return detail;
    if (err.errorCode && DECISION_MESSAGE_BY_CODE[err.errorCode]) return DECISION_MESSAGE_BY_CODE[err.errorCode];
    if (err.statusCode !== undefined) {
      if (DECISION_MESSAGE_BY_STATUS[err.statusCode]) return DECISION_MESSAGE_BY_STATUS[err.statusCode];
      if (err.statusCode >= 500) return 'The service is unavailable right now. Try again shortly.';
    }
  }
  return 'The request could not be completed, so it is not known whether the decision was recorded. Reload the submission to check it.';
}

// ---------------------------------------------------------------------------
// The client
// ---------------------------------------------------------------------------

export interface InternalInvoiceSubmissionOptions {
  /** The injected fetch (`@spaarke/auth`'s `authenticatedFetch` in a Dataverse code page). */
  fetch: ResponseFetchFn;
  /** The BFF base URL, with or without a trailing slash. */
  baseUrl: string;
  /** Dataverse reads for the record picker. */
  dataService: IDataService;
  /** The record the wizard was launched from, when there is one. */
  initialRecord?: InvoiceCoreRecordOption;
  /** When true (and `initialRecord` is set) the picker offers only that record (a visual / form launch). */
  lockRecord?: boolean;
}

export interface InternalInvoiceSubmission {
  loadRecords: (query: InvoiceRecordQuery) => Promise<InvoiceCoreRecordList>;
  loadFirms: () => Promise<InvoiceFirmOption[] | null>;
  submit: (input: InvoiceSubmissionInput) => Promise<InvoiceSubmissionAccepted>;
  getStatus: (submissionId: string) => Promise<InvoiceSubmissionStatusView>;
  getBlocked: (submissionId: string) => Promise<BlockedSubmissionView>;
  decide: (verb: BlockedDecisionVerb, submissionId: string, reason: string) => Promise<BlockedDecisionResponse>;
}

const asString = (value: unknown): string | null => (typeof value === 'string' ? value : null);

export function createInternalInvoiceSubmission(options: InternalInvoiceSubmissionOptions): InternalInvoiceSubmission {
  const { dataService, initialRecord } = options;
  const lockedRecord = options.lockRecord && initialRecord ? initialRecord : undefined;
  const root = options.baseUrl.replace(/\/+$/, '');
  const doFetch = options.fetch;

  /** One BFF call: the parsed JSON body of a 2xx answer, or the screen's error for any failure (thrown or returned). */
  async function call<T>(path: string, init: RequestInit): Promise<T> {
    let response: Response;
    try {
      response = await doFetch(`${root}${path}`, init);
    } catch (err) {
      throw toInvoiceSubmissionError(err);
    }
    if (!response.ok) throw await errorFromResponse(response);
    try {
      return (await response.json()) as T;
    } catch {
      throw new InvoiceSubmissionError({
        statusCode: response.status,
        message: 'The answer of the server could not be read.',
      });
    }
  }

  // The firms linked to a record (D-106), by record, so the prefetch and the submit ask the server once.
  const firmsByRecord = new Map<string, InvoiceFirmOption[]>();
  // The record of the latest submit: if the server (this adapter) needs the user to choose a firm, the wizard asks `loadFirms` again.
  let lastSubmitted: InvoiceCoreRecordOption | undefined;

  async function firmsOf(record: InvoiceCoreRecordOption): Promise<InvoiceFirmOption[]> {
    const key = recordKey(record);
    const known = firmsByRecord.get(key);
    if (known) return known;
    const rows = await call<unknown>(
      `${FINANCE}/core-records/${CORE_KINDS[record.kind].routeType}/${encodeURIComponent(record.recordId)}/firms`,
      { method: 'GET', headers: { Accept: 'application/json' } }
    );
    if (!Array.isArray(rows)) {
      throw new InvoiceSubmissionError({ message: 'The answer of the server about the firms could not be read.' });
    }
    const firms = rows
      .filter(
        (row): row is { firmId: string; name: string } =>
          !!row && typeof row.firmId === 'string' && typeof row.name === 'string'
      )
      .map(row => ({ firmId: row.firmId, name: row.name }));
    firmsByRecord.set(key, firms);
    return firms;
  }

  async function readRecords(kind: InvoiceCoreRecordKind): Promise<InvoiceCoreRecordOption[]> {
    const spec = CORE_KINDS[kind];
    const query =
      `?$select=${spec.id},${spec.name},${spec.number}` +
      `&$filter=statecode eq 0&$orderby=${spec.number} asc&$top=${RECORD_PAGE_SIZE}`;
    const result = await dataService.retrieveMultipleRecords(spec.entity, query);
    return result.entities
      .filter(row => typeof row[spec.id] === 'string')
      .map(row => ({
        kind,
        recordId: stripBraces(String(row[spec.id])),
        name: asString(row[spec.name]) ?? asString(row[spec.number]) ?? '',
        number: asString(row[spec.number]),
      }));
  }

  return {
    async loadRecords(): Promise<InvoiceCoreRecordList> {
      // A locked launch offers only its record. No read, nothing to be partial.
      if (lockedRecord) return { records: [lockedRecord] };

      const reads = await Promise.allSettled(KIND_ORDER.map(readRecords));
      const fulfilled = reads.filter(
        (r): r is PromiseFulfilledResult<InvoiceCoreRecordOption[]> => r.status === 'fulfilled'
      );
      if (fulfilled.length === 0) {
        throw new InvoiceSubmissionError({ message: 'Your records could not be loaded. Try again in a moment.' });
      }
      const records = fulfilled.flatMap(r => r.value);
      // The record the wizard was launched from is always offered, even when it is not in the first page of its source.
      if (initialRecord && !records.some(r => recordKey(r) === recordKey(initialRecord)))
        records.unshift(initialRecord);
      return {
        records,
        partial: fulfilled.length < reads.length,
        truncated: fulfilled.some(r => r.value.length >= RECORD_PAGE_SIZE),
      };
    },

    async loadFirms(): Promise<InvoiceFirmOption[] | null> {
      // The firms depend on the record (D-106). Known up front only for a locked launch; otherwise they are resolved when the
      // user submits (`submit` asks the wizard to show its firm step when the record has several).
      const record = lockedRecord ?? lastSubmitted;
      if (!record) return null;
      return firmsOf(record);
    },

    async submit(input: InvoiceSubmissionInput): Promise<InvoiceSubmissionAccepted> {
      lastSubmitted = input.record;
      let firmId = input.firmId;
      if (!firmId) {
        // The internal route needs the firm named. The server never picks one: this adapter names the record's only firm, and with
        // several asks the wizard (through `firm_required`) to let the user choose.
        const firms = await firmsOf(input.record);
        if (firms.length === 0) {
          throw new InvoiceSubmissionError({
            statusCode: 409,
            errorCode: NO_FIRM_LINKED_ERROR_CODE,
            detail:
              'No active firm is linked to this record, so an invoice cannot be submitted for it. Link the firm to the record and try again.',
          });
        }
        if (firms.length > 1) {
          throw new InvoiceSubmissionError({
            statusCode: 400,
            errorCode: FIRM_REQUIRED_ERROR_CODE,
            detail: 'More than one firm is linked to this record. Choose the firm this invoice is from.',
          });
        }
        firmId = firms[0].firmId;
      }

      const form = new FormData();
      form.append('file', input.file);
      form.append('firmInvoiceNumber', input.firmInvoiceNumber);
      if (input.poNumber) form.append('poNumber', input.poNumber);
      return call<InvoiceSubmissionAccepted>(
        `${FINANCE}/${CORE_KINDS[input.record.kind].segment}/${encodeURIComponent(input.record.recordId)}` +
          `/invoice-submissions?firmId=${encodeURIComponent(firmId)}`,
        { method: 'POST', headers: { Accept: 'application/json' }, body: form }
      );
    },

    getStatus: (submissionId: string) =>
      call<InvoiceSubmissionStatusView>(`${FINANCE}/invoice-submissions/${encodeURIComponent(submissionId)}`, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      }),

    getBlocked: (submissionId: string) =>
      call<BlockedSubmissionView>(`${FINANCE}/submissions/${encodeURIComponent(submissionId)}/blocked`, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      }),

    decide: (verb: BlockedDecisionVerb, submissionId: string, reason: string) =>
      call<BlockedDecisionResponse>(`${FINANCE}/submissions/${encodeURIComponent(submissionId)}/${verb}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ reason }),
      }),
  };
}
