/**
 * ConversationView.tsx
 *
 * Teams-style chat-bubble presentation of a single thread (task 011,
 * FR-02/03). Consumes the SAME conversation core `<CommunicationTimeline />`
 * uses — `communicationTimelineReducer` + `useThreadPoll` + `buildTimeline`
 * (task 060) — unchanged and unforked (NFR-06); this file is a new VIEW over
 * that state, not a second engine.
 *
 * Bubbles align mine-right / others-left keyed STRICTLY on sender identity
 * (`TimelineMessage.senderSystemUserId`, R3 task 002/FR-18's `SentBy`
 * systemuserid), NEVER on email-string matching (FR-02/FR-18). A message
 * whose sender has no resolvable `systemuserid` (e.g. an external
 * participant, or a legacy row predating task 002) can never resolve as
 * "mine" and always renders left.
 *
 * READ-ONLY: no compose/send box here — that is task 013's in-conversation
 * compose surface, layered separately on top of this view (one send engine,
 * ADR-045 — this component does not duplicate it).
 *
 * Reads persisted records ONLY — no client-side ACS SDK (NFR-04). All
 * network I/O flows through the injected `authenticatedFetch` prop via
 * `communicationTimelineApi.ts` (through the core's `useThreadPoll`); this
 * component never imports `@spaarke/auth` (ADR-028). Fluent UI v9 only —
 * `makeStyles` + `tokens`, dark mode passes through the host `FluentProvider`
 * (ADR-021).
 */
import * as React from 'react';
import { Spinner, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { useThreadPoll } from '../CommunicationTimeline/hooks/useThreadPoll';
import { buildTimeline, type TimelineEntry } from '../CommunicationTimeline/CommunicationTimeline.buildTimeline';
import {
  communicationTimelineReducer,
  initialTimelineState,
} from '../CommunicationTimeline/CommunicationTimeline.reducer';
import type { TimelineMessage } from '../CommunicationTimeline/CommunicationTimeline.types';
import { MessageBubble } from './subcomponents/MessageBubble';
import type { ConversationRenderItem, ConversationViewProps } from './ConversationView.types';

// ---------------------------------------------------------------------------
// Styles (ADR-021 — semantic tokens only, no hardcoded colors)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  list: {
    flex: 1,
    minHeight: 0,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  centerState: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '120px',
  },
  emptyState: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  errorCenterState: {
    color: tokens.colorPaletteRedForeground1,
    textAlign: 'center',
  },
  errorBar: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    color: tokens.colorPaletteRedForeground1,
    backgroundColor: tokens.colorPaletteRedBackground1,
  },
  divider: {
    display: 'flex',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  dividerLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
  visuallyHidden: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    padding: 0,
    margin: '-1px',
    overflow: 'hidden',
    clipPath: 'inset(50%)',
    whiteSpace: 'nowrap',
    border: 'none',
  },
});

// ---------------------------------------------------------------------------
// Pure helpers (no I/O — see ADR-012)
// ---------------------------------------------------------------------------

/** Distance-from-bottom (px) within which the list is still considered "at bottom" for auto-scroll purposes. */
const AUTO_SCROLL_THRESHOLD_PX = 48;

/**
 * Mine/others alignment — STRICTLY sender-identity (`senderSystemUserId`),
 * never email-string matching (FR-02/FR-18). A message with no resolvable
 * `senderSystemUserId` can never be "mine".
 */
export function isOwnMessage(message: TimelineMessage, currentUserSystemUserId: string): boolean {
  return !!message.senderSystemUserId && message.senderSystemUserId === currentUserSystemUserId;
}

