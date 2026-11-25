/**
 * An {@link IDataverseClient} whose CHILD-record creates go through the BFF (unified-access-control-r2 task 147 r1; owner
 * round 28 item 1, 'E1 = A1, the existing G5 pattern').
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
  createRecordViaBffWithWarnings,
  isBffChildCreateTable,
  type IBffMappingSource,
} from '../../utils/adapters/bffChildWriteAdapter';
import type { AuthenticatedFetch } from '../../utils/adapters/bffDataServiceAdapter';

/** The BFF delete route of a document (`DataverseDocumentsEndpoints`: `DELETE /api/v1/documents/{id}`). */
function documentDeleteUrl(bffBaseUrl: string, id: string): string {
  const cleanId = id.replace(/[{}]/g, '').toLowerCase();
  return `${bffBaseUrl.replace(/\/+$/, '')}/api/v1/documents/${encodeURIComponent(cleanId)}`;
}

/**
 * Decorates `inner` so its child-record creates go through the BFF; `bffBaseUrl` may be `''` (relative `/api`).
 * `mappingSource` is the record a created row copies its mapped fields from (the parent the documents are filed under);
 * omit it for an unfiled upload, which maps nothing.
 *
 * A create returns the server's non-fatal `warnings` (a mapping source it could not read, a mapped value it did not
 * write) beside the id, only when there are some. `deleteRecord` removes a `sprk_document` through the BFF
 * (`DELETE /api/v1/documents/{id}`) so the cleanup of a row whose file was refused runs in production; any other table is
 * the inner client's.
 */
export function withBffChildCreates(
  inner: IDataverseClient,
  authenticatedFetch: AuthenticatedFetch,
  bffBaseUrl: string,
  mappingSource?: IBffMappingSource
): IDataverseClient {
  return {
    createRecord: async (entityLogicalName: string, data: Record<string, unknown>): Promise<DataverseRecordRef> => {
      if (!isBffChildCreateTable(entityLogicalName)) return inner.createRecord(entityLogicalName, data);
      const { id, warnings } = await createRecordViaBffWithWarnings(
        authenticatedFetch,
        bffBaseUrl,
        entityLogicalName,
        data,
        mappingSource ? { mappingSource } : undefined
      );
      return warnings.length > 0 ? { id, warnings } : { id };
    },
    updateRecord: (entityLogicalName: string, id: string, data: Record<string, unknown>) =>
      inner.updateRecord(entityLogicalName, id, data),
    deleteRecord: async (entityLogicalName: string, id: string): Promise<void> => {
      if (entityLogicalName.toLowerCase() !== 'sprk_document') {
        if (typeof inner.deleteRecord === 'function') await inner.deleteRecord(entityLogicalName, id);
        return;
      }
      const response = await authenticatedFetch(documentDeleteUrl(bffBaseUrl, id), { method: 'DELETE' });
      if (!response.ok) {
        throw new Error(`The document could not be removed (HTTP ${response.status}).`);
      }
    },
  };
}
