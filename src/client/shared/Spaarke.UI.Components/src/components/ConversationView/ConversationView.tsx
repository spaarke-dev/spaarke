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
 * IN-CONVERSATION COMPOSE (task 013 / FR-06): a Teams-style single chat input
 * sits at the bottom of this view. It sends through the EXISTING send path
 * (`sendTimelineMessage` → `sendCommunication`, ADR-045) on the ACS Message
 * branch (`communicationType: 'message'`, ADR-046), stamped onto the active
 * `threadId` — NO new send implementation and no 6th send path. The full
 * `<TimelineComposeBox/>` (To/Cc/Bcc/Subject/attachments email composer) does
 * not fit the bubble UX, so the send WIRING is mirrored here as a minimal chat
 * input, not the whole component. On a successful send the view refreshes
 * immediately via the core's `pollNow` (the sender sees their message without
 * waiting for the ~5s tick); the ~5s interval polling is retained, and a
 * manual refresh control also forces a poll.
 *
 * Reads persisted records ONLY — no client-side ACS SDK (NFR-04). All
 * network I/O flows through the injected `authenticatedFetch` prop via
 * `communicationTimelineApi.ts` (through the core's `useThreadPoll` for reads
 * and `sendTimelineMessage` for the send); this component never imports
 * `@spaarke/auth` (ADR-028). Fluent UI v9 only — `makeStyles` + `tokens`, dark
 * mode passes through the host `FluentProvider` (ADR-021).
 */
