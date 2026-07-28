/**
 * CommunicationAttachmentsService — Layer-1 (React-agnostic) logic, promoted
 * verbatim from the `CommunicationAttachments` PCF (email-communication-solution-r5
 * task 021, per spec FR-12/FR-18 two-layer split + design Lens 4).
 *
 * Reads the current communication's `sprk_communicationattachment` rows via the
 * host `context.webAPI` and projects them into `IAttachmentItem`s.
 *
 * DESIGN NOTE — binding mode (settled at task start, task 093):
 *   The task recommended dataset-binding (bind the PCF to the attachment view so
 *   the platform hands over the recordset). We chose the `context.webAPI`
 *   `retrieveMultipleRecords` path instead, filtered by the page record id.
 *   Rationale:
 *     1. This surface is a standalone build; the owner performs form placement
 *        and we cannot validate a dataset/view column configuration in a live
 *        environment. The webAPI query deterministically `$select`s the exact
 *        columns we need (`_sprk_document_value`, `sprk_attachmenttype`,
 *        `sprk_name`) regardless of which columns a maker adds to a view.
 *     2. It mirrors the SIBLING `CommunicationConnections` PCF on this same form
 *        (field-bound virtual control that reads/writes via `context.webAPI` +
 *        the page record id) — the lowest-risk, already-proven pattern here.
 *     3. Dataset binding would make the `sprk_document` lookup column's presence
 *        a maker responsibility; the webAPI path keeps that guarantee in code.
 *   The manifest therefore exposes a lightweight bound `boundField` anchor
 *   (any text column) purely for field-control placement; the communication id
 *   comes from the page context (see `resolveCommunicationId`).
 *
 * Inline-image attachments (`sprk_attachmenttype = InlineImage`) are filtered
 * OUT — they render in the email body, not as downloadable file attachments.
 */

import { AttachmentType, IAttachmentItem, IAttachmentRecord, IDocumentRecord } from './types';

const DOC_VALUE = '_sprk_document_value';
const DOC_FORMATTED = '_sprk_document_value@OData.Community.Display.V1.FormattedValue';

/** Strip Dataverse braces from a GUID. */
export function cleanGuid(id: string | null | undefined): string {
  return (id ?? '').replace(/[{}]/g, '').trim();
}

/**
 * Project a raw `sprk_communicationattachment` record into the list item shape.
 */
export function projectAttachmentRecord(record: IAttachmentRecord): IAttachmentItem {
  const rawDoc = record[DOC_VALUE] ?? null;
  return {
    attachmentId: cleanGuid(record.sprk_communicationattachmentid),
    name: (record.sprk_name ?? '').trim(),
    attachmentType: typeof record.sprk_attachmenttype === 'number' ? record.sprk_attachmenttype : null,
    documentId: rawDoc ? cleanGuid(rawDoc) : null,
    documentName: record[DOC_FORMATTED] ?? null,
    // Enriched by `enrichUploadStatus` after the document read; false until then.
    uploaded: false,
  };
}

/**
 * True when a `sprk_document` has an uploaded SPE file — the signal behind the
 * per-row upload-status indicator (A11-2/A11-3). Uses the SAME fields the BFF
 * itself treats as "stored in SPE": a non-empty `sprk_graphitemid` (the SPE
 * drive-item id), OR the Dataverse `sprk_hasfile` flag being true. No new BFF
 * call — these columns are read via `context.webAPI`.
 */
export function isDocumentUploaded(record: IDocumentRecord): boolean {
  const itemId = (record.sprk_graphitemid ?? '').trim();
  return itemId.length > 0 || record.sprk_hasfile === true;
}

/**
 * Filter out inline-image attachments (they belong to the body, not the file
 * list). Everything else — including rows with an unset type — is kept.
 */
export function filterFileAttachments(items: readonly IAttachmentItem[]): IAttachmentItem[] {
  return items.filter(i => i.attachmentType !== AttachmentType.InlineImage);
}

/**
 * True when the attachment is an archived email message (.eml / .msg /
 * message-rfc822). Such documents CANNOT render in Graph inline preview
 * (owner decision #4) and must route to download/open instead.
 */
export function isEmailMessageAttachment(item: Pick<IAttachmentItem, 'name'>): boolean {
  const name = (item.name ?? '').toLowerCase().trim();
  return name.endsWith('.eml') || name.endsWith('.msg');
}

/** Derive a short type label (uppercased extension, e.g. "PDF") for display. */
export function fileTypeLabel(name: string): string {
  const dot = name.lastIndexOf('.');
  if (dot < 0 || dot === name.length - 1) return 'File';
  return name.slice(dot + 1).toUpperCase();
}

/** Minimal WebAPI surface this service consumes (keeps the class test-friendly). */
export interface IAttachmentsWebApi {
  retrieveMultipleRecords<T = IAttachmentRecord>(
    entityLogicalName: string,
    options?: string
  ): Promise<{ entities: T[] }>;
}

export class CommunicationAttachmentsService {
  constructor(private readonly webApi: IAttachmentsWebApi) {}

  /**
   * Retrieve the file attachments for a communication (inline images excluded),
   * ordered by creation time.
   *
   * @param communicationId The `sprk_communication` record id.
   */
  async getFileAttachments(communicationId: string): Promise<IAttachmentItem[]> {
    const id = cleanGuid(communicationId);
    if (!id) return [];

    // Filter by the parent communication lookup; select only what we render +
    // need to open the file. `createdon asc` gives a stable, intuitive order.
    const query =
      `?$select=sprk_name,sprk_attachmenttype,${DOC_VALUE}` +
      `&$filter=_sprk_communication_value eq ${id}` +
      `&$orderby=createdon asc`;

    const result = await this.webApi.retrieveMultipleRecords<IAttachmentRecord>('sprk_communicationattachment', query);
    const projected = (result.entities ?? []).map(projectAttachmentRecord);
    const files = filterFileAttachments(projected);
    await this.enrichUploadStatus(files);
    return files;
  }

  /**
   * Enrich each attachment with its document's SPE upload status (A11-2/A11-3).
   * Reads `sprk_hasfile` + `sprk_graphitemid` for the referenced `sprk_document`
   * records in ONE additional `context.webAPI` read (an OR-filter over the
   * distinct document ids) — NOT a BFF call, and NOT a per-row query. Mutates
   * `items` in place. Failures degrade gracefully: rows keep `uploaded = false`
   * (rendered as "not uploaded") rather than breaking the whole list.
   */
  private async enrichUploadStatus(items: IAttachmentItem[]): Promise<void> {
    const docIds = Array.from(
      new Set(items.map(i => i.documentId).filter((d): d is string => typeof d === 'string' && d.length > 0))
    );
    if (docIds.length === 0) return;

    const filter = docIds.map(id => `sprk_documentid eq ${id}`).join(' or ');
    const query = `?$select=sprk_documentid,sprk_hasfile,sprk_graphitemid&$filter=${filter}`;

    try {
      const res = await this.webApi.retrieveMultipleRecords<IDocumentRecord>('sprk_document', query);
      const uploadedById = new Map<string, boolean>();
      for (const doc of res.entities ?? []) {
        uploadedById.set(cleanGuid(doc.sprk_documentid), isDocumentUploaded(doc));
      }
      for (const item of items) {
        if (item.documentId) {
          item.uploaded = uploadedById.get(cleanGuid(item.documentId)) ?? false;
        }
      }
    } catch (err) {
      // Non-fatal — leave uploaded=false so the list still renders.
      console.warn('[CommunicationAttachments] upload-status enrichment failed:', err);
    }
  }
}

export default CommunicationAttachmentsService;
