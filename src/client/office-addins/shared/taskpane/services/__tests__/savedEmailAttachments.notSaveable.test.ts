/**
 * Task 141 review (finding 3): an attachment no email save ever creates a document for (the server's attachment noise
 * filter drops it, or it has no content) is `notSaveable` — never `saved` on a guess, never tickable, and never counted as
 * "unsaved", so the "Unsaved attachments" warning does not come back forever for a calendar invite.
 *
 * The rules mirror `AttachmentFilterService.ShouldFilterAttachment` with the `EmailProcessingOptions` defaults.
 */
import { classifySavedAttachments, hasUnsavedAttachments, isNeverSavedByTheServer } from '../savedEmailAttachments';
import type { AttachmentInfo } from '@shared/adapters/types';

const att = (id: string, name: string, overrides: Partial<AttachmentInfo> = {}): AttachmentInfo => ({
  id,
  name,
  contentType: 'application/pdf',
  size: 20_000,
  isInline: false,
  ...overrides,
});

describe('isNeverSavedByTheServer (task 141 review)', () => {
  it.each([
    ['a calendar file', att('x', 'Meeting.ics', { contentType: 'text/calendar' })],
    ['a vCalendar file', att('x', 'Meeting.VCS')],
    ['a blocked extension', att('x', 'setup.exe')],
    ['an inline attachment', att('x', 'diagram.pdf', { isInline: true })],
    ['a file over 25 MB', att('x', 'huge.pdf', { size: 25 * 1024 * 1024 + 1 })],
    ['a tracking pixel', att('x', 'pixel.gif', { contentType: 'image/gif', size: 50_000 })],
    ['a signature image', att('x', 'image001.png', { contentType: 'image/png', size: 50_000 })],
    ['a tiny image', att('x', 'photo.jpg', { contentType: 'image/jpeg', size: 4 * 1024 })],
    ['an empty file', att('x', 'empty.pdf', { size: 0 })],
    ['a blank name', att('x', '  ')],
  ])('%s is never saved', (_label, attachment) => {
    expect(isNeverSavedByTheServer(attachment)).toBe(true);
  });

  it.each([
    ['a PDF', att('x', 'Settlement terms.pdf')],
    ['a large image that is not a signature', att('x', 'site photo.jpg', { contentType: 'image/jpeg', size: 200_000 })],
    ['a .jar (blocked by the picker, not by the server filter)', att('x', 'tool.jar')],
  ])('negative: %s can be saved', (_label, attachment) => {
    expect(isNeverSavedByTheServer(attachment)).toBe(false);
  });
});

describe('classifySavedAttachments — not saveable (task 141 review)', () => {
  const ATTACHMENTS = [
    att('a', 'Exhibit A.pdf'),
    att('b', 'Exhibit B.pdf'),
    att('ics', 'Meeting.ics', { contentType: 'text/calendar' }),
  ];

  it('an attachment the server never saves is notSaveable, and does not make the email "unsaved"', () => {
    const states = classifySavedAttachments(ATTACHMENTS, ['Exhibit A.pdf', 'Exhibit B.pdf']);

    expect(states.get('ics')).toBe('notSaveable');
    expect(hasUnsavedAttachments(states)).toBe(false);
  });

  it('a name the server reported not saveable is notSaveable even when the pane rules would allow it', () => {
    const states = classifySavedAttachments(ATTACHMENTS, ['Exhibit A.pdf'], ['exhibit b.PDF']);

    expect(states.get('b')).toBe('notSaveable');
    expect(hasUnsavedAttachments(states)).toBe(false);
  });

  it('the server listing a name as saved wins over the pane rules', () => {
    expect(classifySavedAttachments(ATTACHMENTS, ['Meeting.ics']).get('ics')).toBe('saved');
  });

  it('negative: undetermined names stay unknown for every attachment, the never-saved one included', () => {
    const states = classifySavedAttachments(ATTACHMENTS, undefined, ['Exhibit B.pdf']);

    expect([...states.values()]).toEqual(['unknown', 'unknown', 'unknown']);
  });
});
