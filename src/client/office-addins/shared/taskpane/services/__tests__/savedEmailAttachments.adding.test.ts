/**
 * Task 141 review (F2): an attachment a queued add names is `adding` — never `saved` (the server lists it as saved only once
 * its document exists), never `unsaved` (it is being created), and it never counts towards the "Unsaved attachments"
 * warning. A name the server lists as saved stays `saved`.
 */
import { classifySavedAttachments, hasUnsavedAttachments } from '../savedEmailAttachments';
import type { AttachmentInfo } from '@shared/adapters/types';

const pdf = (name: string): AttachmentInfo => ({
  id: `att-${name}`,
  name,
  contentType: 'application/pdf',
  size: 20_000,
  isInline: false,
});
const A = pdf('Exhibit A.pdf');
const B = pdf('Exhibit B.pdf');
const C = pdf('Exhibit C.pdf');

describe('classifySavedAttachments — names a queued add is creating (task 141 review F2)', () => {
  it('a queued name the server does not list yet is adding, and is not counted as unsaved', () => {
    const states = classifySavedAttachments([A, B], ['Exhibit A.pdf'], undefined, ['exhibit b.PDF']);

    expect(states.get(A.id)).toBe('saved');
    expect(states.get(B.id)).toBe('adding');
    expect(hasUnsavedAttachments(states)).toBe(false);
  });

  it('a name the server lists as saved stays saved even while an add names it', () => {
    const states = classifySavedAttachments([B], ['Exhibit B.pdf'], undefined, ['Exhibit B.pdf']);

    expect(states.get(B.id)).toBe('saved');
  });

  it('with the saved names not determined, a queued name is still adding and the rest unknown', () => {
    const states = classifySavedAttachments([A, B], undefined, undefined, ['Exhibit B.pdf']);

    expect(states.get(A.id)).toBe('unknown');
    expect(states.get(B.id)).toBe('adding');
  });

  it('negative: a name no add queued is unsaved as before, and keeps the warning', () => {
    const states = classifySavedAttachments([A, B, C], ['Exhibit A.pdf'], undefined, ['Exhibit B.pdf']);

    expect(states.get(C.id)).toBe('unsaved');
    expect(hasUnsavedAttachments(states)).toBe(true);
  });
});
