/**
 * emailContentCapture.ts — reads an email's body and its attachments IN THE ADD-IN, for the save
 * (spaarkeai-word-add-in-r1 task 116a).
 *
 * WHY. The save used to send only the message id and let the BFF fetch the body and attachments from the mailbox
 * through Microsoft Graph (on behalf of the user). A B2B guest's mailbox is in their HOME tenant, which Graph through
 * Spaarke's tenant cannot reach, so a guest's save stored an `.eml` with headers only and no attachment documents
 * (owner UAT 2026-10-08). Every Model 1 customer's staff are such guests. Office.js reads the open item in any tenant,
 * so the content is read here and sent with the save. The server already prefers content the client sent: it fetches
 * from Graph only when the request carries no body (`OfficeEmailEnricher.EnrichEmailFromGraphAsync`), builds the
 * `.eml` from `email.body` + `email.attachments[].contentBase64`, and creates the attachment documents from that
 * `.eml`, filtered by `email.selectedAttachmentFileNames`.
 *
 * WHAT IS READ. The HTML body and EVERY attachment, so the archived `.eml` is the complete email (owner decision
 * 2026-10-08, task 116a). Only the TICKED attachments become documents: `selectedAttachmentFileNames` names those, and
 * the server creates documents only for names in it (unchanged server behaviour).
 *
 * BUDGET ORDER. One save request has a fixed size budget. It is spent body first, then ticked attachments, then
 * unticked ones — so when it is tight, an unticked attachment is left out of the `.eml` before a ticked one is.
 *
 * WHAT HAPPENS WHEN SOMETHING CANNOT BE SENT. Handled by kind, on purpose:
 * - Deterministic (retrying cannot help): an attachment too large for what is left of the budget, or a cloud
 *   attachment (a link to a file kept in OneDrive/SharePoint, with no file content in the email). It is left out of
 *   the `.eml` (and, if ticked, gets no document), the rest of the save goes ahead, and the result names it with the
 *   reason in {@link EmailContentCapture.skipped} — the caller shows that. Never dropped silently.
 * - Transient (a retry may well work): the body or a TICKED attachment could not be READ. The whole save stops with an
 *   {@link EmailCaptureError} naming what failed, and nothing is sent. Saving without content the user asked for, and
 *   reporting success, is the failure this file exists to end. An UNTICKED attachment that cannot be read does not
 *   stop the save — the user did not ask for it as a document — but it is left out of the `.eml` with a named warning.
 *
 * Pure: everything Office-specific comes in through {@link EmailContentReader}.
 */

import type { AttachmentInfo } from '@shared/adapters/types';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';

/**
 * The largest request `POST /api/office/save` accepts: Kestrel's default `MaxRequestBodySize` (30,000,000 bytes).
 * The route sets no `RequestSizeLimit` of its own, so the default is the limit. A larger request is refused with 413
 * before the save runs.
 */
export const SAVE_REQUEST_MAX_BYTES = 30_000_000;

/**
 * How much of one save request the email's content may use: the JSON-encoded body plus every attachment's base64.
 * The 2,000,000 bytes left over carry the rest of the request (subject, sender, recipients, names, options).
 */
export const EMAIL_CONTENT_BUDGET_BYTES = 28_000_000;

/**
 * The largest single attachment one save can carry: 20 MB, whose base64 (4 bytes per 3) is 27,962,028 bytes — inside
 * {@link EMAIL_CONTENT_BUDGET_BYTES}. The attachment picker uses the same number, so a file the save cannot carry is
 * refused when it is ticked, not after Save is pressed.
 */
export const MAX_ATTACHMENT_BYTES = 20 * 1024 * 1024;

/** Reads the open email. Implemented over the host adapter in the pane and in the ribbon command. */
export interface EmailContentReader {
  /** The body as HTML. */
  getBody(): Promise<string>;
  /** One attachment's content, and the form it came back in (base64 for a file; text for an attached item). */
  getAttachmentContent(attachmentId: string): Promise<{ content: string; format?: AttachmentInfo['contentFormat'] }>;
}

/** The {@link EmailContentReader} over a host adapter (`OutlookAdapter.getBody` / `getAttachmentContent`). */
export function hostAdapterEmailReader(
  adapter: Pick<IHostAdapter, 'getBody' | 'getAttachmentContent'>
): EmailContentReader {
  return {
    getBody: async () => (await adapter.getBody('html')).content,
    getAttachmentContent: async id => {
      const attachment = await adapter.getAttachmentContent(id);
      return {
        content: attachment.content ?? '',
        ...(attachment.contentFormat ? { format: attachment.contentFormat } : {}),
      };
    },
  };
}

