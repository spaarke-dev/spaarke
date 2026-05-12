/**
 * PCF Dataverse Client
 *
 * IDataverseClient implementation that wraps ComponentFramework.WebApi
 * for use in PCF controls (Model-Driven Apps, Custom Pages, Canvas Apps).
 *
 * @version 1.0.0
 */
import type { IDataverseClient, DataverseRecordRef } from './types';
/**
 * Dataverse client backed by PCF context.webAPI.
 *
 * Usage:
 * ```typescript
 * const client = new PcfDataverseClient(context.webAPI);
 * const result = await client.createRecord('sprk_document', payload);
 * ```
 */
export declare class PcfDataverseClient implements IDataverseClient {
    private readonly webApi;
    /**
     * @param webApi - ComponentFramework.WebApi instance from PCF context
     */
    constructor(webApi: ComponentFramework.WebApi);
    /**
     * Create a record via context.webAPI.createRecord().
     */
    createRecord(entityLogicalName: string, data: Record<string, unknown>): Promise<DataverseRecordRef>;
    /**
     * Update a record via context.webAPI.updateRecord().
     */
    updateRecord(entityLogicalName: string, id: string, data: Record<string, unknown>): Promise<void>;
}
//# sourceMappingURL=PcfDataverseClient.d.ts.map