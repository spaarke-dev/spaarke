/**
 * The dialog's launch envelope (ebilling-r1 task 151b). The ribbon script `sprk_/scripts/apexport_ribbon.js` opens the
 * page through `Xrm.Navigation.navigateTo` with `data = "mode=<export|markpaid>&ids=<id>,<id>,..."`; `parseDataParams`
 * (`@spaarke/ui-components`) decodes it. This module validates it: a known mode and 1 to {@link MAX_SELECTION} distinct
 * record ids. Anything else is a launch error the page shows instead of calling the BFF.
 *
 * Task 151c-1b adds two modes opened on one existing batch from the Billing Export form:
 * `data = "mode=<email|marksent>&batch=<batchId>"`.
 */

export type InvoiceLaunchMode = 'export' | 'markpaid';

/** Modes opened on one existing batch (ebilling-r1 task 151c-1b): Email to AP and Mark Sent to AP (Billing Export form). */
export type BatchLaunchMode = 'email' | 'marksent';

export type LaunchMode = InvoiceLaunchMode | BatchLaunchMode;

/** The largest selection the export route accepts (task 150: 1 to 200 invoices). Mark Paid uses the same cap. */
export const MAX_SELECTION = 200;

export type Launch =
  | { ok: true; mode: InvoiceLaunchMode; invoiceIds: string[] }
  | { ok: true; mode: BatchLaunchMode; batchId: string }
  | { ok: false; error: string };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** A record id without braces, lower case (ADR-044: bare and lower case). */
export function cleanId(id: string): string {
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}

export function parseLaunch(params: Record<string, string | undefined>): Launch {
  const mode = (params.mode ?? '').toLowerCase();
  if (mode === 'email' || mode === 'marksent') {
    // Task 151c-1b: the Billing Export form opens the dialog on one batch (`mode=email&batch=<id>`).
    const batchId = cleanId(params.batch ?? '');
    if (!GUID.test(batchId)) {
      return { ok: false, error: 'No export batch was selected. Close this dialog, open the batch and try again.' };
    }
    return { ok: true, mode, batchId };
  }
  if (mode !== 'export' && mode !== 'markpaid') {
    return {
      ok: false,
      error: 'This dialog was opened without a known action. Close it and use Export to AP, Mark Paid, Email to AP or Mark Sent to AP.',
    };
  }
  const ids = (params.ids ?? '')
    .split(',')
    .map(cleanId)
    .filter(id => id.length > 0);
  if (ids.length === 0) {
    return { ok: false, error: 'No invoice was selected. Close this dialog, select one or more invoices and try again.' };
  }
  if (ids.some(id => !GUID.test(id))) {
    return { ok: false, error: 'The selection could not be read. Close this dialog and select the invoices again.' };
  }
  const distinct = Array.from(new Set(ids));
  if (distinct.length > MAX_SELECTION) {
    return { ok: false, error: `Select at most ${MAX_SELECTION} invoices at a time.` };
  }
  return { ok: true, mode, invoiceIds: distinct };
}
