/**
 * formTypes.ts
 * Form state types for Create New Invoice wizard.
 *
 * Field set is the owner-provided, Phase-0-validated manifest — see
 * `projects/visual-host-create-button-r1/notes/field-manifests/invoice.md`
 * (zero schema gaps confirmed for Invoice).
 */

/** Returns today's date as an ISO `YYYY-MM-DD` string (local time). */
export function todayIsoDate(): string {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export interface ICreateInvoiceFormState {
  /** Invoice Number — free text (optional). Maps to sprk_invoicenumber. */
  invoiceNumber: string;
  /** Name — free text (required; sprk_name is NOT NULL on the schema). Maps to sprk_name. */
  name: string;
  /** Description — free text, multi-line (optional). Maps to sprk_description. */
  description: string;
  /** sprk_vendororg lookup (-> sprk_organization) — GUID of the selected vendor org. */
  vendorOrgId: string;
  /** Display name of the selected vendor organization. */
  vendorOrgName: string;
  /**
   * Invoice Date — ISO date string. Defaults to today per spec FR-16 /
   * design §5.10 manifest ("default = today").
   */
  invoiceDate: string;
}

export function buildEmptyInvoiceForm(): ICreateInvoiceFormState {
  return {
    invoiceNumber: '',
    name: '',
    description: '',
    vendorOrgId: '',
    vendorOrgName: '',
    invoiceDate: todayIsoDate(),
  };
}

/**
 * NOTE: this is a snapshot evaluated at module load, not a function — most
 * callers should prefer {@link buildEmptyInvoiceForm} so `invoiceDate` reflects
 * "today" at the moment the wizard actually opens (module-load time could be
 * stale across midnight in a long-lived session). Kept for symmetry with the
 * other wizards' `EMPTY_*_FORM` constants and simple call sites (e.g. tests).
 */
export const EMPTY_INVOICE_FORM: ICreateInvoiceFormState = buildEmptyInvoiceForm();
