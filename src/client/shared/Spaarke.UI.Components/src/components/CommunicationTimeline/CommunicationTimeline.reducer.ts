/**
 * CommunicationTimeline.reducer.ts
 *
 * Single `useReducer` source of truth for `<CommunicationTimeline />`'s
 * thread/unread/poll/send state (task 060, step 3). Pure — no I/O, no
 * platform APIs (ADR-012); the poll hook (`useThreadPoll`) performs the I/O
 * and dispatches these actions.
 */
import type { RegardingThreadGroup, TimelineMessage, UnreadState } from './CommunicationTimeline.types';

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

export type CommunicationTimelineStatus = 'idle' | 'loading' | 'ready' | 'error';

/**
 * Regarding-mode (R2 task 020, FR-03) slice — independent of the thread-id
 * mode fields above (`messages`/`unread`/`isSending`), which stay untouched
 * so the R1 surface is unaffected. `expandedThreadIds` is a plain array (not
 * a `Set`) so the reducer stays trivially serializable/debuggable; toggling
 * is a small linear scan over a per-record thread list, never large enough to
 * matter.
 */
export interface CommunicationTimelineRegardingState {
  status: CommunicationTimelineStatus;
  groups: RegardingThreadGroup[];
  /** `threadId`s currently expanded. Membership, not order, is what matters. */
  expandedThreadIds: string[];
  error?: string;
}

export interface CommunicationTimelineState {
  status: CommunicationTimelineStatus;
  /** Flat, de-duplicated message set (id-keyed merge). Ordering/nesting is derived via `buildTimeline`, not stored. */
  messages: TimelineMessage[];
  unread: UnreadState;
  isSending: boolean;
  error?: string;
  /** Regarding-mode slice — unused (left at its initial value) in thread-id mode. */
  regarding: CommunicationTimelineRegardingState;
}

export const initialRegardingState: CommunicationTimelineRegardingState = {
  status: 'idle',
  groups: [],
  expandedThreadIds: [],
};

export const initialTimelineState: CommunicationTimelineState = {
  status: 'idle',
  messages: [],
  unread: { unreadCount: 0 },
  isSending: false,
  regarding: initialRegardingState,
};

// ---------------------------------------------------------------------------
// Actions
// ---------------------------------------------------------------------------

export type CommunicationTimelineAction =
  /** Full-load replace (first poll cycle, `since` omitted). */
  | { type: 'SET_THREAD'; messages: TimelineMessage[] }
  /** Incremental merge (subsequent poll cycles, `since` = newest rendered `createdOn`). */
  | { type: 'MERGE_POLL'; messages: TimelineMessage[] }
  | { type: 'SET_UNREAD'; unreadCount: number }
  /** Advances the last-seen watermark (clears/reduces unread on next poll — see CommunicationTimeline.tsx). */
  | { type: 'ADVANCE_LAST_SEEN'; lastSeenAt: string }
  | { type: 'BEGIN_SEND' }
  | { type: 'END_SEND' }
  | { type: 'SET_ERROR'; error: string }
  | { type: 'CLEAR_ERROR' }
  // — Regarding mode (R2 task 020, FR-03) — keeps the thread-id-mode actions above untouched —
  /** Regarding-mode fetch started (initial load or a poll re-fetch). */
  | { type: 'REGARDING_LOADING' }
  /** Regarding-mode fetch resolved — replaces `groups` wholesale (the endpoint has no incremental/`since` mode). */
  | { type: 'SET_REGARDING_GROUPS'; groups: RegardingThreadGroup[]; defaultExpandedThreadIds?: string[] }
  | { type: 'SET_REGARDING_ERROR'; error: string }
  /** Toggles one group's expand/collapse state; every other group is unaffected. */
  | { type: 'TOGGLE_REGARDING_GROUP'; threadId: string };

// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

function mergeMessages(existing: TimelineMessage[], incoming: TimelineMessage[]): TimelineMessage[] {
  if (incoming.length === 0) return existing;
  const byId = new Map(existing.map(m => [m.id, m] as const));
  for (const m of incoming) byId.set(m.id, m); // incoming wins — fresher server projection.
  return Array.from(byId.values());
}

export function communicationTimelineReducer(
  state: CommunicationTimelineState,
  action: CommunicationTimelineAction
): CommunicationTimelineState {
  switch (action.type) {
    case 'SET_THREAD':
      return {
        ...state,
        status: 'ready',
        messages: mergeMessages([], action.messages),
        error: undefined,
      };

    case 'MERGE_POLL': {
      if (action.messages.length === 0) {
        return state.status === 'ready' ? state : { ...state, status: 'ready', error: undefined };
      }
      return {
        ...state,
        status: 'ready',
        messages: mergeMessages(state.messages, action.messages),
        error: undefined,
      };
    }

    case 'SET_UNREAD':
      return { ...state, unread: { ...state.unread, unreadCount: action.unreadCount } };

    case 'ADVANCE_LAST_SEEN':
      return { ...state, unread: { ...state.unread, since: action.lastSeenAt, lastSeenAt: action.lastSeenAt } };

    case 'BEGIN_SEND':
      return { ...state, isSending: true };

    case 'END_SEND':
      return { ...state, isSending: false };

    case 'SET_ERROR':
      return { ...state, status: state.messages.length > 0 ? 'ready' : 'error', error: action.error };

    case 'CLEAR_ERROR':
      return { ...state, error: undefined };

    // — Regarding mode (R2 task 020, FR-03) —
    case 'REGARDING_LOADING':
      return {
        ...state,
        regarding: {
          ...state.regarding,
          status: state.regarding.groups.length > 0 ? state.regarding.status : 'loading',
        },
      };

    case 'SET_REGARDING_GROUPS': {
      // The defaultExpandedThreadIds seed only applies on the FIRST successful load (expandedThreadIds still at its
      // initial empty value) — a later poll re-fetch must not stomp the user's own expand/collapse choices.
      const isFirstLoad = state.regarding.status !== 'ready' && state.regarding.expandedThreadIds.length === 0;
      const expandedThreadIds = isFirstLoad
        ? (action.defaultExpandedThreadIds ?? [])
        : state.regarding.expandedThreadIds;
      return {
        ...state,
        regarding: {
          status: 'ready',
          groups: action.groups,
          expandedThreadIds,
          error: undefined,
        },
      };
    }

    case 'SET_REGARDING_ERROR':
      return {
        ...state,
        regarding: {
          ...state.regarding,
          status: state.regarding.groups.length > 0 ? 'ready' : 'error',
          error: action.error,
        },
      };

    case 'TOGGLE_REGARDING_GROUP': {
      const isExpanded = state.regarding.expandedThreadIds.includes(action.threadId);
      const expandedThreadIds = isExpanded
        ? state.regarding.expandedThreadIds.filter(id => id !== action.threadId)
        : [...state.regarding.expandedThreadIds, action.threadId];
      return { ...state, regarding: { ...state.regarding, expandedThreadIds } };
    }

    default:
      return state;
  }
}
