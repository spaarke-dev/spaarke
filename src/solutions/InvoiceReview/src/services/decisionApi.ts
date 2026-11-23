/**
 * Invoice decision API client (ebilling-r1 task 111): every BFF call the decision wizard, the blocked-submission banner and the Context,
 * Related work and History panes make, through `authenticatedFetch` (ADR-028). Sits beside `invoiceReviewApi.ts` (task 110), whose
 * `toFailure` / `ApiResult` / `ApiFailure` it reuses.
 *
 *   Task 103  GET  /api/finance/invoices/{id}/approval                     eligible approvers, canApprove, open-line counts, who decided
 *             POST /api/finance/invoices/{id}/approve                      approve (no body but an optional note: the decider is the caller, the decision id the portion's)
 *   Task 102  GET  /api/finance/invoices/{id}/adjustment-rules             rules a reviewer may cite on an invoice adjustment
 *             POST /api/finance/invoices/{id}/adjustments                  add an adjustment (method, value, reason, rule; the server computes the amount)
 *             DELETE /api/finance/invoices/{id}/adjustments/{lineId}       remove one
 *   Task 104  POST /api/finance/invoices/{id}/reject | /return             reject / return to firm (a reason is required)
 *   Task 132ui POST /api/finance/invoices/{id}/firm-notice/draft?kind=     the drafted notice to the firm (nothing stored or sent); a reject or return carries the
 *                                                                          reviewer-confirmed noticeSubject / noticeProse, or neither (no firm email)
 *   Task 105  GET  /api/finance/submissions/{id}/blocked                   the blocked internal submission: findings, canOverride, canReturn
 *             POST /api/finance/submissions/{id}/override | /return        override (privilege + reason) / return (reason)
 *   Task 106  GET  /api/finance/invoices/{id}/context                      five independent sections, each available / unavailable / notTracked
 *   Task 107  GET  /api/finance/invoices/{id}/lines/{lineId}/related-work  documents behind one fee line, or the empty reason
 *
 * Task 095f-2ui: every decision here takes an optional `note` (approve, reject, return, add and remove adjustment, override, blocked return).
 * A blank or whitespace-only note is left out of the request, never sent as null or "" (design 095f-part2 1.2); a note that is sent is the
 * text as typed.
 *
 * The page decides nothing the server owns (ADR-002, I-1): it never computes a total, an amount or who may decide. Wire types follow
 * InvoiceApprovalService.cs, InvoiceAdjustmentService.cs, InvoiceRejectReturnEndpoints.cs, SubmissionOverrideService.cs,
 * InvoiceContextService.cs and InvoiceRelatedWorkService.cs (camelCase; DateOnly as yyyy-MM-dd).
 */

import type { AuthenticatedFetchFn } from '@spaarke/auth';
// Task 095f-2e2b: `GET /api/finance/invoices/{invoiceId}/correction-prefill` uses the shared library's type and reader (one contract: the
// one the corrected-invoice wizard itself reads).
import { readCorrectionPrefill, type CorrectionPrefill } from '@spaarke/ui-components';
import { toFailure, type ApiFailure, type ApiResult } from './invoiceReviewApi';
import { isNoteRefusal, noteMember } from './noteModel';

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
  /** Approve is refused for a hold: a blocked submission (FR-20) or the invoice's billing Procedures (082c-2). */
  held: boolean;
  /**
   * The hold is (or includes) a blocked submission: Reject and Return are refused too, and the blocked banner's Override / Return is the way
   * out. False for a Procedure-only hold, which refuses Approve only. Optional so an older server reads `held` as a blocked submission.
   */
  blockedSubmissionHeld?: boolean;
  approvedAmount: number | null;
  decidedBy: string | null;
  decidedOn: string | null;
  decisionRecordId: string | null;
  eligibleResolvedOn: string | null;
  /**
   * The invoice has no firm, is held for one, or its re-run after Set firm has not finished (EB-D-185; EB-D-190 adds Reject, EB-D-197 adds
   * adjustments and corrections). The server refuses approve, reject, the line decisions, adjustments and corrections (409) in that state;
   * return to firm has its own firm check. Optional so an older server reads as "not needed".
   */
  firmNeeded?: boolean;
  /**
   * ISS-032 (#2883), a read-only hint: the invoice's status shows this decision, but its Decision Record was never written, and the caller is
   * the one whose repeat finishes it (`InvoiceIncompleteDecisionDto`). Null when there is none, when the caller may not finish it, or when the
   * server could not tell. Optional so an older server reads as none. The decide route still decides.
   */
  incompleteDecision?: IncompleteDecision | null;
}

