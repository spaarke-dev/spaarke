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
import {
  ChevronDownRegular,
  ChevronRightRegular,
  ArrowExpandRegular,
  MailRegular,
  ChatRegular,
} from '@fluentui/react-icons';
import { MessageQuickView, type IMessageQuickViewProps, type TimelineMessage } from '@spaarke/ui-components';
import type { PreviewModel, PreviewThread } from './previewModel';

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
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  headerTitle: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  scroll: { flex: 1, minHeight: 0, overflowY: 'auto' },
  threadHeaderButton: {
    width: '100%',
    justifyContent: 'flex-start',
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
  },
  threadName: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  threadCount: { color: tokens.colorNeutralForeground3, marginInlineStart: tokens.spacingHorizontalXS },
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
    gap: tokens.spacingHorizontalXS,
    width: '100%',
    minWidth: 0,
  },
  messageText: { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 },
  messageSender: { fontWeight: tokens.fontWeightSemibold, color: tokens.colorNeutralForeground1 },
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
    paddingBlock: tokens.spacingVerticalXS,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
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

const MessagePreviewRow: React.FC<{ message: TimelineMessage }> = ({ message }) => {
  const s = useStyles();
  const isEmail = message.channelType === 'email';
  const trigger = (
    <Button appearance="subtle" className={s.messageRow} aria-label={`Preview message from ${senderLabel(message)}`}>
      <span className={s.messageInner}>
        {isEmail ? <MailRegular aria-hidden="true" /> : <ChatRegular aria-hidden="true" />}
        <span className={s.messageText}>
          <Text size={200} className={s.messageSender}>
            {senderLabel(message)}
          </Text>
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
    />
  );
};

const ThreadPreview: React.FC<{ thread: PreviewThread; defaultExpanded: boolean }> = ({ thread, defaultExpanded }) => {
  const s = useStyles();
  const [expanded, setExpanded] = React.useState(defaultExpanded);

  return (
    <div>
      <Button
        appearance="subtle"
        className={s.threadHeaderButton}
        icon={expanded ? <ChevronDownRegular /> : <ChevronRightRegular />}
        aria-expanded={expanded}
        onClick={() => setExpanded(prev => !prev)}
      >
        <span className={s.threadName}>{thread.name}</span>
        <Caption1 className={s.threadCount}>({thread.threadMessageCount})</Caption1>
      </Button>
      {expanded &&
        (thread.messages.length > 0 ? (
          <>
            {thread.messages.map(m => (
              <MessagePreviewRow key={m.id} message={m} />
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
  /** Raised by the header "Open conversations" affordance to launch the record-filtered modal. */
  onOpen: () => void;
  className?: string;
}

export const ConversationPreview: React.FC<IConversationPreviewProps> = ({
  model,
  version,
  showVersionFooter,
  onOpen,
  className,
}) => {
  const s = useStyles();
  const hasThreads = model.threads.length > 0;

  return (
    <div className={mergeClasses(s.root, className)}>
      <div className={s.header}>
        <Text className={s.headerTitle}>Conversations</Text>
        <Button
          appearance="subtle"
          size="small"
          icon={<ArrowExpandRegular />}
          onClick={onOpen}
          aria-label="Open conversations in full view"
        >
          Open
        </Button>
      </div>

      <div className={s.scroll}>
        {hasThreads ? (
          model.threads.map(thread => (
            <ThreadPreview
              key={thread.threadId}
              thread={thread}
              defaultExpanded={thread.threadId === model.defaultExpandedThreadId}
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
