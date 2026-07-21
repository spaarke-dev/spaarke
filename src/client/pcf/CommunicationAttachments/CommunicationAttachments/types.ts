/**
 * Local types for the CommunicationAttachments PCF.
 *
 * Narrowed to what the attachment list surface reads from
 * `sprk_communicationattachment` rows + what the preview modal needs.
 */

/**
 * `sprk_attachmenttype` option-set VALUES (source: the
 * `sprk_communicationattachment` entity schema — File default, InlineImage for
 * body-embedded images). Inline images are filtered OUT of this list surface
 * (they render in the email body, not as downloadable attachments).
 */
export enum AttachmentType {
  File = 100000000,
  InlineImage = 100000001,
}

/**
 * A single attachment row projected from a `sprk_communicationattachment`
 * record. The file identity is the `sprk_document` lookup
 * (`_sprk_document_value`); the display name is the intersection row's
 * `sprk_name` (auto-populated from the document file name).
 */
export interface IAttachmentItem {
  /** `sprk_communicationattachmentid` — stable row key. */
  attachmentId: string;
  /** `sprk_name` — the attachment/file display name (carries the extension). */
  name: string;
  /** `sprk_attachmenttype` option-set integer (File / InlineImage). */
  attachmentType: number | null;
  /** `_sprk_document_value` — the attached `sprk_document` record GUID. */
  documentId: string | null;
  /** Formatted value of the document lookup, when present. */
  documentName: string | null;
}

/**
 * Raw shape returned by `context.webAPI.retrieveMultipleRecords` for
 * `sprk_communicationattachment`. Only the fields we `$select` are typed.
 */
export interface IAttachmentRecord {
  sprk_communicationattachmentid?: string;
  sprk_name?: string | null;
  sprk_attachmenttype?: number | null;
  _sprk_document_value?: string | null;
  '_sprk_document_value@OData.Community.Display.V1.FormattedValue'?: string | null;
}
