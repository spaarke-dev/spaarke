/**
 * ConversationPreview — the COMPACT record preview (task 030 / FR-13).
 *
 * Renders the bounded preview view-model (`buildPreviewModel`): up to 3 threads
 * (newest-active first), the default (first) thread auto-expanded, each thread
 * showing its last ≤5 communications as compact rows. Every row's sender/snippet
 * button is the trigger for the shared `<MessageQuickView/>` popover (per-message
 * quick-view — NFR-06: the shared popover, not a reimplemented one). A footer
 * renders the "N of M" counter (threads shown of total on the record) plus the
 * version, and a header "Open conversations" affordance raises `onOpen` to
 * launch the record-filtered modal.
 *
 * This is a NEW, bounded compact presentation — deliberately NOT the full
 * chat-bubble view (`<ConversationView/>` renders that inside the modal). It
 * renders no bubbles / thread-list / quick-view of its own beyond composing the
 * shared `<MessageQuickView/>`; the message MODEL is the shared `TimelineMessage`
 * (via `buildPreviewModel`), never an invented type. Fluent v9 semantic tokens
 * only (ADR-021) — light/dark pass through the host `FluentProvider`.
 */

import * as React from 'react';
import { Button, Text, Caption1, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { ChevronDownRegular, ChevronRightRegular, OpenRegular } from '@fluentui/react-icons';
import { MessageQuickView, type IMessageQuickViewProps, type TimelineMessage } from '@spaarke/ui-components';
import type { PreviewModel, PreviewThread } from './previewModel';

/** Stable empty-set default so `newThreadIds` omission never forces a re-render. */
const EMPTY_THREAD_ID_SET: ReadonlySet<string> = new Set();

// React 16 type seam: the shared lib's .d.ts is emitted against newer React
// types, whose FC return type is incompatible with React 16's JSX element type.
// Cast at the boundary (same pattern as the sibling CommunicationTimelineRegarding
// control). Runtime is unaffected — the compiled module is identical.
const MessageQuickViewR16 = MessageQuickView as unknown as React.ComponentType<IMessageQuickViewProps>;

const useStyles = makeStyles({
  root: { height: '100%', width: '100%', display: 'flex', flexDirection: 'column', minHeight: 0 },
  header: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    // Item 4 (UAT §A): the divider line under the title row is removed —
    // no bottom border here.
  },
  // Item 2 (UAT §A): section-header spec (docs/standards/UI-DESIGN-STANDARDS.md)
  // — 14px / weight 600 / colorNeutralForeground1 / 20px line height. The
  // UAT's literal #242424 IS colorNeutralForeground1 in light mode; the token
  // (not the hex) is what lets dark mode adapt (ADR-021).
  headerTitle: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  scroll: { flex: 1, minHeight: 0, overflowY: 'auto' },
  threadHeaderButton: {
    width: '100%',
    justifyContent: 'flex-start',
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
  },
  // Item 3 (UAT §A): the button's content fills the row and splits into a
  // left (name) / right (New + count) group so the count can be right-aligned.
  threadRowContent: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    width: '100%',
    minWidth: 0,
    gap: tokens.spacingHorizontalS,
  },
  threadName: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  threadCountGroup: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, flexShrink: 0 },
  newLabel: {
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1,
    whiteSpace: 'nowrap',
  },
  // "Gray circle" count badge (UAT item 3) — neutral background ramp so it
  // reads as gray in both light and dark themes (ADR-021, no hardcoded hex).
  threadCountBadge: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: '20px',
    height: '20px',
    paddingInline: tokens.spacingHorizontalXS,
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorNeutralBackground5,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
  },
  messageRow: {
    display: 'block',
    width: '100%',
    textAlign: 'left',
    paddingInline: tokens.spacingHorizontalL,
    paddingBlock: tokens.spacingVerticalXS,
    minHeight: '20px',
  },
  messageInner: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    width: '100%',
    minWidth: 0,
  },
  // Item 7 (UAT §A): channel pill on the LEFT, text only (no icon inside).
  channelPill: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '18px',
    paddingInline: tokens.spacingHorizontalXS,
    borderRadius: tokens.borderRadiusCircular,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    whiteSpace: 'nowrap',
    flexShrink: 0,
  },
  channelPillMessage: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    color: tokens.colorPaletteGreenForeground2,
  },
  channelPillEmail: {
    backgroundColor: tokens.colorPaletteBlueBackground2,
    color: tokens.colorPaletteBlueForeground2,
  },
  messageText: { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 },
  messageSenderRow: {
    display: 'flex',
    alignItems: 'baseline',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalXS,
    width: '100%',
    minWidth: 0,
  },
  // Item 8 (UAT §A): sender name 14px, NOT bold.
  messageSender: {
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  // Item 8 (UAT §A): date + time received, alongside the sender name.
  messageDateTime: {
    color: tokens.colorNeutralForeground3,
    whiteSpace: 'nowrap',
    flexShrink: 0,
  },
  messageSnippet: {
    color: tokens.colorNeutralForeground2,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  moreRow: {
    paddingInline: tokens.spacingHorizontalL,
    paddingBlock: tokens.spacingVerticalXXS,
    color: tokens.colorNeutralForeground3,
  },
  empty: {
    padding: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  footer: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    paddingInline: tokens.spacingHorizontalM,
    // Item 6 (UAT §A): no bottom (top-of-footer) line + a little more padding
    // than the prior spacingVerticalXS.
    paddingBlock: tokens.spacingVerticalM,
  },
  counterGroup: { display: 'flex', alignItems: 'baseline', gap: tokens.spacingHorizontalXS },
  counter: { color: tokens.colorNeutralForeground2, fontWeight: tokens.fontWeightSemibold },
  counterMuted: { color: tokens.colorNeutralForeground3 },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    whiteSpace: 'nowrap',
  },
});

