/**
 * Invoice Review API client (ebilling-r1 task 110): every BFF call the page makes, through `authenticatedFetch` (ADR-028).
 *
 *   Task 100  GET  /api/finance/invoices/{invoiceId}/review                         the read model (header, totals, lines, findings, boxes)
 *             POST /api/finance/invoices/{invoiceId}/lines/{lineId}/decision        Accept / Reduce / Disallow one line
 *             POST /api/finance/invoices/{invoiceId}/lines/decisions                one action and reason on several lines (bulk, by rule)
 *             POST /api/finance/invoices/{invoiceId}/lines/accept-clean             Accept all Clean lines
 *   Task 101  POST /api/finance/invoices/{invoiceId}/lines/{lineId}/corrections     correct or confirm one line value
 *             POST /api/finance/invoices/{invoiceId}/header/corrections             correct or confirm one header value
 *             GET  /api/finance/invoices/{invoiceId}/corrections                    the invoice's correction rows, newest first
 *   Task 092  GET  /api/finance/files/{fileId}/content                              the invoice PDF bytes (the read model's invoicePdfFileId)
 *   Task 102  adjustment lines and the stored totals arrive in the read model; adding one is 111's approve wizard.
 *
 * The page decides nothing the server owns (ADR-002, I-1): it never computes a total, a status or who may decide. Every outcome is the
 * server's answer. `authenticatedFetch` THROWS on any non-2xx (`ApiError` with the parsed ProblemDetails) and on an exhausted 401
 * (`AuthError`); every outcome is decided in the `catch` through the `@spaarke/auth` guards. Mirrors
 * `src/solutions/InvoiceFiles/src/services/invoiceFilesApi.ts`.
 */

