/**
 * Pure rules for marking email attachments when submitting an invoice (ebilling-r1 task 118a).
 * No React, no I/O. Contract agreed with word-add-in-r1 (2026-10-21) - do not widen.
 */

export type InvoiceAttachmentRole = 'invoice' | 'supporting' | 'skip';

export interface MarkableAttachment {
  /** Opaque: sprk_document id in the email UI, Office attachment id in the add-in. */
  id: string;
  fileName: string;
  contentType?: string;
  sizeBytes?: number;
  suggestedInvoice?: boolean;
  disabledReason?: string;
}

type Mode = 'new' | 'correction' | 'supporting-only';

const PDF_NAME = /\.pdf$/i;

/** Invoice is PDF only (model 9.1). Content type wins when it says PDF; the file name is the fallback. */
function isPdf(a: MarkableAttachment): boolean {
  return /pdf/i.test(a.contentType ?? '') || PDF_NAME.test(a.fileName.trim());
}

/** Pre-select an invoice only when exactly one candidate exists; everything else is 'supporting' (or 'skip' when disabled). */
export function defaultInvoiceMarking(
  attachments: readonly MarkableAttachment[],
  mode: Mode
): Record<string, InvoiceAttachmentRole> {
  const next: Record<string, InvoiceAttachmentRole> = {};
  for (const a of attachments) next[a.id] = a.disabledReason ? 'skip' : 'supporting';
  if (mode === 'supporting-only') return next;

  const pdfs = attachments.filter(a => !a.disabledReason && isPdf(a));
  const suggested = pdfs.filter(a => a.suggestedInvoice);
  const pick =
    suggested.length === 1 ? suggested[0] : suggested.length === 0 && pdfs.length === 1 ? pdfs[0] : undefined;
  if (pick) next[pick.id] = 'invoice';
  return next;
}

/**
 * Validate a marking. Counts and mode are checked from `value` alone; pass `attachments`
 * (optional third argument) to also refuse a disabled attachment marked anything but 'skip'
 * ('disabled-attachment-marked') and a non-PDF marked 'invoice' ('invoice-not-allowed').
 *
 * `value` must hold every attachment id: `defaultInvoiceMarking` does. The component shows an id
 * missing from `value` as 'supporting', but validation and a host's wire marks read `value` only.
 */
export function validateInvoiceMarking(
  value: Record<string, InvoiceAttachmentRole>,
  mode: Mode,
  attachments?: readonly MarkableAttachment[]
):
  | { ok: true }
  | { ok: false; reason: 'no-invoice' | 'more-than-one-invoice' | 'invoice-not-allowed' | 'disabled-attachment-marked' } {
  if (attachments?.some(a => a.disabledReason && a.id in value && value[a.id] !== 'skip')) {
    return { ok: false, reason: 'disabled-attachment-marked' };
  }
  const invoiceIds = Object.keys(value).filter(id => value[id] === 'invoice');
  if (mode === 'supporting-only') {
    return invoiceIds.length > 0 ? { ok: false, reason: 'invoice-not-allowed' } : { ok: true };
  }
  if (attachments) {
    const byId = new Map(attachments.map(a => [a.id, a] as const));
    for (const id of invoiceIds) {
      const a = byId.get(id);
      if (a && (!isPdf(a) || a.disabledReason)) return { ok: false, reason: 'invoice-not-allowed' };
    }
  }
  if (invoiceIds.length === 0) return { ok: false, reason: 'no-invoice' };
  if (invoiceIds.length > 1) return { ok: false, reason: 'more-than-one-invoice' };
  return { ok: true };
}

/** Internal to the folder (not re-exported from index.ts). */
export function canBeInvoice(a: MarkableAttachment, mode: Mode): boolean {
  return mode !== 'supporting-only' && !a.disabledReason && isPdf(a);
}
