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
 *
 * Field mapping is the server's (field-mapping-server-write-path-r1, FR-02, FR-16): the payload carries only what the user
 * entered and the filing parent's bind. The route infers a mapping source only for the tables in its
 * `InferMappingSourceTables` (a matter, a project), and `sprk_document` is not one, so a create that names none maps
 * nothing. The pipeline therefore NAMES the parent the documents are filed under as the `mappingSource`
 * (`?mappingSourceEntity=&mappingSourceId=`), as the Event, To Do, Report Card, Work Assignment and Analysis wizards do.
 * It adds nothing to the body.
 */

import type { DataverseRecordRef, IDataverseClient } from './types';
import {
  createChildRecordViaBff,
  isBffChildCreateTable,
  type IBffMappingSource,
} from '../../utils/adapters/bffChildWriteAdapter';
import type { AuthenticatedFetch } from '../../utils/adapters/bffDataServiceAdapter';

/**
 * Decorates `inner` so its child-record creates go through the BFF; `bffBaseUrl` may be `''` (relative `/api`).
 * `mappingSource` is the record a created row copies its mapped fields from (the parent the documents are filed under);
 * omit it for an unfiled upload, which maps nothing.
 */
export function withBffChildCreates(
  inner: IDataverseClient,
  authenticatedFetch: AuthenticatedFetch,
  bffBaseUrl: string,
  mappingSource?: IBffMappingSource
): IDataverseClient {
  return {
    createRecord: async (entityLogicalName: string, data: Record<string, unknown>): Promise<DataverseRecordRef> =>
      isBffChildCreateTable(entityLogicalName)
        ? {
            id: await createChildRecordViaBff(
              authenticatedFetch,
              bffBaseUrl,
              entityLogicalName,
              data,
              mappingSource ? { mappingSource } : undefined
            ),
          }
        : inner.createRecord(entityLogicalName, data),
    updateRecord: (entityLogicalName: string, id: string, data: Record<string, unknown>) =>
      inner.updateRecord(entityLogicalName, id, data),
  };
}
