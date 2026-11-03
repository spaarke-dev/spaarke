/**
 * Invoice decision API client (ebilling-r1 task 111): every BFF call the decision wizard, the blocked-submission banner and the Context,
 * Related work and History panes make, through `authenticatedFetch` (ADR-028). Sits beside `invoiceReviewApi.ts` (task 110), whose
 * `toFailure` / `ApiResult` / `ApiFailure` it reuses.
 *
 *   Task 103  GET  /api/finance/invoices/{id}/approval                     eligible approvers, canApprove, open-line counts, who decided
 *             POST /api/finance/invoices/{id}/approve                      approve (no body: the decider is the caller, the decision id the portion's)
 *   Task 102  GET  /api/finance/invoices/{id}/adjustment-rules             rules a reviewer may cite on an invoice adjustment
 *             POST /api/finance/invoices/{id}/adjustments                  add an adjustment (method, value, reason, rule; the server computes the amount)
 *             DELETE /api/finance/invoices/{id}/adjustments/{lineId}       remove one
 *   Task 104  POST /api/finance/invoices/{id}/reject | /return             reject / return to firm (a reason is required)
 *   Task 105  GET  /api/finance/submissions/{id}/blocked                   the blocked internal submission: findings, canOverride, canReturn
 *             POST /api/finance/submissions/{id}/override | /return        override (privilege + reason) / return (reason)
 *   Task 106  GET  /api/finance/invoices/{id}/context                      five independent sections, each available / unavailable / notTracked
 *   Task 107  GET  /api/finance/invoices/{id}/lines/{lineId}/related-work  documents behind one fee line, or the empty reason
 *
 * The page decides nothing the server owns (ADR-002, I-1): it never computes a total, an amount or who may decide. Wire types follow
 * InvoiceApprovalService.cs, InvoiceAdjustmentService.cs, InvoiceRejectReturnEndpoints.cs, SubmissionOverrideService.cs,
 * InvoiceContextService.cs and InvoiceRelatedWorkService.cs (camelCase; DateOnly as yyyy-MM-dd).
 */

import type { AuthenticatedFetchFn } from '@spaarke/auth';
import { toFailure, type ApiFailure, type ApiResult } from './invoiceReviewApi';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types
// ─────────────────────────────────────────────────────────────────────────────

/** `Pending`, `Approved`, `Rejected`, `Returned`, `Unknown`, or `None` when the invoice has no approval portion. */
export type ApprovalStatus = 'Pending' | 'Approved' | 'Rejected' | 'Returned' | 'Unknown' | 'None' | string;

export interface ApprovalOpenLines {
  pending: number;
  flagged: number;
  total?: number;
}

export interface ApprovalState {
  invoiceId: string;
  approvalId: string | null;
  status: ApprovalStatus;
  /** Display names of the people who may approve (resolved live from the core record's approver roles). */
  eligibleApprovers: string[];
  /** Nobody may approve: the roles name no assigned person linked to a user (FR-14). */
  noEligibleApprover: boolean;
  /** The live resolution failed; `callerEligible` is false until it succeeds. */
  eligibilityUnavailable: boolean;
  /** The caller's own contact is one of the eligible approvers (EB-D-171). */
  callerEligible: boolean;
  /** Eligible, Write on the invoice, In Review, not held, no open line, no correction waiting, the portion Pending. */
  canApprove: boolean;
  openLines: ApprovalOpenLines;
  /** Clean lines with no decision that an approve accepts in bulk (D-72). */
  undecidedCleanLines: number;
  held: boolean;
  approvedAmount: number | null;
  decidedBy: string | null;
  decidedOn: string | null;
  decisionRecordId: string | null;
  eligibleResolvedOn: string | null;
}

export interface ApproveResponse {
  /** `approved` (this call) or `alreadyApproved` (a no-op). */
  outcome: string;
  decisionId: string | null;
  decisionRecordId: string | null;
  state: ApprovalState | null;
}

export interface DecisionResponse {
  /** `decided` (this call) or `alreadyDecided` (a no-op). */
  outcome: string;
  /** `Rejected` or `Returned`. */
  decision: string;
  decisionId: string | null;
  decisionRecordId: string | null;
}

export interface AdjustmentRule {
  ruleId: string;
  policyCode: string;
  headline: string | null;
  ruleSetId: string;
  ruleSetName: string | null;
  scope: string;
  version: number;
  clauseReference: string | null;
  clauseLocation: string | null;
}

export interface AdjustmentRulesResponse {
  asOf: string | null;
  rules: AdjustmentRule[];
}

export type AdjustmentMethod = 'percent' | 'fixed';