/** One attachment as `POST /api/office/save` takes it (`AttachmentReference` on the server). */
export interface CapturedAttachment {
  attachmentId: string;
  fileName: string;
  size: number;
  contentType: string;
  contentBase64: string;
  isInline: boolean;
}

/** Why an attachment was left out of the saved email. `unreadable` happens only to an UNTICKED attachment. */
export type SkippedAttachmentReason = 'too-large' | 'cloud-link' | 'unreadable';

/** An attachment that was NOT sent, and the sentence that tells the user why. */
export interface SkippedAttachment {
  attachmentId: string;
  name: string;
  /** Whether the user ticked it (it would have become a document). */
  ticked: boolean;
  reason: SkippedAttachmentReason;
  message: string;
}

/** What the save sends for the email's content. */
export interface EmailContentCapture {
  body: string;
  isBodyHtml: true;
  attachments: CapturedAttachment[];
  /**
   * The names of the attachments to create as documents: the TICKED attachments that were sent (a ticked attachment
   * left out is not in the `.eml`, so it cannot become a document). Unticked attachments are in {@link attachments}
   * — the `.eml` — but never here. `undefined` when the email has no attachments at all (the server reads an absent
   * list as "all", an empty one as "none" — `UploadFinalizationWorker.ProcessEmailAttachmentsAsync`).
   */
  selectedAttachmentFileNames: string[] | undefined;
  /** Attachments that were not sent, each with its reason. Empty when the whole email was sent. */
  skipped: SkippedAttachment[];
}

/** The email could not be read, so nothing was saved. `message` is written for the user. */
export class EmailCaptureError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'EmailCaptureError';
  }
}

/** Length in bytes of a string sent as UTF-8. */
function utf8Length(text: string): number {
  return new TextEncoder().encode(text).length;
}

/** The base64 of a string's UTF-8 bytes (an attached item comes back from Outlook as text). */
function utf8ToBase64(text: string): string {
  const bytes = new TextEncoder().encode(text);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i] ?? 0);
  }
  return btoa(binary);
}

/** Length of the base64 of `bytes` bytes. */
export function base64Length(bytes: number): number {
  return Math.ceil(bytes / 3) * 4;
}

