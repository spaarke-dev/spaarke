/**
 * Task 116a (spaarkeai-word-add-in-r1): `OutlookAdapter` reports what KIND each attachment is and what FORM its
 * content came back in, because the email save now reads attachments in the add-in and must treat them differently:
 * a file's content is base64; an attached Outlook item (forwarded email, meeting) comes back as EML / iCalendar TEXT;
 * a cloud attachment's "content" is a link, not the file. Unknown or absent values are left undefined, never thrown.
 */
import { OutlookAdapter } from '../OutlookAdapter';

type MailboxGlobal = { context: { mailbox: { item: unknown } } };

const ATTACHMENTS = [
  { id: 'f', name: 'a.pdf', contentType: 'application/pdf', size: 1, isInline: false, attachmentType: 'file' },
  { id: 'i', name: 'Fwd: terms', contentType: 'message/rfc822', size: 1, isInline: false, attachmentType: 'item' },
  { id: 'c', name: 'Plan.docx', contentType: 'x', size: 1, isInline: false, attachmentType: 'cloud' },
  { id: 'u', name: 'old.bin', contentType: 'x', size: 1, isInline: false }, // a host that sends no type
];

const FORMATS: Record<string, string | undefined> = { f: 'base64', i: 'eml', c: 'url', u: 'somethingNew' };

describe('OutlookAdapter — attachment kind and content form (task 116a)', () => {
  const mailbox = (global as unknown as { Office: MailboxGlobal }).Office.context.mailbox;
  const original = mailbox.item;
  let adapter: OutlookAdapter;

  beforeEach(async () => {
    mailbox.item = {
      itemType: 'message',
      itemId: 'item-1',
      subject: 'Subject',
      from: { emailAddress: 's@contoso.com', displayName: 'S' },
      internetMessageId: '<m@contoso.com>',
      attachments: ATTACHMENTS,
      getAttachmentContentAsync: jest.fn((id: string, cb: (r: unknown) => void) =>
        cb({ status: 'succeeded', value: { content: `content-${id}`, format: FORMATS[id] } })
      ),
    };
    adapter = new OutlookAdapter();
    await adapter.initialize();
  });

  afterAll(() => {
    mailbox.item = original;
  });

  it('getAttachments() carries each attachment type; an absent one stays undefined', async () => {
    const list = await adapter.getAttachments();
    expect(list.map(a => [a.id, a.attachmentType])).toEqual([
      ['f', 'file'],
      ['i', 'item'],
      ['c', 'cloud'],
      ['u', undefined],
    ]);
    expect('attachmentType' in list[3]!).toBe(false);
  });

  it.each([
    ['f', 'file', 'base64'],
    ['i', 'item', 'eml'],
    ['c', 'cloud', 'url'],
  ])('getAttachmentContent(%s) reports type %s and content form %s', async (id, type, format) => {
    const attachment = await adapter.getAttachmentContent(id);
    expect(attachment.attachmentType).toBe(type);
    expect(attachment.contentFormat).toBe(format);
    expect(attachment.content).toBe(`content-${id}`);
  });

  it('an unrecognised content form is left undefined, not thrown', async () => {
    const attachment = await adapter.getAttachmentContent('u');
    expect(attachment.contentFormat).toBeUndefined();
    expect(attachment.content).toBe('content-u');
  });
});
