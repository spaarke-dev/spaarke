/**
 * CommunicationTimeline.tsx
 *
 * The R1 message surface (task 060, FR-10, design §6.2/§8.6) — a polling
 * conversation/timeline that renders a single thread's persisted
 * `sprk_communication` rows (email + chat interleaved, channel-badged,
 * reply-nested), offers a compose/send box reusing `<EmailComposer/>`
 * sub-components, shows an unread indicator, and polls the BFF on a
 * configurable ~5s interval.
 *
 * Reads persisted records ONLY — no client-side ACS SDK (NFR-04). All
 * network I/O flows through the injected `authenticatedFetch` prop via
 * `communicationTimelineApi.ts`; this component never imports `@spaarke/auth`
 * (ADR-028). Fluent UI v9 only — `makeStyles` + `tokens`, dark mode passes
 * through the host `FluentProvider` (ADR-021).
 *
 * ADR-026 Path-A exception: this render engine is packaged as a form-bound
 * PCF by task 061 (NOT a Code Page) — mirrors email-r4's shipped W4 pivot.
 */
import * as React from 'react';
import { Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { useThreadPoll } from './hooks/useThreadPoll';
import { buildTimeline } from './CommunicationTimeline.buildTimeline';
import {
  communicationTimelineReducer,
  initialTimelineState,
  type CommunicationTimelineAction,
} from './CommunicationTimeline.reducer';
import { MessageRow } from './subcomponents/MessageRow';
import { UnreadIndicator } from './subcomponents/UnreadIndicator';
import { TimelineComposeBox, type ITimelineSendPayload } from './subcomponents/TimelineComposeBox';
import { sendTimelineMessage } from '../../services/communicationTimelineApi';
import type { CommunicationTimelineProps, TimelineMessage } from './CommunicationTimeline.types';

// ---------------------------------------------------------------------------
// Styles
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
  },
  emptyState: {
    padding: tokens.spacingVerticalXL,
    color: tokens.colorNeutralForeground3,
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
  visuallyHidden: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    padding: 0,
    margin: '-1px',
    overflow: 'hidden',
    clip: 'rect(0, 0, 0, 0)',
    whiteSpace: 'nowrap',
    border: 'none',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CommunicationTimeline: React.FC<CommunicationTimelineProps> = props => {
  const {
    threadId,
    authenticatedFetch,
    bffBaseUrl,
    pollIntervalMs,
    prefill,
    onSearchRecipients,
    associations,
    sendMode,
    archiveToSpe,
    onSendComplete,
    onError,
    className,
  } = props;

  const styles = useStyles();
  const [state, dispatch] = React.useReducer(communicationTimelineReducer, initialTimelineState);

  // Refs so the poll hook always reads the freshest cursor without re-subscribing its effect.
  const sinceCursorRef = React.useRef<string | undefined>(undefined);
  const unreadSinceRef = React.useRef<string | undefined>(undefined);
  const hasAdvancedOnMountRef = React.useRef(false);
  const liveRegionRef = React.useRef<HTMLDivElement | null>(null);
  const prevMessageCountRef = React.useRef(0);

  React.useEffect(() => {
    unreadSinceRef.current = state.unread.since;
  }, [state.unread.since]);

  React.useEffect(() => {
    let newest: string | undefined;
    for (const m of state.messages) {
      if (m.createdOn && (!newest || m.createdOn > newest)) newest = m.createdOn;
    }
    sinceCursorRef.current = newest;
  }, [state.messages]);

  // `dispatch` from `useReducer` is referentially stable across renders (React
  // contract, same as `useState`'s setter), so these callbacks can safely
  // omit it from their dependency arrays without a ref indirection.
  const handleMessages = React.useCallback((incoming: TimelineMessage[]) => {
    const action: CommunicationTimelineAction = { type: 'MERGE_POLL', messages: incoming };
    dispatch(action);
  }, []);

  const handleUnread = React.useCallback((unreadCount: number) => {
    dispatch({ type: 'SET_UNREAD', unreadCount });
    // "Viewing" the thread advances the last-seen watermark once the first
    // poll cycle has resolved, so subsequent unread counts reflect only
    // messages that arrive AFTER this open (Activities/portal-comments model
    // per design §6.2) — the user can still re-advance via "Mark as read"
    // if new messages accrue during an active session.
    if (!hasAdvancedOnMountRef.current) {
      hasAdvancedOnMountRef.current = true;
      dispatch({ type: 'ADVANCE_LAST_SEEN', lastSeenAt: new Date().toISOString() });
    }
  }, []);

  const handlePollError = React.useCallback(
    (err: unknown) => {
      const message = err instanceof Error ? err.message : 'Failed to refresh conversation.';
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

  // Live-region announcement for newly-arrived inbound messages (screen-reader affordance).
  React.useEffect(() => {
    const count = state.messages.length;
    if (prevMessageCountRef.current > 0 && count > prevMessageCountRef.current && liveRegionRef.current) {
      const added = count - prevMessageCountRef.current;
      liveRegionRef.current.textContent = `${added} new message${added === 1 ? '' : 's'} received.`;
    }
    prevMessageCountRef.current = count;
  }, [state.messages.length]);

  const handleMarkAsRead = React.useCallback(() => {
    dispatch({ type: 'ADVANCE_LAST_SEEN', lastSeenAt: new Date().toISOString() });
  }, []);

  const lastInboundSender = React.useMemo(() => {
    for (let i = timeline.length - 1; i >= 0; i -= 1) {
      const sender = timeline[i].message.sender;
      if (sender) return sender;
    }
    return undefined;
  }, [timeline]);

  const handleSend = React.useCallback(
    async (payload: ITimelineSendPayload) => {
      dispatch({ type: 'BEGIN_SEND' });
      try {
        const result = await sendTimelineMessage(
          {
            to: payload.to,
            body: payload.body,
            bodyFormat: payload.bodyFormat === 'PlainText' ? 'text' : 'html',
            subject: prefill?.subject ?? `Re: conversation ${threadId}`,
            attachmentDocumentIds: payload.attachmentDocumentIds,
            associations,
            sendMode,
            archiveToSpe,
          },
          { authenticatedFetch, bffBaseUrl }
        );
        onSendComplete?.(result);
        // Optimistically advance last-seen for our own outbound send; the
        // next poll cycle reconciles the actual row (echo-dedup is server-side).
        dispatch({ type: 'ADVANCE_LAST_SEEN', lastSeenAt: new Date().toISOString() });
      } catch (err) {
        if (err instanceof Error) onError?.(err);
        throw err;
      } finally {
        dispatch({ type: 'END_SEND' });
      }
    },
    [
      authenticatedFetch,
      bffBaseUrl,
      threadId,
      prefill?.subject,
      associations,
      sendMode,
      archiveToSpe,
      onSendComplete,
      onError,
    ]
  );

  return (
    <div className={mergeClasses(styles.root, className)} role="region" aria-label="Communication timeline">
      <div ref={liveRegionRef} aria-live="polite" className={styles.visuallyHidden} />

      <UnreadIndicator unreadCount={state.unread.unreadCount} onMarkAsRead={handleMarkAsRead} />

      {state.error && (
        <Text role="alert" className={styles.errorBar}>
          {state.error}
        </Text>
      )}

      <div className={styles.list} role="log" aria-label="Messages">
        {timeline.map(entry => (
          <MessageRow key={entry.message.id} message={entry.message} depth={entry.depth} />
        ))}
        {timeline.length === 0 && state.status === 'ready' && (
          <Text className={styles.emptyState}>No messages yet.</Text>
        )}
      </div>

      <TimelineComposeBox
        defaultTo={lastInboundSender ? [lastInboundSender] : undefined}
        prefill={prefill}
        isSending={state.isSending}
        onSend={handleSend}
        onSearchRecipients={onSearchRecipients}
      />
    </div>
  );
};

CommunicationTimeline.displayName = 'CommunicationTimeline';
