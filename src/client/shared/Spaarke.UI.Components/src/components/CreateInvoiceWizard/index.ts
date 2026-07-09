/**
 * index.ts
 * Public barrel export for the CreateInvoiceWizard shared library component.
 *
 * Consumer usage:
 *   import { CreateInvoiceWizard } from './components/CreateInvoiceWizard';
 */

// Primary entry point -- the wizard component
export { CreateInvoiceWizard, default, resolveInvoiceAssociateToStepConfig } from './CreateInvoiceWizard';
export type { ICreateInvoiceWizardProps } from './CreateInvoiceWizard';

// Entity-specific step component
export { CreateInvoiceStep } from './CreateInvoiceStep';
export type { ICreateInvoiceStepProps } from './CreateInvoiceStep';

// Service layer
export { InvoiceService } from './invoiceService';
export type { ICreateInvoiceResult } from './invoiceService';

// Form types
export type { ICreateInvoiceFormState } from './formTypes';
export { EMPTY_INVOICE_FORM, buildEmptyInvoiceForm, todayIsoDate } from './formTypes';
