/**
 * openInvoiceReview - opens the eBilling Invoice Review page (`sprk_invoicereviewpage`) as a dialog on one invoice, focused on
 * one line when the Work Item is about a line (task 175, D-207).
 *
 * Billing Work Items are decided ONLY in that page, so the Worklist opens it instead of the Decision wizard. This is the same
 * code-page-in-a-dialog launch as `EmailComposer/openEmailCompose.ts` (Pattern B): `Xrm.Navigation.navigateTo` with a
 * `webresource` page input and `target: 2` (dialog), called DIRECTLY on `Xrm.Navigation` (an extracted alias loses `this` and
 * becomes a silent no-op). The promise settles when the dialog closes; the caller reads the Worklist again then (no postMessage).
 *
 * Unlike `openEmailCompose`, it THROWS when `Xrm.Navigation.navigateTo` is missing, so the caller can tell the user that Invoice
 * Review could not be opened. Xrm-coupled by design, so the HOST passes it to `WorklistWidget` (the widget itself has no Xrm).
 *
 * The page reads `invoiceId` (and `lineId`) from the `data` envelope; the platform encodes it into
 * `data=invoiceId%3D...%26lineId%3D...` (ebilling `src/solutions/InvoiceReview/src/services/launch.ts`).
 */
import { getXrm } from '../../utils/xrmContext';
import { cleanGuid } from '../../utils/guid';
import type { InvoiceReviewTarget } from './types';

/** Deployed web-resource name of the Invoice Review code page (ebilling task 112). `.html` is dropped for `navigateTo`. */
export const INVOICE_REVIEW_WEBRESOURCE_NAME = 'sprk_invoicereviewpage';

/** The message shown when the page could not be opened. */
export const INVOICE_REVIEW_FAILED = 'Invoice Review could not be opened';

/**
 * Invoice Review is a wide, line-by-line page, so it opens at 90% x 90% (owner/task 175). This is deliberately NOT one of the
 * OOB_MODAL_SIZES names (record 85, createForm 70, wizard 60): a record or a wizard is not what it is.
 */
const INVOICE_REVIEW_DIALOG_SIZE = { value: 90, unit: '%' as const };

/** The `navigateTo` arguments for a target (exported so the shape is testable without Xrm). */
export function buildInvoiceReviewLaunch(target: InvoiceReviewTarget) {
  const invoiceId = cleanGuid(target.invoiceId);
  const lineId = cleanGuid(target.lineId);
  const data = `invoiceId=${encodeURIComponent(invoiceId)}${lineId ? `&lineId=${encodeURIComponent(lineId)}` : ''}`;
  return {
    pageInput: { pageType: 'webresource' as const, webresourceName: INVOICE_REVIEW_WEBRESOURCE_NAME, data },
    navigationOptions: {
      target: 2 as const, // dialog
      position: 1 as const, // centered
      width: INVOICE_REVIEW_DIALOG_SIZE,
      height: INVOICE_REVIEW_DIALOG_SIZE,
    },
  };
}

/**
 * Open Invoice Review for a billing item. Resolves when the dialog closes. Rejects when there is no invoice to open, when this is
 * not a Model-Driven App host (no `Xrm.Navigation.navigateTo`), or when the platform refuses the dialog.
 */
export async function openInvoiceReview(target: InvoiceReviewTarget): Promise<void> {
  if (!cleanGuid(target.invoiceId)) throw new Error('Invoice Review needs an invoice id.');
  const xrm = getXrm('navigation');
  if (typeof xrm?.Navigation?.navigateTo !== 'function') {
    throw new Error('Xrm.Navigation.navigateTo is unavailable: Invoice Review opens only inside the Model-Driven App.');
  }
  const { pageInput, navigationOptions } = buildInvoiceReviewLaunch(target);
  // Call navigateTo DIRECTLY on Xrm.Navigation - never via an extracted local alias.
  await xrm.Navigation.navigateTo(pageInput, navigationOptions);
}
