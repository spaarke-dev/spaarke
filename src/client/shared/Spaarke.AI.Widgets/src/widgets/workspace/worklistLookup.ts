/**
 * worklistLookup - the decision wizard's search for Person / Lookup parameters, answered from Dataverse for the Worklist widget
 * (spaarke-ontology-platform-r1 task 059). The shared Worklist library has no Xrm (ADR-012), so the host builds this and passes it in.
 *
 * It answers only the lookups the decision catalog names:
 *   - Person                  -> contacts (the shared contact search)
 *   - `sprk_budget`           -> the item's own budgets (the core matter or project), narrowed by what was typed
 *   - `sprk_organization`     -> firms, by name (the shared organization search)
 * Any other entity returns no candidates rather than guessing a table: the wizard shows "no matches" and the user can still
 * decide without the field if it is optional.
 *
 * `createWorklistOpenLookup` / `createWorklistOpenRecord` are the side-pane and record-modal counterparts (D-182): the same scope,
 * opened through the host's `INavigationService` instead of typed into the field.
 */

import { searchContactsAsLookup, searchOrganizationsAsLookup } from '@spaarke/ui-components';
import type {
  DecisionLookupSearch,
  DecisionOpenLookup,
  DecisionOpenRecord,
  INavigationService,
} from '@spaarke/ui-components';

/** The one Dataverse call the lookup needs; `Xrm.WebApi` satisfies it. */
export interface WorklistLookupDataSource {
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
}

