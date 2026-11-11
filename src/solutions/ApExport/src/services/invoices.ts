/**
 * The selected invoices, read for display only (ebilling-r1 task 151b). The dialog shows the selection before it calls
 * the BFF: firm, record, invoice number, tracking number, date, approved total, currency and status.
 *
 * Read through `Xrm.WebApi` as the signed-in user (DATA-ACCESS-DECISION-CRITERIA: a read-only display of rows the user
 * already sees in the view; `RuleReview`'s firm lookup is the same pattern). Nothing here decides anything: which
 * invoices may be exported or paid is the server's answer (FR-41, FR-42), shown per invoice after the call. A row the
 * read does not return is still listed (by id) and still sent; the server answers for it.
 */

const FORMATTED = '@OData.Community.Display.V1.FormattedValue';

export const INVOICE_COLUMNS = [
  'sprk_invoiceid',
  'sprk_name',
  'sprk_invoicenumber',
  'sprk_trackingnumber',
  '_sprk_vendororg_value',
  'sprk_regardingrecordname',
  'sprk_invoicedate',
  'sprk_approvedtotal',
  'sprk_currency',
  'statuscode',
] as const;

export interface InvoiceRow {
  id: string;
  /** False when the display read did not return the invoice (the server still answers for it). */
  found: boolean;
  name: string | null;
  invoiceNumber: string | null;
  trackingNumber: string | null;
  firmName: string | null;
  recordName: string | null;
  /** The date as the platform formats it for the user, else the raw value. */
  invoiceDate: string | null;
  approvedTotal: number | null;
  currency: string | null;
  statusLabel: string | null;
}

/** The minimal `Xrm.WebApi` surface the reader needs (injected; the tests pass a fake). */
export interface WebApiLike {
  retrieveMultipleRecords(entity: string, options: string): Promise<{ entities: Array<Record<string, unknown>> }>;
}

/** The OData query options for the selection (`Microsoft.Dynamics.CRM.In` on the id). */
export function invoiceQuery(ids: string[]): string {
  const values = ids.map(id => `'${id}'`).join(',');
  return `?$select=${INVOICE_COLUMNS.join(',')}&$filter=Microsoft.Dynamics.CRM.In(PropertyName='sprk_invoiceid',PropertyValues=[${values}])`;
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function num(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** One `Xrm.WebApi` row as the dialog shows it. */
export function toInvoiceRow(entity: Record<string, unknown>): InvoiceRow {
  return {
    id: String(entity.sprk_invoiceid ?? '').toLowerCase(),
    found: true,
    name: text(entity.sprk_name),
    invoiceNumber: text(entity.sprk_invoicenumber),
    trackingNumber: text(entity.sprk_trackingnumber),
    firmName: text(entity[`_sprk_vendororg_value${FORMATTED}`]),
    recordName: text(entity.sprk_regardingrecordname),
    invoiceDate: text(entity[`sprk_invoicedate${FORMATTED}`]) ?? text(entity.sprk_invoicedate),
    approvedTotal: num(entity.sprk_approvedtotal),
    currency: text(entity.sprk_currency),
    statusLabel: text(entity[`statuscode${FORMATTED}`]),
  };
}

/** A placeholder for an id the read did not return. */
export function missingRow(id: string): InvoiceRow {
  return {
    id,
    found: false,
    name: null,
    invoiceNumber: null,
    trackingNumber: null,
    firmName: null,
    recordName: null,
    invoiceDate: null,
    approvedTotal: null,
    currency: null,
    statusLabel: null,
  };
}

/** The rows in the selection's order; an id the read did not return becomes {@link missingRow}. */
export function orderRows(ids: string[], entities: Array<Record<string, unknown>>): InvoiceRow[] {
  const byId = new Map(entities.map(e => toInvoiceRow(e)).map(r => [r.id, r] as const));
  return ids.map(id => byId.get(id) ?? missingRow(id));
}

/** Read the selection for display. Resolves `{ rows, readFailed }`; never rejects (a failed read lists ids only). */
export async function readInvoices(webApi: WebApiLike | undefined, ids: string[]): Promise<{ rows: InvoiceRow[]; readFailed: boolean }> {
  if (!webApi) return { rows: ids.map(missingRow), readFailed: true };
  try {
    const result = await webApi.retrieveMultipleRecords('sprk_invoice', invoiceQuery(ids));
    return { rows: orderRows(ids, result?.entities ?? []), readFailed: false };
  } catch (err) {
    console.warn('[ApExport] The selected invoices could not be read for display:', err);
    return { rows: ids.map(missingRow), readFailed: true };
  }
}

/** A total per currency (R1 is one currency; a mixed selection shows one line per currency). */
export interface CurrencyTotal {
  currency: string;
  amount: number;
}

export interface SelectionSummary {
  count: number;
  totals: CurrencyTotal[];
  /** Rows with no approved total read (not found, or empty). */
  withoutTotal: number;
}

export function summarize(rows: InvoiceRow[]): SelectionSummary {
  const totals = new Map<string, number>();
  let withoutTotal = 0;
  for (const row of rows) {
    if (row.approvedTotal === null) {
      withoutTotal++;
      continue;
    }
    const currency = row.currency ?? '';
    totals.set(currency, (totals.get(currency) ?? 0) + row.approvedTotal);
  }
  return {
    count: rows.length,
    totals: Array.from(totals, ([currency, amount]) => ({ currency, amount: Math.round(amount * 100) / 100 })),
    withoutTotal,
  };
}

/** An amount with its currency: ISO 4217 codes through `Intl`, anything else as a plain number and the code. */
export function formatAmount(amount: number, currency: string | null | undefined, locale?: string): string {
  const code = (currency ?? '').trim();
  if (/^[A-Za-z]{3}$/.test(code)) {
    try {
      return new Intl.NumberFormat(locale, { style: 'currency', currency: code.toUpperCase() }).format(amount);
    } catch {
      /* an unknown code: fall through */
    }
  }
  const plain = new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
  return code ? `${plain} ${code}` : plain;
}
