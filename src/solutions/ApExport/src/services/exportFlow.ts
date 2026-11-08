/**
 * Export to AP: what the dialog shows for each answer of `POST /api/finance/export-batches` (task 150; FR-41, EB-D-178).
 *
 * Pure functions, so the tests can pin every state without rendering:
 *   - 201: the batch (name, count, total) and the files it can be downloaded in;
 *   - 400 `ebilling.export.selection_refused`: one line per refused invoice with its reason, and the rest of the
 *     selection, which the user may export without the refused ones (a new request: the server checks them again);
 *   - every other refusal: one message. A 403 `not_permitted` names no invoice (the server names none, EB-D-178).
 * Nothing was exported in any refusal except the 500 `*_incomplete` codes, which name the batch for an administrator.
 */

import { describeFailure, downloadableFormats, type ApiFailure, type ApiResult, type ExportBatch, type ExportFormat } from './apExportApi';

export const CODE_SELECTION_REFUSED = 'ebilling.export.selection_refused';
export const CODE_NOT_PERMITTED = 'ebilling.export.not_permitted';
export const CODE_AUTHORIZATION_UNAVAILABLE = 'ebilling.export.authorization_unavailable';
export const CODE_INVOICE_CONFLICT = 'ebilling.export.invoice_conflict';
export const CODE_UNAVAILABLE = 'ebilling.export.unavailable';
export const CODE_FILE_FAILED = 'ebilling.export.file_failed';
export const CODE_TRANSITION_FAILED = 'ebilling.export.transition_failed';
export const CODE_EBILLING_DISABLED = 'ebilling.disabled';

/** The three per-invoice reasons of a 400 `selection_refused` (task 150, `ApExportCodes.Reason*`). */
export const REFUSAL_TEXT: Readonly<Record<string, string>> = {
  invoice_not_approved: 'Not Approved: only an Approved invoice can be exported.',
  invoice_already_exported: 'Already in an export batch.',
  invoice_not_found: 'Not found, or not available to you.',
};

export function refusalText(reason: string): string {
  return REFUSAL_TEXT[reason] ?? `Refused (${reason || 'no reason given'}).`;
}

export interface RefusedInvoice {
  invoiceId: string;
  reason: string;
  text: string;
}

export type ExportOutcome =
  | { kind: 'created'; batch: ExportBatch; formats: ExportFormat[] }
  | { kind: 'refused'; refused: RefusedInvoice[]; remaining: string[]; message: string }
  | { kind: 'failed'; message: string; retryable: boolean; batchId: string | null };

/** The message for a refusal other than `selection_refused`. */
export function exportFailureMessage(f: ApiFailure): string {
  if (f.signInFailed) return 'Sign-in could not be verified. Close this dialog, reload the page and try again. Nothing was exported.';
  switch (f.errorCode) {
    case CODE_NOT_PERMITTED:
      return 'You may not export this selection. Nothing was exported.';
    case CODE_AUTHORIZATION_UNAVAILABLE:
      return 'Your rights could not be confirmed. Nothing was exported; try again.';
    case CODE_INVOICE_CONFLICT:
      return 'Another export is using one of these invoices. Nothing was exported. Refresh the view and try again.';
    case CODE_EBILLING_DISABLED:
      return 'eBilling is turned off in this environment. Nothing was exported.';
    default:
      break;
  }
  const text = describeFailure(f);
  return f.status === 500 && f.batchId ? `${text} Give an administrator the batch id ${f.batchId}.` : text;
}

/** The message for a failed file download (151a): the server's own words (scan pending, no such file, ...) with the code. */
export function downloadFailureMessage(f: ApiFailure): string {
  if (f.signInFailed) return 'Sign-in could not be verified. Close this dialog, reload the page and try again.';
  return describeFailure(f);
}

/** True when the same request may succeed if sent again (a transient refusal; nothing was exported). */
export function isRetryable(f: ApiFailure): boolean {
  if (f.status === 0 && !f.signInFailed) return true;
  return (
    f.errorCode === CODE_AUTHORIZATION_UNAVAILABLE ||
    f.errorCode === CODE_INVOICE_CONFLICT ||
    f.errorCode === CODE_UNAVAILABLE ||
    f.errorCode === CODE_FILE_FAILED ||
    f.errorCode === CODE_TRANSITION_FAILED
  );
}

/**
 * The selection without the refused invoices, in the selection's order. Ids are compared lower case (the server answers
 * with its own casing of the GUID).
 */
export function withoutRefused(requested: string[], refused: Array<{ invoiceId: string }>): string[] {
  const out = new Set(refused.map(r => r.invoiceId.toLowerCase()));
  return requested.filter(id => !out.has(id.toLowerCase()));
}

/** What the dialog shows for an answer of create-batch made for `requested`. */
export function exportOutcome(result: ApiResult<ExportBatch>, requested: string[]): ExportOutcome {
  if (result.ok) {
    return { kind: 'created', batch: result.value, formats: downloadableFormats(result.value) };
  }
  const f = result.failure;
  if (f.status === 400 && f.errorCode === CODE_SELECTION_REFUSED) {
    const refused = f.invoices.map(i => ({ invoiceId: i.invoiceId, reason: i.reason, text: refusalText(i.reason) }));
    const remaining = withoutRefused(requested, refused);
    const n = refused.length;
    const message =
      `${n} of ${requested.length} selected invoice${requested.length === 1 ? '' : 's'} cannot be exported. Nothing was exported.`;
    return { kind: 'refused', refused, remaining, message };
  }
  return { kind: 'failed', message: exportFailureMessage(f), retryable: isRetryable(f), batchId: f.batchId };
}