/** A size as people read it: "20 MB", "512 KB". Same units as the attachment picker. */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${parseFloat((bytes / 1024).toFixed(1))} KB`;
  return `${parseFloat((bytes / (1024 * 1024)).toFixed(1))} MB`;
}

function hasExtension(name: string, extension: string): boolean {
  return name.toLowerCase().endsWith(extension);
}

/** The message a host adapter error or a thrown Error carries (a `HostAdapterError` is a plain `{ code, message }`). */
function errorText(err: unknown): string | undefined {
  if (err instanceof Error) return err.message;
  const message = (err as { message?: unknown } | null | undefined)?.message;
  return typeof message === 'string' ? message : undefined;
}

/**
 * How an attachment is stored, from what Outlook returned. An attached Outlook item comes back as TEXT (`eml` /
 * `icalendar`), so it is encoded here; its `name` has no extension (Office.js documents this), so one is added — the
 * stored document is then a real `.eml` / `.ics` file.
 *
 * An attached email is sent as `application/octet-stream`, NOT `message/rfc822`: the server builds the `.eml` with
 * MimeKit, which turns a `message/rfc822` attachment into an embedded message part, and the attachment extraction that
 * creates the documents takes plain MIME parts only — it would never become a document.
 */
function toStoredAttachment(
  attachment: AttachmentInfo,
  content: string,
  format: AttachmentInfo['contentFormat']
): { fileName: string; contentType: string; contentBase64: string } | 'cloud-link' {
  const isItem = attachment.attachmentType === 'item';
  switch (format ?? (isItem ? 'eml' : 'base64')) {
    case 'url':
      return 'cloud-link';
    case 'eml':
      return {
        fileName: hasExtension(attachment.name, '.eml') ? attachment.name : `${attachment.name}.eml`,
        contentType: 'application/octet-stream',
        contentBase64: utf8ToBase64(content),
      };
    case 'icalendar':
      return {
        fileName: hasExtension(attachment.name, '.ics') ? attachment.name : `${attachment.name}.ics`,
        contentType: 'text/calendar',
        contentBase64: utf8ToBase64(content),
      };
    default:
      // base64. An attached item can also come back as base64 (Outlook on the web) — it still needs its extension.
      return {
        fileName: isItem && !hasExtension(attachment.name, '.eml') ? `${attachment.name}.eml` : attachment.name,
        contentType: isItem ? 'application/octet-stream' : attachment.contentType || 'application/octet-stream',
        contentBase64: content,
      };
  }
}

function tooLarge(attachment: AttachmentInfo, ticked: boolean, budget: number): SkippedAttachment {
  const limit = formatSize(Math.floor((budget * 3) / 4));
  return {
    attachmentId: attachment.id,
    name: attachment.name,
    ticked,
    reason: 'too-large',
    message:
      `"${attachment.name}" (${formatSize(attachment.size)}) was not saved with the email: one save can carry about ` +
      `${limit} of email and attachments together, and this attachment did not fit in what was left.`,
  };
}

function cloudLink(attachment: AttachmentInfo, ticked: boolean): SkippedAttachment {
  return {
    attachmentId: attachment.id,
    name: attachment.name,
    ticked,
    reason: 'cloud-link',
    message:
      `"${attachment.name}" was not saved with the email: it is a link to a file stored in the cloud (for example ` +
      'OneDrive), not a file in the email. Save that file from where it is stored.',
  };
}

function unreadable(attachment: AttachmentInfo, detail: string | undefined): SkippedAttachment {
  return {
    attachmentId: attachment.id,
    name: attachment.name,
    ticked: false,
    reason: 'unreadable',
    message: `"${attachment.name}" was not saved with the email: Outlook could not read it.${detail ? ` (${detail})` : ''}`,
  };
}

/**
 * Read the body and every attachment, and decide what one save request carries.
 *
 * Budget order: the body, then the TICKED attachments, then the unticked ones (each group in the email's own order).
 * Each attachment must fit in what is left of `budget`; the size Outlook reports is checked BEFORE reading, so a file
 * that cannot fit is never read, and the real base64 length is checked again after. The attachments sent keep the
 * email's own order, so the `.eml` lists them as the email did.
 *
 * @throws {EmailCaptureError} the body or a TICKED attachment could not be read, or the body alone does not fit.
 */
export async function captureEmailContent(
  reader: EmailContentReader,
  attachments: readonly AttachmentInfo[],
  selectedIds: ReadonlySet<string>,
  budget: number = EMAIL_CONTENT_BUDGET_BYTES
): Promise<EmailContentCapture> {
  let body: string;
  try {
    body = await reader.getBody();
  } catch (err) {
    const detail = errorText(err);
    throw new EmailCaptureError(
      `Couldn't read this email's text, so nothing was saved. Try again.${detail ? ` (${detail})` : ''}`
    );
  }

  // The body travels as a JSON string, so its escaped length is what counts against the request limit.
  let used = utf8Length(JSON.stringify(body));
  if (used > budget) {
    throw new EmailCaptureError(
      `This email's text is too large to save (${formatSize(used)}; one save can carry about ` +
        `${formatSize(budget)}). Nothing was saved.`
    );
  }

  const indexed = attachments.map((attachment, index) => ({
    attachment,
    index,
    ticked: selectedIds.has(attachment.id),
  }));
  const byPriority = [...indexed.filter(a => a.ticked), ...indexed.filter(a => !a.ticked)];

  const sent: Array<{ attachment: CapturedAttachment; index: number; ticked: boolean }> = [];
  const skipped: SkippedAttachment[] = [];

  for (const { attachment, index, ticked } of byPriority) {
    if (attachment.attachmentType === 'cloud') {
      skipped.push(cloudLink(attachment, ticked));
      continue;
    }
    if (used + base64Length(attachment.size) > budget) {
      skipped.push(tooLarge(attachment, ticked, budget));
      continue;
    }

    let read: { content: string; format?: AttachmentInfo['contentFormat'] };
    try {
      read = await reader.getAttachmentContent(attachment.id);
    } catch (err) {
      const detail = errorText(err);
      if (!ticked) {
        skipped.push(unreadable(attachment, detail));
        continue;
      }
      throw new EmailCaptureError(
        `Couldn't read the attachment "${attachment.name}", so nothing was saved. Try again, or untick it to save ` +
          `the email without it as a document.${detail ? ` (${detail})` : ''}`
      );
    }

    const stored = toStoredAttachment(attachment, read.content, read.format);
    if (stored === 'cloud-link') {
      skipped.push(cloudLink(attachment, ticked));
      continue;
    }
    if (used + stored.contentBase64.length > budget) {
      skipped.push(tooLarge(attachment, ticked, budget));
      continue;
    }

    used += stored.contentBase64.length;
    sent.push({
      attachment: {
        attachmentId: attachment.id,
        fileName: stored.fileName,
        size: attachment.size,
        contentType: stored.contentType,
        contentBase64: stored.contentBase64,
        isInline: attachment.isInline,
      },
      index,
      ticked,
    });
  }

  sent.sort((a, b) => a.index - b.index);
  return {
    body,
    isBodyHtml: true,
    attachments: sent.map(s => s.attachment),
    selectedAttachmentFileNames:
      attachments.length > 0 ? sent.filter(s => s.ticked).map(s => s.attachment.fileName) : undefined,
    skipped,
  };
}
