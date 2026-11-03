/**
 * Shared builders for the Invoice Review page's tests (ebilling-r1 task 110). Shapes follow the BFF's camelCase wire contract
 * (InvoiceReviewReadService.cs, InvoiceLineDecisionService.cs, InvoiceCorrectionService.cs).
 */

import { ApiError, AuthError } from '@spaarke/auth';
import type {
  ApiResult,
  InvoiceReview,
  InvoiceReviewApi,
  LineDecisionResponse,
  ReviewField,
  ReviewFinding,
  ReviewLine,
} from '../services/invoiceReviewApi';
import { toFailure } from '../services/invoiceReviewApi';

export const INVOICE_ID = '0d9178d3-9f32-7f46-ed9a-4dec59f66c1a';
export const PDF_ID = 'f0f0f0f0-0000-4000-8000-0000000000f1';
export const MATTER_ID = 'aaaaaaaa-0000-4000-8000-0000000000a1';

export const lineId = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

export const ok = <T>(value: T): ApiResult<T> => ({ ok: true, value });

/** An `ApiError` as `authenticatedFetch` throws it, with a ProblemDetails body. */
export function problem(status: number, errorCode: string | null, extra: Record<string, unknown> = {}): ApiError {
  return new ApiError(`HTTP ${status}`, status, { status, title: 'x', errorCode, ...extra } as never);
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
    fetchFile: jest.fn(async () => ok(new ArrayBuffer(8))),
  };
}

export { ApiError, AuthError };
