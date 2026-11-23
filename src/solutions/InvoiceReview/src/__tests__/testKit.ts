/**
 * Shared builders for the Invoice Review page's tests (ebilling-r1 task 110). Shapes follow the BFF's camelCase wire contract
 * (InvoiceReviewReadService.cs, InvoiceLineDecisionService.cs, InvoiceCorrectionService.cs).
 */

import * as fs from 'fs';
import * as path from 'path';
import { ApiError, AuthError } from '@spaarke/auth';
import type {
  ApiResult,
  InvoiceReview,
  InvoiceReviewApi,
  LineDecisionResponse,
  ReviewDecisionRecord,
  ReviewField,
  ReviewFinding,
  ReviewLine,
} from '../services/invoiceReviewApi';
import type { ApprovalState, BlockedSubmission, ContextSection, InvoiceContext, InvoiceDecisionApi } from '../services/decisionApi';
import { toFailure } from '../services/invoiceReviewApi';

export const INVOICE_ID = '0d9178d3-9f32-7f46-ed9a-4dec59f66c1a';
export const PDF_ID = 'f0f0f0f0-0000-4000-8000-0000000000f1';
export const MATTER_ID = 'aaaaaaaa-0000-4000-8000-0000000000a1';

export const lineId = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

/** The line a mocked Add line creates (task 164). */
export const ADDED_LINE_ID = lineId(99);

export const ok = <T>(value: T): ApiResult<T> => ({ ok: true, value });

/** An `ApiError` as `authenticatedFetch` throws it, with a ProblemDetails body. */
export function problem(status: number, errorCode: string | null, extra: Record<string, unknown> = {}): ApiError {
  return new ApiError(`HTTP ${status}`, status, { status, title: 'x', errorCode, ...extra } as never);
}

/**
 * A 403 as ProblemDetailsHelper.Forbidden answers it: the stable code is under `reasonCode`, not `errorCode` (a hand-built 403 with
 * `errorCode` would hide the page's code-reading bug).
 */
export function forbidden(reasonCode: string, extra: Record<string, unknown> = {}): ApiError {
  return new ApiError('HTTP 403', 403, { status: 403, title: 'Forbidden', reasonCode, ...extra } as never);
}

export function failed<T>(err: unknown): ApiResult<T> {
  return { ok: false, failure: toFailure(err) };
}

export function field(overrides: Partial<ReviewField> = {}): ReviewField {
  return { confidence: 0.98, combined: 0.97, lowConfidence: false, page: 1, box: { page: 1, x: 0.1, y: 0.3, w: 0.1, h: 0.02 }, repair: null, ...overrides };
}

export function finding(overrides: Partial<ReviewFinding> = {}): ReviewFinding {
  return {
    ruleType: 'RateCap',
    policyCode: 'RATE-CAP',
    policyId: 'p1',
    policyVersionId: 'pv000001-0000',
    clause: 'Rates above the approved rate are not payable.',
    clauseLocation: '§4.2',
    reasonCode: 'rate.above_approved',
    message: 'The billed rate 750.00 is above the approved rate 650.00.',
    severity: 'Warning',
    consequence: 'Flag',
    rejects: false,
    heldBecause: null,
    detailWithheld: false,
    overridden: false,
    suggestedAdjustment: { kind: 'Reduce', amount: 112.5 },
    valuesRead: [{ name: 'Rate', value: '750.00', field: 'Rate', confidence: 0.98, belowReliabilityThreshold: false }],
    ...overrides,
  };
}

