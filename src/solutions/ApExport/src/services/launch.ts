/**
 * The dialog's launch envelope (ebilling-r1 task 151b). The ribbon script `sprk_/scripts/apexport_ribbon.js` opens the
 * page through `Xrm.Navigation.navigateTo` with `data = "mode=<export|markpaid>&ids=<id>,<id>,..."`; `parseDataParams`
 * (`@spaarke/ui-components`) decodes it. This module validates it: a known mode and 1 to {@link MAX_SELECTION} distinct
 * record ids. Anything else is a launch error the page shows instead of calling the BFF.
 */

export type LaunchMode = 'export' | 'markpaid';

/** The largest selection the export route accepts (task 150: 1 to 200 invoices). Mark Paid uses the same cap. */
export const MAX_SELECTION = 200;

export type Launch = { ok: true; mode: LaunchMode; invoiceIds: string[] } | { ok: false; error: string };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** A record id without braces, lower case (ADR-044: bare and lower case). */
export function cleanId(id: string): string {
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}

export function parseLaunch(params: Record<string, string | undefined>): Launch {
  const mode = (params.mode ?? '').toLowerCase();
  if (mode !== 'export' && mode !== 'markpaid') {
    return { ok: false, error: 'This dialog was opened without a known action. Close it and use Export to AP or Mark Paid.' };
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
