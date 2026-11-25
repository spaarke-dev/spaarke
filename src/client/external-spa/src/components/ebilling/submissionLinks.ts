/**
 * submissionLinks — what the invoice submission result screen opens (ebilling-r1 iss052, #3082; owner checklist Q, 2026-11-24:
 * "the 'invoice received' should have navigation to the Invoice and to the related record").
 *
 * The shared result screen (`InvoiceSubmissionOutcome`, also inside `InvoiceSubmissionWizard`) offers "Open invoice" and
 * "Open {matter|project|work assignment}" only when its host passes `onOpenInvoice` / `onOpenRecord`, and only for an id the
 * server sent (the BFF names the invoice only for an Accepted submission the caller may see). This module turns those ids into
 * the record view's targets. Opening one grants nothing: the record view reads it through the caller's own external token, and a
 * record the caller cannot see shows the modal's "You don't have access" state.
 *
 * Two hosts:
 *   - `InvoiceSubmitPage` (the wizard and the `?submissionId=` resume view) opens both through the shell's opener
 *     (`useOpenRecord`), as `intakeSuccess.tsx` opens a new request.
 *   - `SubmitInvoiceAction` (inside a record modal) opens them as that modal's children: see that file.
 *
 * No runtime import of the opener: the record action registry imports this module, and the opener imports the record modal, so
 * they would import each other at load time. The one runtime import, `recordAccess`, imports only types and the access-level hook.
 */
import type { InvoiceSubmissionRecordRef } from '@spaarke/ui-components/components/InvoiceSubmission';
import type { RecordTarget } from '../detail/record-targets';
import { ENTITY_OF_INVOICE_KIND } from '../record/recordAccess';

/** The invoice's record-view entity. */
export const INVOICE_ENTITY = 'sprk_invoice';

/** The record-view entity of each core record kind: derived from the record types that offer "Submit invoice". */
export { ENTITY_OF_INVOICE_KIND };

/** The record view's target for an invoice id. */
export function invoiceTarget(invoiceId: string): RecordTarget {
  return { entityName: INVOICE_ENTITY, recordId: invoiceId.toLowerCase() };
}

/** The record view's target for a core record. */
export function coreRecordTarget(record: InvoiceSubmissionRecordRef): RecordTarget {
  return { entityName: ENTITY_OF_INVOICE_KIND[record.kind], recordId: record.recordId.toLowerCase() };
}
