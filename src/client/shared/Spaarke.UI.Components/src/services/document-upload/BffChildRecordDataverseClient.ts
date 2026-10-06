/**
 * An {@link IDataverseClient} whose CHILD-record creates go through the BFF (unified-access-control-r2 task 147 r1; owner
 * round 28 item 1, "E1 = A1, the existing G5 pattern").
 *
 * The upload pipeline creates one `sprk_document` row per uploaded file. Through `Xrm.WebApi` (or the OData client) that
 * row is owned by the user, in the user's own business unit — a document of a secure project would be readable by
 * everyone in that unit. Wrapping the pipeline's client with this decorator sends the same payload to
 * `POST /api/v1/child-records/sprk_document`: the BFF checks the caller's rights and creates the row owned by the team the
 * ownership rule names, recording the caller as its creator person. An unfiled document still saves (owned by the
 * caller's business-unit team, owner round 5). Every other call passes through unchanged.
 */

import type { DataverseRecordRef, IDataverseClient } from './types';
import { createChildRecordViaBff, isBffChildCreateTable } from '../../utils/adapters/bffChildWriteAdapter';
import type { AuthenticatedFetch } from '../../utils/adapters/bffDataServiceAdapter';

/** Decorates `inner` so its child-record creates go through the BFF; `bffBaseUrl` may be `''` (relative `/api`). */
export function withBffChildCreates(
  inner: IDataverseClient,
  authenticatedFetch: AuthenticatedFetch,
  bffBaseUrl: string
): IDataverseClient {
  return {
    createRecord: async (entityLogicalName: string, data: Record<string, unknown>): Promise<DataverseRecordRef> =>
      isBffChildCreateTable(entityLogicalName)
        ? { id: await createChildRecordViaBff(authenticatedFetch, bffBaseUrl, entityLogicalName, data) }
        : inner.createRecord(entityLogicalName, data),
    updateRecord: (entityLogicalName: string, id: string, data: Record<string, unknown>) =>
      inner.updateRecord(entityLogicalName, id, data),
  };
}