import { isApiError, isAuthFailure, problemOf, type AuthenticatedFetchFn } from '@spaarke/auth';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types (the BFF's JSON, camelCase): InvoiceReviewReadService.cs, InvoiceLineDecisionService.cs, InvoiceCorrectionService.cs
// ─────────────────────────────────────────────────────────────────────────────

export type Severity = 'Info' | 'Warning' | 'Critical';
export type LineStatus = 'Pending' | 'Clean' | 'Flagged' | 'Accepted' | 'Reduced' | 'Disallowed';
export type LineType = 'Fee' | 'Expense' | 'Adjustment';

export interface ReviewRef {
  id: string;
  name: string | null;
}

export interface ReviewCoreRecord {
  /** `matter`, `project` or `workAssignment`. */
  type: string;
  id: string;
  name: string | null;
}

export interface ReviewHeader {
  invoiceId: string;
  name: string | null;
  firmInvoiceNumber: string | null;
  trackingNumber: string | null;
  firm: ReviewRef | null;
  coreRecord: ReviewCoreRecord | null;
  /** ISO date (yyyy-mm-dd). */
  invoiceDate: string | null;
  periodStart: string | null;
  periodEnd: string | null;
  /** Lifecycle status name: `InReview`, `Approved`, `SentToAp`, `Paid`, `Rejected`, `ReturnedToFirm`, `Cancelled`, `Unknown`. */
  status: string;
  statusCode: number;
  /** A blocked internal submission awaiting Override / Return (D-101). The blocked state itself is task 111's. */
  held: boolean;
}

export interface ReviewTotals {
  billed: number | null;
  fees: number | null;
  expenses: number | null;
  lineAdjustments: number | null;
  invoiceAdjustments: number | null;
  approved: number | null;
}

export interface ReviewFlags {
  canRead: boolean;
  /** Write on the invoice (D-77) and the invoice is In Review. Advisory; the decide routes check again. */
  canDecide: boolean;
}

export interface ReviewTimekeeper {
  id: string;
  name: string | null;
  classification: number | null;
}

export interface ReviewCode {
  asBilled: string | null;
  code: string | null;
  firmCode: string | null;
  mismatch: boolean;
}

export interface ReviewCodes {
  task: ReviewCode;
  activity: ReviewCode;
  expense: ReviewCode;
  confidence: number | null;
  codeSetNeedsReview: boolean;
  notCoded: boolean;
  refused: string[];
}

export interface ReviewBox {
  page: number | null;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface ReviewField {
  confidence: number | null;
  combined: number | null;
  lowConfidence: boolean;
  page: number | null;
  box: ReviewBox | null;
  repair: string | null;
}

export interface ReviewValueRead {
  name: string;
  value: string | null;
  field: string | null;
  confidence: number | null;
  belowReliabilityThreshold: boolean;
}

export interface ReviewSuggestion {
  /** `Reduce` (amount = what to take off the line) or `Disallow` (amount = the line amount). */
  kind: string;
  amount: number | null;
}

export interface ReviewFinding {
  ruleType: string;
  policyCode: string;
  policyId: string | null;
  policyVersionId: string | null;
  clause: string | null;
  clauseLocation: string | null;
  reasonCode: string;
  /** Only the primary reason of a group carries the stored sentence (D-123); the screen words the others from the code. */
  message: string | null;
  severity: Severity | null;
  consequence: string | null;
  rejects: boolean;
  heldBecause: string | null;
  detailWithheld: boolean;
  overridden: boolean;
  suggestedAdjustment: ReviewSuggestion | null;
  valuesRead: ReviewValueRead[];
}

/** EB-D-175 / EB-D-176 / EB-D-101 review F2: why a line is Flagged when no policy finding says so. */
export interface ReviewLineFinding {
  /** `coding.not_returned`, `line.no_description`, `rules.not_checked`. */
  code: string;
  message: string | null;
}

export interface ReviewLine {
  id: string;
  sequence: number;
  type: LineType | string;
  date: string | null;
  timekeeperAsBilled: string | null;
  /** The roster entry the name matched; null = not matched (the unmatched mark). */
  timekeeper: ReviewTimekeeper | null;
  /** Hours (fee) or quantity (expense). */
  quantity: number | null;
  rate: number | null;
  approvedRate: number | null;
  amount: number;
  narrative: string | null;
  codes: ReviewCodes;
  status: LineStatus | string;
  /** The server's display label (Pending reads Processing, F-1). */
  statusLabel: string;
  approvedAmount: number | null;
  reason: string | null;
  reviewedBy: ReviewRef | null;
  reviewedOn: string | null;
  /** Policy findings only. */
  findingCount: number;
  highestSeverity: Severity | null;
  findings: ReviewFinding[];
  fields: Record<string, ReviewField>;
  lineFindings: ReviewLineFinding[];
}

export interface ReviewDecisionRecord {
  recordId: string;
  number: string | null;
  actionCode: string | null;
  subject: string;
  lineId: string | null;
  recordClass: string | null;
  outcome: string | null;
  decidedOn: string | null;
  decidedBy: ReviewRef | null;
  reason: string | null;
}

export interface InvoiceReview {
  header: ReviewHeader;
  totals: ReviewTotals;
  lines: ReviewLine[];
  invoiceFindings: ReviewFinding[];
  submission: unknown;
  decisionHistory: ReviewDecisionRecord[];
  lineCounts: Record<string, number>;
  flags: ReviewFlags;
  invoicePdfFileId: string | null;
}

export interface DecidedLine {
  lineId: string;
  status: string;
  approvedAmount: number;
  reason: string | null;
  reviewedOn: string;
  alreadyRecorded: boolean;
}

export interface LineDecisionResponse {
  decisionId: string;
  lines: DecidedLine[];
  totals: ReviewTotals | null;
}

export interface Correction {
  id: string;
  lineId: string | null;
  field: string;
  aiValue: string | null;
  finalValue: string | null;
  /** `Corrected`, `Confirmed individually`, `Accepted in bulk` ... */
  outcome: string;
  isLabel: boolean;
  aiConfidence: number | null;
  reviewedOn?: string | null;
}

export interface CorrectionResponse {
  correction: Correction;
  alreadyApplied: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Requests
// ─────────────────────────────────────────────────────────────────────────────

export type DecisionAction = 'accept' | 'reduce' | 'disallow';

export interface SingleDecision {
  /** The client's id; only makes a repeat idempotent. Reuse it to repeat a 503 incomplete. */
  decisionId: string;
  action: DecisionAction;
  reason: string | null;
  /** Reduce only: the approved amount (between 0 and the billed amount; the server checks). */
  approvedAmount: number | null;
}

export interface MultiDecision {
  decisionId: string;
  action: DecisionAction;
  reason: string | null;
  lines: Array<{ lineId: string; approvedAmount: number | null }>;
}

export interface CorrectionRequest {
  field: string;
  value: string | null;
  confirm: boolean;
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
  /** `failedStep` of a 503 incomplete (the change was saved; repeat the same request to finish). */
  failedStep: string | null;
}

export type ApiResult<T> = { ok: true; value: T } | { ok: false; failure: ApiFailure };

export const CODE_EBILLING_DISABLED = 'ebilling.disabled';
export const CODE_DECISION_INCOMPLETE = 'ebilling.decision.incomplete';
export const CODE_CORRECTION_INCOMPLETE = 'ebilling.correction.incomplete';

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
      // Only the server's own ProblemDetails detail is shown; the error's message is transport text ("HTTP 409").
      detail: str(p?.detail),
      signInFailed: isAuthFailure(err),
      failedStep: str(p?.failedStep),
    };
  }
  return {
    status: 0,
    errorCode: null,
    detail: err instanceof Error ? err.message : null,
    signInFailed: isAuthFailure(err),
    failedStep: null,
  };
}

