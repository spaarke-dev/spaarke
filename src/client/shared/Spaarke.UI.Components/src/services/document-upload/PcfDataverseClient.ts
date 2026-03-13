/**
 * PCF Dataverse Client
 *
 * IDataverseClient implementation that wraps ComponentFramework.WebApi
 * for use in PCF controls (Model-Driven Apps, Custom Pages, Canvas Apps).
 *
 * @version 1.0.0
 */

import type { IDataverseClient, DataverseRecordRef } from "./types";

/**
 * Dataverse client backed by PCF context.webAPI.
 *
 * Usage:
 * ```typescript
 * const client = new PcfDataverseClient(context.webAPI);
 * const result = await client.createRecord('sprk_document', payload);
 * ```
 */
export class PcfDataverseClient implements IDataverseClient {
  /**
   * @param webApi - ComponentFramework.WebApi instance from PCF context
   */
  constructor(private readonly webApi: ComponentFramework.WebApi) {}

  /**
   * Create a record via context.webAPI.createRecord().
   */
  async createRecord(
    entityLogicalName: string,
    data: Record<string, unknown>,
  ): Promise<DataverseRecordRef> {
    const result = await this.webApi.createRecord(entityLogicalName, data);
    return { id: result.id };
  }

  /**
   * Update a record via context.webAPI.updateRecord().
   */
  async updateRecord(
    entityLogicalName: string,
    id: string,
    data: Record<string, unknown>,
  ): Promise<void> {
    await this.webApi.updateRecord(entityLogicalName, id, data);
  }
}
