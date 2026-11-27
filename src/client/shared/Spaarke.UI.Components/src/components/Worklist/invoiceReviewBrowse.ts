/**
 * invoiceReviewBrowse - the browse set of the in-app Invoice Review modal (`InvoiceReviewModal`, task 195). Pure: no React, no Xrm.
 *
 * The set is the billing items the user currently sees in the list the item was activated from (same lane, same count filter, same
 * order), reduced to ONE entry per invoice: two lines of one invoice are one stop in "N of M", because the page opens on the invoice.
 * The first item of an invoice supplies the line the page focuses; the activated item's own entry takes the activated line instead, so
 * the page opens on exactly what the user opened. The invoice of the activated item is the start index.
 */

import { cleanGuid } from '../../utils/guid';
import type { InvoiceReviewTarget } from './types';

export interface InvoiceBrowseSet {
  /** One target per invoice, in display order. */
  targets: InvoiceReviewTarget[];
  /** Index in `targets` of the activated item's invoice. */
  startIndex: number;
}

/**
 * @param visible The `invoiceReview` of each visible item in display order; `null` / `undefined` for an item that is not a billing item.
 * @param activated The target of the item the user activated.
 */
export function buildInvoiceBrowseSet(
  visible: ReadonlyArray<InvoiceReviewTarget | null | undefined>,
  activated: InvoiceReviewTarget
): InvoiceBrowseSet {
  const targets: InvoiceReviewTarget[] = [];
  const seen = new Set<string>();
  for (const target of visible) {
    const invoiceId = cleanGuid(target?.invoiceId);
    if (!target || !invoiceId || seen.has(invoiceId)) continue;
    seen.add(invoiceId);
    targets.push({ invoiceId: target.invoiceId, lineId: target.lineId ?? null });
  }

  const activatedInvoice = cleanGuid(activated.invoiceId);
  const own: InvoiceReviewTarget = { invoiceId: activated.invoiceId, lineId: activated.lineId ?? null };
  const at = targets.findIndex(t => cleanGuid(t.invoiceId) === activatedInvoice);
  if (at >= 0) {
    targets[at] = own;
    return { targets, startIndex: at };
  }
  // Not among the visible billing items (cannot happen from the list; kept so the modal still opens on what was activated).
  return { targets: [own, ...targets], startIndex: 0 };
}
