/**
 * The page's launch envelope (ebilling-r1 task 084a). The matter, project and work assignment main forms host the page as a web resource
 * control on the Billing tab with "Pass record object-type code and unique identifier as parameters", so the URL carries `id={record guid}`
 * and `typename={sprk_matter|sprk_project|sprk_workassignment}`. The table decides which of the three routes is called; a `typename` the page
 * does not serve, or an `id` that is not a GUID, is a launch error the page shows instead of calling the BFF. An unsaved form has no `id`.
 *
 * `parseDataParams` (`@spaarke/ui-components`) decodes the URL; this module validates its result. Exported for the tests.
 */

import type { CoreRoute } from './recordProceduresApi';

export type CoreTable = 'sprk_matter' | 'sprk_project' | 'sprk_workassignment';

export type Launch =
  | { ok: true; core: CoreRoute; table: CoreTable; recordId: string }
  /** The form is a new, unsaved record: no Procedures yet. */
  | { ok: false; kind: 'unsaved'; message: string }
  | { ok: false; kind: 'invalid'; message: string };

/** The three core records the panel serves: the form's table, its route segment and the word the panel uses for it. */
export const CORE_RECORDS: Readonly<Record<CoreTable, { route: CoreRoute; noun: string }>> = {
  sprk_matter: { route: 'matters', noun: 'matter' },
  sprk_project: { route: 'projects', noun: 'project' },
  sprk_workassignment: { route: 'workassignments', noun: 'work assignment' },
};

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** A record id without braces, lower case (ADR-044: bare and lower case). */
export function cleanId(id: string): string {
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}

const isCoreTable = (t: string): t is CoreTable => Object.prototype.hasOwnProperty.call(CORE_RECORDS, t);

export function parseLaunch(params: Record<string, string | undefined>): Launch {
  const typename = (params.typename ?? '').trim().toLowerCase();
  if (!isCoreTable(typename)) {
    return { ok: false, kind: 'invalid', message: 'Procedures can only be shown on a matter, a project or a work assignment.' };
  }
  // `id` is what the form passes; `recordId` lets the page be opened on its own for testing.
  const id = cleanId(params.id ?? params.recordId ?? '');
  if (id.length === 0) {
    return { ok: false, kind: 'unsaved', message: `Save the ${CORE_RECORDS[typename].noun} to see its Procedures.` };
  }
  if (!GUID.test(id)) {
    return { ok: false, kind: 'invalid', message: 'The record could not be identified. Reload the form.' };
  }
  return { ok: true, core: CORE_RECORDS[typename].route, table: typename, recordId: id };
}
