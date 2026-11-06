/**
 * The decision wizard's and the panes' view logic (ebilling-r1 task 111), kept pure so it is tested without rendering.
 *
 * Nothing here is a server decision (ADR-002, I-1): no total, eligibility or authorization is computed. These are the page's lenses on what
 * the server returned: which actions to show for the signals it sent, what to say when Approve is not available, how a Decision Record reads,
 * and what an empty Related work answer means.
 */

import type { StatusBadgeTone } from '@spaarke/ui-components';
import type { Correction, InvoiceReview, ReviewDecisionRecord } from './invoiceReviewApi';
import type { ApprovalState } from './decisionApi';

// ─────────────────────────────────────────────────────────────────────────────
// What the server lets this caller do (FR-35: actions render only from server signals)
// ─────────────────────────────────────────────────────────────────────────────

export type DecisionAccess =
  /** An eligible approver may decide now: "Decide invoice…" opens the wizard. */
  | 'decide'
  /** The invoice was decided: the wizard opens read-only on the recorded decision. */
  | 'view'
  /** Nothing to offer: not eligible, not allowed to write, held (the blocked banner owns it), or the state is not known. */
  | 'none';

const DECIDED_APPROVALS: ReadonlySet<string> = new Set(['Approved', 'Rejected', 'Returned']);

/**
 * Whether the page offers "Decide invoice…", "View decision" or nothing. The server stays authoritative (a 403 or 409 is shown when the
 * action runs); this only keeps buttons the server's own signals say will be refused off the screen.
 */
export function decisionAccess(review: Pick<InvoiceReview, 'header'>, approval: ApprovalState | null, canWrite: boolean): DecisionAccess {
  if (!approval) return 'none';
  if (DECIDED_APPROVALS.has(approval.status)) return 'view';
  if (approval.status !== 'Pending') return 'none';
  if (approval.held || review.header.held) return 'none';
  if (review.header.status !== 'InReview') return 'none';
  return canWrite && approval.callerEligible ? 'decide' : 'none';
}

/** The words for why Approve and Reject are not available while the invoice needs its firm (EB-D-190), or null when it does not. */
export function firmNeededBlocker(approval: ApprovalState): string | null {
  return approval.firmNeeded === true
    ? 'This invoice needs its firm first (or its billing rules are being checked again after the firm was set). Add the firm, then decide.'
    : null;
}

/** The words for why Approve is not available now (I-2: open counts first), or null when it is available. */
export function approveBlocker(approval: ApprovalState): string | null {
  if (approval.canApprove) return null;
  if (approval.firmNeeded === true) return firmNeededBlocker(approval);
  const { pending, flagged } = approval.openLines;
  const open: string[] = [];
  if (flagged > 0) open.push(`${flagged} ${flagged === 1 ? 'line' : 'lines'} to decide`);
  if (pending > 0) open.push(`${pending} still processing`);
  if (open.length > 0) return `Not yet: ${open.join(', ')}.`;
  if (approval.held) return 'This invoice is held until its blocked submission is overridden or returned.';
  if (approval.noEligibleApprover) return 'No eligible approver is named for this invoice.';
  return 'Not available right now. A saved correction may still be finishing; reload and try again.';
}

/** The header line for the approval: who it is waiting for, or how it closed. Null when there is nothing to say. */
export interface AwaitingLine {
  tone: 'neutral' | 'warning';
  text: string;
}

const MAX_NAMES = 4;

export function awaitingLine(approval: ApprovalState | null): AwaitingLine | null {
  if (!approval) return null;
  switch (approval.status) {
    case 'Approved':
    case 'Rejected':
    case 'Returned': {
      const verb = approval.status === 'Approved' ? 'Approved' : approval.status === 'Rejected' ? 'Rejected' : 'Returned to firm';
      return { tone: 'neutral', text: `${verb}${approval.decidedBy ? ` by ${approval.decidedBy}` : ''}` };
    }
    case 'Pending': {
      if (approval.held) return { tone: 'warning', text: 'On hold: the submission is blocked and waits for an override or a return.' };
      if (approval.eligibilityUnavailable) return { tone: 'warning', text: 'Approvers could not be resolved right now.' };
      if (approval.noEligibleApprover || approval.eligibleApprovers.length === 0) {
        return { tone: 'warning', text: 'No eligible approver: the approver roles on this record name nobody who can approve.' };
      }
      const names = approval.eligibleApprovers;
      const shown = names.slice(0, MAX_NAMES).join(', ');
      return { tone: 'neutral', text: `Awaiting: ${shown}${names.length > MAX_NAMES ? ` and ${names.length - MAX_NAMES} more` : ''}` };
    }
    default:
      return null;
  }
}

/** A signature of what the approval state depends on in the review, so it is re-read when that changes and not on every poll. */
export function approvalSignature(review: InvoiceReview): string {
  const counts: Record<string, number> = {};
  for (const l of review.lines) counts[l.status] = (counts[l.status] ?? 0) + 1;
  const h = review.header;
  return [h.status, h.held ? 'held' : '', JSON.stringify(Object.entries(counts).sort()), review.totals.approved, review.totals.invoiceAdjustments, review.decisionHistory.length].join('|');
}

// ─────────────────────────────────────────────────────────────────────────────
// The recorded invoice decision (read-only wizard) and the History pane
// ─────────────────────────────────────────────────────────────────────────────

export type InvoiceOutcome = 'approve' | 'return' | 'reject';

export const INVOICE_OUTCOME_LABEL: Readonly<Record<InvoiceOutcome, string>> = {
  approve: 'Approve',
  return: 'Return to firm',
  reject: 'Reject (final)',
};