/** True when the change was saved but a later step did not finish: the same request, repeated, completes it. */
export function isIncomplete(f: ApiFailure): boolean {
  return f.errorCode === CODE_DECISION_INCOMPLETE || f.errorCode === CODE_CORRECTION_INCOMPLETE;
}

/** The page-level sentence for a failed read of the review. */
export function loadFailureMessage(f: ApiFailure): string {
  if (f.signInFailed) return 'Sign-in could not be verified for this session. Retry, or reload the page.';
  if (f.errorCode === CODE_EBILLING_DISABLED) return 'eBilling is not enabled in this environment.';
  if (f.status === 403) return 'You do not have access to this invoice.';
  if (f.status === 404) return 'This invoice could not be found, or you do not have access to it.';
  if (f.status === 503) return 'The invoice is temporarily unavailable. Retry in a moment.';
  if (f.status === 0) return 'The invoice could not be loaded. Check your connection and retry.';
  return 'The invoice could not be loaded. Retry in a moment.';
}

/**
 * The sentence shown for a refused write. The server's `detail` is its own explanation (ProblemDetails, ADR-019) and is shown as is,
 * with the stable code beside it; a missing detail falls back to a fixed sentence per status.
 */
export function writeFailureMessage(f: ApiFailure): string {
  if (f.signInFailed) return 'Sign-in could not be verified for this session. Retry, or reload the page.';
  if (f.errorCode === CODE_EBILLING_DISABLED) return 'eBilling is not enabled in this environment.';
  if (isIncomplete(f)) {
    return `${f.detail ?? 'The change was saved, but a later step did not finish.'} Repeat it to finish.`;
  }
  if (f.detail) return f.detail;
  if (f.status === 403) return 'You do not have permission to change this invoice.';
  if (f.status === 404) return 'This invoice or line could not be found.';
  if (f.status === 409) return 'The invoice changed or cannot be changed now. Reload and try again.';
  if (f.status === 0) return 'The change could not be sent. Check your connection and retry.';
  return 'The change could not be made. Try again in a moment.';
}

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

export interface InvoiceReviewApi {
  getReview(invoiceId: string): Promise<ApiResult<InvoiceReview>>;
  decideLine(invoiceId: string, lineId: string, decision: SingleDecision): Promise<ApiResult<LineDecisionResponse>>;
  decideLines(invoiceId: string, decision: MultiDecision): Promise<ApiResult<LineDecisionResponse>>;
  acceptClean(invoiceId: string, decisionId: string): Promise<ApiResult<LineDecisionResponse>>;
  correctLine(invoiceId: string, lineId: string, request: CorrectionRequest): Promise<ApiResult<CorrectionResponse>>;
  correctHeader(invoiceId: string, request: CorrectionRequest): Promise<ApiResult<CorrectionResponse>>;
  listCorrections(invoiceId: string): Promise<ApiResult<Correction[]>>;
  /** The invoice PDF bytes. */
  fetchFile(fileId: string): Promise<ApiResult<ArrayBuffer>>;
}

function trimBase(bffBaseUrl: string): string {
  return bffBaseUrl.replace(/\/+$/, '');
}