/** ISS-032: a reject, return or approve that stopped after its status transition, before it was complete (recorded AND linked). */
export interface IncompleteDecision {
  /** `Approve`, `Reject` or `Return`. */
  verb: 'Approve' | 'Reject' | 'Return' | string;
  /**
   * Where it stopped: `portion` or `record` (no Decision Record yet), or `link` (the record exists; its follow-up steps did not finish, and
   * the repeat finishes from the stored record, ignoring the request's reason and note).
   */
  failedStep: 'portion' | 'record' | 'link' | string | null;
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
  /** Task 095f-2ui: the optional note on the adjustment's Decision Record. Blank is left out of the request. */
  note?: string;
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

// Firm notice (131 draft route, 132 decide routes)

/** The kind the draft route is asked for: Reject asks `InvoiceRejected`, Return to firm asks `ReturnedToFirm` (exact names; the server refuses any other word). */
export type FirmNoticeKind = 'InvoiceRejected' | 'ReturnedToFirm';

/** 200 body of `POST /api/finance/invoices/{id}/firm-notice/draft` (`FirmNoticeDraftResponse`). `html` is the server's preview of the whole notice; this page does not render it. */
export interface FirmNoticeDraft {
  kind: string;
  /** The subject line; the invoice's tracking number is appended by code (` (tracking)`). */
  subject: string;
  /** The editable plain-text prose. */
  prose: string;
  html: string;
  /** `Action` (the AI drafted it) or `Template` (the fixed wording: the AI was unavailable, off, or its text failed the checks). */
  source: 'Action' | 'Template' | string;
  actionVersion: string | null;
  /** A stable code, set only when `source` is `Template`. */
  fallbackReason: string | null;
}

/** The reviewer-confirmed notice a reject or return carries (`noticeSubject` and `noticeProse` on the wire). */
export interface FirmNotice {
  subject: string;
  prose: string;
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
  /** iss034c (EB-D-253): the user whose call completed the decision (the operator on an operator finish). Absent or null on a stamp written before it existed ("not recorded"). */
  completedBySystemUserId?: string | null;
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
  /**
   * ISS-034 (EB-D-244), a read-only hint: the one decision recorded on this held submission that has not finished, or null. Optional so an
   * older server reads as none. The finish route still decides.
   */
  strandedDecision?: BlockedStrandedDecision | null;
  /**
   * ISS-034 (EB-D-244), the server's word that the caller may finish `strandedDecision` as an operator. The page never works this out itself.
   * Optional so an older server reads as false.
   */
  canFinishAsOperator?: boolean;
}

/** ISS-034: a decision recorded on a held submission that has not finished (`BlockedStrandedDecision`). */
export interface BlockedStrandedDecision {
  /** `Override` or `Return`. */
  verb: 'Override' | 'Return' | string;
  /** Its Decision Record. */
  recordId: string;
  /** The caller recorded it (they finish it by repeating it, never through the operator finish). */
  callerIsDecider: boolean;
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
 * 409 `InvoiceAdjustmentService.AdjustmentCodes.IdReused`: the adjustment id (an add's, or a removal's, which is derived from the line) was
 * already used for other content or by another reviewer. On a removal this is the stale-page answer (another reviewer removed it, or a
 * repeat carried another note): the page re-reads the list. Pinned to the server's source by `__tests__/serverWordsPin.test.ts`.
 */
export const CODE_ADJUSTMENT_ID_REUSED = 'ebilling.adjustment.id_reused';

/**
 * A 503 that saved part of the change: one of the documented codes, or any 503 that names the step it stopped at or the lines it committed
 * (the approve route answers those with the line service's own code, so the list alone would miss a code it does not know yet).
 */
export const isDecisionIncomplete = (f: ApiFailure): boolean =>
  f.status === 503 && ((f.errorCode !== null && INCOMPLETE_CODES.has(f.errorCode)) || f.failedStep !== null || f.committedLineIds.length > 0);

/**
 * Task 095f-2ui (review round 1): a send whose outcome is not known. Either it saved part of the change (`isDecisionIncomplete`) or no answer
 * came back at all (status 0: the request may have been recorded). Either way the form is locked and the repeat is the SAME request: the same
 * reason, note and ids. The server keeps the first note it saw, so a changed text would be dropped without a word or refused.
 */
export const isOutcomeUnknown = (f: ApiFailure): boolean => f.status === 0 || isDecisionIncomplete(f);

/**
 * 409 `SubmissionOverrideService.BlockedSubmissionErrors.ConcurrentChange`. For an Override or a Return of the blocked submission the server
 * returns it both BEFORE and AFTER the Decision Record is written (the release or the return ran out of retries), so a record MAY have been
 * written, and a repeat by the same decider finishes from the STORED reason and note and ignores the request's text (095f-2c known limit (c)).
 * So it is treated as an unknown outcome for the form, like a 503. That is deliberately conservative: a repeat with the same text succeeds
 * either way. Pinned to the server's source by `__tests__/serverWordsPin.test.ts`.
 */
export const CODE_BLOCKED_CONCURRENT_CHANGE = 'ebilling.blocked.concurrent_change';

/**
 * Review round 2: an Override or Return of the blocked submission whose outcome is not known: `isOutcomeUnknown`, or a 409
 * `ebilling.blocked.concurrent_change` (a record may already be written). The other blocked 409s (`not_blocked`, `already_overridden`,
 * `already_returned`, `invoice_not_held`, `in_progress`, `still_processing`, `decision_conflict`) are answered before any record is written.
 */
export const isBlockedOutcomeUnknown = (f: ApiFailure): boolean =>
  isOutcomeUnknown(f) || (f.status === 409 && f.errorCode === CODE_BLOCKED_CONCURRENT_CHANGE);

/** 409: another request for this submission is running right now (retry, the same request can still finish). Pinned to the server by `serverWordsPin`. */
export const CODE_BLOCKED_IN_PROGRESS = 'ebilling.blocked.in_progress';
/** 409: the submission is still being processed (retry, the same request can still finish). Pinned to the server by `serverWordsPin`. */
export const CODE_BLOCKED_STILL_PROCESSING = 'ebilling.blocked.still_processing';

/** 403 (ISS-034): the caller may not finish another person's stranded decision. Pinned to the server by `serverWordsPin`. */
export const CODE_BLOCKED_OPERATOR_NOT_PERMITTED = 'ebilling.blocked.operator_not_permitted';
/** 409 (ISS-034): no recorded, unfinished decision is waiting on the submission. Pinned to the server by `serverWordsPin`. */
export const CODE_BLOCKED_NOTHING_STRANDED = 'ebilling.blocked.nothing_stranded';
/** 409 (ISS-034): both an Override and a Return are recorded; no route finishes either, it needs support. Pinned by `serverWordsPin`. */
export const CODE_BLOCKED_BOTH_DECISIONS_RECORDED = 'ebilling.blocked.both_decisions_recorded';
/** 409 (ISS-034): the caller recorded the decision themself; they finish it by repeating it. Pinned by `serverWordsPin`. */
export const CODE_BLOCKED_OWN_DECISION_PENDING = 'ebilling.blocked.own_decision_pending';
/** 503 (ISS-034): the operator's record could not be written right now; nothing was changed. Pinned by `serverWordsPin`. */
export const CODE_BLOCKED_RECORD_UNAVAILABLE = 'ebilling.blocked.record_unavailable';

/**
 * The sentence shown for a blocked-submission refusal when the server sent no `detail` of its own (the server's detail always wins, ADR-019).
 * These are the five codes ISS-034 added, and the repeat answers and rights refusals the finish also returns. Every other code falls back to
 * the per-status sentence of `decisionFailureMessage`.
 */
export const BLOCKED_REFUSAL_SENTENCES: Readonly<Record<string, string>> = {
  [CODE_BLOCKED_OPERATOR_NOT_PERMITTED]: "You cannot finish another person's decision on this submission.",
  [CODE_BLOCKED_NOTHING_STRANDED]: 'No recorded override or return of this submission is waiting to be finished.',
  [CODE_BLOCKED_BOTH_DECISIONS_RECORDED]: 'Both an override and a return of this submission are recorded, so neither can be finished. Contact support.',
  [CODE_BLOCKED_OWN_DECISION_PENDING]:
    "This decision is yours and has not finished. Repeat your own decision to finish it; this route finishes another person's decision.",
  [CODE_BLOCKED_RECORD_UNAVAILABLE]: 'The record could not be saved right now. Nothing was changed. Try again.',
  'ebilling.blocked.already_overridden': 'This submission was already overridden.',
  'ebilling.blocked.already_returned': 'This submission was already returned to the firm.',
  'ebilling.blocked.invoice_not_held': 'This invoice is no longer held by this submission.',
  'ebilling.blocked.not_authorized': 'You do not have the rights to close the signals this decision resolves.',
  'ebilling.blocked.decision_conflict': 'A different decision is already recorded for this submission.',
  'ebilling.blocked.other_decision_pending': 'The other decision on this submission is recorded but has not finished, so this one cannot be made.',
};

/**
 * ISS-034: the refusals of the operator finish that say the page's picture of the submission is out of date (the decision was taken by the
 * caller, was never there, the invoice is no longer held, or the caller may not finish it). The page re-reads after them so the offer follows
 * the server's state. A decision that is already finished is not here: the finish route answers it 200 `alreadyDecided`, not a refusal.
 */
export const isFinishStaleRefusal = (f: ApiFailure): boolean =>
  (f.status === 403 && f.errorCode === CODE_BLOCKED_OPERATOR_NOT_PERMITTED) ||
  (f.status === 409 &&
    f.errorCode !== null &&
    [
      CODE_BLOCKED_NOTHING_STRANDED,
      CODE_BLOCKED_OWN_DECISION_PENDING,
      CODE_BLOCKED_BOTH_DECISIONS_RECORDED,
      'ebilling.blocked.invoice_not_held',
      'ebilling.blocked.not_blocked',
      'ebilling.blocked.other_decision_pending',
    ].includes(f.errorCode));

/**
 * Review round 3: the server's DEFINITE refusal of an Override or Return: a 4xx that is not an unknown outcome (`isBlockedOutcomeUnknown`), not
 * a refused note (400 note codes: the form is fixed and sent again), not one of the two transient 409s (`in_progress`, `still_processing`), and
 * not a 401, 408 or 429 (sign-in expired, timed out, rate limited: the same request can still finish later).
 * `decision_conflict`, `record_refused`, `findings_too_large`, `not_blocked`, `already_returned`, `already_overridden`, `invoice_not_held`
 * and a 403 are all this. A repeat answered so will never finish, so a lock kept for it has to be released. A 5xx is never definite here.
 */
export const isBlockedDefiniteRefusal = (f: ApiFailure): boolean =>
  f.status >= 400 &&
  f.status < 500 &&
  // Round 4: a 401 (expired sign-in), 408 (timeout) and 429 (rate limit) say nothing final: after a re-sign-in or a wait the same request can
  // still finish, and the server finishes it from the STORED text, so the lock stays.
  f.status !== 401 &&
  f.status !== 408 &&
  f.status !== 429 &&
  !isBlockedOutcomeUnknown(f) &&
  !isNoteRefusal(f) &&
  !(f.status === 409 && (f.errorCode === CODE_BLOCKED_IN_PROGRESS || f.errorCode === CODE_BLOCKED_STILL_PROCESSING));

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
  else if (f.errorCode !== null && Object.prototype.hasOwnProperty.call(BLOCKED_REFUSAL_SENTENCES, f.errorCode)) base = BLOCKED_REFUSAL_SENTENCES[f.errorCode];
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
  approve(invoiceId: string, note?: string): Promise<ApiResult<ApproveResponse>>;
  /** Task 132ui: `notice` is the reviewer-confirmed firm notice; absent, the decision stands and no firm email is sent. */
  reject(invoiceId: string, reason: string, note?: string, notice?: FirmNotice): Promise<ApiResult<DecisionResponse>>;
  returnToFirm(invoiceId: string, reason: string, note?: string, notice?: FirmNotice): Promise<ApiResult<DecisionResponse>>;
  /** Task 132ui: the drafted notice for the chosen outcome. Nothing is stored or sent. */
  getFirmNoticeDraft(invoiceId: string, kind: FirmNoticeKind): Promise<ApiResult<FirmNoticeDraft>>;
  listAdjustmentRules(invoiceId: string): Promise<ApiResult<AdjustmentRulesResponse>>;
  addAdjustment(invoiceId: string, request: AddAdjustmentRequest): Promise<ApiResult<AdjustmentResponse>>;
  removeAdjustment(invoiceId: string, lineId: string, note?: string): Promise<ApiResult<AdjustmentResponse>>;
  getBlocked(submissionId: string): Promise<ApiResult<BlockedSubmission>>;
  overrideBlocked(submissionId: string, reason: string, note?: string): Promise<ApiResult<BlockedDecisionResponse>>;
  returnBlocked(submissionId: string, reason: string, note?: string): Promise<ApiResult<BlockedDecisionResponse>>;
  /** ISS-034: finish another person's recorded, unfinished Override or Return exactly as recorded (the server derives the verb). */
  finishStranded(submissionId: string, reason: string, note?: string): Promise<ApiResult<BlockedDecisionResponse>>;
  getContext(invoiceId: string): Promise<ApiResult<InvoiceContext>>;
  getRelatedWork(invoiceId: string, lineId: string): Promise<ApiResult<RelatedWork>>;
  /** Task 095f-2e2b: can this invoice be replaced by a corrected one, and what is its own firm, record and number. */
  getCorrectionPrefill(invoiceId: string): Promise<ApiResult<CorrectionPrefill>>;
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
  firmNoticeDraft: (base: string, invoiceId: string, kind: FirmNoticeKind) =>
    `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/firm-notice/draft?kind=${encodeURIComponent(kind)}`,
  adjustmentRules: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/adjustment-rules`,
  adjustments: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/adjustments`,
  adjustment: (base: string, invoiceId: string, lineId: string) =>
    `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/adjustments/${id(lineId)}`,
  blocked: (base: string, submissionId: string) => `${trimBase(base)}/api/finance/submissions/${id(submissionId)}/blocked`,
  override: (base: string, submissionId: string) => `${trimBase(base)}/api/finance/submissions/${id(submissionId)}/override`,
  returnBlocked: (base: string, submissionId: string) => `${trimBase(base)}/api/finance/submissions/${id(submissionId)}/return`,
  finishStranded: (base: string, submissionId: string) => `${trimBase(base)}/api/finance/submissions/${id(submissionId)}/finish-stranded`,
  context: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/context`,
  correctionPrefill: (base: string, invoiceId: string) => `${trimBase(base)}/api/finance/invoices/${id(invoiceId)}/correction-prefill`,
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

/**
 * The request body members for a firm notice: `{ noticeSubject, noticeProse }` (both trimmed) when BOTH parts have text, else nothing, so the keys
 * are absent from the JSON (never empty strings: the server screens the notice when either part is non-blank, and sends no firm email with neither).
 */
function noticeMembers(notice: FirmNotice | undefined): { noticeSubject?: string; noticeProse?: string } {
  const subject = notice?.subject.trim() ?? '';
  const prose = notice?.prose.trim() ?? '';
  return subject !== '' && prose !== '' ? { noticeSubject: subject, noticeProse: prose } : {};
}

export function createInvoiceDecisionApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string): InvoiceDecisionApi {
  /** One call; a body that does not pass `valid` is a failure, never an empty answer. */
  async function call<T>(url: string, method: 'GET' | 'POST' | 'DELETE', body: unknown, valid: (b: unknown) => boolean, what: string): Promise<ApiResult<T>> {
    try {
      // A POST always carries a JSON body (`{}` for none); a DELETE carries one only when it has something to say (the removal's note).
      const json = { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) };
      const res = await fetchFn(url, method === 'POST' || (method === 'DELETE' && body !== null) ? { method, ...json } : { method });
      const parsed = (await res.json()) as unknown;
      return valid(parsed) ? { ok: true, value: parsed as T } : { ok: false, failure: unexpected(what) };
    } catch (err) {
      return { ok: false, failure: toFailure(err) };
    }
  }

  return {
    getApproval: invoiceId =>
      call(decisionRoutes.approval(bffBaseUrl, invoiceId), 'GET', null, b => isObject(b) && typeof b.status === 'string' && Array.isArray(b.eligibleApprovers), 'approval body'),
    approve: (invoiceId, note) => call(decisionRoutes.approve(bffBaseUrl, invoiceId), 'POST', { ...noteMember(note) }, b => isObject(b) && typeof b.outcome === 'string', 'approve body'),
    reject: (invoiceId, reason, note, notice) =>
      call(decisionRoutes.reject(bffBaseUrl, invoiceId), 'POST', { reason, ...noteMember(note), ...noticeMembers(notice) }, b => isObject(b) && typeof b.outcome === 'string', 'decision body'),
    returnToFirm: (invoiceId, reason, note, notice) =>
      call(decisionRoutes.returnToFirm(bffBaseUrl, invoiceId), 'POST', { reason, ...noteMember(note), ...noticeMembers(notice) }, b => isObject(b) && typeof b.outcome === 'string', 'decision body'),
    getFirmNoticeDraft: (invoiceId, kind) =>
      call(
        decisionRoutes.firmNoticeDraft(bffBaseUrl, invoiceId, kind),
        'POST',
        null,
        b => isObject(b) && typeof b.subject === 'string' && typeof b.prose === 'string' && typeof b.source === 'string',
        'firm notice draft body'
      ),
    listAdjustmentRules: invoiceId =>
      call(decisionRoutes.adjustmentRules(bffBaseUrl, invoiceId), 'GET', null, b => isObject(b) && Array.isArray(b.rules), 'adjustment rules body'),
    addAdjustment: (invoiceId, r) =>
      call(
        decisionRoutes.adjustments(bffBaseUrl, invoiceId),
        'POST',
        { adjustmentId: r.adjustmentId, method: r.method, value: r.value, reason: r.reason, ruleId: r.ruleId, ...noteMember(r.note) },
        isObject,
        'adjustment body'
      ),
    // The removal's note rides in an optional JSON body `{ note }` (RemoveAdjustmentRequest); with no note there is no body at all.
    removeAdjustment: (invoiceId, lineId, note) => {
      const body = noteMember(note);
      return call(decisionRoutes.adjustment(bffBaseUrl, invoiceId, lineId), 'DELETE', 'note' in body ? body : null, isObject, 'adjustment body');
    },
    getBlocked: submissionId =>
      call(decisionRoutes.blocked(bffBaseUrl, submissionId), 'GET', null, b => isObject(b) && typeof b.state === 'string' && Array.isArray(b.findings), 'blocked body'),
    overrideBlocked: (submissionId, reason, note) =>
      call(decisionRoutes.override(bffBaseUrl, submissionId), 'POST', { reason, ...noteMember(note) },b => isObject(b) && typeof b.outcome === 'string', 'override body'),
    returnBlocked: (submissionId, reason, note) =>
      call(decisionRoutes.returnBlocked(bffBaseUrl, submissionId), 'POST', { reason, ...noteMember(note) },b => isObject(b) && typeof b.outcome === 'string', 'return body'),
    finishStranded: (submissionId, reason, note) =>
      call(decisionRoutes.finishStranded(bffBaseUrl, submissionId), 'POST', { reason, ...noteMember(note) }, b => isObject(b) && typeof b.outcome === 'string', 'finish body'),
    getContext: invoiceId =>
      call(decisionRoutes.context(bffBaseUrl, invoiceId), 'GET', null, b => isObject(b) && isObject(b.coreRecord) && isObject(b.budget), 'context body'),
    getRelatedWork: (invoiceId, lineId) =>
      call(decisionRoutes.relatedWork(bffBaseUrl, invoiceId, lineId), 'GET', null, b => isObject(b) && typeof b.status === 'string' && Array.isArray(b.items), 'related work body'),
    getCorrectionPrefill: invoiceId =>
      call(
        decisionRoutes.correctionPrefill(bffBaseUrl, invoiceId),
        'GET',
        null,
        b => readCorrectionPrefill(b) !== null,
        'correction prefill body'
      ),
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