/** Formats an ISO timestamp as "Jul 20, 3:45 PM" (date + time received — UAT item 8). */
function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}

/** Strips markup + collapses whitespace so the compact snippet shows words, not tags. */
function toSnippet(message: TimelineMessage, max = 80): string {
  const raw = message.body ?? '';
  const stripped = message.bodyFormat === 'html' ? raw.replace(/<[^>]*>/g, ' ') : raw;
  const clean = stripped.replace(/\s+/g, ' ').trim();
  if (message.channelType === 'email' && message.subject && message.subject.trim().length > 0) {
    const subj = message.subject.trim();
    return clean ? `${subj} — ${clean}`.slice(0, max) : subj.slice(0, max);
  }
  return clean.slice(0, max);
}

function senderLabel(message: TimelineMessage): string {
  return (message.senderName ?? message.sender ?? 'Unknown sender').toString();
}

const MessagePreviewRow: React.FC<{ message: TimelineMessage; onOpenThread?: () => void }> = ({
  message,
  onOpenThread,
}) => {
  const s = useStyles();
  const isEmail = message.channelType === 'email';
  const dateTimeLabel = formatDateTime(message.sentOn ?? message.createdOn);
  // Control the preview popover so a double-click (which opens the full modal) can force it closed — otherwise the
  // preview lingers in front of the modal (round-8.4 UAT: PCF item 1).
  const [previewOpen, setPreviewOpen] = React.useState(false);
  const handleDoubleClick = React.useCallback(() => {
    setPreviewOpen(false);
    onOpenThread?.();
  }, [onOpenThread]);
  const trigger = (
    <Button
      appearance="subtle"
      className={s.messageRow}
      aria-label={`Preview message from ${senderLabel(message)}`}
      // Round-8.4 item 8: double-click the row opens the full modal ON this message's thread.
      onDoubleClick={handleDoubleClick}
    >
      <span className={s.messageInner}>
        {/* Item 7 (UAT §A): channel pill on the left, text only, no icon. */}
        <span
          className={mergeClasses(s.channelPill, isEmail ? s.channelPillEmail : s.channelPillMessage)}
          aria-hidden="true"
        >
          {isEmail ? 'Email' : 'Message'}
        </span>
        <span className={s.messageText}>
          <span className={s.messageSenderRow}>
            <Text size={300} className={s.messageSender}>
              {senderLabel(message)}
            </Text>
            {dateTimeLabel && (
              <Text size={100} className={s.messageDateTime}>
                {dateTimeLabel}
              </Text>
            )}
          </span>
          <Text size={200} className={s.messageSnippet}>
            {toSnippet(message) || '(no preview)'}
          </Text>
        </span>
      </span>
    </Button>
  );

  return (
    <MessageQuickViewR16
      trigger={trigger}
      message={message}
      to={message.to}
      subject={message.subject}
      positioning="before"
      open={previewOpen}
      onOpenChange={setPreviewOpen}
    />
  );
};

