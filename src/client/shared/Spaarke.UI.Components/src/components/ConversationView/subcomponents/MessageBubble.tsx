/**
 * MessageBubble.tsx
 *
 * One rendered chat bubble in `<ConversationView />` (task 011, FR-02/03),
 * Teams-style (R3 task 062 / UAT §B9): the type pill + sender name + date/time
 * sit ON A HEADER ROW ABOVE the bubble; the bubble body below is a soft
 * LIGHT-GRAY fill for others and a LIGHT-BLUE fill for the current user. Bubble
 * shape/spacing is still anchored to `SprkChat/SprkChatMessage.tsx` — same
 * visual language, NOT a fork of its send/streaming/citation logic (this
 * component renders a persisted `sprk_communication` row, not an AI chat
 * message).
 *
 * Alignment is decided by the CALLER (`ConversationView.tsx`) via the
 * `isOwn` prop, computed STRICTLY from `message.senderSystemUserId ===
 * currentUserSystemUserId` (FR-02/FR-18) — this component itself performs no
 * identity comparison, it only renders the decision.
 *
 * All colors are Fluent v9 SEMANTIC tokens (ADR-021) — the light-gray
 * (`colorNeutralBackground3`) and light-blue (`colorBrandBackground2`) bubbles
 * both adapt to dark mode through the host `FluentProvider`; no hardcoded
 * colors.
 *
 * PRESERVED behaviors: the type pill (`ChannelBadge`), the privacy/privilege
 * markers (task 043 / FR-21 — `PrivacyMarkers`), and the attachment open
 * affordances (task 042 / FR-20 — `MessageAttachments`) all still render; only
 * their LAYOUT moved (pill+markers+name+time to the header row).
 *
 * HTML bodies come from persisted email content (external senders included)
 * and are UNTRUSTED — sanitized via the shared hardened `sanitizeEmailHtml`
 * util (allow-list DOMPurify: no script/iframe/object, no `on*` handlers,
 * schemes restricted to http/https/mailto, anchors forced to
 * `rel="noopener noreferrer" target="_blank"`; task 001 / FR-16 / NFR-03)
 * before `dangerouslySetInnerHTML`, same as
 * `CommunicationTimeline/subcomponents/MessageRow.tsx`.
 */
import * as React from 'react';
import { Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { sanitizeEmailHtml } from '../../../utils/sanitizeEmailHtml';
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
  /**
   * Optional trailing action(s) rendered inline at the end of the meta header row (right of the sender/time), NOT in a
   * separate row below the bubble (round-8.4 UAT items 3a/9). Revealed on row hover/focus via the anchor's
   * `[data-message-actions]` rule in ConversationView. Used for the per-message Forward + Delete controls.
   */
  headerAction?: React.ReactNode;
}

const useStyles = makeStyles({
  row: {
    display: 'flex',
    flexDirection: 'column',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  rowOwn: {
    alignItems: 'flex-end',
  },
  rowOther: {
    alignItems: 'flex-start',
  },
  // Teams-style meta header ABOVE the bubble (task 062 / §B9): type pill +
  // privacy markers + sender name + date/time. Sits outside the fill so the
  // bubble body reads as a clean chat bubble.
  header: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginBottom: tokens.spacingVerticalXXS,
    maxWidth: '75%',
  },
  headerOwn: {
    // Trailing edge for own messages so the pill/time hug the right side above
    // the right-aligned bubble.
    justifyContent: 'flex-end',
  },
  senderLabel: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  headerTimestamp: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  // Trailing action slot in the header row (round-8.4 items 3a/9). Opacity-only reveal (keeps layout box + tab order);
  // the anchor's `:hover [data-message-actions]` rule flips it to 1, and its own `:focus-within` covers keyboard users.
  headerActionSlot: {
    display: 'inline-flex',
    alignItems: 'center',
    marginInlineStart: tokens.spacingHorizontalXS,
    opacity: 0,
    transitionProperty: 'opacity',
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ':focus-within': {
      opacity: 1,
    },
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
  // Light-blue fill for the current user (task 062 / §B9) — the soft brand
  // background token (NOT the strong `colorBrandBackground`), so it reads as a
  // pale blue in light mode and adapts in dark mode; neutral foreground stays
  // legible on it.
  bubbleOwn: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorNeutralForeground1,
  },
  // Light-gray fill for others (task 062 / §B9).
  bubbleOther: {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
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
  statusText: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  statusFailed: {
    color: tokens.colorPaletteRedForeground2,
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

export const MessageBubble: React.FC<IMessageBubbleProps> = ({
  message,
  isOwn,
  status,
  onOpenAttachment,
  headerAction,
}) => {
  const styles = useStyles();

  const sanitizedHtml = React.useMemo(() => {
    if (message.bodyFormat !== 'html' || !message.body) return '';
    return sanitizeEmailHtml(message.body);
  }, [message.bodyFormat, message.body]);

  const displayName = message.senderName ?? message.sender ?? 'Unknown sender';
  const timestampLabel = formatTimestamp(message.sentOn);
  const ariaLabel = isOwn
    ? `Your message${timestampLabel ? ` at ${timestampLabel}` : ''}`
    : `Message from ${displayName}${timestampLabel ? ` at ${timestampLabel}` : ''}`;

  return (
    <div className={mergeClasses(styles.row, isOwn ? styles.rowOwn : styles.rowOther)}>
      {/* Teams-style meta header ABOVE the bubble (task 062 / §B9): type pill +
          privacy markers + sender name (others only) + date/time. Own messages
          omit the redundant self-name (Teams convention). */}
      <div className={mergeClasses(styles.header, isOwn ? styles.headerOwn : undefined)}>
        <ChannelBadge channelType={message.channelType} />
        <PrivacyMarkers
          privilege={message.privilege}
          isInternalOnly={message.isInternalOnly}
          isPrivate={message.isPrivate}
        />
        {!isOwn && <Text className={styles.senderLabel}>{displayName}</Text>}
        {timestampLabel && <Text className={styles.headerTimestamp}>{timestampLabel}</Text>}
        {headerAction && (
          <span className={styles.headerActionSlot} data-message-actions>
            {headerAction}
          </span>
        )}
      </div>

      <div
        className={mergeClasses(styles.bubble, isOwn ? styles.bubbleOwn : styles.bubbleOther)}
        role="article"
        tabIndex={0}
        aria-label={ariaLabel}
      >
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

        {isOwn && status && (
          <div className={styles.footerRow}>
            <Text className={mergeClasses(styles.statusText, status === 'failed' ? styles.statusFailed : undefined)}>
              {status === 'failed' ? <ErrorCircleRegular fontSize={12} /> : <CheckmarkCircleRegular fontSize={12} />}{' '}
              {statusLabel(status)}
            </Text>
          </div>
        )}
      </div>
    </div>
  );
};

MessageBubble.displayName = 'MessageBubble';
