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
 *
 * Task 063 (FR-13) — bidirectional inline content quoting. "Quote into
 * message" reads a row's `sprk_body`/`sprk_bodyformat`, formats it via the
 * pure `quoteBody()` helper (`utils/quoteBody.ts`), and merges it into the
 * SAME `prefill` state fed to `<TimelineComposeBox/>` that the host-supplied
 * `prefill` prop already drives — one prefill mechanism, not two (§11).
 * "Quote into email" does the equivalent read/format step but hands the
 * result to the host via `onQuoteIntoEmail` (shaped exactly like
 * `<EmailComposer/>`'s own `initial*` props) — this component never mounts
 * `<EmailComposer/>` itself, since email compose is a separate host-owned
 * surface (dialog/page), not an in-timeline concern.
 *
 * R2 task 020 (FR-03) — regarding mode. `<CommunicationTimeline mode="regarding" .../>`
 * renders ALL of a `(entityType, id)` regarding record's threads as
 * collapsible groups (see `subcomponents/ThreadGroup.tsx`) instead of one
 * thread's messages. The public `CommunicationTimeline` component below is a
 * thin mode switch — `ThreadModeCommunicationTimeline` is the UNCHANGED R1
 * implementation (renamed, behavior identical), `RegardingModeCommunicationTimeline`
 * is the new additive path. Both reuse the same `buildTimeline`/`MessageRow`/
 * `ChannelBadge` engine — regarding mode never forks it (ADR-012/§11).
 */
import * as React from 'react';
import { Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { useThreadPoll } from './hooks/useThreadPoll';
import { useRegardingPoll } from './hooks/useRegardingPoll';
import { buildTimeline } from './CommunicationTimeline.buildTimeline';
import {
  communicationTimelineReducer,
  initialTimelineState,
  type CommunicationTimelineAction,
} from './CommunicationTimeline.reducer';
import { MessageRow } from './subcomponents/MessageRow';
import { UnreadIndicator } from './subcomponents/UnreadIndicator';
import { ThreadGroup } from './subcomponents/ThreadGroup';
import { TimelineComposeBox, type ITimelineSendPayload } from './subcomponents/TimelineComposeBox';
import { sendTimelineMessage } from '../../services/communicationTimelineApi';
import { quoteBody } from '../../utils/quoteBody';
import { BODY_FORMAT_HTML, BODY_FORMAT_PLAIN_TEXT } from './CommunicationTimeline.types';
import type {
  CommunicationTimelinePrefill,
  CommunicationTimelineProps,
  CommunicationTimelineRegardingModeProps,
  CommunicationTimelineThreadModeProps,
  RegardingThreadGroup,
  TimelineMessage,
} from './CommunicationTimeline.types';

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
// Regarding-mode styles (R2 task 020, FR-03)
// ---------------------------------------------------------------------------

const useRegardingStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
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
});

// ---------------------------------------------------------------------------
// Component (mode switch)
// ---------------------------------------------------------------------------

/**
 * Public entry point. Discriminates on `props.mode` (`CommunicationTimeline.types.ts`):
 * `'regarding'` renders `RegardingModeCommunicationTimeline` (R2 task 020);
 * everything else (`undefined`/`'thread'`) renders the UNCHANGED R1
 * `ThreadModeCommunicationTimeline`.
 */
export const CommunicationTimeline: React.FC<CommunicationTimelineProps> = props => {
  if (props.mode === 'regarding') {
    return <RegardingModeCommunicationTimeline {...props} />;
  }
  return <ThreadModeCommunicationTimeline {...props} />;
};

CommunicationTimeline.displayName = 'CommunicationTimeline';

// ---------------------------------------------------------------------------
// Thread-id mode (R1, task 060) — UNCHANGED behavior, renamed for the mode switch above.
// ---------------------------------------------------------------------------

