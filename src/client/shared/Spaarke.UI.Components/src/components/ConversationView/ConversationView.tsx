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
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Input,
  Link,
  Spinner,
  Text,
  Textarea,
  ToggleButton,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowCircleDownRegular,
  ArrowClockwiseRegular,
  ArrowForwardRegular,
  ChatRegular,
  DeleteRegular,
  MailRegular,
  SearchRegular,
  SendRegular,
} from '@fluentui/react-icons';
import { useThreadPoll } from '../CommunicationTimeline/hooks/useThreadPoll';
import { buildTimeline, type TimelineEntry } from '../CommunicationTimeline/CommunicationTimeline.buildTimeline';
import {
  communicationTimelineReducer,
  initialTimelineState,
} from '../CommunicationTimeline/CommunicationTimeline.reducer';
import type { TimelineMessage } from '../CommunicationTimeline/CommunicationTimeline.types';
import { deactivateMessage, sendTimelineMessage } from '../../services/communicationTimelineApi';
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
    // Full-width + min-width:0 so the pane's width is driven by its flex parent,
    // NOT by the widest message bubble (R3 UAT 2026-07-22 item 3).
    width: '100%',
    minWidth: 0,
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
    // Hide the visible scrollbar (round 4) — the jump-to-latest circle-down
    // button + mouse wheel drive scrolling; the bar itself is suppressed. Scroll
    // behavior (wheel, programmatic scrollTop) is unaffected.
    scrollbarWidth: 'none',
    '::-webkit-scrollbar': { width: 0, height: 0 },
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
    // Round-8 UAT: actions sit on the message's OWN side (near the bubble), not
    // pinned to the far pane edge — see messageActionsOwn / messageActionsOther.
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
  // Own (right-aligned bubble) → actions hug the right, under the bubble.
  messageActionsOwn: {
    justifyContent: 'flex-end',
  },
  // Others (left-aligned bubble) → actions hug the left, next to the message —
  // NOT floated to the far right of the pane (round-8 UAT feedback).
  messageActionsOther: {
    justifyContent: 'flex-start',
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

// Chat-area icon toolbar (task 062 / §B7): icon-only Email/Message/Unread
// filters + a search icon that reveals a message text filter. Replaces the
// former Email/Message text ToggleButtons + "All messages" word dropdown. The
// additive filter SEMANTICS are unchanged (see `messagePassesFilters`); only
// the controls became icon-only + a revealed search box. Semantic tokens only
// (ADR-021).
const useFilterStyles = makeStyles({
  // Message toolbar (R3 UAT 2026-07-22 item 4): the tools right-align at the
  // trailing edge with roomier spacing. Refresh (item 7) + Mark-as-read (item
  // 5c) live here now alongside the icon filters.
  bar: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'flex-end',
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
  spacer: {
    flex: 1,
    minWidth: 0,
  },
  // Thread name, left-aligned in the tools row (round 7 item 9 / round 8 update 2).
  // `marginInlineEnd: auto` pushes the trailing tools to the right edge; ellipsis
  // keeps a long name from crowding the tools. Typography per operator spec:
  // 14px (fontSizeBase300) · 600 (fontWeightSemibold) · #242424
  // (colorNeutralForeground1 in light; semantic token adapts in dark — ADR-021).
  title: {
    flexShrink: 1,
    minWidth: 0,
    marginInlineEnd: 'auto',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  search: {
    minWidth: '180px',
  },
  // Active/ON filter state (R3 UAT 2026-07-22 item 4): dark-blue brand fill when
  // the facet is on, transparent (subtle) when off — mirrors the Calendar
  // brand-active pattern. Semantic tokens only (ADR-021), so it adapts in dark
  // mode. Applied over an `appearance="subtle"` ToggleButton whose default
  // checked fill would otherwise be a low-contrast neutral.
  // Inactive/OFF filter state (round 3 items 7/8/9): a BLUE icon on transparent
  // (vs the default neutral-gray), so the pair reads blue-icon-off →
  // white-on-blue-on. Semantic tokens only (ADR-021).
  toggleInactive: {
    color: tokens.colorBrandForeground1,
    ':hover': {
      color: tokens.colorBrandForeground1,
    },
  },
});

// Jump-to-latest affordance (task 062 / §B8): a circular down-arrow button
// floating at the bottom of the message list, shown while the user is scrolled
// up. Replaces reliance on a visible scrollbar; the auto-scroll behavior is
// unchanged. Semantic tokens only (ADR-021).
const useListStyles = makeStyles({
  wrapper: {
    position: 'relative',
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  jumpButton: {
    position: 'absolute',
    right: tokens.spacingHorizontalL,
    bottom: tokens.spacingVerticalL,
    zIndex: 1,
    boxShadow: tokens.shadow8,
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorNeutralBackground1,
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
        {/* Send is icon-only (R3 UAT 2026-07-22 item 6) — no "Send" text label.
            Refresh moved OUT of this row into the message toolbar (item 7). */}
        <Tooltip content="Send message" relationship="label">
          <Button
            appearance="primary"
            icon={<SendRegular />}
            aria-label="Send message"
            disabled={!canSend}
            onClick={() => void submit()}
          />
        </Tooltip>
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
    onOpenAttachment,
    className,
  } = props;

  const styles = useStyles();
  const filterStyles = useFilterStyles();
  const listStyles = useListStyles();
  const [state, dispatch] = React.useReducer(communicationTimelineReducer, initialTimelineState);

  // In-conversation channel filter (R3 UAT 2026-07-23 item 10). Single-select,
  // Teams-style: default `'all'` shows everything; clicking the Email icon SOLOS
  // emails (`'email'`), clicking Message SOLOS chats (`'message'`); clicking the
  // active one returns to `'all'`. The pure predicate `messagePassesFilters`
  // (and its exhaustive unit tests) keep their `emailEnabled`/`messageEnabled`
  // shape — we DERIVE those two booleans from `channelFilter` so the helper is
  // unchanged. The former additive both-on-by-default toggles + the Unread facet
  // are removed (item 11). Purely presentational — never re-fetches the core.
  const [channelFilter, setChannelFilter] = React.useState<'all' | 'email' | 'message'>('all');
  const emailEnabled = channelFilter !== 'message';
  const messageEnabled = channelFilter !== 'email';
  const [word, setWord] = React.useState('');
  // Search reveal (task 062 / §B7): the word facet's text box is hidden behind
  // a search icon and revealed on demand, Teams-style.
  const [searchOpen, setSearchOpen] = React.useState(false);
  // Jump-to-latest visibility (task 062 / §B8): true while the user is scrolled
  // up away from the newest message.
  const [showJumpToLatest, setShowJumpToLatest] = React.useState(false);

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

  const handleMessages = React.useCallback((incoming: TimelineMessage[], isInitialLoad: boolean) => {
    // Initial load (mount OR threadId change) REPLACES — the batch is the full,
    // authoritative message set for this thread. Merging it instead would union
    // the new thread's messages into the previous thread's when switching threads
    // (RB R3 UAT 2026-07-28: a fresh thread showed every message on the record).
    dispatch(isInitialLoad ? { type: 'SET_THREAD', messages: incoming } : { type: 'MERGE_POLL', messages: incoming });
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

  // Delete-message (round 7 item 8): soft-delete a message-type row (NOT email —
  // email deletion is owned by the email surface). ConversationView already makes
  // BFF calls (send + poll) via `authenticatedFetch`, so it owns the deactivate
  // call directly (ADR-028 — no Xrm, no host wiring) and forces a poll to refresh.
  // `pendingDeleteMessage` drives the confirmation dialog; null = closed.
  const [pendingDeleteMessage, setPendingDeleteMessage] = React.useState<{ id: string; label: string } | null>(null);

  const confirmDeleteMessage = React.useCallback(() => {
    const target = pendingDeleteMessage;
    setPendingDeleteMessage(null);
    if (!target) return;
    deactivateMessage(target.id, { authenticatedFetch, bffBaseUrl })
      .then(() => pollNow())
      .catch(err => {
        if (err instanceof Error) onError?.(err);
      });
  }, [pendingDeleteMessage, authenticatedFetch, bffBaseUrl, pollNow, onError]);

  const timeline = React.useMemo(() => buildTimeline(state.messages), [state.messages]);

  // Additive filters (FR-09 + task 062 Unread facet) applied to the ALREADY-
  // built timeline before day-divider grouping, so dividers recompute for the
  // filtered set (no empty-day headers). The word facet is now driven by the
  // revealed search box (task 062 / §B7) rather than a dropdown.
  const filters = React.useMemo<ConversationFilters>(
    () => ({ emailEnabled, messageEnabled, word }),
    [emailEnabled, messageEnabled, word]
  );
  const filteredTimeline = React.useMemo(
    () => timeline.filter(entry => messagePassesFilters(entry.message, filters)),
    [timeline, filters]
  );
  const renderItems = React.useMemo(() => buildConversationRenderItems(filteredTimeline), [filteredTimeline]);

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
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < AUTO_SCROLL_THRESHOLD_PX;
    isAtBottomRef.current = atBottom;
    // Show the circle down-arrow affordance (task 062 / §B8) whenever the user
    // is scrolled up away from the newest message.
    setShowJumpToLatest(!atBottom);
  }, []);

  // Imperatively jump to the newest message (task 062 / §B8). Keyboard-operable
  // (the affordance is a native <Button>); re-arms auto-scroll by marking the
  // list as at-bottom.
  const jumpToLatest = React.useCallback(() => {
    const el = listRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
    isAtBottomRef.current = true;
    setShowJumpToLatest(false);
  }, []);

  React.useEffect(() => {
    const el = listRef.current;
    if (!el) return;
    if (!hasScrolledInitiallyRef.current) {
      el.scrollTop = el.scrollHeight;
      hasScrolledInitiallyRef.current = true;
      isAtBottomRef.current = true;
      setShowJumpToLatest(false);
      return;
    }
    if (isAtBottomRef.current) {
      el.scrollTop = el.scrollHeight;
      setShowJumpToLatest(false);
    } else {
      // New content arrived while the user is reading history — surface the
      // jump-to-latest affordance instead of yanking the scroll position.
      setShowJumpToLatest(true);
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

      {/* Transient poll error while messages are already loaded — inline banner, list stays visible. */}
      {state.error && state.status === 'ready' && (
        <Text role="alert" className={styles.errorBar}>
          {state.error}
        </Text>
      )}

      {/* Chat-area toolbar (R3 UAT 2026-07-23 items 10/11 + round 7 item 9). The
          thread NAME is now left-aligned in this same row (round 7 item 9 — no
          separate title header). It links to the associated record ONLY when the
          thread has a `regarding` AND the host wired `onOpenRecord` (delegated to
          the sanctioned OOB record-scoped modal — ConversationView imports no
          `Xrm`, ADR-012); otherwise plain text. The trailing tools are SOLO
          channel filters + search + Refresh, right-aligned. Every control keeps a
          resolvable accessible name (NFR-05). */}
      {(title || state.status === 'ready') && (
        <div className={filterStyles.bar} role="group" aria-label="Message tools">
          {title && (
            // role=heading (aria-level 2) on the WRAPPER so a screen-reader user
            // can jump to the conversation title by heading nav; the interactive
            // link/plain text sits INSIDE and keeps its own role (a button, not a
            // heading — the wrapper carries the heading semantics). `marginInlineEnd:
            // auto` (filterStyles.title) pushes the trailing tools to the right.
            <div className={filterStyles.title} role="heading" aria-level={2}>
              {regarding && onOpenRecord ? (
                <Link
                  as="button"
                  // Native <button> defaults to type="submit" — force "button" so
                  // activating the title link never submits a host <form>.
                  type="button"
                  className={styles.titleLink}
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
          {timeline.length > 0 && (
            <>
              <ToggleButton
                size="small"
                appearance={channelFilter === 'email' ? 'primary' : 'subtle'}
                className={channelFilter === 'email' ? undefined : filterStyles.toggleInactive}
                icon={<MailRegular />}
                checked={channelFilter === 'email'}
                aria-pressed={channelFilter === 'email'}
                aria-label="Show only emails"
                title="Show only emails"
                onClick={() => setChannelFilter(prev => (prev === 'email' ? 'all' : 'email'))}
              />
              <ToggleButton
                size="small"
                appearance={channelFilter === 'message' ? 'primary' : 'subtle'}
                className={channelFilter === 'message' ? undefined : filterStyles.toggleInactive}
                icon={<ChatRegular />}
                checked={channelFilter === 'message'}
                aria-pressed={channelFilter === 'message'}
                aria-label="Show only messages"
                title="Show only messages"
                onClick={() => setChannelFilter(prev => (prev === 'message' ? 'all' : 'message'))}
              />
              <ToggleButton
                size="small"
                appearance={searchOpen ? 'primary' : 'subtle'}
                className={searchOpen ? undefined : filterStyles.toggleInactive}
                icon={<SearchRegular />}
                checked={searchOpen}
                aria-pressed={searchOpen}
                aria-expanded={searchOpen}
                aria-label="Search messages"
                title="Search messages"
                onClick={() =>
                  setSearchOpen(prev => {
                    const next = !prev;
                    // Closing the search clears the active word facet so a hidden
                    // filter never keeps silently narrowing the list.
                    if (!next) setWord('');
                    return next;
                  })
                }
              />
              {searchOpen && (
                <Input
                  className={filterStyles.search}
                  size="small"
                  contentBefore={<SearchRegular />}
                  value={word}
                  placeholder="Filter messages"
                  aria-label="Filter messages by text"
                  onChange={(_e, data) => setWord(data.value)}
                />
              )}
            </>
          )}
          {/* Refresh — always available so an empty thread can still be polled. */}
          <Tooltip content="Refresh conversation" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowClockwiseRegular />}
              aria-label="Refresh conversation"
              onClick={pollNow}
            />
          </Tooltip>
        </div>
      )}

      <div className={listStyles.wrapper}>
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
                (() => {
                  const isOwn = isOwnMessage(item.entry.message, currentUserSystemUserId);
                  const isEmail = item.entry.message.channelType === 'email';
                  return (
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
                      {isEmail ? (
                        <EmailInFlowBlock message={item.entry.message} isOwn={isOwn} onOpen={onOpenEmail} />
                      ) : (
                        <MessageBubble
                          message={item.entry.message}
                          isOwn={isOwn}
                          status={isOwn ? 'sent' : undefined}
                          onOpenAttachment={onOpenAttachment}
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
                      {/* Trailing per-message actions (Forward + Delete). Delete
                      (round 7 item 8) is offered for MESSAGE-type rows only — email
                      deletion is owned by the email surface (not here). Both are
                      revealed on row hover/focus (NFR-05) and kept in the DOM/tab
                      order. Rendered when at least one action applies. */}
                      {(onForwardMessage || !isEmail) && (
                        <div
                          className={mergeClasses(
                            styles.messageActions,
                            isOwn ? styles.messageActionsOwn : styles.messageActionsOther
                          )}
                          data-message-actions
                        >
                          {onForwardMessage && (
                            <Tooltip content={forwardLabel(item.entry.message)} relationship="label">
                              <Button
                                appearance="subtle"
                                size="small"
                                icon={<ArrowForwardRegular />}
                                aria-label={forwardLabel(item.entry.message)}
                                onClick={() => onForwardMessage(item.entry.message)}
                              />
                            </Tooltip>
                          )}
                          {!isEmail && (
                            <Tooltip content="Delete message" relationship="label">
                              <Button
                                appearance="subtle"
                                size="small"
                                icon={<DeleteRegular />}
                                aria-label="Delete message"
                                onClick={() =>
                                  setPendingDeleteMessage({
                                    id: item.entry.message.id,
                                    label: item.entry.message.body?.slice(0, 60) ?? 'this message',
                                  })
                                }
                              />
                            </Tooltip>
                          )}
                        </div>
                      )}
                    </div>
                  );
                })()
              )
            )}
        </div>

        {/* Circle down-arrow jump-to-latest (task 062 / §B8) — shown only while
            scrolled up; a native <Button> so it's keyboard-operable + labeled
            (NFR-05). Auto-scroll behavior is unchanged. */}
        {showJumpToLatest && (
          <Tooltip content="Jump to latest messages" relationship="label">
            <Button
              className={listStyles.jumpButton}
              shape="circular"
              appearance="subtle"
              icon={<ArrowCircleDownRegular />}
              aria-label="Jump to latest messages"
              onClick={jumpToLatest}
            />
          </Tooltip>
        )}
      </div>

      <ConversationComposeBar
        threadId={threadId}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        onRefresh={pollNow}
      />

      {/* Delete-message confirmation (round 7 item 8). Soft-delete (deactivate) is
          reversible, but a mis-click still hides the message, so we gate it behind
          an explicit confirm. */}
      <Dialog
        open={pendingDeleteMessage !== null}
        onOpenChange={(_e, data) => {
          if (!data.open) setPendingDeleteMessage(null);
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete message?</DialogTitle>
            <DialogContent>
              This message will be removed from the conversation. This deactivates the message; it can be restored by an
              administrator.
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setPendingDeleteMessage(null)}>
                Cancel
              </Button>
              <Button appearance="primary" onClick={confirmDeleteMessage}>
                Delete
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
});

ConversationView.displayName = 'ConversationView';