export interface AddAdjustmentRequest {
  /** The client's id; only makes a repeat idempotent (reuse it to repeat a 503 incomplete). */
  adjustmentId: string;
  method: AdjustmentMethod;
  value: number;
  reason: string;
  ruleId: string | null;
}

export interface AdjustmentDto {
  lineId: string;
  adjustmentId: string;
  method: string;
  value: number;
  amount: number;
  reason: string;
  ruleId: string | null;
  reviewedOn: string;
}

export interface AdjustmentResponse {
  adjustment: AdjustmentDto | null;
  removedLineId: string | null;
  totals: { billed: number | null; fees: number | null; expenses: number | null; lineAdjustments: number | null; invoiceAdjustments: number | null; approved: number | null } | null;
  alreadyApplied: boolean;
}

// Blocked submission (105)
export interface BlockedValueRead {
  name: string | null;
  value: string | null;
  line: number | null;
  field: string | null;
  confidence: number | null;
  belowReliabilityThreshold: boolean;
}

export interface BlockedFinding {
  index: number;
  ruleType: string | null;
  policyCode: string | null;
  policyVersionId: string | null;
  clause: string | null;
  clauseLocation: string | null;
  subject: string | null;
  line: number | null;
  reasonCode: string | null;
  detailWithheld: boolean;
  consequence: string | null;
  blocking: boolean;
  heldBecause: string | null;
  overridden: boolean;
  valuesRead: BlockedValueRead[];
}

export interface BlockedDecisionStamp {
  recordId: string;
  bySystemUserId: string;
  on: string;
  reason: string;
}

/** `Blocked`, `Overridden`, `Returned` or `NotBlocked`. */
export type BlockedState = 'Blocked' | 'Overridden' | 'Returned' | 'NotBlocked' | string;

export interface BlockedSubmission {
  submissionId: string;
  invoiceId: string | null;
  trackingNumber: string | null;
  firmInvoiceNumber: string | null;
  channel: string | null;
  status: string | null;
  outcomeReasonCode: string | null;
  state: BlockedState;
  /** Blocked, the invoice is still held, and the caller holds the eBilling Override privilege. */
  canOverride: boolean;
  /** Blocked and not decided otherwise; needs no privilege (D-73). */
  canReturn: boolean;
  findings: BlockedFinding[];
  override: BlockedDecisionStamp | null;
  return: BlockedDecisionStamp | null;
}

export interface BlockedDecisionResponse {
  submissionId: string;
  invoiceId: string | null;
  /** `Overridden` or `Returned`. */
  outcome: string;
  recordId: string | null;
  alreadyDecided: boolean;
}

// Context (106)
export type ContextSectionStatus = 'available' | 'unavailable' | 'notTracked' | string;

export interface ContextSection<T> {
  status: ContextSectionStatus;
  data: T | null;
  code: string | null;
  message: string | null;
}

export interface CoreRecordContext {
  type: string;
  id: string;
  name: string | null;
  number: string | null;
  statusCode: number | null;
  status: string | null;
  openedDate: string | null;
  closedDate: string | null;
}

export interface BudgetContext {
  budget: number | null;
  spendToDate: number | null;
  remaining: number | null;
  utilizationPercent: number | null;
  invoiceCount: number | null;
  asOf: string | null;
}

export interface PriorInvoiceItem {
  id: string;
  number: string | null;
  invoiceDate: string | null;
  billed: number | null;
  approved: number | null;
  status: string;
  statusCode: number;
}

export interface PriorInvoicesContext {
  limit: number;
  hasMore: boolean;
  items: PriorInvoiceItem[];
}

export interface DisallowReason {
  reason: string;
  count: number;
}

export interface DisallowHistoryContext {
  invoicesConsidered: number;
  reducedCount: number;
  reducedAmount: number;
  disallowedCount: number;
  disallowedAmount: number;
  topReasons: DisallowReason[];
}

export interface ApprovedRateItem {
  effectiveFrom: string | null;
  effectiveTo: string | null;
  approvedRate: number;
  rackRate: number | null;
  basis: string;
  scope: string;
}

export interface BilledRateItem {
  rate: number;
  lineCount: number;
  firstSeen: string | null;
  lastSeen: string | null;
}

export interface TimekeeperRateHistory {
  timekeeperId: string | null;
  name: string;
  approvedRates: ApprovedRateItem[];
  billedOnThisInvoice: BilledRateItem[];
  billedPreviously: BilledRateItem[];
}

export interface RateHistoryContext {
  timekeepers: TimekeeperRateHistory[];
}

export interface InvoiceContext {
  invoiceId: string;
  coreRecord: ContextSection<CoreRecordContext>;
  budget: ContextSection<BudgetContext>;
  priorInvoices: ContextSection<PriorInvoicesContext>;
  disallowHistory: ContextSection<DisallowHistoryContext>;
  rateHistory: ContextSection<RateHistoryContext>;
}

