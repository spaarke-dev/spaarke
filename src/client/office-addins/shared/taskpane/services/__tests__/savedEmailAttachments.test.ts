/**
 * Task 141: which of an email's attachments are saved under its saved `.eml` — by name, case-insensitive. Undetermined
 * names (`undefined`) make every attachment `unknown`, never `unsaved`; an empty list is an answer (none saved).
 */
import {
  classifySavedAttachments,
  hasUnsavedAttachments,
  mergeSavedAttachmentNames,
  withoutNames,
} from '../savedEmailAttachments';
import type { AttachmentInfo } from '@shared/adapters/types';

const att = (id: string, name: string): AttachmentInfo => ({
  id,
  name,
  contentType: 'application/pdf',
  size: 1,
  isInline: false,
});
const ATTACHMENTS = [att('a', 'Exhibit A.pdf'), att('b', 'Exhibit B.pdf')];

describe('savedEmailAttachments (task 141)', () => {
  it('a name the server lists is saved, case-insensitive; the others are unsaved', () => {
    const states = classifySavedAttachments(ATTACHMENTS, ['exhibit a.PDF']);

    expect(states.get('a')).toBe('saved');
    expect(states.get('b')).toBe('unsaved');
    expect(hasUnsavedAttachments(states)).toBe(true);
  });

  it('every name listed: nothing unsaved', () => {
    expect(hasUnsavedAttachments(classifySavedAttachments(ATTACHMENTS, ['Exhibit A.pdf', 'Exhibit B.pdf']))).toBe(
      false
    );
  });

  it('an empty list is an answer: none saved', () => {
    const states = classifySavedAttachments(ATTACHMENTS, []);
    expect([...states.values()]).toEqual(['unsaved', 'unsaved']);
  });

  it('negative: undetermined names make every attachment unknown — never unsaved', () => {
    const states = classifySavedAttachments(ATTACHMENTS, undefined);

    expect([...states.values()]).toEqual(['unknown', 'unknown']);
    expect(hasUnsavedAttachments(states)).toBe(false);
  });

  it('merging an add keeps one of each name (case-insensitive) and leaves undetermined names undetermined', () => {
    expect(mergeSavedAttachmentNames(['Exhibit A.pdf'], ['exhibit a.pdf', 'Exhibit B.pdf'])).toEqual([
      'Exhibit A.pdf',
      'Exhibit B.pdf',
    ]);
    expect(mergeSavedAttachmentNames(undefined, ['Exhibit B.pdf'])).toBeUndefined();
  });

  it('withoutNames drops the excluded names, case-insensitive', () => {
    expect(withoutNames(['Exhibit A.pdf', 'Exhibit B.pdf'], ['EXHIBIT B.PDF'])).toEqual(['Exhibit A.pdf']);
    expect(withoutNames(['Exhibit A.pdf'], undefined)).toEqual(['Exhibit A.pdf']);
  });
});
