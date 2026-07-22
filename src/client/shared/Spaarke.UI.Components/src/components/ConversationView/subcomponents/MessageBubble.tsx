/**
 * MessageBubble.tsx
 *
 * One rendered chat bubble in `<ConversationView />` (task 011, FR-02/03).
 * Bubble shape/alignment/spacing/token usage is anchored to
 * `SprkChat/SprkChatMessage.tsx` (`userContainer`/`assistantContainer`
 * styles) — same visual language, NOT a fork of its send/streaming/citation
 * logic (this component renders a persisted `sprk_communication` row, not an
 * AI chat message).
 *
 * Alignment is decided by the CALLER (`ConversationView.tsx`) via the
 * `isOwn` prop, computed STRICTLY from `message.senderSystemUserId ===
 * currentUserSystemUserId` (FR-02/FR-18) — this component itself performs no
 * identity comparison, it only renders the decision.
 *
 * HTML bodies come from persisted email content (external senders included)
 * and are UNTRUSTED — sanitized via DOMPurify before `dangerouslySetInnerHTML`,
 * same as `CommunicationTimeline/subcomponents/MessageRow.tsx`.
 */
import * as React from 'react';
import { Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import DOMPurify from 'dompurify';
import { CheckmarkCircleRegular, ErrorCircleRegular } from '@fluentui/react-icons';
import { ChannelBadge } from '../../CommunicationTimeline/subcomponents/ChannelBadge';
import { PrivacyMarkers } from '../../CommunicationTimeline/subcomponents/PrivacyMarkers';
import { MessageAttachments } from '../../CommunicationTimeline/subcomponents/MessageAttachments';
import type { TimelineAttachment, TimelineMessage } from '../../CommunicationTimeline/CommunicationTimeline.types';
import type { MessageBubbleStatus } from '../ConversationView.types';

export interface IMessageBubbleProps {
  message: TimelineMessage;
  /** Mine (`true`) → right-aligned with status. Others (`false`) → left-aligned with sender label. */
  isOwn: boolean;
  /** Only rendered when `isOwn`. Omit to render no status (e.g. status unknown). */
  status?: MessageBubbleStatus;
  /**
   * Open/preview/download an attachment via the existing SPE document-viewer
   * path (task 042 / FR-20). Threaded from `ConversationView`; the host mounts
   * `<RichFilePreviewDialog />`. Omit it and attachments render as passive chips.
   */
  onOpenAttachment?: (attachment: TimelineAttachment, message: TimelineMessage) => void;
}

const useStyles = makeStyles({
  row: {
    display: 'flex',
    flexDirection: 'column',
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  rowOwn: {
    alignItems: 'flex-end',
  },
  rowOther: {
    alignItems: 'flex-start',
  },
  senderLabel: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalXXS,
    marginLeft: tokens.spacingHorizontalXS,
  },
  bubble: {
    display: 'flex',
    flexDirection: 'column',
    maxWidth: '75%',
    paddingTop: '8px',
    paddingBottom: '8px',
    paddingLeft: '12px',
    paddingRight: '12px',
    borderRadius: tokens.borderRadiusMedium,
    wordBreak: 'break-word',
  },
  bubbleOwn: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  bubbleOther: {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
  },
  metaRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    marginBottom: tokens.spacingVerticalXXS,
  },
  bodyHtml: {
    wordBreak: 'break-word',
  },
  bodyText: {
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    fontFamily: tokens.fontFamilyBase,
  },
  footerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    marginTop: '4px',
  },
  timestamp: {
    fontSize: tokens.fontSizeBase100,
  },
  timestampOwn: {
    color: tokens.colorNeutralForegroundOnBrand,
    opacity: 0.8,
  },
  timestampOther: {
    color: tokens.colorNeutralForeground3,
  },
  statusText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForegroundOnBrand,
    opacity: 0.8,
  },
  statusFailed: {
    color: tokens.colorPaletteRedForeground2,
    opacity: 1,
  },
  attachmentsRow: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
  },
});

function formatTimestamp(iso: string | null | undefined): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function statusLabel(status: MessageBubbleStatus): string {
  switch (status) {
    case 'failed':
      return 'Failed to send';
    case 'delivered':
      return 'Delivered';
    case 'sent':
    default:
      return 'Sent';
  }
}

export const MessageBubble: React.FC<IMessageBubbleProps> = ({ message, isOwn, status, onOpenAttachment }) => {
  const styles = useStyles();

  const sanitizedHtml = React.useMemo(() => {
    if (message.bodyFormat !== 'html' || !message.body) return '';
    return DOMPurify.sanitize(message.body, { USE_PROFILES: { html: true } });
  }, [message.bodyFormat, message.body]);

  const displayName = message.senderName ?? message.sender ?? 'Unknown sender';
  const timestampLabel = formatTimestamp(message.sentOn);
  const ariaLabel = isOwn
    ? `Your message${timestampLabel ? ` at ${timestampLabel}` : ''}`
    : `Message from ${displayName}${timestampLabel ? ` at ${timestampLabel}` : ''}`;

  return (
    <div className={mergeClasses(styles.row, isOwn ? styles.rowOwn : styles.rowOther)}>
      {!isOwn && <Text className={styles.senderLabel}>{displayName}</Text>}

      <div
        className={mergeClasses(styles.bubble, isOwn ? styles.bubbleOwn : styles.bubbleOther)}
        role="article"
        tabIndex={0}
        aria-label={ariaLabel}
      >
        <div className={styles.metaRow}>
          <ChannelBadge channelType={message.channelType} />
          <PrivacyMarkers
            privilege={message.privilege}
            isInternalOnly={message.isInternalOnly}
            isPrivate={message.isPrivate}
          />
        </div>

        {message.body ? (
          message.bodyFormat === 'html' ? (
            <div className={styles.bodyHtml} dangerouslySetInnerHTML={{ __html: sanitizedHtml }} />
          ) : (
            <div className={styles.bodyText}>{message.body}</div>
          )
        ) : (
          <Text italic>No content</Text>
        )}

        {message.attachments.length > 0 && (
          <MessageAttachments
            className={styles.attachmentsRow}
            attachments={message.attachments}
            message={message}
            onOpenAttachment={onOpenAttachment}
          />
        )}

        <div className={styles.footerRow}>
          {timestampLabel && (
            <Text className={mergeClasses(styles.timestamp, isOwn ? styles.timestampOwn : styles.timestampOther)}>
              {timestampLabel}
            </Text>
          )}
          {isOwn && status && (
            <Text className={mergeClasses(styles.statusText, status === 'failed' ? styles.statusFailed : undefined)}>
              {status === 'failed' ? <ErrorCircleRegular fontSize={12} /> : <CheckmarkCircleRegular fontSize={12} />}{' '}
              {statusLabel(status)}
            </Text>
          )}
        </div>
      </div>
    </div>
  );
};

MessageBubble.displayName = 'MessageBubble';
