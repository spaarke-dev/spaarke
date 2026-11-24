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
 * Type-only imports: the record action registry imports this module, and a runtime import of the opener (which imports the record
 * modal) would make them import each other at load time.
 */
import type {
  InvoiceCoreRecordKind,
  InvoiceSubmissionRecordRef,
} from '@spaarke/ui-components/components/InvoiceSubmission';
import type { RecordTarget } from '../detail/record-targets';

/** The invoice's record-view entity. */
export const INVOICE_ENTITY = 'sprk_invoice';

/** The record-view entity of each core record kind. */
export const ENTITY_OF_INVOICE_KIND: Readonly<Record<InvoiceCoreRecordKind, string>> = {
  matter: 'sprk_matter',
  project: 'sprk_project',
  workassignment: 'sprk_workassignment',
};

/** The record view's target for an invoice id. */
export function invoiceTarget(invoiceId: string): RecordTarget {
  return { entityName: INVOICE_ENTITY, recordId: invoiceId.toLowerCase() };
}

/** The record view's target for a core record. */
export function coreRecordTarget(record: InvoiceSubmissionRecordRef): RecordTarget {
  return { entityName: ENTITY_OF_INVOICE_KIND[record.kind], recordId: record.recordId.toLowerCase() };
}
