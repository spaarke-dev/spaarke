/**
 * Xrm.WebApi Adapter for IDataService
 *
 * Bridges the Xrm.WebApi runtime API to the platform-agnostic IDataService
 * interface, enabling shared components to perform Dataverse CRUD operations
 * without coupling to the Xrm SDK directly.
 *
 * @see IDataService in ../../types/serviceInterfaces
 * @see ADR-012 - Shared Component Library
 *
 * @example
 * ```typescript
 * import { createXrmDataService } from "@spaarke/ui-components";
 *
 * const dataService = createXrmDataService();
 * const id = await dataService.createRecord("sprk_matter", { sprk_name: "Acme v. Beta" });
 * const record = await dataService.retrieveRecord("sprk_matter", id);
 * ```
 */

import type { IDataService } from '../../types/serviceInterfaces';
import { getXrm } from '../xrmContext';

/**
 * Creates an IDataService implementation backed by Xrm.WebApi.
 *
 * The adapter delegates each method to the corresponding Xrm.WebApi call,
 * normalising return types (e.g. stripping the EntityReference wrapper from
 * create/update/delete responses) so consumers only deal with plain values.
 *
 * @returns An IDataService backed by the current Xrm.WebApi context
 * @throws Error if the Xrm context is unavailable at call time
 *
 * @example
 * ```typescript
 * const dataService = createXrmDataService();
 * const matters = await dataService.retrieveMultipleRecords(
 *   "sprk_matter",
 *   "?$select=sprk_name&$filter=statecode eq 0&$top=50"
 * );
 * console.log(`Found ${matters.entities.length} active matters`);
 * ```
 */
export function createXrmDataService(): IDataService {
  /**
   * Resolves the Xrm.WebApi reference, throwing a descriptive error when
   * the host environment does not expose an Xrm context.
   */
  function getWebApi() {
    const xrm = getXrm();
    if (!xrm?.WebApi) {
      throw new Error(
        'Xrm.WebApi is not available. Ensure this adapter is used within a Dataverse-hosted context (PCF control or Code Page).'
      );
    }
    return xrm.WebApi;
  }

  return {
    async createRecord(
      entityName: string,
      data: Record<string, unknown>
    ): Promise<string> {
      const webApi = getWebApi();
      const ref = await webApi.createRecord(entityName, data);
      return ref.id;
    },

    async retrieveRecord(
      entityName: string,
      id: string,
      options?: string
    ): Promise<Record<string, unknown>> {
      const webApi = getWebApi();
      return webApi.retrieveRecord(entityName, id, options);
    },

    async retrieveMultipleRecords(
      entityName: string,
      options?: string
    ): Promise<{ entities: Record<string, unknown>[] }> {
      const webApi = getWebApi();
      const result = await webApi.retrieveMultipleRecords(entityName, options);
      return { entities: result.entities };
    },

    async updateRecord(
      entityName: string,
      id: string,
      data: Record<string, unknown>
    ): Promise<void> {
      const webApi = getWebApi();
      await webApi.updateRecord(entityName, id, data);
    },

    async deleteRecord(entityName: string, id: string): Promise<void> {
      const webApi = getWebApi();
      await webApi.deleteRecord(entityName, id);
    },
  };
}
