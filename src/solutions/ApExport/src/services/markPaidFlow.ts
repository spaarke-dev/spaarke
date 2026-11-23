/**
 * Mark Paid: one `POST /api/finance/invoices/{invoiceId}/mark-paid` per selected invoice (task 151a; FR-42), in the
 * selection's order, and what the dialog shows for each answer.
 *
 *   - 200: Paid.
 *   - 409 `invoice.transition_not_allowed` with `currentStatus`: not changed; the line names the invoice's status
 *     (only Sent to AP can be marked Paid; an invoice already Paid is refused the same way).
 *   - 409 `invoice.status_unknown` / `invoice.concurrent_change`: not changed.
 *   - 404 (the uniform record-not-found body): no right to pay it, or it does not exist. Not changed.
 *   - 503 `ebilling.invoice.payment_unavailable`: the server could not record it; check the status and retry.
 *   - A sign-in failure or `ebilling.disabled` stops the run: every later invoice would be refused the same way, so the
 *     rest are listed as not attempted.
 * Each call is independent: a refusal of one invoice never undoes another's payment (partial outcomes are normal).
 */

import { describeFailure, CODE_EBILLING_DISABLED, type ApiFailure, type ApiResult, type MarkPaidResponse } from './apExportApi';

export const CODE_TRANSITION_NOT_ALLOWED = 'invoice.transition_not_allowed';
export const CODE_STATUS_UNKNOWN = 'invoice.status_unknown';
export const CODE_CONCURRENT_CHANGE = 'invoice.concurrent_change';
export const CODE_PAYMENT_UNAVAILABLE = 'ebilling.invoice.payment_unavailable';

/** The lifecycle's status names (151a's `currentStatus`) as the app labels them. */
export const STATUS_LABELS: Readonly<Record<string, string>> = {
  InReview: 'In Review',
  Approved: 'Approved',
  SentToAp: 'Sent to AP',
  Paid: 'Paid',
  Rejected: 'Rejected',
  ReturnedToFirm: 'Returned to Firm',
  Cancelled: 'Cancelled',
  Superseded: 'Superseded',
};

export function statusLabel(name: string | null): string {
  if (!name) return 'unknown';
  return STATUS_LABELS[name] ?? name;
}

export type MarkPaidKind = 'paid' | 'refused' | 'unavailable' | 'failed' | 'skipped';

export interface MarkPaidOutcome {
  invoiceId: string;
  kind: MarkPaidKind;
  /** The invoice's status from a 409, else null. */
  currentStatus: string | null;
  /** The stable code of a refusal, else null. */
  errorCode: string | null;
  message: string;
  /** True when this answer means every later call would be refused the same way (sign-in, kill switch). */
  stopsRun: boolean;
}

function outcomeOfFailure(invoiceId: string, f: ApiFailure): MarkPaidOutcome {
  const base = { invoiceId, currentStatus: f.currentStatus, errorCode: f.errorCode, stopsRun: false };
  if (f.signInFailed) {
    return { ...base, kind: 'failed', message: 'Sign-in could not be verified. Not changed.', stopsRun: true };
  }
  if (f.errorCode === CODE_EBILLING_DISABLED) {
    return { ...base, kind: 'failed', message: 'eBilling is turned off in this environment. Not changed.', stopsRun: true };
  }
  if (f.status === 409 && f.errorCode === CODE_TRANSITION_NOT_ALLOWED) {
    const message =
      f.currentStatus === 'Paid'
        ? 'Already Paid. Not changed.'
        : `Not changed: the invoice is ${statusLabel(f.currentStatus)}, and only a Sent to AP invoice can be marked Paid.`;
    return { ...base, kind: 'refused', message };
  }
  if (f.status === 409 && f.errorCode === CODE_STATUS_UNKNOWN) {
    return { ...base, kind: 'refused', message: 'Not changed: the invoice has a status the system does not recognize.' };
  }
  if (f.status === 409 && f.errorCode === CODE_CONCURRENT_CHANGE) {
    return { ...base, kind: 'failed', message: 'The invoice changed while it was being updated. Not changed; try again.' };
  }
  if (f.status === 404) {
    return { ...base, kind: 'unavailable', message: 'Not found, or you may not mark it Paid. Not changed.' };
  }
  return { ...base, kind: 'failed', message: describeFailure(f) };
}

/** What the dialog shows for one answer of Mark Paid. */
export function classifyMarkPaid(invoiceId: string, result: ApiResult<MarkPaidResponse>): MarkPaidOutcome {
  if (result.ok) {
    return { invoiceId, kind: 'paid', currentStatus: 'Paid', errorCode: null, message: 'Paid.', stopsRun: false };
  }
  return outcomeOfFailure(invoiceId, result.failure);
}

export function skipped(invoiceId: string): MarkPaidOutcome {
  return {
    invoiceId,
    kind: 'skipped',
    currentStatus: null,
    errorCode: null,
    message: 'Not attempted.',
    stopsRun: false,
  };
}

/**
 * Mark each invoice Paid, one call at a time, in order. `onOutcome` is called after each answer (the dialog shows the
 * progress). A call that rejects (it should not: the client turns failures into results) counts as failed.
 */
export async function runMarkPaid(
  invoiceIds: string[],
  markPaid: (invoiceId: string) => Promise<ApiResult<MarkPaidResponse>>,
  onOutcome?: (outcome: MarkPaidOutcome, index: number) => void
): Promise<MarkPaidOutcome[]> {
  const outcomes: MarkPaidOutcome[] = [];
  let stopped = false;
  for (let i = 0; i < invoiceIds.length; i++) {
    const id = invoiceIds[i];
    let outcome: MarkPaidOutcome;
    if (stopped) {
      outcome = skipped(id);
    } else {
      try {
        outcome = classifyMarkPaid(id, await markPaid(id));
      } catch (err) {
        outcome = {
          invoiceId: id,
          kind: 'failed',
          currentStatus: null,
          errorCode: null,
          message: err instanceof Error ? err.message : 'The request failed.',
          stopsRun: false,
        };
      }
      stopped = outcome.stopsRun;
    }
    outcomes.push(outcome);
    onOutcome?.(outcome, i);
  }
  return outcomes;
}

export interface MarkPaidSummary {
  total: number;
  paid: number;
  notPaid: number;
  text: string;
}

export function summarizeMarkPaid(outcomes: MarkPaidOutcome[]): MarkPaidSummary {
  const paid = outcomes.filter(o => o.kind === 'paid').length;
  const total = outcomes.length;
  const notPaid = total - paid;
  const text =
    notPaid === 0
      ? `${paid} of ${total} marked Paid.`
      : `${paid} of ${total} marked Paid; ${notPaid} not changed. See each invoice below.`;
  return { total, paid, notPaid, text };
}
