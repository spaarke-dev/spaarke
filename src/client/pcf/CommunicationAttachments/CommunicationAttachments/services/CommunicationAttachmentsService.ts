/**
 * CommunicationAttachmentsService
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

import { AttachmentType, IAttachmentItem, IAttachmentRecord } from '../types';

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
  };
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
  retrieveMultipleRecords(
    entityLogicalName: string,
    options?: string
  ): Promise<{ entities: IAttachmentRecord[] }>;
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

    const result = await this.webApi.retrieveMultipleRecords('sprk_communicationattachment', query);
    const projected = (result.entities ?? []).map(projectAttachmentRecord);
    return filterFileAttachments(projected);
  }
}

export default CommunicationAttachmentsService;
