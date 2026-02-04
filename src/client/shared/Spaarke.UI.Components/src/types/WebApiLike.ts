/**
 * WebApiLike - Context-agnostic WebAPI interface for dependency injection
 *
 * This interface abstracts the Dataverse WebAPI so that services can be used
 * in both PCF controls (ComponentFramework.WebApi) and Custom Pages (Xrm.WebApi)
 * without coupling to either specific implementation.
 *
 * @see ADR-012 - Shared Component Library (no PCF-specific dependencies)
 * @see EventTypeService - Consumer of this interface
 */

/**
 * Standard OData response wrapper for retrieveMultipleRecords
 */
export interface IWebApiRetrieveMultipleResponse<T = Record<string, unknown>> {
  /** Array of retrieved entities */
  entities: T[];
  /** OData next link for pagination (if more records exist) */
  nextLink?: string;
}

/**
 * Context-agnostic WebAPI interface
 *
 * This interface matches the subset of ComponentFramework.WebApi that we need,
 * allowing services to work with either:
 * - PCF context.webAPI
 * - Xrm.WebApi
 * - Mock implementations for testing
 *
 * @example PCF usage:
 * ```typescript
 * // In PCF control
 * const config = await getEventTypeFieldConfig(context.webAPI, eventTypeId);
 * ```
 *
 * @example Custom Page usage:
 * ```typescript
 * // In Custom Page - wrap Xrm.WebApi
 * const webApiLike: IWebApiLike = {
 *   retrieveRecord: (entityType, id, options) =>
 *     Xrm.WebApi.retrieveRecord(entityType, id, options),
 *   retrieveMultipleRecords: (entityType, options) =>
 *     Xrm.WebApi.retrieveMultipleRecords(entityType, options),
 * };
 * const config = await getEventTypeFieldConfig(webApiLike, eventTypeId);
 * ```
 *
 * @example Test usage:
 * ```typescript
 * const mockWebApi: IWebApiLike = {
 *   retrieveRecord: jest.fn().mockResolvedValue({ sprk_fieldconfigjson: '{}' }),
 *   retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
 * };
 * ```
 */
export interface IWebApiLike {
  /**
   * Retrieves a single entity record.
   *
   * @param entityType - The logical name of the entity (e.g., "sprk_eventtype")
   * @param id - The GUID of the record to retrieve
   * @param options - OData query options (e.g., "$select=field1,field2")
   * @returns Promise resolving to the entity record
   * @throws Error if record not found (404) or other API errors
   */
  retrieveRecord(
    entityType: string,
    id: string,
    options?: string
  ): Promise<Record<string, unknown>>;

  /**
   * Retrieves multiple entity records based on OData query.
   *
   * @param entityType - The logical name of the entity (e.g., "sprk_eventtype")
   * @param options - OData query string (e.g., "?$filter=statecode eq 0&$select=field1")
   * @param maxPageSize - Maximum number of records per page (default varies by implementation)
   * @returns Promise resolving to response with entities array
   */
  retrieveMultipleRecords(
    entityType: string,
    options?: string,
    maxPageSize?: number
  ): Promise<IWebApiRetrieveMultipleResponse>;
}

/**
 * Helper to create IWebApiLike from Xrm.WebApi in Custom Pages
 *
 * @param xrmWebApi - The Xrm.WebApi object from window.parent.Xrm or Xrm global
 * @returns IWebApiLike compatible interface
 *
 * @example
 * ```typescript
 * // In Custom Page
 * const webApi = createWebApiFromXrm(Xrm.WebApi);
 * const config = await getEventTypeFieldConfig(webApi, eventTypeId);
 * ```
 */
export function createWebApiFromXrm(xrmWebApi: {
  retrieveRecord: (
    entityType: string,
    id: string,
    options?: string
  ) => Promise<Record<string, unknown>>;
  retrieveMultipleRecords: (
    entityType: string,
    options?: string,
    maxPageSize?: number
  ) => Promise<IWebApiRetrieveMultipleResponse>;
}): IWebApiLike {
  return {
    retrieveRecord: (entityType, id, options) =>
      xrmWebApi.retrieveRecord(entityType, id, options),
    retrieveMultipleRecords: (entityType, options, maxPageSize) =>
      xrmWebApi.retrieveMultipleRecords(entityType, options, maxPageSize),
  };
}
