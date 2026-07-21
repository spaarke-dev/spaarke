/**
 * attachmentsSource.ts — enumerate a communication's attachment DOCUMENTS so the
 * composer can offer to carry them on Reply / Reply All / Forward (task 104, D1/D2).
 *
 * Reuses the SAME `sprk_communicationattachment` → `sprk_document` data model the
 * CommunicationAttachments PCF reads (see
 * `CommunicationAttachments/services/CommunicationAttachmentsService.ts`): same
 * entity, same `$select`/`$filter`, same inline-image exclusion. No new BFF
 * endpoint and no new send-contract field — the projected `documentId`s flow into
 * the composer's existing `SendCommunicationRequest.AttachmentDocumentIds` path,
 * and the `linkUrl` feeds the composer's body-link path.
 *
 * The list read runs in the user's Dataverse session via host-context
 * `context.webAPI` (single-entity, in-session, no OBO/AI/cross-system → Xrm.WebApi,
 * per docs/standards/DATA-ACCESS-DECISION-CRITERIA.md).
 */

import type { IAttachmentItem } from '@spaarke/ui-components';

/** `sprk_attachmenttype` option-set value for a body-embedded image (excluded from the file list). */
const ATTACHMENT_TYPE_INLINE_IMAGE = 100000001;

const DOC_VALUE = '_sprk_document_value';

/** Minimal WebAPI surface consumed here (keeps the projection unit-testable). */
export interface IActionsWebApi {
  retrieveMultipleRecords(
    entityLogicalName: string,
    options?: string
  ): Promise<{ entities: ISourceAttachmentRecord[] }>;
}

/** Raw `sprk_communicationattachment` row — only the `$select`ed fields are typed. */
export interface ISourceAttachmentRecord {
  sprk_communicationattachmentid?: string;
  sprk_name?: string | null;
  sprk_attachmenttype?: number | null;
  _sprk_document_value?: string | null;
}

/** Strip Dataverse braces from a GUID. */
function cleanGuid(id: string | null | undefined): string {
  return (id ?? '').replace(/[{}]/g, '').trim();
}

/**
 * Build a Dataverse record deep-link to the backing `sprk_document`. Synchronous
 * and always available (no extra network call). NOTE: this opens the Document
 * record in the app — useful for internal recipients; a future enhancement can
 * swap in an SPE sharing link via `/api/documents/{id}/open-links` for external
 * recipients (best-effort, out of scope for task 104).
 */
export function buildDocumentLinkUrl(dataverseUrl: string, documentId: string): string {
  const base = (dataverseUrl || '').replace(/\/+$/, '');
  return `${base}/main.aspx?pagetype=entityrecord&etn=sprk_document&id=${encodeURIComponent(documentId)}`;
}

/**
 * Project a raw attachment row into the composer's `IAttachmentItem`. Rows without
 * a resolvable `sprk_document` are dropped (best-effort — they can be neither
 * attached nor linked). `sizeBytes` is 0 (the intersection row carries no size;
 * the composer's size cap tolerates unknown sizes).
 */
export function projectSourceAttachment(
  record: ISourceAttachmentRecord,
  dataverseUrl: string
): IAttachmentItem | null {
  const documentId = cleanGuid(record[DOC_VALUE]);
  if (!documentId) return null;
  return {
    id: `source:${cleanGuid(record.sprk_communicationattachmentid) || documentId}`,
    source: 'related',
    fileName: (record.sprk_name ?? '').trim() || 'Attachment',
    sizeBytes: 0,
    documentId,
    linkUrl: buildDocumentLinkUrl(dataverseUrl, documentId),
  };
}

/** Drop inline-image attachments — they belong to the body, not the file list. */
export function filterFileAttachments(records: readonly ISourceAttachmentRecord[]): ISourceAttachmentRecord[] {
  return records.filter(r => r.sprk_attachmenttype !== ATTACHMENT_TYPE_INLINE_IMAGE);
}

/**
 * Fetch the file attachments for a communication as composer `IAttachmentItem`s.
 * Best-effort: any failure resolves to `[]` (reply/forward still works, just
 * without the carry-attachment affordance).
 */
export async function fetchSourceAttachments(
  webApi: IActionsWebApi,
  communicationId: string,
  dataverseUrl: string
): Promise<IAttachmentItem[]> {
  const id = cleanGuid(communicationId);
  if (!id) return [];
  try {
    const query =
      `?$select=sprk_name,sprk_attachmenttype,${DOC_VALUE}` +
      `&$filter=_sprk_communication_value eq ${id}` +
      `&$orderby=createdon asc`;
    const result = await webApi.retrieveMultipleRecords('sprk_communicationattachment', query);
    return filterFileAttachments(result.entities ?? [])
      .map(r => projectSourceAttachment(r, dataverseUrl))
      .filter((a): a is IAttachmentItem => a !== null);
  } catch (err) {
    console.warn('[CommunicationActions] source attachment enumeration failed:', err);
    return [];
  }
}
