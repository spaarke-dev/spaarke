/**
 * Pure helpers shared by the three Submit Invoice entry points (ebilling-r1 task 118). No React, no I/O.
 *
 * Kept beside the flow, not in `InvoiceAttachmentMarking/`: that folder's exports are an agreed contract with
 * the Outlook add-in (118a) and must not grow.
 */

const PDF_NAME = /\.pdf$/i;

/** True when a file name says PDF. The Invoice mark is PDF only (model 9.1); the server checks the real type. */
export function isPdfFileName(fileName: string | null | undefined): boolean {
  return PDF_NAME.test((fileName ?? '').trim());
}

/**
 * True when at least one attachment is a PDF: the Email Review dialog shows its "Invoice" tab only then
 * (owner 2026-10-21). Goes by the file name (`name` of an attachment row, `fileName` of a markable one).
 */
export function hasPdfAttachment(
  attachments: ReadonlyArray<{ name?: string | null; fileName?: string | null }> | null | undefined
): boolean {
  return (attachments ?? []).some(a => isPdfFileName(a.name ?? a.fileName));
}
