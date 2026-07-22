/**
 * CommunicationTimeline.characterize.reducer.test.ts (task 010, NFR-08)
 *
 * Characterization (golden-master) coverage for the THREAD-ID-MODE slice of
 * `communicationTimelineReducer` — `SET_THREAD` / `MERGE_POLL` / `SET_UNREAD` /
 * `ADVANCE_LAST_SEEN` / `BEGIN_SEND` / `END_SEND` / `SET_ERROR` / `CLEAR_ERROR`.
 *
 * COVERAGE GAP this file fills: the existing
 * `__tests__/CommunicationTimeline.reducer.test.ts` (R2 task 020) covers ONLY
 * the regarding-mode slice (`REGARDING_LOADING` / `SET_REGARDING_GROUPS` /
 * `SET_REGARDING_ERROR` / `TOGGLE_REGARDING_GROUP`) — its own header says so
 * explicitly ("The thread-id-mode actions (task 060) are unchanged by this
 * task and are not re-tested here"). No file characterizes the R1 thread-id
 * actions before Phase 2/3 (task 011 bubble `ConversationView`, task 020/025
 * send extensions) touch this reducer. Pins CURRENT behavior exactly per
 * NFR-08 "characterize before extending" — no production change in this task.
 */
import { communicationTimelineReducer, initialTimelineState } from '../CommunicationTimeline.reducer';
import type { TimelineMessage } from '../CommunicationTimeline.types';

function message(id: string, overrides: Partial<TimelineMessage> = {}): TimelineMessage {
  return {
    id,
    channelType: 'email',
    channelTypeRaw: 100000000,
    sender: `sender-${id}@example.com`,
    sentOn: '2026-07-01T10:00:00Z',
    createdOn: '2026-07-01T09:59:00Z',
    body: `body-${id}`,
    bodyFormat: 'html',
    inReplyTo: undefined,
    privilege: 0,
    attachments: [],
    ...overrides,
  };
}

describe('communicationTimelineReducer — thread-id mode — initial state', () => {
  it('starts idle, with no messages, zero unread, and not sending', () => {
    expect(initialTimelineState.status).toBe('idle');
    expect(initialTimelineState.messages).toEqual([]);
    expect(initialTimelineState.unread).toEqual({ unreadCount: 0 });
    expect(initialTimelineState.isSending).toBe(false);
    expect(initialTimelineState.error).toBeUndefined();
  });
});

describe('communicationTimelineReducer — SET_THREAD (full-load replace)', () => {
  it('replaces messages wholesale and sets status "ready"', () => {
    const state = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [message('m1'), message('m2')],
    });
    expect(state.status).toBe('ready');
    expect(state.messages.map(m => m.id)).toEqual(['m1', 'm2']);
    expect(state.error).toBeUndefined();
  });

  it('clears an existing error', () => {
    const errored = communicationTimelineReducer(initialTimelineState, { type: 'SET_ERROR', error: 'boom' });
    const state = communicationTimelineReducer(errored, { type: 'SET_THREAD', messages: [message('m1')] });
    expect(state.error).toBeUndefined();
  });

  it('a later SET_THREAD is a FULL replace, not a merge — messages absent from the new set are discarded', () => {
    const first = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [message('m1')],
    });
    const second = communicationTimelineReducer(first, { type: 'SET_THREAD', messages: [message('m2')] });
    expect(second.messages.map(m => m.id)).toEqual(['m2']);
  });

  it('de-dupes duplicate ids WITHIN the incoming SET_THREAD payload, the LAST occurrence winning', () => {
    const dup1 = message('m1', { body: 'first-version' });
    const dup2 = message('m1', { body: 'second-version' });
    const state = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [dup1, dup2],
    });
    expect(state.messages).toHaveLength(1);
    expect(state.messages[0].body).toBe('second-version');
  });
});