// Related work (107)
export interface RelatedWorkItem {
  documentId: string;
  name: string | null;
  createdOn: string;
  score: number;
}

/** `available`, `empty` (see `reason`) or `unavailable` (see `code` and `message`). */
export interface RelatedWork {
  invoiceId: string;
  lineId: string;
  status: string;
  reason: string | null;
  code: string | null;
  message: string | null;
  windowDays: number | null;
  truncated: boolean;
  items: RelatedWorkItem[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Failures
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Stable codes of a 503 where part of the change was saved: the same request, repeated, finishes it and writes nothing twice (approve and
 * reject / return derive their decision id server-side, an adjustment carries the client's id, a blocked decision's repeat completes the close).
 */
export const INCOMPLETE_CODES: ReadonlySet<string> = new Set([
  'ebilling.approval.incomplete',
  // The bulk accept of an approve stopped after some lines were written (InvoiceApprovalService.FromLineResult).
  'ebilling.decision.incomplete',
  'ebilling.invoice.decision_incomplete',
  'ebilling.adjustment.incomplete',
  'ebilling.blocked.unavailable',
]);

/**
 * A 503 that saved part of the change: one of the documented codes, or any 503 that names the step it stopped at or the lines it committed
 * (the approve route answers those with the line service's own code, so the list alone would miss a code it does not know yet).
 */
export const isDecisionIncomplete = (f: ApiFailure): boolean =>
  f.status === 503 && ((f.errorCode !== null && INCOMPLETE_CODES.has(f.errorCode)) || f.failedStep !== null || f.committedLineIds.length > 0);

/**
 * The sentence shown for a refused decision step. The server's `detail` is its own explanation (ADR-019) and is shown as is, followed by the
 * stable code in brackets (a 403 or 409 is shown with its code); a missing detail falls back to a fixed sentence per status. Raw transport
 * text never reaches here (`toFailure` drops it).
 */
export function decisionFailureMessage(f: ApiFailure): string {
  if (f.signInFailed) return 'Sign-in could not be verified for this session. Retry, or reload the page.';
  if (f.errorCode === 'ebilling.disabled') return 'eBilling is not enabled in this environment.';
  let base: string;
  if (f.detail) base = f.detail;
  else if (f.status === 403) base = 'You do not have permission to do this.';
  else if (f.status === 404) base = 'This invoice or record could not be found.';
  else if (f.status === 409) base = 'The invoice changed or cannot be changed now. Close this and reload the page.';
  else if (f.status === 0) base = 'The request could not be sent. Check your connection and retry.';
  else base = 'The request could not be completed. Try again in a moment.';
  const coded = f.errorCode ? `${base} (${f.errorCode})` : base;
  return isDecisionIncomplete(f) ? `${coded} It was saved in part: repeat it to finish.` : coded;
}

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

export interface InvoiceDecisionApi {
  getApproval(invoiceId: string): Promise<ApiResult<ApprovalState>>;
  approve(invoiceId: string): Promise<ApiResult<ApproveResponse>>;
  reject(invoiceId: string, reason: string): Promise<ApiResult<DecisionResponse>>;
  returnToFirm(invoiceId: string, reason: string): Promise<ApiResult<DecisionResponse>>;
  listAdjustmentRules(invoiceId: string): Promise<ApiResult<AdjustmentRulesResponse>>;
  addAdjustment(invoiceId: string, request: AddAdjustmentRequest): Promise<ApiResult<AdjustmentResponse>>;
  removeAdjustment(invoiceId: string, lineId: string): Promise<ApiResult<AdjustmentResponse>>;
  getBlocked(submissionId: string): Promise<ApiResult<BlockedSubmission>>;
  overrideBlocked(submissionId: string, reason: string): Promise<ApiResult<BlockedDecisionResponse>>;
  returnBlocked(submissionId: string, reason: string): Promise<ApiResult<BlockedDecisionResponse>>;
  getContext(invoiceId: string): Promise<ApiResult<InvoiceContext>>;
  getRelatedWork(invoiceId: string, lineId: string): Promise<ApiResult<RelatedWork>>;
}

function trimBase(bffBaseUrl: string): string {
  return bffBaseUrl.replace(/\/+$/, '');
}

function id(value: string): string {
  return encodeURIComponent(value.toLowerCase());
}

/** Route builders. `bffBaseUrl` is the BFF host without `/api` (runtime config). Exported for the tests. */
export const decisionRoutes = {
  approval: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/approval`,
  approve: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/approve`,
  reject: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/reject`,
  returnToFirm: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/return`,
  adjustmentRules: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/adjustment-rules`,
  adjustments: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/adjustments`,
  adjustment: (base: string, invoiceId: string, lineId: string) =>
    `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/adjustments/${id(lineId)}`,
  blocked: (base: string, submissionId: string) => `${trimBase(base)}/api/finance/submissions/${id(submissionId)}/blocked`,
  override: (base: string, submissionId: string) => `${trimBase(base)}/api/finance/submissions/${id(submissionId)}/override`,
  returnBlocked: (base: string, submissionId: string) => `${trimBase(base)}/api/finance/submissions/${id(submissionId)}/return`,
  context: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/context`,
  relatedWork: (base: string, invoiceId: string, lineId: string) =>
    `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/lines/${id(lineId)}/related-work`,
};

const unexpected = (what: string): ApiFailure => ({
  status: 200,
  errorCode: null,
  detail: `The server answered with an unexpected ${what}.`,
  signInFailed: false,
  failedStep: null,
  committedLineIds: [],
});

const isObject = (b: unknown): b is Record<string, unknown> => !!b && typeof b === 'object';

export function createInvoiceDecisionApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string): InvoiceDecisionApi {
  /** One call; a body that does not pass `valid` is a failure, never an empty answer. */
  async function call<T>(url: string, method: 'GET' | 'POST' | 'DELETE', body: unknown, valid: (b: unknown) => boolean, what: string): Promise<ApiResult<T>> {
    try {
      const res = await fetchFn(
        url,
        method === 'POST' ? { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) } : { method }
      );
      const parsed = (await res.json()) as unknown;
      return valid(parsed) ? { ok: true, value: parsed as T } : { ok: false, failure: unexpected(what) };
    } catch (err) {
      return { ok: false, failure: toFailure(err) };
    }
  }

  return {
    getApproval: invoiceId =>
      call(decisionRoutes.approval(bffBaseUrl, invoiceId), 'GET', null, b => isObject(b) && typeof b.status === 'string' && Array.isArray(b.eligibleApprovers), 'approval body'),
    approve: invoiceId => call(decisionRoutes.approve(bffBaseUrl, invoiceId), 'POST', {}, b => isObject(b) && typeof b.outcome === 'string', 'approve body'),
    reject: (invoiceId, reason) =>
      call(decisionRoutes.reject(bffBaseUrl, invoiceId), 'POST', { reason }, b => isObject(b) && typeof b.outcome === 'string', 'decision body'),
    returnToFirm: (invoiceId, reason) =>
      call(decisionRoutes.returnToFirm(bffBaseUrl, invoiceId), 'POST', { reason }, b => isObject(b) && typeof b.outcome === 'string', 'decision body'),
    listAdjustmentRules: invoiceId =>
      call(decisionRoutes.adjustmentRules(bffBaseUrl, invoiceId), 'GET', null, b => isObject(b) && Array.isArray(b.rules), 'adjustment rules body'),
    addAdjustment: (invoiceId, r) =>
      call(
        decisionRoutes.adjustments(bffBaseUrl, invoiceId),
        'POST',
        { adjustmentId: r.adjustmentId, method: r.method, value: r.value, reason: r.reason, ruleId: r.ruleId },
        isObject,
        'adjustment body'
      ),
    removeAdjustment: (invoiceId, lineId) => call(decisionRoutes.adjustment(bffBaseUrl, invoiceId, lineId), 'DELETE', null, isObject, 'adjustment body'),
    getBlocked: submissionId =>
      call(decisionRoutes.blocked(bffBaseUrl, submissionId), 'GET', null, b => isObject(b) && typeof b.state === 'string' && Array.isArray(b.findings), 'blocked body'),
    overrideBlocked: (submissionId, reason) =>
      call(decisionRoutes.override(bffBaseUrl, submissionId), 'POST', { reason }, b => isObject(b) && typeof b.outcome === 'string', 'override body'),
    returnBlocked: (submissionId, reason) =>
      call(decisionRoutes.returnBlocked(bffBaseUrl, submissionId), 'POST', { reason }, b => isObject(b) && typeof b.outcome === 'string', 'return body'),
    getContext: invoiceId =>
      call(decisionRoutes.context(bffBaseUrl, invoiceId), 'GET', null, b => isObject(b) && isObject(b.coreRecord) && isObject(b.budget), 'context body'),
    getRelatedWork: (invoiceId, lineId) =>
      call(decisionRoutes.relatedWork(bffBaseUrl, invoiceId, lineId), 'GET', null, b => isObject(b) && typeof b.status === 'string' && Array.isArray(b.items), 'related work body'),
  };
}

/** A new client id for an adjustment (crypto.randomUUID where available). */
export function newAdjustmentId(): string {
  const c = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (c?.randomUUID) return c.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, ch => {
    const r = (Math.random() * 16) | 0;
    return (ch === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}
