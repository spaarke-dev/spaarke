/**
 * ebilling-client — the firm outcome report's BFF contract (ebilling-r1 task 134, FR-38 / FR-18, D-66, EB-D-202).
 *
 *   GET /api/v1/external/ebilling/invoices/{invoiceId}/outcome        the firm-visible outcome of one invoice
 *   GET /api/v1/external/ebilling/submissions/{submissionId}/outcome  the firm-visible outcome of one invoice submission
 *
 * The body is `FirmOutcome` (`Services/Finance/FirmFindingsProjection.cs`), serialized camelCase with the enums as their
 * PascalCase names. It is an allow-list the SERVER built: this client shows what it returns and decides nothing about who may
 * see what (NFR-06). It still reads the answer through an allow-list of its own ({@link normalizeOutcome}): only the members of
 * the contract are copied, so a key the contract does not name can never reach the screen.
 *
 * Each call resolves to an explicit {@link OutcomeResult} state and never rejects:
 *   - `ok`          the outcome.
 *   - `noAccess`    403: not this caller's to see (the server says nothing about why, and neither does the page).
 *   - `notFound`    404.
 *   - `disabled`    503 `ebilling.disabled` (the kill switch): the surface is hidden, it is not an outage.
 *   - `unavailable` any other 503, or an answer marked `degraded` / of kind `Unavailable` (a read faulted: retry shortly).
 *   - `error`       anything else, including an answer that is not the contract.
 *
 * ADR-028: every call goes through `bffApiCall` (token, active backend and 401 retry live there); no raw fetch, no header here.
 */

import { bffApiCall } from '../auth/bff-client';
import { isEbillingDisabledError } from '@spaarke/ui-components/components/InvoiceSubmission';
import { toInvoiceSubmissionError } from './web-api-client';

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** `FirmOutcomeKind`. */
export type FirmOutcomeKind =
  | 'SubmissionInProgress'
  | 'SubmissionRejected'
  | 'InvoiceInReview'
  | 'Approved'
  | 'ApprovedWithAdjustments'
  | 'Rejected'
  | 'ReturnedToFirm'
  | 'SentToAp'
  | 'Paid'
  | 'Unavailable'
  | 'Closed';

/** `FirmOutcomeDecision`. */
export type FirmOutcomeDecision = 'Reduced' | 'Disallowed' | 'Added' | 'Adjustment' | 'RejectionFinding';

/** `FirmOutcomeLine`: the line as the firm billed it (or the reviewer added it). `date` is yyyy-MM-dd. */
export interface FirmOutcomeLine {
  date: string | null;
  timekeeperAsBilled: string | null;
  descriptionExcerpt: string | null;
  billedAmount: number | null;
  approvedAmount: number | null;
}

/** `FirmOutcomeAdjustment`: an invoice-level write-down. `method` is `Percent` or `Fixed`. */
export interface FirmOutcomeAdjustment {
  method: string | null;
  value: number | null;
  amount: number | null;
}

/**
 * `FirmOutcomeCitation`: the rule behind an item, present only when the policy's firm flag publishes it. `label` is the firm-facing
 * name (the Procedure's firm-facing name); the rule set's own name is never part of the contract.
 */
export interface FirmOutcomeCitation {
  ruleName: string | null;
  label: string | null;
  version: number | null;
  sectionReference: string | null;
  quote: string | null;
}

/** `FirmOutcomeValueRead`: one value a rule read for a rejection finding. */
export interface FirmOutcomeValueRead {
  name: string;
  value: string;
}

/** `FirmOutcomeItem`. `lineNumber`, `valuesRead` and `reasonCode` are present for a rejection finding only. */
export interface FirmOutcomeItem {
  decision: FirmOutcomeDecision;
  line: FirmOutcomeLine | null;
  adjustment: FirmOutcomeAdjustment | null;
  reason: string | null;
  firmReasons: string[];
  citations: FirmOutcomeCitation[];
  lineNumber: number | null;
  valuesRead: FirmOutcomeValueRead[] | null;
  reasonCode: string | null;
}

/** `FirmOutcome`. `notShownCount` and `noticeProse` are omitted by the server when they do not apply. */
export interface FirmOutcome {
  kind: FirmOutcomeKind;
  trackingNumber: string | null;
  statusLabel: string;
  billedTotal: number | null;
  approvedTotal: number | null;
  items: FirmOutcomeItem[];
  degraded: boolean;
  notShownCount: number | null;
  noticeProse: string | null;
}

/** The explicit state of one read. */
export type OutcomeResult =
  | { state: 'ok'; outcome: FirmOutcome }
  | { state: 'noAccess' }
  | { state: 'notFound' }
  | { state: 'disabled' }
  | { state: 'unavailable' }
  | { state: 'error' };

