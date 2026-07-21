/**
 * CommunicationTimeline.reducer.test.ts (R2 task 020, FR-03)
 *
 * Unit coverage for the regarding-mode slice of `communicationTimelineReducer`
 * (`REGARDING_LOADING` / `SET_REGARDING_GROUPS` / `SET_REGARDING_ERROR` /
 * `TOGGLE_REGARDING_GROUP`). The reducer is pure (no I/O, no platform APIs —
 * ADR-012), so these are ADR-038 domain-logic behavior contracts
 * (MAINTAIN-class), not scaffolding. The thread-id-mode actions (task 060)
 * are unchanged by this task and are not re-tested here.
 */
import { communicationTimelineReducer, initialTimelineState } from '../CommunicationTimeline.reducer';
import type { RegardingThreadGroup } from '../CommunicationTimeline.types';

function group(threadId: string, overrides: Partial<RegardingThreadGroup> = {}): RegardingThreadGroup {
  return {
    threadId,
    name: `Thread ${threadId}`,
    messageCount: 1,
    messages: [],
    ...overrides,
  };
}

describe('communicationTimelineReducer — regarding mode', () => {
  it('starts idle with no groups and nothing expanded', () => {
    expect(initialTimelineState.regarding).toEqual({ status: 'idle', groups: [], expandedThreadIds: [] });
  });

  it('REGARDING_LOADING sets status to loading on first load', () => {
    const state = communicationTimelineReducer(initialTimelineState, { type: 'REGARDING_LOADING' });
    expect(state.regarding.status).toBe('loading');
  });

  it('REGARDING_LOADING does not clear already-loaded groups (a poll re-fetch stays "ready" while in flight)', () => {
    const loaded = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_REGARDING_GROUPS',
      groups: [group('t1')],
    });

    const reloading = communicationTimelineReducer(loaded, { type: 'REGARDING_LOADING' });

    expect(reloading.regarding.status).toBe('ready');
    expect(reloading.regarding.groups).toEqual(loaded.regarding.groups);
  });

  it('SET_REGARDING_GROUPS sets status ready and stores the groups', () => {
    const groups = [group('t1'), group('t2')];
    const state = communicationTimelineReducer(initialTimelineState, { type: 'SET_REGARDING_GROUPS', groups });

    expect(state.regarding.status).toBe('ready');
    expect(state.regarding.groups).toEqual(groups);
    expect(state.regarding.error).toBeUndefined();
  });

  it('SET_REGARDING_GROUPS seeds expandedThreadIds from defaultExpandedThreadIds on first load', () => {
    const state = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_REGARDING_GROUPS',
      groups: [group('t1'), group('t2')],
      defaultExpandedThreadIds: ['t1'],
    });

    expect(state.regarding.expandedThreadIds).toEqual(['t1']);
  });

  it('defaults expandedThreadIds to empty (all collapsed) when no defaultExpandedThreadIds is given', () => {
    const state = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_REGARDING_GROUPS',
      groups: [group('t1')],
    });

    expect(state.regarding.expandedThreadIds).toEqual([]);
  });

  it('a later SET_REGARDING_GROUPS (poll re-fetch) does NOT re-seed expandedThreadIds, preserving user toggles', () => {
    const firstLoad = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_REGARDING_GROUPS',
      groups: [group('t1'), group('t2')],
      defaultExpandedThreadIds: [],
    });

    const userExpanded = communicationTimelineReducer(firstLoad, { type: 'TOGGLE_REGARDING_GROUP', threadId: 't2' });

    const refetched = communicationTimelineReducer(userExpanded, {
      type: 'SET_REGARDING_GROUPS',
      groups: [group('t1'), group('t2', { messageCount: 2 })],
      defaultExpandedThreadIds: ['t1'], // ignored — not the first load
    });

    expect(refetched.regarding.expandedThreadIds).toEqual(['t2']);
    expect(refetched.regarding.groups[1].messageCount).toBe(2);
  });

  it('SET_REGARDING_ERROR sets status "error" when no groups have loaded yet', () => {
    const state = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_REGARDING_ERROR',
      error: 'boom',
    });

    expect(state.regarding.status).toBe('error');
    expect(state.regarding.error).toBe('boom');
  });

  it('SET_REGARDING_ERROR keeps status "ready" (with the groups still shown) when groups already loaded', () => {
    const loaded = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_REGARDING_GROUPS',
      groups: [group('t1')],
    });

    const errored = communicationTimelineReducer(loaded, { type: 'SET_REGARDING_ERROR', error: 'poll failed' });

    expect(errored.regarding.status).toBe('ready');
    expect(errored.regarding.groups).toEqual(loaded.regarding.groups);
    expect(errored.regarding.error).toBe('poll failed');
  });

  it('TOGGLE_REGARDING_GROUP expands a collapsed group', () => {
    const state = communicationTimelineReducer(initialTimelineState, {
      type: 'TOGGLE_REGARDING_GROUP',
      threadId: 't1',
    });

    expect(state.regarding.expandedThreadIds).toEqual(['t1']);
  });

  it('TOGGLE_REGARDING_GROUP collapses an already-expanded group', () => {
    const expanded = communicationTimelineReducer(initialTimelineState, {
      type: 'TOGGLE_REGARDING_GROUP',
      threadId: 't1',
    });

    const collapsed = communicationTimelineReducer(expanded, { type: 'TOGGLE_REGARDING_GROUP', threadId: 't1' });

    expect(collapsed.regarding.expandedThreadIds).toEqual([]);
  });

  it('toggling one group does not affect another group already expanded', () => {
    const oneExpanded = communicationTimelineReducer(initialTimelineState, {
      type: 'TOGGLE_REGARDING_GROUP',
      threadId: 't1',
    });

    const bothExpanded = communicationTimelineReducer(oneExpanded, {
      type: 'TOGGLE_REGARDING_GROUP',
      threadId: 't2',
    });

    expect(bothExpanded.regarding.expandedThreadIds).toEqual(['t1', 't2']);
  });

  it('thread-id-mode fields are untouched by regarding-mode actions', () => {
    const state = communicationTimelineReducer(initialTimelineState, {
      type: 'SET_REGARDING_GROUPS',
      groups: [group('t1')],
    });

    expect(state.messages).toEqual(initialTimelineState.messages);
    expect(state.unread).toEqual(initialTimelineState.unread);
    expect(state.isSending).toBe(initialTimelineState.isSending);
    expect(state.status).toBe(initialTimelineState.status);
  });
});
