/**
 * `@spaarke/communication-components/components/SubmitInvoice` - the email UI's Submit Invoice flow
 * (ebilling-r1 task 118): the one flow, its modal host, and the pure attachment helpers.
 */
export { SubmitInvoiceFromEmail, initialMarking } from './SubmitInvoiceFromEmail';
export type { SubmitInvoiceFromEmailProps } from './SubmitInvoiceFromEmail';
export { SubmitInvoiceDialog } from './SubmitInvoiceDialog';
export type { SubmitInvoiceDialogProps } from './SubmitInvoiceDialog';
export { hasPdfAttachment, isPdfFileName } from './invoiceAttachments';
