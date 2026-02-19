/**
 * Xrm WebApi type definitions for standalone HTML web resources.
 *
 * These types mirror the subset of ComponentFramework.WebApi used by
 * DataverseService and MatterService. At runtime, the actual Xrm.WebApi
 * object provides these methods identically to the PCF context.webAPI.
 *
 * This avoids importing @types/powerapps-component-framework in a non-PCF project.
 */

/**
 * Minimal type for a Dataverse entity record returned by WebApi queries.
 * Fields are accessed by logical name (string keys).
 */
export type WebApiEntity = Record<string, unknown>;

/**
 * Result shape from retrieveMultipleRecords.
 */
export interface RetrieveMultipleResult {
  entities: WebApiEntity[];
  nextLink?: string;
}

/**
 * Minimal WebApi interface matching the methods used by DataverseService
 * and MatterService. Both ComponentFramework.WebApi and Xrm.WebApi
 * implement this interface identically.
 */
export interface IWebApi {
  retrieveMultipleRecords(
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ): Promise<RetrieveMultipleResult>;

  retrieveRecord(
    entityLogicalName: string,
    id: string,
    options?: string
  ): Promise<WebApiEntity>;

  createRecord(
    entityLogicalName: string,
    data: WebApiEntity
  ): Promise<{ id: string }>;

  updateRecord(
    entityLogicalName: string,
    id: string,
    data: WebApiEntity
  ): Promise<{ id: string }>;

  deleteRecord(
    entityLogicalName: string,
    id: string
  ): Promise<{ id: string }>;
}