export function line(n: number, overrides: Partial<ReviewLine> = {}): ReviewLine {
  return {
    id: lineId(n),
    sequence: n,
    type: 'Fee',
    date: '2025-03-12',
    timekeeperAsBilled: 'A. Partner',
    timekeeper: { id: 'tk1', name: 'A. Partner', classification: 1 },
    quantity: 1.5,
    rate: 750,
    approvedRate: 650,
    amount: 1125,
    narrative: `Work on line ${n}`,
    codes: {
      task: { asBilled: 'L110', code: 'L110', firmCode: 'L110', mismatch: false },
      activity: { asBilled: 'A101', code: 'A101', firmCode: 'A101', mismatch: false },
      expense: { asBilled: null, code: null, firmCode: null, mismatch: false },
      confidence: 0.9,
      codeSetNeedsReview: false,
      notCoded: false,
      refused: [],
    },
    status: 'Clean',
    statusLabel: 'Clean',
    approvedAmount: null,
    reason: null,
    reviewedBy: null,
    reviewedOn: null,
    findingCount: 0,
    highestSeverity: null,
    findings: [],
    fields: {
      Date: field({ box: { page: 1, x: 0.05, y: 0.3 + n * 0.03, w: 0.08, h: 0.02 } }),
      Hours: field({ box: { page: 1, x: 0.6, y: 0.3 + n * 0.03, w: 0.05, h: 0.02 } }),
      Amount: field({ box: { page: 1, x: 0.8, y: 0.3 + n * 0.03, w: 0.1, h: 0.02 } }),
      Narrative: field({ box: { page: 1, x: 0.2, y: 0.3 + n * 0.03, w: 0.35, h: 0.02 } }),
    },
    lineFindings: [],
    reviewerAdded: false,
    ...overrides,
  };
}

/** A Flagged line with one rate finding suggesting a 112.50 reduction. */
export function flagged(n: number, overrides: Partial<ReviewLine> = {}): ReviewLine {
  return line(n, {
    status: 'Flagged',
    statusLabel: 'Flagged',
    findingCount: 1,
    highestSeverity: 'Warning',
    findings: [finding()],
    ...overrides,
  });
}

export function review(lines: ReviewLine[], overrides: Partial<InvoiceReview> = {}): InvoiceReview {
  return {
    header: {
      invoiceId: INVOICE_ID,
      name: 'INV-2025-78232.3',
      firmInvoiceNumber: 'INV-2025-78232',
      trackingNumber: 'INV-2025-78232.3',
      firm: { id: 'f1', name: 'Example Firm LLP' },
      coreRecord: { type: 'matter', id: MATTER_ID, name: 'Example matter' },
      invoiceDate: '2025-04-01',
      periodStart: '2025-03-01',
      periodEnd: '2025-03-31',
      status: 'InReview',
      statusCode: 1,
      held: false,
    },
    totals: { billed: 5000, fees: 5000, expenses: 0, lineAdjustments: 0, invoiceAdjustments: 0, approved: 5000 },
    lines,
    invoiceFindings: [],
    submission: null,
    decisionHistory: [],
    lineCounts: {},
    flags: { canRead: true, canDecide: true },
    invoicePdfFileId: PDF_ID,
    ...overrides,
  };
}

export function decided(lineIds: string[], approved: number | null = null): LineDecisionResponse {
  return {
    decisionId: 'd',
    lines: lineIds.map(id => ({ lineId: id, status: 'Accepted', approvedAmount: approved ?? 0, reason: null, reviewedOn: '2026-11-03T10:00:00Z', alreadyRecorded: false })),
    totals: null,
  };
}

export type MockApi = { [K in keyof InvoiceReviewApi]: jest.Mock };