const ThreadPreview: React.FC<{
  thread: PreviewThread;
  defaultExpanded: boolean;
  isNew: boolean;
  onOpenThread?: (threadId: string) => void;
}> = ({ thread, defaultExpanded, isNew, onOpenThread }) => {
  const s = useStyles();
  const [expanded, setExpanded] = React.useState(defaultExpanded);
  const countLabel = `${thread.threadMessageCount} message${thread.threadMessageCount === 1 ? '' : 's'}`;

  return (
    <div>
      <Button
        appearance="subtle"
        className={s.threadHeaderButton}
        icon={expanded ? <ChevronDownRegular /> : <ChevronRightRegular />}
        aria-expanded={expanded}
        aria-label={`${thread.name}, ${countLabel}${isNew ? ', new' : ''}`}
        onClick={() => setExpanded(prev => !prev)}
      >
        <span className={s.threadRowContent}>
          <span className={s.threadName}>{thread.name}</span>
          {/* Item 3 (UAT §A): count in a gray circle, right-aligned; "New" to its left when unread. */}
          <span className={s.threadCountGroup} aria-hidden="true">
            {isNew && <Text className={s.newLabel}>New</Text>}
            <span className={s.threadCountBadge}>{thread.threadMessageCount}</span>
          </span>
        </span>
      </Button>
      {expanded &&
        (thread.messages.length > 0 ? (
          <>
            {thread.messages.map(m => (
              <MessagePreviewRow
                key={m.id}
                message={m}
                onOpenThread={onOpenThread ? () => onOpenThread(thread.threadId) : undefined}
              />
            ))}
            {thread.hasMore && (
              <div className={s.moreRow}>
                <Caption1>
                  Showing the latest {thread.messages.length} of {thread.threadMessageCount}…
                </Caption1>
              </div>
            )}
          </>
        ) : (
          <div className={s.moreRow}>
            <Caption1>No messages to preview.</Caption1>
          </div>
        ))}
    </div>
  );
};

export interface IConversationPreviewProps {
  model: PreviewModel;
  version: string;
  showVersionFooter: boolean;
  /** Header title text (item 1, UAT §A) — configurable via the PCF `title` input property, default "MESSAGES". */
  title: string;
  /** Thread ids to flag "New" (item 3, UAT §A) — see the baseline-tracking note in CommunicationConversationPanelApp.tsx. */
  newThreadIds?: ReadonlySet<string>;
  /** Raised by the header "Open conversations" affordance to launch the record-filtered modal. */
  onOpen: () => void;
  /** Raised when a message row is double-clicked (round-8.4 item 8) — opens the modal ON that message's thread. */
  onOpenThread?: (threadId: string) => void;
  className?: string;
}

export const ConversationPreview: React.FC<IConversationPreviewProps> = ({
  model,
  version,
  showVersionFooter,
  title,
  newThreadIds,
  onOpen,
  onOpenThread,
  className,
}) => {
  const s = useStyles();
  const hasThreads = model.threads.length > 0;
  const newIds = newThreadIds ?? EMPTY_THREAD_ID_SET;

  return (
    <div className={mergeClasses(s.root, className)}>
      <div className={s.header}>
        <Text className={s.headerTitle}>{title}</Text>
        {/* Item 5 (UAT §A): box-with-arrow open icon, icon-only, no label. */}
        <Button
          appearance="subtle"
          size="small"
          icon={<OpenRegular />}
          onClick={onOpen}
          aria-label="Open conversations in full view"
        />
      </div>

      <div className={s.scroll}>
        {hasThreads ? (
          model.threads.map(thread => (
            <ThreadPreview
              key={thread.threadId}
              thread={thread}
              defaultExpanded={thread.threadId === model.defaultExpandedThreadId}
              isNew={newIds.has(thread.threadId)}
              onOpenThread={onOpenThread}
            />
          ))
        ) : (
          <div className={s.empty}>
            <Text size={200}>No conversations for this record yet.</Text>
          </div>
        )}
      </div>

      <div className={s.footer}>
        <div className={s.counterGroup}>
          <Caption1 className={s.counter} aria-label="Conversations shown counter">
            {model.shownThreadCount} of {model.totalThreadCount}
          </Caption1>
          {model.totalMessageCount > 0 && (
            <Caption1 className={s.counterMuted}>
              · {model.totalMessageCount} message{model.totalMessageCount === 1 ? '' : 's'}
            </Caption1>
          )}
        </div>
        {showVersionFooter && <Text className={s.versionText}>v{version}</Text>}
      </div>
    </div>
  );
};