function id(value: string): string {
  return encodeURIComponent(value.toLowerCase());
}

/** Route builders. `bffBaseUrl` is the BFF host without `/api` (runtime config). Exported for the tests. */
export const routes = {
  review: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/review`,
  decideLine: (base: string, invoiceId: string, lineId: string) =>
    `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/lines/${id(lineId)}/decision`,
  decideLines: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/lines/decisions`,
  acceptClean: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/lines/accept-clean`,
  correctLine: (base: string, invoiceId: string, lineId: string) =>
    `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/lines/${id(lineId)}/corrections`,
  correctHeader: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/header/corrections`,
  corrections: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/corrections`,
  fileContent: (base: string, fileId: string) => `${trimBase(base)}/api/finance/files/${id(fileId)}/content`,
};

const unexpected = (what: string): ApiFailure => ({
  status: 200,
  errorCode: null,
  detail: `The server answered with an unexpected ${what}.`,
  signInFailed: false,
  failedStep: null,
});

function isReview(body: unknown): body is InvoiceReview {
  const b = body as InvoiceReview | null;
  return !!b && typeof b === 'object' && !!b.header && !!b.totals && Array.isArray(b.lines) && !!b.flags;
}

export function createInvoiceReviewApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string): InvoiceReviewApi {
  async function post<T>(url: string, body: unknown): Promise<ApiResult<T>> {
    try {
      const res = await fetchFn(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      return { ok: true, value: (await res.json()) as T };
    } catch (err) {
      return { ok: false, failure: toFailure(err) };
    }
  }

  return {
    async getReview(invoiceId) {
      try {
        const res = await fetchFn(routes.review(bffBaseUrl, invoiceId), { method: 'GET' });
        const body = (await res.json()) as unknown;
        // A body that is not the read model is a failure, never an empty invoice.
        return isReview(body) ? { ok: true, value: body } : { ok: false, failure: unexpected('review body') };
      } catch (err) {
        return { ok: false, failure: toFailure(err) };
      }
    },

    decideLine(invoiceId, lineId, d) {
      return post(routes.decideLine(bffBaseUrl, invoiceId, lineId), {
        decisionId: d.decisionId,
        action: d.action,
        reason: d.reason,
        approvedAmount: d.approvedAmount,
      });
    },

    decideLines(invoiceId, d) {
      return post(routes.decideLines(bffBaseUrl, invoiceId), {
        decisionId: d.decisionId,
        action: d.action,
        reason: d.reason,
        lines: d.lines,
      });
    },

    acceptClean(invoiceId, decisionId) {
      return post(routes.acceptClean(bffBaseUrl, invoiceId), { decisionId });
    },

    correctLine(invoiceId, lineId, r) {
      return post(routes.correctLine(bffBaseUrl, invoiceId, lineId), { field: r.field, value: r.value, confirm: r.confirm });
    },

    correctHeader(invoiceId, r) {
      return post(routes.correctHeader(bffBaseUrl, invoiceId), { field: r.field, value: r.value, confirm: r.confirm });
    },

    async listCorrections(invoiceId) {
      try {
        const res = await fetchFn(routes.corrections(bffBaseUrl, invoiceId), { method: 'GET' });
        const body = (await res.json()) as { corrections?: unknown } | null;
        return Array.isArray(body?.corrections)
          ? { ok: true, value: body!.corrections as Correction[] }
          : { ok: false, failure: unexpected('corrections body') };
      } catch (err) {
        return { ok: false, failure: toFailure(err) };
      }
    },

    async fetchFile(fileId) {
      try {
        const res = await fetchFn(routes.fileContent(bffBaseUrl, fileId), { method: 'GET' });
        return { ok: true, value: await res.arrayBuffer() };
      } catch (err) {
        return { ok: false, failure: toFailure(err) };
      }
    },
  };
}

/** A new client decision id (crypto.randomUUID where available). */
export function newDecisionId(): string {
  const c = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (c?.randomUUID) return c.randomUUID();
  // Fallback for hosts without randomUUID: an RFC 4122 v4 shape from Math.random (idempotency only, not security).
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, ch => {
    const r = (Math.random() * 16) | 0;
    return (ch === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}
