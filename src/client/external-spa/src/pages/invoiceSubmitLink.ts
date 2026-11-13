/**
 * invoiceSubmitLink.ts — the address of the invoice-submission page and the optional record preselect it reads
 * (spaarke-SPA-external-access-platform-r4 task 007, FR-10; agreed with ebilling-r1).
 *
 * Router-free on purpose: it imports nothing from react-router or the wizard, so the Invoices widget can name the
 * route without importing the page (and with it the router and the shared wizard). `InvoiceSubmitPage` reads the
 * address with the same functions. Since task 010 no in-app link builds an `invoiceRecord` address (the record view's
 * "Submit invoice" opens a modal); the page still honours one, so a saved or pasted address keeps working.
 *
 *   /invoices/submit?invoiceRecord={kind}:{guid}
 *
 * The parameter is NOT `record`: that name is the app-wide record deep link (`RecordDeepLink`, mounted on every route),
 * which would reopen the record modal over the page and strip the parameter.
 */
import type { InvoiceCoreRecordKind } from '@spaarke/ui-components/components/InvoiceSubmission';

export const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The page's route; the Invoices widget's "Submit invoice" action opens it. */
export const INVOICE_SUBMIT_PATH = '/invoices/submit';

/** The optional query parameter that names a record to preselect: `invoiceRecord={kind}:{guid}` (R4 task 007, FR-10). */
export const INVOICE_RECORD_PARAM = 'invoiceRecord';

const INVOICE_RECORD_KINDS: readonly InvoiceCoreRecordKind[] = ['matter', 'project', 'workassignment'];

export interface InvoiceRecordRef {
  kind: InvoiceCoreRecordKind;
  recordId: string;
}

/** Reads `{kind}:{guid}`; anything malformed (no colon, unknown kind, not a GUID) is `null`: the link is ignored. */
export function parseInvoiceRecordParam(value: string | null): InvoiceRecordRef | null {
  if (!value) return null;
  const at = value.indexOf(':');
  if (at <= 0) return null;
  const kind = value.slice(0, at);
  const recordId = value.slice(at + 1);
  if (!(INVOICE_RECORD_KINDS as readonly string[]).includes(kind) || !GUID_PATTERN.test(recordId)) return null;
  return { kind: kind as InvoiceCoreRecordKind, recordId };
}
