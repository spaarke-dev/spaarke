/**
 * WebApiAdapter - Adapts PCF WebApi to IWebApiLike interface
 *
 * This adapter allows the EventFormController to use the shared EventTypeService
 * by wrapping the PCF ComponentFramework.WebApi in the context-agnostic IWebApiLike interface.
 *
 * @see ADR-012 - Shared Component Library (no PCF-specific dependencies in shared code)
 * @see EventTypeService - Consumer of IWebApiLike interface
 */

import { IWebApiLike, IWebApiRetrieveMultipleResponse } from "@spaarke/ui-components";

/**
 * Creates an IWebApiLike adapter from PCF ComponentFramework.WebApi
 *
 * This function wraps the PCF-specific WebApi interface to match the
 * context-agnostic IWebApiLike interface used by shared services.
 *
 * @param webApi - PCF ComponentFramework.WebApi instance
 * @returns IWebApiLike compatible interface
 *
 * @example
 * ```typescript
 * // In EventFormController or EventFormControllerApp
 * const webApiLike = createWebApiAdapter(context.webAPI);
 * const result = await getEventTypeFieldConfig(webApiLike, eventTypeId);
 * ```
 */
export function createWebApiAdapter(
    webApi: ComponentFramework.WebApi
): IWebApiLike {
    return {
        retrieveRecord: async (
            entityType: string,
            id: string,
            options?: string
        ): Promise<Record<string, unknown>> => {
            const result = await webApi.retrieveRecord(entityType, id, options);
            // PCF WebApi returns ComponentFramework.WebApi.Entity which is compatible with Record<string, unknown>
            return result as unknown as Record<string, unknown>;
        },

        retrieveMultipleRecords: async (
            entityType: string,
            options?: string,
            maxPageSize?: number
        ): Promise<IWebApiRetrieveMultipleResponse> => {
            const result = await webApi.retrieveMultipleRecords(entityType, options, maxPageSize);
            // PCF WebApi returns RetrieveMultipleResponse which has entities array and nextLink
            return {
                entities: result.entities as Record<string, unknown>[],
                nextLink: result.nextLink
            };
        }
    };
}
