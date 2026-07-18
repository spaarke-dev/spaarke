/**
 * CommunicationTimeline.reducer.ts
 *
 * Single `useReducer` source of truth for `<CommunicationTimeline />`'s
 * thread/unread/poll/send state (task 060, step 3). Pure — no I/O, no
 * platform APIs (ADR-012); the poll hook (`useThreadPoll`) performs the I/O
 * and dispatches these actions.
 */
import type { TimelineMessage, UnreadState } from './CommunicationTimeline.types';

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

export type CommunicationTimelineStatus = 'idle' | 'loading' | 'ready' | 'error';

export interface CommunicationTimelineState {
  status: CommunicationTimelineStatus;
  /** Flat, de-duplicated message set (id-keyed merge). Ordering/nesting is derived via `buildTimeline`, not stored. */
  messages: TimelineMessage[];
  unread: UnreadState;
  isSending: boolean;
  error?: string;
}

export const initialTimelineState: CommunicationTimelineState = {
  status: 'idle',
  messages: [],
  unread: { unreadCount: 0 },
  isSending: false,
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
  | { type: 'CLEAR_ERROR' };

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

    default:
      return state;
  }
}
