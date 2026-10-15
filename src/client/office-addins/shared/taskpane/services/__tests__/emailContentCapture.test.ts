/**
 * Task 116a (spaarkeai-word-add-in-r1): the email save reads the body and the attachments in the add-in, because the
 * server's Graph fetch cannot reach a B2B guest's home-tenant mailbox (owner UAT 2026-10-08: a guest's save stored a
 * header-only .eml and no attachment documents).
 *
 * Pins `captureEmailContent`, the pure decision of what one save request carries (owner decision 2026-10-08):
 * - the `.eml` carries EVERY readable attachment; only the TICKED ones are named to become documents;
 * - the size budget is spent body first, then ticked attachments, then unticked ones;
 * - an attachment one save cannot carry (too large, or a cloud link) is left out WITH a per-attachment reason, and
 *   the rest are kept — never dropped silently;
 * - a body or TICKED-attachment read failure stops the save (nothing is sent) with a message naming what failed; an
 *   UNTICKED attachment that cannot be read is left out of the `.eml` with a named warning instead;
 * - an attached Outlook item, which Office.js returns as TEXT, is encoded and stored under a real file name.
 */
import {
  captureEmailContent,
  base64Length,
  EmailCaptureError,
  EMAIL_CONTENT_BUDGET_BYTES,
  MAX_ATTACHMENT_BYTES,
  SAVE_REQUEST_MAX_BYTES,
  type EmailContentReader,
} from '../emailContentCapture';
import type { AttachmentInfo } from '@shared/adapters/types';

function att(id: string, overrides: Partial<AttachmentInfo> = {}): AttachmentInfo {
  return {
    id,
    name: `${id}.pdf`,
    contentType: 'application/pdf',
    size: 3,
    isInline: false,
    attachmentType: 'file',
    ...overrides,
  };
}

/** A reader over a fixed body and per-id contents; records every read. An id with no content fails to read. */
function reader(
  body: string,
  contents: Record<string, { content: string; format?: AttachmentInfo['contentFormat'] }> = {}
): EmailContentReader & { getBody: jest.Mock; getAttachmentContent: jest.Mock } {
  return {
    getBody: jest.fn().mockResolvedValue(body),
    getAttachmentContent: jest.fn(async (id: string) => {
      const found = contents[id];
      if (!found) throw { code: 'CONTENT_RETRIEVAL_FAILED', message: `no content for ${id}` };
      return found;
    }),
  };
}

const B64_ABC = 'YWJj'; // "abc"
const ABC = { content: B64_ABC, format: 'base64' as const };