import * as React from 'react';
import {
  Button,
  Dropdown,
  Link,
  Option,
  Spinner,
  Text,
  Textarea,
  ToggleButton,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { ArrowClockwiseRegular, ArrowForwardRegular, SendRegular } from '@fluentui/react-icons';
import { useThreadPoll } from '../CommunicationTimeline/hooks/useThreadPoll';
import { buildTimeline, type TimelineEntry } from '../CommunicationTimeline/CommunicationTimeline.buildTimeline';
import {
  communicationTimelineReducer,
  initialTimelineState,
} from '../CommunicationTimeline/CommunicationTimeline.reducer';
import type { TimelineMessage } from '../CommunicationTimeline/CommunicationTimeline.types';
import { sendTimelineMessage } from '../../services/communicationTimelineApi';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import { MessageBubble } from './subcomponents/MessageBubble';
import { EmailInFlowBlock } from './subcomponents/EmailInFlowBlock';
import type { ConversationRenderItem, ConversationViewHandle, ConversationViewProps } from './ConversationView.types';

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
  // Conversation title header (task 025 / FR-12) — only rendered when a `title`
  // is supplied. Semantic tokens only (ADR-021).
  header: {
    display: 'flex',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Plain (record-less) title — non-interactive text.
  titleText: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  // Record-linked title — a Fluent Link (rendered as a button) that delegates
  // the record open to the host's `onOpenRecord`. Weight matches the plain
  // title so linking a title doesn't reflow the header.
  titleLink: {
    fontWeight: tokens.fontWeightSemibold,
    textAlign: 'left',
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
  // Stable DOM anchor wrapping each MessageBubble so `scrollToMessage(id)` can
  // find + flash it (task 023 / FR-05). MessageBubble itself is untouched.
  messageAnchor: {
    display: 'flex',
    flexDirection: 'column',
    borderRadius: tokens.borderRadiusMedium,
    transitionProperty: 'background-color, outline-color',
    transitionDuration: tokens.durationSlow,
    transitionTimingFunction: tokens.curveEasyEase,
    outlineWidth: tokens.strokeWidthThick,
    outlineStyle: 'solid',
    outlineColor: 'transparent',
    // Pointer users: hovering anywhere on the message row reveals its trailing
    // Forward action (task 022 / FR-08). Keyboard reveal is handled by the
    // action row's own `:focus-within` (see `messageActions`).
    ':hover': {
      '& [data-message-actions]': {
        opacity: 1,
      },
    },
  },
  // Transient open→pin highlight — semantic tokens only (ADR-021), adapts to
  // light/dark via the host FluentProvider. Auto-cleared after ~1.5s.
  messageAnchorHighlight: {
    backgroundColor: tokens.colorNeutralBackground3Selected,
    outlineColor: tokens.colorBrandStroke1,
  },
  dividerLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
  // Per-message action row inside the anchor wrapper (task 022 / FR-08). Holds
  // the Forward affordance and applies to BOTH the chat bubble and the
  // email-in-flow block (the child swaps; this row + the anchor do not). Kept
  // subtle — the action stays out of the way by default and is revealed on
  // hover/keyboard focus of the message row, but is ALWAYS present in the DOM
  // and keyboard-reachable (NFR-05). Right-aligned so it sits at the trailing
  // edge for both mine-right and others-left messages.
  messageActions: {
    display: 'flex',
    justifyContent: 'flex-end',
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    // Unobtrusive by default; the reveal is opacity-only so the button keeps
    // its layout box + focusability (never `display:none`/`visibility:hidden`,
    // which would drop it from the tab order and the accessibility tree).
    opacity: 0,
    transitionProperty: 'opacity',
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    // Keyboard users: revealing when the button itself gains focus keeps it
    // fully operable without a pointer.
    ':focus-within': {
      opacity: 1,
    },
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

const useComposeStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  inputRow: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalS,
  },
  input: {
    flex: 1,
    minWidth: 0,
  },
  feedbackRow: {
    minHeight: '16px',
    display: 'flex',
    alignItems: 'center',
  },
  sentText: {
    color: tokens.colorNeutralForeground3,
  },
  failedText: {
    color: tokens.colorPaletteRedForeground1,
  },
});

const useFilterStyles = makeStyles({
  bar: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  word: {
    minWidth: '160px',
  },
});

// ---------------------------------------------------------------------------
// Compose bar (task 013 / FR-06)
// ---------------------------------------------------------------------------

type ComposeStatus = 'idle' | 'sending' | 'sent' | 'failed';

interface ConversationComposeBarProps {
  threadId: string;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl?: string;
  /**
   * Forces an immediate out-of-band poll — invoked right after a successful
   * send (so the sender's message appears without waiting for the ~5s tick)
   * and by the manual refresh control. This is the core's `useThreadPoll`
   * `pollNow`; the ~5s interval keeps running independently.
   */
  onRefresh: () => void;
}

/**
 * Minimal Teams-style chat input. Sends via the EXISTING send path
 * (`sendTimelineMessage` → `sendCommunication`, ADR-045) on the ACS Message
 * branch (`communicationType: 'message'`, ADR-046), stamped onto `threadId`.
 * Message sends need only a body — ACS Chat addresses by thread, so no
 * recipient/subject fields are surfaced here (see `SendCommunicationOptions`).
 * Local `useState` (a small single-purpose form, not the multi-mode
 * `<EmailComposer/>` reducer — root CLAUDE.md §11).
 */
const ConversationComposeBar: React.FC<ConversationComposeBarProps> = ({
  threadId,
  authenticatedFetch,
  bffBaseUrl,
  onRefresh,
}) => {
  const styles = useComposeStyles();
  const [draft, setDraft] = React.useState('');
  const [status, setStatus] = React.useState<ComposeStatus>('idle');
  const [errorMessage, setErrorMessage] = React.useState<string | undefined>();

  // Concurrency guard, independent of the DISPLAY `status`. The textarea stays
  // enabled during a send (Teams-style — the user can start their next line),
  // so `status` can be reset to 'idle' by typing; that must NOT open a second
  // send. `inFlightRef` is the real single-flight lock. `mountedRef` prevents
  // setState-after-unmount from the async continuation on the React-16.14 PCF
  // target (ADR-022).
  const inFlightRef = React.useRef(false);
  const mountedRef = React.useRef(true);
  React.useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const canSend = draft.trim().length > 0 && status !== 'sending' && !!threadId;

  const submit = React.useCallback(async () => {
    const text = draft.trim();
    if (!text || inFlightRef.current || !threadId) return;
    inFlightRef.current = true;
    setStatus('sending');
    setErrorMessage(undefined);
    try {
      await sendTimelineMessage(
        { communicationType: 'message', threadId, body: text, bodyFormat: 'text' },
        { authenticatedFetch, bffBaseUrl }
      );
      if (!mountedRef.current) return;
      setDraft('');
      setStatus('sent');
      onRefresh(); // optimistic refresh — see onRefresh doc
    } catch (err) {
      if (!mountedRef.current) return;
      setStatus('failed');
      setErrorMessage(err instanceof Error ? err.message : 'Failed to send message.');
      // draft intentionally retained so the user can retry with one more Send.
    } finally {
      inFlightRef.current = false;
    }
  }, [draft, threadId, authenticatedFetch, bffBaseUrl, onRefresh]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      // Enter sends; Shift+Enter inserts a newline; ignore IME composition.
      if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
        e.preventDefault();
        void submit();
      }
    },
    [submit]
  );

  return (
    <div className={styles.wrapper} role="region" aria-label="Compose message">
      <div className={styles.inputRow}>
        <Textarea
          className={styles.input}
          value={draft}
          onChange={(_, data) => {
            setDraft(data.value);
            // Clear only TERMINAL feedback (sent/failed) once the user starts a
            // new message — NEVER collapse an in-flight 'sending' (that would
            // hide the spinner and re-enable Send mid-flight → double send).
            setStatus(prev => (prev === 'sent' || prev === 'failed' ? 'idle' : prev));
          }}
          onKeyDown={handleKeyDown}
          placeholder="Type a message"
          aria-label="Message"
          resize="vertical"
        />
        <Tooltip content="Refresh conversation" relationship="label">
          <Button
            appearance="subtle"
            icon={<ArrowClockwiseRegular />}
            aria-label="Refresh conversation"
            onClick={onRefresh}
          />
        </Tooltip>
        <Button
          appearance="primary"
          icon={<SendRegular />}
          aria-label="Send message"
          disabled={!canSend}
          onClick={() => void submit()}
        >
          Send
        </Button>
      </div>
      {/* No wrapper live region — each transient child is its own live region
          (role=status → polite, role=alert → assertive) to avoid nested-live-
          region double announcements (NFR-05). */}
      <div className={styles.feedbackRow}>
        {status === 'sending' && (
          <div role="status">
            <Spinner size="tiny" label="Sending…" />
          </div>
        )}
        {status === 'sent' && (
          <Text role="status" size={200} className={styles.sentText}>
            Sent
          </Text>
        )}
        {status === 'failed' && (
          <Text role="alert" size={200} className={styles.failedText}>
            {errorMessage ?? 'Failed to send message.'}
          </Text>
        )}
      </div>
    </div>
  );
};

