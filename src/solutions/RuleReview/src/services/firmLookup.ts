/**
 * Read-only firm lookup for the Rule Review page (ebilling-r1 task 077).
 *
 * A firm is a `sprk_organization`. Picking one for a rule set, and showing a firm's name where the rule set API returns only its id,
 * is a simple lookup scoped to the signed-in user, so it goes through `Xrm.WebApi` (DATA-ACCESS-DECISION-CRITERIA: "simple lookup,
 * host-context"). This adapter is deliberately READ-ONLY: every write method throws, so nothing on this page can write a Dataverse row
 * directly (ADR-002). The rule set itself is only ever read and changed through the BFF (`ruleSetApi.ts`).
 *
 * The search is the shared `searchOrganizationsAsLookup` (the same one the Create Matter / Invoice wizards use).
 */

import { getXrm, searchOrganizationsAsLookup, type IDataService, type ILookupItem } from '@spaarke/ui-components';

const READ_ONLY = 'The Rule Review page does not write Dataverse rows; rule sets are changed only through the BFF (ADR-002).';

/** The lookup's data service: reads through `Xrm.WebApi` (resolved at call time), writes throw. */
export function createReadOnlyDataService(): IDataService {
  const webApi = () => {
    const api = getXrm()?.WebApi;
    if (!api) throw new Error('Xrm.WebApi is not available. The Rule Review page needs a Dataverse-hosted context for the firm lookup.');
    return api;
  };

  return {
    retrieveRecord: (entityName, id, options) => webApi().retrieveRecord(entityName, id, options),
    retrieveMultipleRecords: async (entityName, options) => {
      const result = await webApi().retrieveMultipleRecords(entityName, options);
      return { entities: result.entities as Record<string, unknown>[] };
    },
    createRecord: () => Promise.reject(new Error(READ_ONLY)),
    updateRecord: () => Promise.reject(new Error(READ_ONLY)),
    deleteRecord: () => Promise.reject(new Error(READ_ONLY)),
  };
}

export interface FirmLookup {
  /** Search firms by a name fragment (two characters or more). */
  search(query: string): Promise<ILookupItem[]>;
  /** The names of the given firms; an id that cannot be resolved is left out. */
  names(ids: readonly string[]): Promise<Record<string, string>>;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const CHUNK = 20;

export function createFirmLookup(data: IDataService = createReadOnlyDataService()): FirmLookup {
  return {
    search: query => searchOrganizationsAsLookup(data, query),

    async names(ids) {
      const valid = Array.from(new Set(ids.filter(id => GUID.test(id))));
      const out: Record<string, string> = {};
      for (let i = 0; i < valid.length; i += CHUNK) {
        const chunk = valid.slice(i, i + CHUNK);
        const filter = chunk.map(id => `sprk_organizationid eq ${id}`).join(' or ');
        const result = await data.retrieveMultipleRecords(
          'sprk_organization', `?$select=sprk_organizationid,sprk_organizationname&$filter=${filter}`
        );
        for (const row of result.entities) {
          const id = row['sprk_organizationid'];
          const name = row['sprk_organizationname'];
          if (typeof id === 'string' && typeof name === 'string') out[id.toLowerCase()] = name;
        }
      }
      return out;
    },
  };
}
