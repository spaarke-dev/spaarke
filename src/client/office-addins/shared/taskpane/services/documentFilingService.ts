import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';
import { cleanGuid } from '@spaarke/ui-components/guid';
import { describeFetchFailure } from '../utils/errorMessages';
import type { EntityType } from '../hooks/useEntitySearch';
import type { ResolvedRelatedRecord } from './documentIdentityService';

/**
 * documentFilingService.ts — `PUT /api/v1/documents/{id}` as the pane's "File to record" action
 * (spaarkeai-word-add-in-r1 task 111, owner UAT round 11 item 4).
 *
 * Files a document that is KNOWN to have no record (a URL-resolved identity with `relatedRecord === null`) under the
 * record the user picked. The route is existing and authorized server-side: the caller needs write on the document and
 * AppendTo on the new parent (`AuthorizeRefileTargetsAsync`). It SETS the one lookup the body names and never clears
 * another slot — so the caller MUST NOT offer this for a document that may already have a record; the pane decides that
 * (`documentIdentity.relatedRecordKnown`), this service only sends the request.
 *
 * Exactly ONE lookup is sent — the one matching the picked entity type. Account / Contact are not filing targets and are
 * refused here without a request.
 */

/** The picker entity types that map to a document lookup on `UpdateDocumentRequest`. */
type FilingLookupField = 'matterLookup' | 'projectLookup' | 'invoiceLookup';

const LOOKUP_BY_ENTITY_TYPE: Partial<Record<EntityType, { field: FilingLookupField; logicalName: string }>> = {
  Matter: { field: 'matterLookup', logicalName: 'sprk_matter' },
  Project: { field: 'projectLookup', logicalName: 'sprk_project' },
  Invoice: { field: 'invoiceLookup', logicalName: 'sprk_invoice' },
};

/** What a "File to record" attempt came to. Never throws — callers switch on `.ok`. */
export type FileDocumentOutcome =
  | { ok: true; record: ResolvedRelatedRecord }
  | { ok: false; reason: 'forbidden' | 'unsupported' | 'error'; message: string };

export interface FileDocumentRequest {
  apiBaseUrl: string;
  getAccessToken: () => Promise<string>;
  documentId: string;
  /** The picked record's type, id and display name (the picker's `EntitySearchResult`). */
  entityType: EntityType;
  recordId: string;
  recordName: string;
  /** The record's number, when the picker knows it (`displayInfo`). */
  recordNumber?: string | null;
}

/** The request body for a filing — exactly one lookup, or `null` for an entity type that is not a filing target. */
export function buildFilingBody(entityType: EntityType, recordId: string): Record<string, string> | null {
  const target = LOOKUP_BY_ENTITY_TYPE[entityType];
  const id = cleanGuid(recordId);
  return target && id ? { [target.field]: id } : null;
}

export async function fileDocumentToRecord(request: FileDocumentRequest): Promise<FileDocumentOutcome> {
  const { apiBaseUrl, getAccessToken, documentId, entityType, recordId, recordName, recordNumber } = request;
  const target = LOOKUP_BY_ENTITY_TYPE[entityType];
  const body = buildFilingBody(entityType, recordId);
  const docId = cleanGuid(documentId);
  if (!target || !body || !docId) {
    return { ok: false, reason: 'unsupported', message: "This kind of record can't be used to file a document." };
  }
  if (!apiBaseUrl) {
    return { ok: false, reason: 'error', message: "The pane isn't fully configured. Reload and try again." };
  }

  let res: Response;
  try {
    const token = await getAccessToken();
    res = await authenticatedJsonFetch(
      `${apiBaseUrl}/api/v1/documents/${encodeURIComponent(docId)}`,
      { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) },
      token,
      { getRetryToken: getAccessToken }
    );
  } catch {
    return {
      ok: false,
      reason: 'error',
      message: "Couldn't reach Spaarke to file the document. Check your connection and try again.",
    };
  }

  if (!res.ok) {
    if (res.status === 403) {
      return {
        ok: false,
        reason: 'forbidden',
        message: "You don't have permission to file this document to that record. Pick a different record.",
      };
    }
    return { ok: false, reason: 'error', message: (await describeFetchFailure(res)).message };
  }

  return {
    ok: true,
    record: {
      entityType: target.logicalName,
      id: cleanGuid(recordId),
      name: recordName,
      displayName: recordName,
      number: recordNumber ?? null,
    },
  };
}