describe('captureEmailContent — the .eml is the complete email; only ticked attachments become documents', () => {
  it('reads the body and EVERY attachment; sends them in the email order; names only the ticked ones', async () => {
    const r = reader('<p>Hello</p>', { a: ABC, b: ABC, c: ABC });
    const result = await captureEmailContent(r, [att('a'), att('b'), att('c')], new Set(['c', 'a']));

    expect(r.getBody).toHaveBeenCalledTimes(1);
    // Budget order: ticked first (a, c), then unticked (b).
    expect(r.getAttachmentContent.mock.calls.map(c => c[0])).toEqual(['a', 'c', 'b']);

    expect(result.body).toBe('<p>Hello</p>');
    expect(result.isBodyHtml).toBe(true);
    expect(result.attachments.map(a => a.attachmentId)).toEqual(['a', 'b', 'c']);
    expect(result.attachments[0]).toEqual({
      attachmentId: 'a',
      fileName: 'a.pdf',
      size: 3,
      contentType: 'application/pdf',
      contentBase64: B64_ABC,
      isInline: false,
    });
    expect(result.selectedAttachmentFileNames).toEqual(['a.pdf', 'c.pdf']);
    expect(result.skipped).toEqual([]);
  });

  it('an UNTICKED attachment is in the request content but not in selectedAttachmentFileNames', async () => {
    const result = await captureEmailContent(reader('b', { u: ABC, t: ABC }), [att('u'), att('t')], new Set(['t']));

    expect(result.attachments.map(a => a.fileName)).toContain('u.pdf');
    expect(result.attachments.find(a => a.fileName === 'u.pdf')?.contentBase64).toBe(B64_ABC);
    expect(result.selectedAttachmentFileNames).toEqual(['t.pdf']);
    expect(result.selectedAttachmentFileNames).not.toContain('u.pdf');
  });

  it('nothing ticked: every attachment is still in the .eml, and an EMPTY name list is sent ("create no documents")', async () => {
    const r = reader('body', { a: ABC });
    const result = await captureEmailContent(r, [att('a')], new Set());

    expect(r.getAttachmentContent).toHaveBeenCalledWith('a');
    expect(result.attachments.map(a => a.fileName)).toEqual(['a.pdf']);
    expect(result.selectedAttachmentFileNames).toEqual([]);
  });

  it('an email with no attachments sends NO name list (undefined), as before', async () => {
    const result = await captureEmailContent(reader('body'), [], new Set());
    expect(result.selectedAttachmentFileNames).toBeUndefined();
  });

  it('an inline image is carried, flagged inline', async () => {
    const result = await captureEmailContent(
      reader('b', { img: ABC }),
      [att('img', { name: 'image001.png', contentType: 'image/png', isInline: true })],
      new Set()
    );
    expect(result.attachments[0]).toMatchObject({ fileName: 'image001.png', isInline: true, contentType: 'image/png' });
  });
});

describe('captureEmailContent — the budget is spent body first, then ticked, then unticked', () => {
  it('a tight budget drops an UNTICKED attachment from the .eml before a ticked one, even when the unticked comes first', async () => {
    // Body "b" = 3 bytes as JSON; each 3-byte file = 4 base64 bytes. Budget 8 fits the body and ONE attachment.
    const r = reader('b', { u: ABC, t: ABC });
    const result = await captureEmailContent(r, [att('u'), att('t')], new Set(['t']), 8);

    expect(result.attachments.map(a => a.attachmentId)).toEqual(['t']);
    expect(result.selectedAttachmentFileNames).toEqual(['t.pdf']);
    expect(result.skipped).toEqual([
      expect.objectContaining({ attachmentId: 'u', ticked: false, reason: 'too-large' }),
    ]);
    expect(r.getAttachmentContent).not.toHaveBeenCalledWith('u');
  });

  it('a TICKED attachment that cannot fit gets its own message, and the rest (ticked and unticked) still save', async () => {
    const r = reader('b', { small: ABC, other: ABC });
    const result = await captureEmailContent(
      r,
      [att('big', { name: 'scan.pdf', size: 900 }), att('small'), att('other')],
      new Set(['big', 'small']),
      1000
    );

    expect(r.getAttachmentContent).not.toHaveBeenCalledWith('big');
    expect(result.attachments.map(a => a.attachmentId)).toEqual(['small', 'other']);
    expect(result.selectedAttachmentFileNames).toEqual(['small.pdf']);
    expect(result.skipped).toEqual([
      expect.objectContaining({ attachmentId: 'big', name: 'scan.pdf', ticked: true, reason: 'too-large' }),
    ]);
    expect(result.skipped[0]!.message).toMatch(
      /^"scan\.pdf" \(900 B\) was not saved with the email: one save can carry about .+ did not fit in what was left\.$/
    );
  });

  it('the body counts against the budget: an attachment that fits alone but not after the body is reported', async () => {
    const body = 'x'.repeat(90);
    // JSON-encoded body = 92 bytes; base64 of a 9-byte file = 12 bytes; 92 + 12 > 100.
    const r = reader(body, { a: { content: 'YWJjZGVmZ2hp', format: 'base64' } });
    const result = await captureEmailContent(r, [att('a', { size: 9 })], new Set(['a']), 100);

    expect(result.attachments).toEqual([]);
    expect(result.selectedAttachmentFileNames).toEqual([]);
    expect(result.skipped[0]).toMatchObject({ attachmentId: 'a', reason: 'too-large' });
  });

  it('the REAL base64 length is checked after reading (a reported size can understate it)', async () => {
    const r = reader('b', { a: { content: 'A'.repeat(200), format: 'base64' } });
    const result = await captureEmailContent(r, [att('a', { size: 3 })], new Set(['a']), 100);

    expect(r.getAttachmentContent).toHaveBeenCalledWith('a');
    expect(result.attachments).toEqual([]);
    expect(result.skipped[0]).toMatchObject({ attachmentId: 'a', reason: 'too-large' });
  });
});