const str = (v: unknown): string | null => (typeof v === 'string' ? v : null);
const num = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v) ? v : null);
const rec = (v: unknown): Record<string, unknown> | null =>
  v !== null && typeof v === 'object' && !Array.isArray(v) ? (v as Record<string, unknown>) : null;
const strings = (v: unknown): string[] => (Array.isArray(v) ? v.filter((s): s is string => typeof s === 'string') : []);

function normalizeItem(raw: unknown): FirmOutcomeItem | null {
  const r = rec(raw);
  if (!r || typeof r.decision !== 'string') return null;
  const line = rec(r.line);
  const adjustment = rec(r.adjustment);
  const citations = Array.isArray(r.citations)
    ? r.citations.flatMap((c): FirmOutcomeCitation[] => {
        const cr = rec(c);
        return cr
          ? [
              {
                ruleName: str(cr.ruleName),
                label: str(cr.label),
                version: num(cr.version),
                sectionReference: str(cr.sectionReference),
                quote: str(cr.quote),
              },
            ]
          : [];
      })
    : [];
  const valuesRead = Array.isArray(r.valuesRead)
    ? r.valuesRead.flatMap((v): FirmOutcomeValueRead[] => {
        const vr = rec(v);
        return vr && typeof vr.name === 'string' && typeof vr.value === 'string'
          ? [{ name: vr.name, value: vr.value }]
          : [];
      })
    : null;
  return {
    decision: r.decision as FirmOutcomeDecision,
    line: line
      ? {
          date: str(line.date),
          timekeeperAsBilled: str(line.timekeeperAsBilled),
          descriptionExcerpt: str(line.descriptionExcerpt),
          billedAmount: num(line.billedAmount),
          approvedAmount: num(line.approvedAmount),
        }
      : null,
    adjustment: adjustment
      ? { method: str(adjustment.method), value: num(adjustment.value), amount: num(adjustment.amount) }
      : null,
    reason: str(r.reason),
    firmReasons: strings(r.firmReasons),
    citations,
    lineNumber: num(r.lineNumber),
    valuesRead,
    reasonCode: str(r.reasonCode),
  };
}

/**
 * Copies the members of the contract and nothing else. Returns null when the body is not a `FirmOutcome` (no `kind`, no
 * `statusLabel`, or `items` that is not a list): a body that is not the contract is a failed read, never an empty report.
 */
export function normalizeOutcome(raw: unknown): FirmOutcome | null {
  const r = rec(raw);
  if (!r || typeof r.kind !== 'string' || typeof r.statusLabel !== 'string' || !Array.isArray(r.items)) return null;
  return {
    kind: r.kind as FirmOutcomeKind,
    trackingNumber: str(r.trackingNumber),
    statusLabel: r.statusLabel,
    billedTotal: num(r.billedTotal),
    approvedTotal: num(r.approvedTotal),
    items: r.items.flatMap(i => {
      const item = normalizeItem(i);
      return item ? [item] : [];
    }),
    degraded: r.degraded === true,
    notShownCount: num(r.notShownCount),
    noticeProse: str(r.noticeProse),
  };
}

async function read(path: string): Promise<OutcomeResult> {
  let body: unknown;
  try {
    body = await bffApiCall<unknown>(path);
  } catch (err) {
    const failure = toInvoiceSubmissionError(err);
    if (isEbillingDisabledError(failure)) return { state: 'disabled' };
    switch (failure.statusCode) {
      case 403:
        return { state: 'noAccess' };
      case 404:
        return { state: 'notFound' };
      case 503:
        return { state: 'unavailable' };
      default:
        // The status only: the text of a BFF failure can carry detail that is not for the page or the console.
        console.error(`[ebilling] The outcome could not be read (${failure.statusCode ?? 'no response'}).`);
        return { state: 'error' };
    }
  }
  const outcome = normalizeOutcome(body);
  if (!outcome) return { state: 'error' };
  // A read that did not complete is a retry, never a report with a missing list.
  if (outcome.degraded || outcome.kind === 'Unavailable') return { state: 'unavailable' };
  return { state: 'ok', outcome };
}

/** Reads the outcome of one invoice. */
export function getInvoiceOutcome(invoiceId: string): Promise<OutcomeResult> {
  if (!GUID_RE.test(invoiceId)) return Promise.resolve({ state: 'error' });
  return read(`/api/v1/external/ebilling/invoices/${encodeURIComponent(invoiceId)}/outcome`);
}

/** Reads the outcome of one invoice submission (a rejected one carries the findings that rejected it). */
export function getSubmissionOutcome(submissionId: string): Promise<OutcomeResult> {
  if (!GUID_RE.test(submissionId)) return Promise.resolve({ state: 'error' });
  return read(`/api/v1/external/ebilling/submissions/${encodeURIComponent(submissionId)}/outcome`);
}
