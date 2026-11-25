/**
 * Task 138 (#2209): the words for "the save was asked to add attachments the stored email does not hold".
 */
import { describeAttachmentsNotStored, readAttachmentsNotStored } from '../attachmentsAddedNotice';

describe('attachmentsNotStoredNotice (task 138)', () => {
  it.each([
    [['Exhibit B.pdf'], ['Exhibit B.pdf']],
    [
      ['Exhibit B.pdf', '', 7, null, 'Exhibit C.pdf'],
      ['Exhibit B.pdf', 'Exhibit C.pdf'],
    ],
    [[], []],
    [undefined, []],
    [null, []],
    ['Exhibit B.pdf', []],
    [3, []],
  ])('reads %p as %p', (value, expected) => {
    expect(readAttachmentsNotStored(value)).toEqual(expected);
  });

  it('names one attachment', () => {
    expect(describeAttachmentsNotStored(['Exhibit B.pdf'])).toBe(
      'This attachment could not be added because it was not saved with the email: Exhibit B.pdf. To include it, choose "Save again as a new document", which saves a second copy of the email.'
    );
  });

  it('names several attachments', () => {
    expect(describeAttachmentsNotStored(['Exhibit B.pdf', 'Exhibit C.pdf'])).toBe(
      'These attachments could not be added because they were not saved with the email: Exhibit B.pdf, Exhibit C.pdf. To include them, choose "Save again as a new document", which saves a second copy of the email.'
    );
  });

  it.each([[[]], [undefined]])('says nothing for %p', names => {
    expect(describeAttachmentsNotStored(names)).toBeNull();
  });
});
