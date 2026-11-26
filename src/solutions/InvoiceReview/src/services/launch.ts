/**
 * The page's launch envelope (ebilling-r1 task 110). The page is opened with an invoice id, either on the URL (`?invoiceId={guid}`) or in
 * the Xrm data envelope (`pagetype=webresource&webresourceName=sprk_invoicereviewpage&data=invoiceId%3D{guid}`), or from an invoice
 * form command / view row as `id` + `typename=sprk_invoice`. `parseDataParams` (`@spaarke/ui-components`) decodes both shapes; this module
 * validates its result (the `src/solutions/sprk_invoicespage` `parseMatterId` precedence: explicit id first, then the envelope). A missing
 * or malformed id is a launch error the page shows instead of calling the BFF. Exported for the tests.
 *
 * Optional `lineId` (EB-D-194; `?lineId={guid}` or `data=invoiceId%3D{guid}%26lineId%3D{guid}`, `lineid` also accepted): the Console's billing
 * Work Items open the review on one line. It is a hint, never a gate: missing, or present but not a GUID, the invoice still opens and
 * `lineId` is undefined. A line id that is a GUID but not one of the invoice's lines is the page's to ignore (no error, default selection).
 *
 * Result shape: `{ ok: true; invoiceId: string; lineId?: string }` (both ids bare and lower case, ADR-044) or `{ ok: false; message: string }`.
 */

export type Launch = { ok: true; invoiceId: string; lineId?: string } | { ok: false; message: string };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** A record id without braces, lower case (ADR-044: bare and lower case). */
export function cleanId(id: string): string {
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}

export function parseLaunch(params: Record<string, string | undefined>): Launch {
  const typename = (params.typename ?? '').trim().toLowerCase();
  if (typename && typename !== 'sprk_invoice') {
    return { ok: false, message: 'The Invoice Review page opens only for an invoice.' };
  }
  const raw = params.invoiceId ?? params.invoiceid ?? params.id ?? '';
  const id = cleanId(raw);
  if (id.length === 0) {
    return { ok: false, message: 'No invoice was given. Open the review from an invoice.' };
  }
  if (!GUID.test(id)) {
    return { ok: false, message: 'The invoice could not be identified. Open the review from the invoice again.' };
  }
  const lineId = cleanId(params.lineId ?? params.lineid ?? '');
  // A bad line id is dropped, not an error: the hint must never block the review of the invoice.
  return GUID.test(lineId) ? { ok: true, invoiceId: id, lineId } : { ok: true, invoiceId: id };
}