function dayKey(date: Date): string {
  return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

function formatDayLabel(date: Date): string {
  const now = new Date();
  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const startOfDate = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const diffDays = Math.round((startOfToday.getTime() - startOfDate.getTime()) / 86_400_000);
  if (diffDays === 0) return 'Today';
  if (diffDays === 1) return 'Yesterday';
  return startOfDate.toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' });
}

/**
 * Inserts a day-boundary divider row whenever the calendar day changes across
 * the (already chronologically-ordered, per `buildTimeline`) entries. Pure
 * VIEW-layer grouping — `buildTimeline` itself stays flat/unmodified (task
 * 010 characterized it that way; see `notes/task-010-notes.md`).
 */
export function buildConversationRenderItems(entries: TimelineEntry[]): ConversationRenderItem[] {
  const items: ConversationRenderItem[] = [];
  let lastKey: string | null = null;

  for (const entry of entries) {
    const raw = entry.message.sentOn ?? entry.message.createdOn;
    const date = raw ? new Date(raw) : null;
    const validDate = date && !Number.isNaN(date.getTime()) ? date : null;
    const key = validDate ? dayKey(validDate) : null;

    if (key !== null && key !== lastKey) {
      items.push({ kind: 'divider', key: `divider-${key}`, label: formatDayLabel(validDate as Date) });
      lastKey = key;
    }

    items.push({ kind: 'message', key: entry.message.id, entry });
  }

  return items;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ConversationView: React.FC<ConversationViewProps> = props => {
  const { threadId, currentUserSystemUserId, authenticatedFetch, bffBaseUrl, pollIntervalMs, onError, className } =
    props;

  const styles = useStyles();
  const [state, dispatch] = React.useReducer(communicationTimelineReducer, initialTimelineState);

  // Refs so the poll hook always reads the freshest cursor without re-subscribing its effect
  // (same pattern as `CommunicationTimeline.tsx`'s thread-id mode).
  const sinceCursorRef = React.useRef<string | undefined>(undefined);
  const unreadSinceRef = React.useRef<string | undefined>(undefined);
  const liveRegionRef = React.useRef<HTMLDivElement | null>(null);
  const prevMessageCountRef = React.useRef(0);

  React.useEffect(() => {
    let newest: string | undefined;
    for (const m of state.messages) {
      if (m.createdOn && (!newest || m.createdOn > newest)) newest = m.createdOn;
    }
    sinceCursorRef.current = newest;
  }, [state.messages]);

  const handleMessages = React.useCallback((incoming: TimelineMessage[]) => {
    dispatch({ type: 'MERGE_POLL', messages: incoming });
  }, []);

  // ConversationView renders no unread badge of its own — that affordance belongs to the
  // thread-list host (task 012's `ConversationWorkspace`/`ThreadList`), which polls the
  // unread-count endpoint independently. `useThreadPoll` requires an `onUnread` callback
  // (it fetches both endpoints in one tick); this is an intentional no-op, not a gap.
  const handleUnread = React.useCallback(() => {
    /* intentionally no-op — see comment above */
  }, []);

  const handlePollError = React.useCallback(
    (err: unknown) => {
      const message = err instanceof Error ? err.message : 'Failed to load conversation.';
      dispatch({ type: 'SET_ERROR', error: message });
      if (err instanceof Error) onError?.(err);
    },
    [onError]
  );

  useThreadPoll({
    threadId,
    authenticatedFetch,
    bffBaseUrl,
    pollIntervalMs,
    sinceCursorRef,
    unreadSinceRef,
    onMessages: handleMessages,
    onUnread: handleUnread,
    onError: handlePollError,
  });

  const timeline = React.useMemo(() => buildTimeline(state.messages), [state.messages]);
  const renderItems = React.useMemo(() => buildConversationRenderItems(timeline), [timeline]);

  // Live-region announcement for newly-arrived messages (screen-reader affordance, NFR-05).
  React.useEffect(() => {
    const count = state.messages.length;
    if (prevMessageCountRef.current > 0 && count > prevMessageCountRef.current && liveRegionRef.current) {
      const added = count - prevMessageCountRef.current;
      liveRegionRef.current.textContent = `${added} new message${added === 1 ? '' : 's'}.`;
    }
    prevMessageCountRef.current = count;
  }, [state.messages.length]);

  // Auto-scroll to newest on mount and on new messages — but ONLY while the user is
  // already at (or near) the bottom, so scrolling up to read history is never yanked away.
  const listRef = React.useRef<HTMLDivElement | null>(null);
  const isAtBottomRef = React.useRef(true);
  const hasScrolledInitiallyRef = React.useRef(false);

  const handleScroll = React.useCallback(() => {
    const el = listRef.current;
    if (!el) return;
    isAtBottomRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < AUTO_SCROLL_THRESHOLD_PX;
  }, []);

  React.useEffect(() => {
    const el = listRef.current;
    if (!el) return;
    if (!hasScrolledInitiallyRef.current) {
      el.scrollTop = el.scrollHeight;
      hasScrolledInitiallyRef.current = true;
      isAtBottomRef.current = true;
      return;
    }
    if (isAtBottomRef.current) {
      el.scrollTop = el.scrollHeight;
    }
  }, [renderItems.length]);

  const isLoading = state.status === 'idle';
  const isErrorState = state.status === 'error';
  const isEmpty = state.status === 'ready' && renderItems.length === 0;

  return (
    <div className={mergeClasses(styles.root, className)} role="region" aria-label="Conversation">
      <div ref={liveRegionRef} aria-live="polite" className={styles.visuallyHidden} />

      {/* Transient poll error while messages are already loaded — inline banner, list stays visible. */}
      {state.error && state.status === 'ready' && (
        <Text role="alert" className={styles.errorBar}>
          {state.error}
        </Text>
      )}

      <div
        ref={listRef}
        className={styles.list}
        role="log"
        aria-label="Conversation messages"
        onScroll={handleScroll}
      >
        {isLoading && (
          <div className={styles.centerState}>
            <Spinner size="small" label="Loading conversation…" />
          </div>
        )}

        {isErrorState && (
          <div className={styles.centerState}>
            <Text role="alert" className={styles.errorCenterState}>
              {state.error ?? 'Failed to load conversation.'}
            </Text>
          </div>
        )}

        {!isLoading && !isErrorState && isEmpty && (
          <div className={styles.centerState}>
            <Text className={styles.emptyState}>No messages yet.</Text>
          </div>
        )}

        {!isLoading &&
          !isErrorState &&
          renderItems.map(item =>
            item.kind === 'divider' ? (
              <div key={item.key} role="separator" aria-orientation="horizontal" className={styles.divider}>
                <Text size={200} className={styles.dividerLabel}>
                  {item.label}
                </Text>
              </div>
            ) : (
              <MessageBubble
                key={item.key}
                message={item.entry.message}
                isOwn={isOwnMessage(item.entry.message, currentUserSystemUserId)}
                status={isOwnMessage(item.entry.message, currentUserSystemUserId) ? 'sent' : undefined}
              />
            )
          )}
      </div>
    </div>
  );
};

ConversationView.displayName = 'ConversationView';
