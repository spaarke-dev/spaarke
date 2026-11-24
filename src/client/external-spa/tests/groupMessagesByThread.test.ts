/**
 * R4 task 035 (FR-22, #2453): the pure grouping of the record's messages into threads. The only key is the server's `threadId`;
 * nothing else (subject, sender, direction, `inReplyTo`, time) may ever group two messages.
 */
import { describe, expect, it } from 'vitest';
import type { ExternalMessage } from '../src/api/messages-client';
import { groupMessagesByThread } from '../src/components/detail/groupMessagesByThread';

const msg = (id: string, over: Partial<ExternalMessage> & Record<string, unknown> = {}): ExternalMessage => ({
  sprk_communicationid: id,
  isMine: false,
  createdon: '2026-03-01T10:00:00Z',
  ...over,
});

const ids = (groups: ReturnType<typeof groupMessagesByThread>): string[][] =>
  groups.map(g => g.messages.map(m => m.sprk_communicationid));

describe('groupMessagesByThread', () => {
  it('groups messages that share a threadId, oldest first, at the place of the thread latest message', () => {
    const groups = groupMessagesByThread([
      msg('a', { threadId: 'T1' }),
      msg('b', { threadId: 'T2' }),
      msg('c', { threadId: 'T1' }),
      msg('d'),
      msg('e', { threadId: 'T1' }),
    ]);

    expect(ids(groups)).toEqual([['b'], ['d'], ['a', 'c', 'e']]);
    expect(groups.map(g => g.threadId)).toEqual(['T2', null, 'T1']);
  });

  it('keeps a list with no threadId exactly as it came: one entry per message, in order', () => {
    const groups = groupMessagesByThread([msg('a'), msg('b', { threadId: null }), msg('c', { threadId: '' })]);

    expect(ids(groups)).toEqual([['a'], ['b'], ['c']]);
    expect(groups.every(g => g.threadId === null)).toBe(true);
  });

  it('never groups on subject, sender, direction, inReplyTo or time (must-not-fire)', () => {
    const same = {
      sprk_subject: 'Re: Budget',
      sendername: 'Pat',
      sprk_direction: 100000000,
      createdon: '2026-03-01T10:00:00Z',
      inReplyTo: '<m1@example>',
    };

    expect(ids(groupMessagesByThread([msg('a', same), msg('b', same), msg('c', same)]))).toEqual([['a'], ['b'], ['c']]);
  });

  it('groups on a shared threadId whatever else differs (must-fire)', () => {
    const groups = groupMessagesByThread([
      msg('a', { threadId: 'T1', sprk_subject: 'One', sendername: 'Pat', createdon: '2026-03-01T10:00:00Z' }),
      msg('b', { threadId: 'T1', sprk_subject: 'Other', sendername: 'Sam', createdon: '2026-04-09T08:00:00Z' }),
    ]);

    expect(ids(groups)).toEqual([['a', 'b']]);
  });

  it('is empty for an empty list and does not mutate its input', () => {
    expect(groupMessagesByThread([])).toEqual([]);
    const input = [msg('a', { threadId: 'T1' }), msg('b', { threadId: 'T1' })];
    const copy = [...input];

    groupMessagesByThread(input);

    expect(input).toEqual(copy);
  });
});
