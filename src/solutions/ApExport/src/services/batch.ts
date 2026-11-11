/**
 * The batch and the AP address, read for display only (ebilling-r1 task 151c-1b).
 *
 * The Email to AP step shows the batch (name, invoice count, total, whether a Sent to AP Date is already held) and the
 * AP address Email to AP will use. Both are read through `Xrm.WebApi` as the signed-in user, the same display-read
 * pattern as `invoices.ts`. Nothing here decides anything: whether the user may send, and to whom, is the server's
 * answer (Read and Write on the batch, EB-D-183; the settings address, 409 `ap_email_not_configured`). A read that
 * fails leaves the value unknown, never "none": the dialog then lets the server answer.
 */

import { formatAmount } from './invoices';
import type { ExportBatch } from './apExportApi';
import type { WebApiLike } from './invoices';

const FORMATTED = '@OData.Community.Display.V1.FormattedValue';

/** `sprk_billingexport.sprk_exportstatus`: Completed (only a Completed batch has files to email). */
export const EXPORT_STATUS_COMPLETED = 100000001;

export const BATCH_COLUMNS = ['sprk_name', 'sprk_exportstatus', 'sprk_invoicecount', 'sprk_totalamount', 'sprk_senttoapdate'] as const;

export interface BatchInfo {
  batchId: string;
  name: string | null;
  /** True/false when the status was read; null when it is unknown (the server decides). */
  completed: boolean | null;
  invoiceCount: number | null;
  /** The total as the platform formats it, else a plain amount, else null. */
  totalLabel: string | null;
  /** The Sent to AP Date held on the batch (ISO), else null. */
  sentToApDate: string | null;
}

/** The minimal record reader (`Xrm.WebApi.retrieveRecord`); injected, the tests pass a fake. */
export interface RecordReaderLike extends WebApiLike {
  retrieveRecord(entity: string, id: string, options: string): Promise<Record<string, unknown>>;
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function num(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

export function unknownBatch(batchId: string): BatchInfo {
  return { batchId, name: null, completed: null, invoiceCount: null, totalLabel: null, sentToApDate: null };
}

export function toBatchInfo(batchId: string, entity: Record<string, unknown>): BatchInfo {
  const status = num(entity.sprk_exportstatus);
  const total = num(entity.sprk_totalamount);
  return {
    batchId,
    name: text(entity.sprk_name),
    completed: status === null ? null : status === EXPORT_STATUS_COMPLETED,
    invoiceCount: num(entity.sprk_invoicecount),
    totalLabel: text(entity[`sprk_totalamount${FORMATTED}`]) ?? (total === null ? null : formatAmount(total, null)),
    sentToApDate: text(entity.sprk_senttoapdate),
  };
}

/** The batch a just-finished export created (it is Completed on a 201; its Sent to AP Date is empty). */
export function batchFromExport(batch: ExportBatch, totalLabel: string | null): BatchInfo {
  return {
    batchId: batch.batchId,
    name: batch.name,
    completed: batch.status.toLowerCase() === 'completed',
    invoiceCount: batch.invoiceCount,
    totalLabel,
    sentToApDate: null,
  };
}

/** Read the batch for display. Resolves `{ batch, readFailed }`; never rejects (a failed read leaves the batch unknown). */
export async function readBatch(
  webApi: RecordReaderLike | undefined,
  batchId: string
): Promise<{ batch: BatchInfo; readFailed: boolean }> {
  if (!webApi) return { batch: unknownBatch(batchId), readFailed: true };
  try {
    const entity = await webApi.retrieveRecord('sprk_billingexport', batchId, `?$select=${BATCH_COLUMNS.join(',')}`);
    return { batch: toBatchInfo(batchId, entity ?? {}), readFailed: false };
  } catch (err) {
    console.warn('[ApExport] The export batch could not be read for display:', err);
    return { batch: unknownBatch(batchId), readFailed: true };
  }
}

/** The configured AP address: set, empty, or unknown (the read failed or the user may not read the settings). */
export type ApAddress = { kind: 'address'; address: string } | { kind: 'none' } | { kind: 'unknown' };

export const AP_ADDRESS_QUERY = '?$select=sprk_apexportemail&$filter=statecode eq 0&$orderby=createdon asc&$top=1';

export async function readApAddress(webApi: WebApiLike | undefined): Promise<ApAddress> {
  if (!webApi) return { kind: 'unknown' };
  try {
    const result = await webApi.retrieveMultipleRecords('sprk_ebillingsetting', AP_ADDRESS_QUERY);
    const rows = result?.entities ?? [];
    if (rows.length === 0) return { kind: 'none' };
    const address = text(rows[0].sprk_apexportemail)?.trim();
    return address ? { kind: 'address', address } : { kind: 'none' };
  } catch (err) {
    console.warn('[ApExport] The AP address could not be read for display:', err);
    return { kind: 'unknown' };
  }
}

/** A date and time as the user's locale shows it; the raw text when it is not a date. */
export function formatDateTime(iso: string | null | undefined, locale?: string, timeZone?: string): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(date);
}