const ThreadModeCommunicationTimeline: React.FC<CommunicationTimelineThreadModeProps> = props => {
  const {
    threadId,
    authenticatedFetch,
    bffBaseUrl,
    pollIntervalMs,
    prefill,
    onQuoteIntoEmail,
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

  // Effective compose-box prefill (task 063) — seeded from the host-supplied
  // `prefill` prop, and re-synced whenever ITS primitive fields change
  // (mirrors `TimelineComposeBox`'s own prefill-merge effect). "Quote into
  // message" (`handleQuoteIntoMessage` below) updates this SAME state
  // directly — one prefill mechanism feeding `<TimelineComposeBox/>`, not two.
  const [effectivePrefill, setEffectivePrefill] = React.useState<CommunicationTimelinePrefill | undefined>(prefill);
  const prefillBody = prefill?.body;
  const prefillFormat = prefill?.bodyFormat;
  const prefillSubject = prefill?.subject;
  const prefillToKey = prefill?.to?.join(',');
  React.useEffect(() => {
    setEffectivePrefill(prefill);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prefillBody, prefillFormat, prefillSubject, prefillToKey]);

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

  // "Quote into message" (task 063) — reads the row's `sprk_body`/
  // `sprk_bodyformat`, formats it as an inline quote via `quoteBody()`
  // targeting HTML (the compose box's default authoring format — matches
  // `TimelineComposeBox`'s own `bodyFormat` default), and prefills the
  // SAME `<TimelineComposeBox/>` this timeline already renders — no
  // navigation, no re-mount.
  const handleQuoteIntoMessage = React.useCallback((message: TimelineMessage) => {
    const sourceFormat = message.bodyFormat === 'html' ? BODY_FORMAT_HTML : BODY_FORMAT_PLAIN_TEXT;
    const quoted = quoteBody(message.body, sourceFormat, BODY_FORMAT_HTML, {
      sender: message.sender ?? undefined,
      sentOn: message.sentOn ?? undefined,
    });
    setEffectivePrefill({
      to: message.sender ? [message.sender] : undefined,
      body: quoted,
      bodyFormat: 'HTML',
    });
  }, []);

  // "Quote into email" (task 063) — reads the row's `sprk_body`/
  // `sprk_bodyformat`, formats it via `quoteBody()`, and hands the host a
  // payload shaped exactly like `<EmailComposer/>`'s own `initial*` props
  // (`QuoteIntoEmailPayload`) — the host owns mounting `<EmailComposer/>`.
  const handleQuoteIntoEmail = React.useCallback(
    (message: TimelineMessage) => {
      if (!onQuoteIntoEmail) return;
      const sourceFormat = message.bodyFormat === 'html' ? BODY_FORMAT_HTML : BODY_FORMAT_PLAIN_TEXT;
      const quoted = quoteBody(message.body, sourceFormat, BODY_FORMAT_HTML, {
        sender: message.sender ?? undefined,
        sentOn: message.sentOn ?? undefined,
      });
      onQuoteIntoEmail({
        initialTo: message.sender ? [message.sender] : undefined,
        initialBody: quoted,
        initialBodyFormat: 'HTML',
      });
    },
    [onQuoteIntoEmail]
  );

  const handleSend = React.useCallback(
    async (payload: ITimelineSendPayload) => {
      dispatch({ type: 'BEGIN_SEND' });
      try {
        const result = await sendTimelineMessage(
          {
            to: payload.to,
            cc: payload.cc,
            bcc: payload.bcc,
            body: payload.body,
            bodyFormat: payload.bodyFormat === 'PlainText' ? 'text' : 'html',
            // Task 060 (FR-10): the compose box's own Subject field is now
            // required and always non-empty at this point — no more
            // hardcoded `Re: conversation {threadId}` filler. Populates
            // `sprk_subject` + the server-derived `sprk_name` ("Message:
            // {subject}"/"Email: {subject}").
            subject: payload.subject,
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
    [authenticatedFetch, bffBaseUrl, associations, sendMode, archiveToSpe, onSendComplete, onError]
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
          <MessageRow
            key={entry.message.id}
            message={entry.message}
            depth={entry.depth}
            onQuoteIntoMessage={handleQuoteIntoMessage}
            onQuoteIntoEmail={onQuoteIntoEmail ? handleQuoteIntoEmail : undefined}
          />
        ))}
        {timeline.length === 0 && state.status === 'ready' && (
          <Text className={styles.emptyState}>No messages yet.</Text>
        )}
      </div>

      <TimelineComposeBox
        defaultTo={lastInboundSender ? [lastInboundSender] : undefined}
        prefill={effectivePrefill}
        isSending={state.isSending}
        onSend={handleSend}
        onSearchRecipients={onSearchRecipients}
      />
    </div>
  );
};

ThreadModeCommunicationTimeline.displayName = 'ThreadModeCommunicationTimeline';

// ---------------------------------------------------------------------------
// Regarding mode (R2 task 020, FR-03)
// ---------------------------------------------------------------------------

const RegardingModeCommunicationTimeline: React.FC<CommunicationTimelineRegardingModeProps> = props => {
  const {
    entityType,
    id,
    authenticatedFetch,
    bffBaseUrl,
    pollIntervalMs,
    defaultExpandedThreadIds,
    onError,
    className,
  } = props;

  const styles = useRegardingStyles();
  const [state, dispatch] = React.useReducer(communicationTimelineReducer, initialTimelineState);

  const handleLoading = React.useCallback(() => {
    dispatch({ type: 'REGARDING_LOADING' });
  }, []);

  const handleGroups = React.useCallback(
    (groups: RegardingThreadGroup[]) => {
      dispatch({ type: 'SET_REGARDING_GROUPS', groups, defaultExpandedThreadIds });
    },
    [defaultExpandedThreadIds]
  );

  const handlePollError = React.useCallback(
    (err: unknown) => {
      const message = err instanceof Error ? err.message : 'Failed to load communications for this record.';
      dispatch({ type: 'SET_REGARDING_ERROR', error: message });
      if (err instanceof Error) onError?.(err);
    },
    [onError]
  );

  useRegardingPoll({
    entityType,
    id,
    authenticatedFetch,
    bffBaseUrl,
    pollIntervalMs,
    onLoading: handleLoading,
    onGroups: handleGroups,
    onError: handlePollError,
  });

  const handleToggleGroup = React.useCallback((threadId: string) => {
    dispatch({ type: 'TOGGLE_REGARDING_GROUP', threadId });
  }, []);

  const { groups, expandedThreadIds, status, error } = state.regarding;

  return (
    <div
      className={mergeClasses(styles.root, className)}
      role="region"
      aria-label={`Communications for ${entityType} ${id}`}
    >
      {error && (
        <Text role="alert" className={styles.errorBar}>
          {error}
        </Text>
      )}

      {groups.map(group => (
        <ThreadGroup
          key={group.threadId}
          group={group}
          expanded={expandedThreadIds.includes(group.threadId)}
          onToggle={handleToggleGroup}
        />
      ))}

      {groups.length === 0 && status === 'ready' && (
        <Text className={styles.emptyState}>No communications for this record yet.</Text>
      )}
    </div>
  );
};

RegardingModeCommunicationTimeline.displayName = 'RegardingModeCommunicationTimeline';