describe('captureEmailContent — cloud (link) attachments', () => {
  it('a cloud attachment is never read and is reported, ticked or not, while the rest are saved', async () => {
    const r = reader('b', { a: ABC });
    const result = await captureEmailContent(
      r,
      [
        att('cloud', { name: 'Plan.docx', attachmentType: 'cloud' }),
        att('link2', { name: 'Deck.pptx', attachmentType: 'cloud' }),
        att('a'),
      ],
      new Set(['cloud', 'a'])
    );

    expect(r.getAttachmentContent).not.toHaveBeenCalledWith('cloud');
    expect(r.getAttachmentContent).not.toHaveBeenCalledWith('link2');
    expect(result.attachments.map(a => a.attachmentId)).toEqual(['a']);
    expect(result.selectedAttachmentFileNames).toEqual(['a.pdf']);
    expect(result.skipped).toEqual([
      expect.objectContaining({ attachmentId: 'cloud', ticked: true, reason: 'cloud-link' }),
      expect.objectContaining({ attachmentId: 'link2', ticked: false, reason: 'cloud-link' }),
    ]);
    expect(result.skipped[0]!.message).toMatch(
      /^"Plan\.docx" was not saved with the email: it is a link to a file stored in the cloud/
    );
  });

  it('content that comes back as a URL (a cloud attachment the metadata did not flag) is reported, not stored', async () => {
    const r = reader('b', { x: { content: 'https://contoso-my.sharepoint.com/x', format: 'url' } });
    const untyped = att('x');
    delete untyped.attachmentType;
    const result = await captureEmailContent(r, [untyped], new Set(['x']));

    expect(result.attachments).toEqual([]);
    expect(result.skipped[0]).toMatchObject({ attachmentId: 'x', reason: 'cloud-link' });
  });
});

describe('captureEmailContent — read failures', () => {
  it('the body cannot be read: rejects with a user message and reads no attachment', async () => {
    const r = reader('b', { a: ABC });
    r.getBody.mockRejectedValue({ code: 'CONTENT_RETRIEVAL_FAILED', message: 'Failed to get body' });

    const promise = captureEmailContent(r, [att('a')], new Set(['a']));
    await expect(promise).rejects.toBeInstanceOf(EmailCaptureError);
    await expect(promise).rejects.toThrow(
      "Couldn't read this email's text, so nothing was saved. Try again. (Failed to get body)"
    );
    expect(r.getAttachmentContent).not.toHaveBeenCalled();
  });

  it('a TICKED attachment cannot be read (a HostAdapterError plain object): the save stops, naming that attachment', async () => {
    const r = reader('b');
    r.getAttachmentContent.mockRejectedValue({ code: 'CONTENT_RETRIEVAL_FAILED', message: 'The item was not found.' });

    await expect(captureEmailContent(r, [att('a', { name: 'Contract.pdf' })], new Set(['a']))).rejects.toThrow(
      'Couldn\'t read the attachment "Contract.pdf", so nothing was saved. Try again, or untick it to save the ' +
        'email without it as a document. (The item was not found.)'
    );
  });

  it('an UNTICKED attachment that cannot be read is left out of the .eml with a named warning; the save goes on', async () => {
    const r = reader('b', { t: ABC }); // no content for 'u' → its read fails
    const result = await captureEmailContent(r, [att('u', { name: 'Old.pdf' }), att('t')], new Set(['t']));

    expect(result.attachments.map(a => a.attachmentId)).toEqual(['t']);
    expect(result.selectedAttachmentFileNames).toEqual(['t.pdf']);
    expect(result.skipped).toEqual([
      {
        attachmentId: 'u',
        name: 'Old.pdf',
        ticked: false,
        reason: 'unreadable',
        message: '"Old.pdf" was not saved with the email: Outlook could not read it. (no content for u)',
      },
    ]);
  });

  it('a body larger than one save can carry is refused, not truncated', async () => {
    await expect(captureEmailContent(reader('x'.repeat(200)), [], new Set(), 100)).rejects.toThrow(
      /This email's text is too large to save/
    );
  });
});

