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
export function createWebApiFromXrm(xrmWebApi) {
    return {
        retrieveRecord: (entityType, id, options) => xrmWebApi.retrieveRecord(entityType, id, options),
        retrieveMultipleRecords: (entityType, options, maxPageSize) => xrmWebApi.retrieveMultipleRecords(entityType, options, maxPageSize),
    };
}
//# sourceMappingURL=WebApiLike.js.map