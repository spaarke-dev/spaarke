/**
 * IDataService → webApi-shaped adapter.
 *
 * `CreateRecordWizard`, `EntityCreationService` and `PolymorphicResolverService` take a
 * webApi-shaped object (`createRecord` resolving to `{ id }`, as `Xrm.WebApi` does), while the
 * wizards and widgets receive an `IDataService` (`createRecord` resolving to the bare id). This is
 * the one adapter between the two; before #2866 every wizard carried its own inline copy.
 *
 * @see IDataService in ../../types/serviceInterfaces
 * @see IWebApiWithCreate in ../../types/WebApiLike
 */

import type { IDataService } from '../../types/serviceInterfaces';
import type { IWebApiWithCreate } from '../../types/WebApiLike';

/** The webApi shape returned by {@link createWebApiFromDataService}. */
export interface IDataServiceWebApi extends IWebApiWithCreate {
  retrieveMultipleRecords(
    entityType: string,
    options?: string,
    maxPageSize?: number
  ): Promise<{ entities: Record<string, unknown>[] }>;
  updateRecord(entityType: string, id: string, data: Record<string, unknown>): Promise<{ id: string }>;
  deleteRecord(entityType: string, id: string): Promise<{ id: string }>;
}

/**
 * Wraps an `IDataService` in the webApi shape. Every call goes to `dataService`; only the return
 * shapes change (`createRecord`/`updateRecord`/`deleteRecord` resolve to `{ id }`). `maxPageSize`
 * is accepted for signature compatibility and ignored, because `IDataService` has no page size.
 */
export function createWebApiFromDataService(dataService: IDataService): IDataServiceWebApi {
  return {
    retrieveMultipleRecords: async (entityType: string, options?: string, _maxPageSize?: number) => {
      const result = await dataService.retrieveMultipleRecords(entityType, options);
      return { entities: result.entities };
    },
    retrieveRecord: (entityType: string, id: string, options?: string) =>
      dataService.retrieveRecord(entityType, id, options),
    createRecord: async (entityType: string, data: Record<string, unknown>) => {
      const id = await dataService.createRecord(entityType, data);
      return { id };
    },
    updateRecord: async (entityType: string, id: string, data: Record<string, unknown>) => {
      await dataService.updateRecord(entityType, id, data);
      return { id };
    },
    deleteRecord: async (entityType: string, id: string) => {
      await dataService.deleteRecord(entityType, id);
      return { id };
    },
  };
}
