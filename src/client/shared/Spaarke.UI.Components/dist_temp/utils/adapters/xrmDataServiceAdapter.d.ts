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
export declare function createXrmDataService(): IDataService;
//# sourceMappingURL=xrmDataServiceAdapter.d.ts.map