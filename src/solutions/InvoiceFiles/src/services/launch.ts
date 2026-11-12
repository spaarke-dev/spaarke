/**
 * The page's launch envelope (ebilling-r1 task 151e). The invoice main form hosts the page as a web resource control with "Pass record
 * object-type code and unique identifier as parameters", so the URL carries `id={invoice guid}` and `typename=sprk_invoice` (the form's
 * record). A saved invoice is the only thing the page lists files for: an unsaved form has no `id` and shows a prompt, and a `typename`
 * other than `sprk_invoice` or an `id` that is not a GUID is a launch error the page shows instead of calling the BFF.
 *
 * `parseDataParams` (`@spaarke/ui-components`) decodes the URL; this module validates its result. Exported for the tests.
 */

export type Launch =
  | { ok: true; invoiceId: string }
  /** The form is a new, unsaved record: no files yet. */
  | { ok: false; kind: 'unsaved'; message: string }
  | { ok: false; kind: 'invalid'; message: string };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** A record id without braces, lower case (ADR-044: bare and lower case). */
export function cleanId(id: string): string {
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}

export function parseLaunch(params: Record<string, string | undefined>): Launch {
  const typename = (params.typename ?? '').trim().toLowerCase();
  if (typename && typename !== 'sprk_invoice') {
    return { ok: false, kind: 'invalid', message: 'This section can only be shown on an invoice.' };
  }
  // `id` is what the form passes; `invoiceId` lets the page be opened on its own for testing.
  const id = cleanId(params.id ?? params.invoiceId ?? '');
  if (id.length === 0) {
    return { ok: false, kind: 'unsaved', message: 'Save the invoice to see its files.' };
  }
  if (!GUID.test(id)) {
    return { ok: false, kind: 'invalid', message: 'The invoice could not be identified. Reload the form.' };
  }
  return { ok: true, invoiceId: id };
}
