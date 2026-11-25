/**
 * Task 138 (#2209): the words for "this save added attachments to the already-saved email".
 */
import { describeAttachmentsAdded, readAttachmentsAddedCount } from '../attachmentsAddedNotice';

describe('attachmentsAddedNotice (task 138)', () => {
  it.each([
    [['Exhibit A.pdf'], 1],
    [['Exhibit A.pdf', 'Exhibit B.pdf'], 2],
    [['Exhibit A.pdf', '', 7, null], 1],
    [[], 0],
    [undefined, 0],
    [null, 0],
    ['Exhibit A.pdf', 0],
    [3, 0],
  ])('reads %p as a count of %p', (value, expected) => {
    expect(readAttachmentsAddedCount(value)).toBe(expected);
  });

  it('describes one and several attachments', () => {
    expect(describeAttachmentsAdded(1)).toBe('Added 1 attachment to the saved email');
    expect(describeAttachmentsAdded(3)).toBe('Added 3 attachments to the saved email');
  });

  it.each([[0], [undefined], [-1]])('describes %p as nothing (the caller keeps its own title)', count => {
    expect(describeAttachmentsAdded(count)).toBeNull();
  });
});