const MIN_QUERY = 2;
const BUDGET_ID_FIELD = 'sprk_budgetid';
const CORE_BUDGET_FIELD: Record<string, string> = {
  sprk_matter: '_sprk_matter_value',
  sprk_project: '_sprk_project_value',
};

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const odataString = (value: string) => value.replace(/'/g, "''");

export function createWorklistLookup(source: WorklistLookupDataSource): DecisionLookupSearch {
  // The shared helpers only call `retrieveMultipleRecords`.
  const dataService = source as unknown as Parameters<typeof searchContactsAsLookup>[0];

  return async request => {
    const query = request.query.trim();

    if (request.kind === 'Person') {
      return searchContactsAsLookup(dataService, query);
    }

    switch (request.entity) {
      case 'sprk_organization':
        return searchOrganizationsAsLookup(dataService, query);

      case 'sprk_budget': {
        const core = request.core;
        const field = core ? CORE_BUDGET_FIELD[core.recordType] : undefined;
        // Budgets are offered for the item's own record; with no core there is nothing to scope to, so only a typed name searches.
        const filters = ['statecode eq 0'];
        if (core && field) {
          const id = core.recordId.replace(/[{}]/g, '');
          if (!GUID.test(id)) return [];
          filters.push(`${field} eq ${id}`);
        } else if (query.length < MIN_QUERY) {
          return [];
        }
        if (query.length >= MIN_QUERY) filters.push(`contains(sprk_name,'${odataString(query)}')`);
        const result = await source.retrieveMultipleRecords(
          'sprk_budget',
          `?$select=${BUDGET_ID_FIELD},sprk_name&$filter=${filters.join(' and ')}&$orderby=sprk_name asc&$top=10`
        );
        return result.entities
          .filter(e => typeof e[BUDGET_ID_FIELD] === 'string')
          .map(e => ({ id: e[BUDGET_ID_FIELD] as string, name: (e['sprk_name'] as string) || 'Budget' }));
      }

      default:
        return [];
    }
  };
}

// ---------------------------------------------------------------------------
// The side-pane lookup and the record modal (D-182, task 158)
// ---------------------------------------------------------------------------

const ACTIVE_ONLY = '<condition attribute="statecode" operator="eq" value="0" />';
const andFilter = (...conditions: string[]) => `<filter type="and">${conditions.join('')}</filter>`;

/** The tables `createWorklistOpenLookup` can open: a Person is `contact`; the catalog's budget and firm lookups. */
export const WORKLIST_OPEN_LOOKUP_ENTITIES: readonly string[] = ['sprk_budget', 'sprk_organization', 'contact'];

/**
 * The decision wizard's side-pane lookup (`Xrm.Utility.lookupObjects` through `INavigationService.openLookup`), scoped exactly as
 * `createWorklistLookup` scopes the inline search, and no wider:
 *   - Person                  -> active contacts
 *   - `sprk_organization`     -> firms
 *   - `sprk_budget`           -> active budgets of the item's own matter or project (with no core, active budgets only)
 * Any other entity opens nothing (null), as the inline search returns no candidates for it. Single select; null on cancel.
 * With `source` (task 160), a Person pick also carries the contact's `emailaddress1` as `email`.
 */
export function createWorklistOpenLookup(
  navigation: Pick<INavigationService, 'openLookup'>,
  source?: WorklistLookupDataSource
): DecisionOpenLookup {
  const openLookup: DecisionOpenLookup = async request => {
    let filters: { entityLogicalName: string; filterXml: string }[] | undefined;

    if (request.kind === 'Person') {
      filters = [{ entityLogicalName: 'contact', filterXml: andFilter(ACTIVE_ONLY) }];
    } else if (request.entity === 'sprk_budget') {
      const conditions = [ACTIVE_ONLY];
      // The core record type is the budget's lookup column (`sprk_matter`, `sprk_project`).
      if (request.core && CORE_BUDGET_FIELD[request.core.recordType]) {
        const id = request.core.recordId.replace(/[{}]/g, '');
        if (!GUID.test(id)) return null;
        conditions.push(`<condition attribute="${request.core.recordType}" operator="eq" value="${id}" />`);
      }
      filters = [{ entityLogicalName: 'sprk_budget', filterXml: andFilter(...conditions) }];
    } else if (request.entity !== 'sprk_organization') {
      return null;
    }

    const results = await navigation.openLookup({
      entityType: request.entity,
      entityTypes: [request.entity],
      allowMultiSelect: false,
      ...(filters ? { filters } : {}),
    });
    // An empty result (cancel) is possible at runtime even though the array type says otherwise.
    const picked = results.length > 0 ? results[0] : undefined;
    if (!picked?.id) return null;
    // Task 160 (D-179): a Person pick also carries the contact's address when the host can read it, so the inquiry's To lookup
    // adds the contact by its address (the address is what is committed). No address, or a read that fails: none.
    const email = request.kind === 'Person' && source ? await contactEmail(source, picked.id) : null;
    return email ? { id: picked.id, name: picked.name, email } : { id: picked.id, name: picked.name };
  };
  // K2: the tables handled above. The wizard keeps the inline field for any other table instead of a dead pane field.
  return Object.assign(openLookup, { supportedEntities: WORKLIST_OPEN_LOOKUP_ENTITIES });
}

/** The contact's `emailaddress1`, or null when it has none, the id is not a GUID or the read fails. */
async function contactEmail(source: WorklistLookupDataSource, contactId: string): Promise<string | null> {
  const id = contactId.replace(/[{}]/g, '').toLowerCase();
  if (!GUID.test(id)) return null;
  try {
    const result = await source.retrieveMultipleRecords(
      'contact',
      `?$select=emailaddress1&$filter=contactid eq ${id}&$top=1`
    );
    const email = result.entities[0]?.['emailaddress1'];
    return typeof email === 'string' && email.trim() !== '' ? email.trim() : null;
    // eslint-disable-next-line no-restricted-syntax -- an unreadable address is "no address": the pick adds nothing to To
  } catch {
    return null;
  }
}

/** Opens a record in a platform modal above the wizard. Undefined when the host cannot (never `openForm`, which would leave the wizard). */
export function createWorklistOpenRecord(
  navigation: Partial<Pick<INavigationService, 'openRecordModal'>>
): DecisionOpenRecord | undefined {
  const open = navigation.openRecordModal;
  return open ? (entityName, entityId) => open.call(navigation, entityName, entityId) : undefined;
}