describe('communicationTimelineReducer — MERGE_POLL (incremental merge + de-dup)', () => {
  it('appends new messages onto the existing set', () => {
    const loaded = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [message('m1')],
    });
    const merged = communicationTimelineReducer(loaded, { type: 'MERGE_POLL', messages: [message('m2')] });
    expect(merged.messages.map(m => m.id)).toEqual(['m1', 'm2']);
  });

  it('de-dupes by message id — an incoming message sharing an existing id REPLACES it (fresher server projection wins)', () => {
    const loaded = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [message('m1', { body: 'original' })],
    });
    const merged = communicationTimelineReducer(loaded, {
      type: 'MERGE_POLL',
      messages: [message('m1', { body: 'updated' })],
    });
    expect(merged.messages).toHaveLength(1);
    expect(merged.messages[0].body).toBe('updated');
  });

  it('an empty incoming array is a referential no-op when status is already "ready"', () => {
    const loaded = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [message('m1')],
    });
    const merged = communicationTimelineReducer(loaded, { type: 'MERGE_POLL', messages: [] });
    // Pins the current early-return optimization — same object reference, not just equal value.
    expect(merged).toBe(loaded);
  });

  it('an empty incoming array still flips status to "ready" and clears error when not already ready', () => {
    const errored = communicationTimelineReducer(initialTimelineState, { type: 'SET_ERROR', error: 'boom' });
    const merged = communicationTimelineReducer(errored, { type: 'MERGE_POLL', messages: [] });
    expect(merged.status).toBe('ready');
    expect(merged.error).toBeUndefined();
  });

  it('merge order is insertion order (existing first, then new ids) — the reducer does NOT sort by time (buildTimeline owns ordering)', () => {
    const loaded = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [message('z-later', { sentOn: '2026-07-01T12:00:00Z' })],
    });
    const merged = communicationTimelineReducer(loaded, {
      type: 'MERGE_POLL',
      messages: [message('a-earlier', { sentOn: '2026-07-01T08:00:00Z' })],
    });
    expect(merged.messages.map(m => m.id)).toEqual(['z-later', 'a-earlier']);
  });
});

describe('communicationTimelineReducer — unread + last-seen watermark', () => {
  it('SET_UNREAD sets unreadCount while preserving since/lastSeenAt', () => {
    const advanced = communicationTimelineReducer(initialTimelineState, {
      type: 'ADVANCE_LAST_SEEN',
      lastSeenAt: '2026-07-01T10:00:00Z',
    });
    const state = communicationTimelineReducer(advanced, { type: 'SET_UNREAD', unreadCount: 3 });
    expect(state.unread).toEqual({
      unreadCount: 3,
      since: '2026-07-01T10:00:00Z',
      lastSeenAt: '2026-07-01T10:00:00Z',
    });
  });

  it('ADVANCE_LAST_SEEN sets both since and lastSeenAt to the same value, preserving unreadCount', () => {
    const withCount = communicationTimelineReducer(initialTimelineState, { type: 'SET_UNREAD', unreadCount: 5 });
    const state = communicationTimelineReducer(withCount, {
      type: 'ADVANCE_LAST_SEEN',
      lastSeenAt: '2026-07-01T11:00:00Z',
    });
    expect(state.unread).toEqual({ unreadCount: 5, since: '2026-07-01T11:00:00Z', lastSeenAt: '2026-07-01T11:00:00Z' });
  });
});

describe('communicationTimelineReducer — send lifecycle', () => {
  it('BEGIN_SEND sets isSending true', () => {
    const state = communicationTimelineReducer(initialTimelineState, { type: 'BEGIN_SEND' });
    expect(state.isSending).toBe(true);
  });

  it('END_SEND sets isSending false', () => {
    const sending = communicationTimelineReducer(initialTimelineState, { type: 'BEGIN_SEND' });
    const state = communicationTimelineReducer(sending, { type: 'END_SEND' });
    expect(state.isSending).toBe(false);
  });
});

describe('communicationTimelineReducer — error transitions', () => {
  it('SET_ERROR sets status "error" when no messages have loaded yet', () => {
    const state = communicationTimelineReducer(initialTimelineState, { type: 'SET_ERROR', error: 'poll failed' });
    expect(state.status).toBe('error');
    expect(state.error).toBe('poll failed');
  });

  it('SET_ERROR keeps status "ready" (existing messages still shown) once messages have loaded', () => {
    const loaded = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [message('m1')],
    });
    const errored = communicationTimelineReducer(loaded, { type: 'SET_ERROR', error: 'poll failed' });
    expect(errored.status).toBe('ready');
    expect(errored.messages).toEqual(loaded.messages);
    expect(errored.error).toBe('poll failed');
  });

  it('CLEAR_ERROR clears the error without changing status', () => {
    const errored = communicationTimelineReducer(initialTimelineState, { type: 'SET_ERROR', error: 'boom' });
    const cleared = communicationTimelineReducer(errored, { type: 'CLEAR_ERROR' });
    expect(cleared.error).toBeUndefined();
    expect(cleared.status).toBe(errored.status);
  });
});

describe('communicationTimelineReducer — mode isolation', () => {
  it('thread-id-mode actions leave the regarding slice untouched', () => {
    const state = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_THREAD',
      messages: [message('m1')],
    });
    expect(state.regarding).toEqual(initialTimelineState.regarding);
  });
});
