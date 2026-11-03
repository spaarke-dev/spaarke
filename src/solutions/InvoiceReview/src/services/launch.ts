/**
 * The page's launch envelope (ebilling-r1 task 110). The page is opened with an invoice id, either on the URL (`?invoiceId={guid}`) or in
 * the Xrm data envelope (`pagetype=webresource&webresourceName=sprk_invoicereviewpage&data=invoiceId%3D{guid}`), or from an invoice
 * form command / view row as `id` + `typename=sprk_invoice`. `parseDataParams` (`@spaarke/ui-components`) decodes both shapes; this module
 * validates its result (the `src/solutions/sprk_invoicespage` `parseMatterId` precedence: explicit id first, then the envelope). A missing
 * or malformed id is a launch error the page shows instead of calling the BFF. Exported for the tests.
 */

export type Launch = { ok: true; invoiceId: string } | { ok: false; message: string };

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
  return { ok: true, invoiceId: id };
}