ConversationComposeBar.displayName = 'ConversationComposeBar';

// ---------------------------------------------------------------------------
// Pure helpers (no I/O — see ADR-012)
// ---------------------------------------------------------------------------

/** Distance-from-bottom (px) within which the list is still considered "at bottom" for auto-scroll purposes. */
const AUTO_SCROLL_THRESHOLD_PX = 48;

/** How long the open→pin highlight stays lit before it clears (task 023 / FR-05). */
const PIN_HIGHLIGHT_MS = 1500;

/**
 * Mine/others alignment — STRICTLY sender-identity (`senderSystemUserId`),
 * never email-string matching (FR-02/FR-18). A message with no resolvable
 * `senderSystemUserId` can never be "mine".
 */
export function isOwnMessage(message: TimelineMessage, currentUserSystemUserId: string): boolean {
  return !!message.senderSystemUserId && message.senderSystemUserId === currentUserSystemUserId;
}

/**
 * Contextual accessible name for a message's Forward affordance (NFR-05). A
 * bare "Forward message" repeated N times is indistinguishable to a screen
 * reader, so we fold in the message's subject → sender name → sender address,
 * degrading to a generic label only when none is present.
 */
export function forwardLabel(message: TimelineMessage): string {
  const context = message.subject?.trim() || message.senderName?.trim() || message.sender?.trim();
  return context ? `Forward ${context}` : 'Forward message';
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
// In-conversation filters (task 014 / FR-09) — purely presentational over the
// already-polled timeline; NEVER touches the reducer/poll/`buildTimeline`/send.
// ---------------------------------------------------------------------------

export interface ConversationFilters {
  /** Show `channelType==='email'` bubbles (incl. teams/sms/notification/null, which fold to 'email'). Default true. */
  emailEnabled: boolean;
  /** Show `channelType==='message'` bubbles. Default true. */
  messageEnabled: boolean;
  /** Case-insensitive substring; empty string = no word facet. */
  word: string;
}

/** Visible plain-text body — strips HTML tags from html bodies so matching/options see words, not markup. */
function messagePlainBody(message: TimelineMessage): string {
  const rawBody = message.body ?? '';
  return message.bodyFormat === 'html' ? rawBody.replace(/<[^>]*>/g, ' ') : rawBody;
}

/**
 * Plain-text projection of a message for word-filter MATCHING: folds in the
 * visible body + `subject` + `sender` + `to` recipients + `senderName`,
 * lowercased. Subject + recipients are included (task 021/FR-04) so an email
 * rendered as a compact subject/from/to block is still findable by its
 * headline + recipient — otherwise a filter would hide a word the block shows.
 * (Address strings — `sender`/`to` — are included for MATCHING but deliberately
 * excluded from the dropdown OPTIONS — see `extractWordOptions` — so fragments
 * like "com" don't pollute the picker.)
 */
export function messageSearchText(message: TimelineMessage): string {
  return [
    messagePlainBody(message),
    message.subject ?? '',
    message.sender ?? '',
    ...(message.to ?? []),
    message.senderName ?? '',
  ]
    .join(' ')
    .toLowerCase();
}

/**
 * Additive AND-of-facets (FR-09): a message renders iff its channel type is
 * enabled AND (no word filter OR its search text contains the word). Non-
 * Message channel types all fold to `channelType==='email'` per
 * `TimelineChannelType`, so they follow the Email toggle. Type strings
 * unchanged (`COMMUNICATION_TYPE_EMAIL` / `COMMUNICATION_TYPE_MESSAGE`).
 */
export function messagePassesFilters(message: TimelineMessage, filters: ConversationFilters): boolean {
  const typeEnabled = message.channelType === 'message' ? filters.messageEnabled : filters.emailEnabled;
  if (!typeEnabled) return false;
  const word = filters.word.trim().toLowerCase();
  if (!word) return true;
  return messageSearchText(message).includes(word);
}

/**
 * Distinct ≥3-char alphanumeric word options for the filter dropdown, drawn
 * from the WHOLE (unfiltered) timeline so the option list stays stable as the
 * user narrows. Sorted + capped so a long thread can't produce an unbounded
 * dropdown.
 */
export function extractWordOptions(messages: TimelineMessage[], cap = 40): string[] {
  const seen = new Set<string>();
  for (const m of messages) {
    // Options come from visible body + subject + display name only — NOT the
    // email addresses (sender/to), whose domain fragments like "com" would
    // match nearly every row.
    const optionText = [messagePlainBody(m), m.subject ?? '', m.senderName ?? ''].join(' ').toLowerCase();
    for (const token of optionText.split(/[^a-z0-9]+/i)) {
      if (token.length >= 3) seen.add(token);
    }
  }
  return Array.from(seen).sort().slice(0, cap);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ConversationView = React.forwardRef<ConversationViewHandle, ConversationViewProps>((props, ref) => {
  const {
    threadId,
    title,
    regarding,
    onOpenRecord,
    currentUserSystemUserId,
    authenticatedFetch,
    bffBaseUrl,
    pollIntervalMs,
    onError,
    onOpenEmail,
    onForwardMessage,
    className,
  } = props;

  const styles = useStyles();
  const filterStyles = useFilterStyles();
  const [state, dispatch] = React.useReducer(communicationTimelineReducer, initialTimelineState);

  // In-conversation filters (task 014 / FR-09). Purely presentational local
  // state — filtering the rendered set never re-fetches or mutates the core.
  const [emailEnabled, setEmailEnabled] = React.useState(true);
  const [messageEnabled, setMessageEnabled] = React.useState(true);
  const [word, setWord] = React.useState('');

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

  const { pollNow } = useThreadPoll({
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

  // Additive filters (FR-09) applied to the ALREADY-built timeline before
  // day-divider grouping, so dividers recompute for the filtered set (no empty-
  // day headers). `wordOptions` is derived from the UNFILTERED timeline so the
  // dropdown stays stable as the user narrows.
  const filters = React.useMemo<ConversationFilters>(
    () => ({ emailEnabled, messageEnabled, word }),
    [emailEnabled, messageEnabled, word]
  );
  const filteredTimeline = React.useMemo(
    () => timeline.filter(entry => messagePassesFilters(entry.message, filters)),
    [timeline, filters]
  );
  const renderItems = React.useMemo(() => buildConversationRenderItems(filteredTimeline), [filteredTimeline]);
  const wordOptions = React.useMemo(() => extractWordOptions(timeline.map(entry => entry.message)), [timeline]);

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

  // ── open→pin scroll-to-message (task 023 / FR-05) ──────────────────────────
  // A per-message DOM anchor map + a transient-highlight id. Minimal + additive:
  // the auto-scroll model above is untouched; this only lets a host jump to and
  // flash one already-rendered bubble via the imperative handle below.
  const anchorRefs = React.useRef<Map<string, HTMLDivElement>>(new Map());
  const [highlightedId, setHighlightedId] = React.useState<string | null>(null);
  const highlightTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  const setAnchorRef = React.useCallback(
    (messageId: string) => (el: HTMLDivElement | null) => {
      if (el) anchorRefs.current.set(messageId, el);
      else anchorRefs.current.delete(messageId);
    },
    []
  );

  React.useEffect(
    () => () => {
      if (highlightTimerRef.current) clearTimeout(highlightTimerRef.current);
    },
    []
  );

  React.useImperativeHandle(
    ref,
    (): ConversationViewHandle => ({
      scrollToMessage: (messageId: string) => {
        const el = anchorRefs.current.get(messageId);
        if (!el) return; // not rendered (filtered out / different thread) — never throw
        // Guard: jsdom (and any host that hasn't polyfilled it) may not implement
        // scrollIntoView — the highlight still applies regardless.
        if (typeof el.scrollIntoView === 'function') {
          el.scrollIntoView({ block: 'center', behavior: 'smooth' });
        }
        setHighlightedId(messageId);
        if (highlightTimerRef.current) clearTimeout(highlightTimerRef.current);
        highlightTimerRef.current = setTimeout(() => setHighlightedId(null), PIN_HIGHLIGHT_MS);
      },
    }),
    []
  );

  const isLoading = state.status === 'idle';
  const isErrorState = state.status === 'error';
  // Thread genuinely has no messages vs. the current filters hid them all — distinct states (NFR-05).
  const isThreadEmpty = state.status === 'ready' && timeline.length === 0;
  const isFilteredEmpty = state.status === 'ready' && timeline.length > 0 && renderItems.length === 0;

  return (
    <div className={mergeClasses(styles.root, className)} role="region" aria-label="Conversation">
      <div ref={liveRegionRef} aria-live="polite" className={styles.visuallyHidden} />

      {/* Conversation title header (task 025 / FR-12). The title links to the
          associated record ONLY when the thread has a `regarding` AND the host
          wired `onOpenRecord`; clicking delegates the open to the host, which
          uses the sanctioned OOB record-scoped modal (MODAL-DECISION-CRITERIA
          Layout 1) — ConversationView imports no `Xrm` and embeds no iframe
          (ADR-012). Record-less threads (no regarding), or hosts that provide
          no `onOpenRecord`, render a plain, non-interactive title. Rendered
          only when a `title` is supplied (header-less otherwise). */}
      {title && (
        // role=heading (aria-level 2) so a screen-reader user can jump to the
        // conversation title via heading navigation; the interactive link/plain
        // text sits inside (NFR-05).
        <div className={styles.header} role="heading" aria-level={2}>
          {regarding && onOpenRecord ? (
            <Link
              as="button"
              // Native <button> defaults to type="submit" — force "button" so
              // activating the title link never submits a host <form> (matches
              // the SprkChatMessageRenderer / TextareaField convention).
              type="button"
              className={styles.titleLink}
              // Accessible name keeps the visible title and states the action so
              // a screen-reader user knows the link opens the record (NFR-05).
              aria-label={`${title}, open associated record`}
              onClick={() => onOpenRecord(regarding.entityType, regarding.id)}
            >
              {title}
            </Link>
          ) : (
            <Text className={styles.titleText}>{title}</Text>
          )}
        </div>
      )}

      {/* Transient poll error while messages are already loaded — inline banner, list stays visible. */}
      {state.error && state.status === 'ready' && (
        <Text role="alert" className={styles.errorBar}>
          {state.error}
        </Text>
      )}

      {/* Additive in-conversation filters (task 014 / FR-09) — only meaningful once there's a thread to filter. */}
      {timeline.length > 0 && (
        <div className={filterStyles.bar} role="group" aria-label="Filter messages">
          <ToggleButton
            size="small"
            checked={emailEnabled}
            aria-pressed={emailEnabled}
            aria-label="Show email messages"
            onClick={() => setEmailEnabled(prev => !prev)}
          >
            Email
          </ToggleButton>
          <ToggleButton
            size="small"
            checked={messageEnabled}
            aria-pressed={messageEnabled}
            aria-label="Show chat messages"
            onClick={() => setMessageEnabled(prev => !prev)}
          >
            Message
          </ToggleButton>
          <Dropdown
            className={filterStyles.word}
            size="small"
            aria-label="Filter by word"
            placeholder="All messages"
            value={word || 'All messages'}
            selectedOptions={[word]}
            onOptionSelect={(_, data) => setWord(data.optionValue ?? '')}
          >
            <Option value="" text="All messages">
              All messages
            </Option>
            {wordOptions.map(w => (
              <Option key={w} value={w} text={w}>
                {w}
              </Option>
            ))}
          </Dropdown>
        </div>
      )}

      <div ref={listRef} className={styles.list} role="log" aria-label="Conversation messages" onScroll={handleScroll}>
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

        {!isLoading && !isErrorState && isThreadEmpty && (
          <div className={styles.centerState}>
            <Text className={styles.emptyState}>No messages yet.</Text>
          </div>
        )}

        {!isLoading && !isErrorState && isFilteredEmpty && (
          <div className={styles.centerState}>
            {/* No own live region — this sits inside the role="log" list, which
                already announces content changes (avoid nested live regions). */}
            <Text className={styles.emptyState}>No messages match the current filters.</Text>
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
              <div
                key={item.key}
                ref={setAnchorRef(item.entry.message.id)}
                data-message-id={item.entry.message.id}
                data-highlighted={highlightedId === item.entry.message.id ? 'true' : undefined}
                className={mergeClasses(
                  styles.messageAnchor,
                  highlightedId === item.entry.message.id && styles.messageAnchorHighlight
                )}
              >
                {/* Email-type communications render as a compact in-flow block
                    (subject/from/to + single "Email" indicator + open-icon,
                    task 021 / FR-04); message-type keep the chat bubble. Only
                    the child swaps — the anchor wrapper (scrollToMessage) +
                    filters are untouched. */}
                {item.entry.message.channelType === 'email' ? (
                  <EmailInFlowBlock
                    message={item.entry.message}
                    isOwn={isOwnMessage(item.entry.message, currentUserSystemUserId)}
                    onOpen={onOpenEmail}
                  />
                ) : (
                  <MessageBubble
                    message={item.entry.message}
                    isOwn={isOwnMessage(item.entry.message, currentUserSystemUserId)}
                    status={isOwnMessage(item.entry.message, currentUserSystemUserId) ? 'sent' : undefined}
                  />
                )}

                {/* Forward affordance (task 022 / FR-08) — rendered in the anchor
                    wrapper so it applies uniformly to BOTH the chat bubble and
                    the email-in-flow block WITHOUT editing either subcomponent.
                    It only hands the message back to the host via
                    `onForwardMessage`; the HOST opens `<SendEmailDialog/>` in
                    forward mode and owns the draft — ConversationView persists
                    nothing on forward (FR-08 / ADR-012). Keyboard-reachable
                    (NFR-05); revealed on hover/focus. Rendered ONLY when a
                    handler is wired — a keyboard user never reaches a Forward
                    button that does nothing. The accessible name is
                    contextualized per message so N Forward buttons are
                    distinguishable to a screen reader. */}
                {onForwardMessage && (
                  <div className={styles.messageActions} data-message-actions>
                    <Tooltip content={forwardLabel(item.entry.message)} relationship="label">
                      <Button
                        appearance="subtle"
                        size="small"
                        icon={<ArrowForwardRegular />}
                        aria-label={forwardLabel(item.entry.message)}
                        onClick={() => onForwardMessage(item.entry.message)}
                      />
                    </Tooltip>
                  </div>
                )}
              </div>
            )
          )}
      </div>

      <ConversationComposeBar
        threadId={threadId}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        onRefresh={pollNow}
      />
    </div>
  );
});

ConversationView.displayName = 'ConversationView';