export function outcomeOfApproval(status: string): InvoiceOutcome | undefined {
  return status === 'Approved' ? 'approve' : status === 'Rejected' ? 'reject' : status === 'Returned' ? 'return' : undefined;
}

const INVOICE_ACTIONS: ReadonlySet<string> = new Set(['approve-invoice', 'reject-invoice', 'return-to-firm']);

/** The invoice-level Decision Record of the invoice's decision (the newest), or null. */
export function invoiceDecisionRecord(review: Pick<InvoiceReview, 'decisionHistory'>): ReviewDecisionRecord | null {
  const records = review.decisionHistory.filter(r => r.subject === 'invoice' && r.actionCode !== null && INVOICE_ACTIONS.has(r.actionCode));
  return sortNewestFirst(records)[0] ?? null;
}

/** Newest first by decided time (records without a time sort last, keeping the server's order among themselves). */
export function sortNewestFirst<T extends { decidedOn?: string | null; reviewedOn?: string | null }>(items: readonly T[], serverOrder: "oldestFirst" | "newestFirst" = "oldestFirst"): T[] {
  const time = (x: T): number => {
    const raw = x.decidedOn ?? x.reviewedOn ?? null;
    const t = raw ? Date.parse(raw) : NaN;
    return Number.isNaN(t) ? Number.NEGATIVE_INFINITY : t;
  };
  return items
    .map((item, index) => ({ item, index }))
    .sort((a, b) => time(b.item) - time(a.item) || (serverOrder === "oldestFirst" ? b.index - a.index : a.index - b.index))
    .map(x => x.item);
}

const ACTION_VERB: Readonly<Record<string, { verb: string; tone: StatusBadgeTone }>> = {
  'accept-line': { verb: 'Accepted', tone: 'success' },
  'reduce-line': { verb: 'Reduced', tone: 'warning' },
  'disallow-line': { verb: 'Disallowed', tone: 'critical' },
  'approve-invoice': { verb: 'Approved', tone: 'success' },
  'reject-invoice': { verb: 'Rejected', tone: 'critical' },
  'return-to-firm': { verb: 'Returned to firm', tone: 'warning' },
  'override-submission': { verb: 'Overridden', tone: 'warning' },
};

/** The badge label and tone of a Decision Record (the eBilling words, through RecordRow's `statusOverride`). */
export function recordBadge(record: Pick<ReviewDecisionRecord, 'actionCode' | 'outcome'>): { label: string; tone: StatusBadgeTone } {
  const known = record.actionCode ? ACTION_VERB[record.actionCode] : undefined;
  if (known) return { label: known.verb, tone: known.tone };
  if (record.outcome === 'Authorized') return { label: 'Authorized', tone: 'success' };
  if (record.outcome === 'Denied') return { label: 'Denied', tone: 'critical' };
  return { label: record.outcome ?? 'Recorded', tone: 'neutral' };
}

export function recordTitle(record: ReviewDecisionRecord, lineSequence: number | null): string {
  const { label } = recordBadge(record);
  if (record.subject === 'line') return `Line ${lineSequence ?? '?'} ${label.toLowerCase()}`;
  return `Invoice ${label.toLowerCase()}`;
}

export type HistoryItem =
  | { kind: 'decision'; decidedOn: string | null; record: ReviewDecisionRecord }
  | { kind: 'correction'; decidedOn: string | null; correction: Correction };

/** The Decision Records (task 100) and the corrections (task 101) as one list, newest first. */
export function historyItems(records: readonly ReviewDecisionRecord[], corrections: readonly Correction[]): HistoryItem[] {
  const merged: HistoryItem[] = [
    ...records.map((record): HistoryItem => ({ kind: 'decision', decidedOn: record.decidedOn, record })),
    ...corrections.map((correction): HistoryItem => ({ kind: 'correction', decidedOn: correction.reviewedOn ?? null, correction })),
  ];
  return sortNewestFirst(merged);
}

/** A correction fixes what the system read; it is not a decision, so its badge says what was done to the value. */
export function correctionBadge(outcome: string): { label: string; tone: StatusBadgeTone } {
  if (outcome === 'Corrected') return { label: 'Corrected', tone: 'warning' };
  if (/confirm/i.test(outcome)) return { label: 'Confirmed', tone: 'info' };
  return { label: outcome || 'Recorded', tone: 'neutral' };
}

export function correctionTitle(correction: Correction, fieldName: string, lineSequence: number | null): string {
  const where = correction.lineId ? `Line ${lineSequence ?? '?'} · ` : 'Invoice · ';
  return `${where}${fieldName} ${correctionBadge(correction.outcome).label.toLowerCase()}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Related work: what an empty answer means (107 reason codes)
// ─────────────────────────────────────────────────────────────────────────────

export function relatedWorkEmptyText(reason: string | null, windowDays: number | null): string {
  const days = windowDays ?? 7;
  switch (reason) {
    case 'not-a-fee-line':
      return 'Only a fee line has related work.';
    case 'no-timekeeper':
      return "This line's timekeeper is not on the firm's roster, so the work behind it cannot be traced.";
    case 'no-linked-contact':
      return "This line's timekeeper has no linked contact, so the work behind it cannot be traced.";
    case 'no-core-record':
      return 'This line is not filed under a matter, project or work assignment.';
    case 'no-line-date':
      return 'This line has no date, so no window can be searched.';
    case 'no-narrative':
      return 'This line has no narrative to compare documents with.';
    case 'none-in-window':
      return `No documents by this timekeeper on this record within ${days} days of the line date.`;
    case 'none-indexed':
      return 'Documents exist in the window but none is indexed for search yet, so none could be ranked.';
    default:
      return `No related work was found${reason ? ` (${reason})` : ''}.`;
  }
}