describe('captureEmailContent — attached Outlook items (forwarded emails, meetings)', () => {
  it('an attached email returned as EML text is UTF-8 encoded, named .eml, and sent as a plain file part', async () => {
    const eml = 'Subject: Fwd héllo\r\n\r\nBody';
    const r = reader('b', { item: { content: eml, format: 'eml' } });
    const result = await captureEmailContent(
      r,
      [att('item', { name: 'Fwd héllo', contentType: 'message/rfc822', attachmentType: 'item' })],
      new Set(['item'])
    );

    const sent = result.attachments[0]!;
    expect(sent.fileName).toBe('Fwd héllo.eml');
    // Not message/rfc822: MimeKit would embed that as a message part, which the server's document extraction skips.
    expect(sent.contentType).toBe('application/octet-stream');
    expect(Buffer.from(sent.contentBase64, 'base64').toString('utf8')).toBe(eml);
    expect(result.selectedAttachmentFileNames).toEqual(['Fwd héllo.eml']);
  });

  it('an attached meeting returned as iCalendar text is named .ics, text/calendar', async () => {
    const r = reader('b', { m: { content: 'BEGIN:VCALENDAR\r\nEND:VCALENDAR', format: 'icalendar' } });
    const result = await captureEmailContent(
      r,
      [att('m', { name: 'Kickoff', attachmentType: 'item' })],
      new Set(['m'])
    );
    expect(result.attachments[0]).toMatchObject({ fileName: 'Kickoff.ics', contentType: 'text/calendar' });
    expect(Buffer.from(result.attachments[0]!.contentBase64, 'base64').toString('utf8')).toContain('BEGIN:VCALENDAR');
  });

  it('an attached email returned as base64 (Outlook on the web) keeps its bytes and still gets the .eml name', async () => {
    const r = reader('b', { item: ABC });
    const result = await captureEmailContent(
      r,
      [att('item', { name: 'Re: terms', contentType: 'message/rfc822', attachmentType: 'item' })],
      new Set(['item'])
    );
    expect(result.attachments[0]).toMatchObject({
      fileName: 'Re: terms.eml',
      contentType: 'application/octet-stream',
      contentBase64: B64_ABC,
    });
  });
});

describe('the limits', () => {
  it('the largest attachment the picker allows fits in one save on its own', () => {
    expect(base64Length(MAX_ATTACHMENT_BYTES)).toBeLessThanOrEqual(EMAIL_CONTENT_BUDGET_BYTES);
    expect(base64Length(MAX_ATTACHMENT_BYTES + 1024 * 1024)).toBeGreaterThan(EMAIL_CONTENT_BUDGET_BYTES);
  });

  it('the content budget leaves room for the rest of the request under the server limit', () => {
    expect(SAVE_REQUEST_MAX_BYTES - EMAIL_CONTENT_BUDGET_BYTES).toBeGreaterThanOrEqual(2_000_000);
  });
});