/** A fake API: `getReview` answers each queued review in turn and then keeps answering the last one. */
export function mockApi(reviews: InvoiceReview[] | InvoiceReview): MockApi {
  const queue = Array.isArray(reviews) ? [...reviews] : [reviews];
  let last = queue[0];
  return {
    getReview: jest.fn(async () => {
      if (queue.length) last = queue.shift()!;
      return ok(last);
    }),
    decideLine: jest.fn(async (_i: string, id: string) => ok(decided([id]))),
    decideLines: jest.fn(async (_i: string, d: { lines: Array<{ lineId: string }> }) => ok(decided(d.lines.map(l => l.lineId)))),
    acceptClean: jest.fn(async () => ok(decided([]))),
    correctLine: jest.fn(async (_i: string, id: string, r: { field: string; value: string | null }) =>
      ok({ correction: { id: 'c1', lineId: id, field: r.field, aiValue: '25', finalValue: r.value, outcome: 'Corrected', isLabel: true, aiConfidence: 0.4 }, alreadyApplied: false })
    ),
    correctHeader: jest.fn(async (_i: string, r: { field: string; value: string | null }) =>
      ok({ correction: { id: 'c2', lineId: null, field: r.field, aiValue: null, finalValue: r.value, outcome: 'Corrected', isLabel: true, aiConfidence: null }, alreadyApplied: false })
    ),
    listCorrections: jest.fn(async () => ok([])),
    addLine: jest.fn(async (_i: string, r: { requestId: string }) =>
      ok({ requestId: r.requestId, lineId: ADDED_LINE_ID, decisionId: 'dd', status: 'Flagged', alreadyApplied: false, totals: null })
    ),
    fetchFile: jest.fn(async () => ok(new ArrayBuffer(8))),
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Task 111: the decision wizard, the blocked view and the panes
// ─────────────────────────────────────────────────────────────────────────────

export const SUBMISSION_ID = '5b5b5b5b-0000-4000-8000-0000000000b5';

/** An approval state (task 103): an eligible approver on a pending invoice with nothing open, unless overridden. */
export function approval(overrides: Partial<ApprovalState> = {}): ApprovalState {
  return {
    invoiceId: INVOICE_ID,
    approvalId: 'ap-1',
    status: 'Pending',
    eligibleApprovers: ['Pat Approver', 'Sam Second'],
    noEligibleApprover: false,
    eligibilityUnavailable: false,
    callerEligible: true,
    canApprove: true,
    openLines: { pending: 0, flagged: 0, total: 0 },
    undecidedCleanLines: 0,
    held: false,
    approvedAmount: null,
    decidedBy: null,
    decidedOn: null,
    decisionRecordId: null,
    eligibleResolvedOn: null,
    ...overrides,
  };
}

export function blockedSubmission(overrides: Partial<BlockedSubmission> = {}): BlockedSubmission {
  return {
    submissionId: SUBMISSION_ID,
    invoiceId: INVOICE_ID,
    trackingNumber: 'INV-2025-78232.3',
    firmInvoiceNumber: 'INV-2025-78232',
    channel: 'Internal',
    status: 'Rejected',
    outcomeReasonCode: 'submission.blocked',
    state: 'Blocked',
    canOverride: true,
    canReturn: true,
    findings: [
      {
        index: 0,
        ruleType: 'Duplicate',
        policyCode: 'DUP-INV',
        policyVersionId: 'pv1',
        clause: 'No duplicate invoices',
        clauseLocation: '§2',
        subject: 'invoice',
        line: null,
        reasonCode: 'invoice.duplicate',
        detailWithheld: false,
        consequence: 'Block',
        blocking: true,
        heldBecause: 'Looks like a duplicate of an earlier invoice',
        overridden: false,
        valuesRead: [],
      },
    ],
    override: null,
    return: null,
    ...overrides,
  };
}

/** A review whose invoice is held by a blocked submission. */
export function heldReview(lines: ReviewLine[] = [line(1)]): InvoiceReview {
  const base = review(lines);
  return { ...base, header: { ...base.header, held: true }, submission: { submissionId: SUBMISSION_ID, trackingNumber: 'INV-2025-78232.3', kind: 'Blocked' } };
}

export function decisionRecord(overrides: Partial<ReviewDecisionRecord> = {}): ReviewDecisionRecord {
  return {
    recordId: 'rec-1',
    number: 'DR-0001',
    actionCode: 'accept-line',
    subject: 'line',
    lineId: lineId(1),
    recordClass: 'Routine',
    outcome: 'Authorized',
    decidedOn: '2026-11-03T09:00:00Z',
    decidedBy: { id: 'u1', name: 'Reviewer One' },
    reason: null,
    ...overrides,
  };
}

export type MockDecisionApi = { [K in keyof InvoiceDecisionApi]: jest.Mock };

const emptySection = <T>(data: T): ContextSection<T> => ({ status: 'available', data, code: null, message: null });

export function invoiceContext(overrides: Partial<InvoiceContext> = {}): InvoiceContext {
  return {
    invoiceId: INVOICE_ID,
    coreRecord: emptySection({ type: 'matter', id: MATTER_ID, name: 'Example matter', number: 'M-100', statusCode: 1, status: 'Active', openedDate: '2025-01-05', closedDate: null }),
    budget: emptySection({ budget: 100000, spendToDate: 42000, remaining: 58000, utilizationPercent: 42, invoiceCount: 4, asOf: '2025-04-01T00:00:00Z' }),
    priorInvoices: emptySection({
      limit: 5,
      hasMore: false,
      items: [{ id: 'pi1', number: 'INV-2025-77001', invoiceDate: '2025-03-01', billed: 4000, approved: 3900, status: 'Approved', statusCode: 3 }],
    }),
    disallowHistory: emptySection({ invoicesConsidered: 4, reducedCount: 3, reducedAmount: 450, disallowedCount: 1, disallowedAmount: 200, topReasons: [{ reason: 'Rate above approved', count: 3 }] }),
    rateHistory: emptySection({
      timekeepers: [
        {
          timekeeperId: 'tk1',
          name: 'A. Partner',
          approvedRates: [{ effectiveFrom: '2025-01-01', effectiveTo: null, approvedRate: 650, rackRate: 750, basis: 'Negotiated', scope: 'Matter' }],
          billedOnThisInvoice: [{ rate: 750, lineCount: 3, firstSeen: null, lastSeen: null }],
          billedPreviously: [],
        },
      ],
    }),
    ...overrides,
  };
}

/** A fake decision API: every route succeeds with the happy-path body unless a test overrides it. */
export function mockDecisionApi(initial: ApprovalState = approval()): MockDecisionApi {
  return {
    getApproval: jest.fn(async () => ok(initial)),
    approve: jest.fn(async () => ok({ outcome: 'approved', decisionId: 'd1', decisionRecordId: 'rec-9', state: approval({ status: 'Approved', canApprove: false, approvedAmount: 5000 }) })),
    reject: jest.fn(async () => ok({ outcome: 'decided', decision: 'Rejected', decisionId: 'd2', decisionRecordId: 'rec-10' })),
    returnToFirm: jest.fn(async () => ok({ outcome: 'decided', decision: 'Returned', decisionId: 'd3', decisionRecordId: 'rec-11' })),
    listAdjustmentRules: jest.fn(async () =>
      ok({
        asOf: null,
        rules: [{ ruleId: 'r1', policyCode: 'BILL-CAP', headline: 'Block billing', ruleSetId: 'rs1', ruleSetName: 'Example Guidelines', scope: 'Firm', version: 2, clauseReference: '§7', clauseLocation: null }],
      })
    ),
    addAdjustment: jest.fn(async () =>
      ok({ adjustment: { lineId: lineId(90), adjustmentId: 'a1', method: 'percent', value: 5, amount: -250, reason: 'Courtesy', ruleId: null, reviewedOn: '2026-11-03T10:00:00Z' }, removedLineId: null, totals: null, alreadyApplied: false })
    ),
    removeAdjustment: jest.fn(async (_i: string, id: string) => ok({ adjustment: null, removedLineId: id, totals: null, alreadyApplied: false })),
    getBlocked: jest.fn(async () => ok(blockedSubmission())),
    overrideBlocked: jest.fn(async () => ok({ submissionId: SUBMISSION_ID, invoiceId: INVOICE_ID, outcome: 'Overridden', recordId: 'rec-12', alreadyDecided: false })),
    returnBlocked: jest.fn(async () => ok({ submissionId: SUBMISSION_ID, invoiceId: INVOICE_ID, outcome: 'Returned', recordId: 'rec-13', alreadyDecided: false })),
    getContext: jest.fn(async () => ok(invoiceContext())),
    getRelatedWork: jest.fn(async (_i: string, id: string) =>
      ok({
        invoiceId: INVOICE_ID,
        lineId: id,
        status: 'available',
        reason: null,
        code: null,
        message: null,
        windowDays: 7,
        truncated: false,
        items: [{ documentId: 'doc1', name: 'Draft agreement.docx', createdOn: '2025-03-11T10:00:00Z', score: 0.8 }],
      })
    ),
  };
}

/**
 * Every `.ts` / `.tsx` source file of the page (tests excluded). The colour-literal guard of task 110 and the source guards of task 111
 * walk the page with THIS function, so a folder added later is covered by the guard that already exists, not by a copy of its walker.
 */
export function listSourceFiles(dir: string = path.resolve(__dirname, '..')): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) return e.name === '__tests__' ? [] : listSourceFiles(p);
    return /\.(ts|tsx)$/.test(e.name) ? [p] : [];
  });
}

export { ApiError, AuthError };
