/**
 * MessageRow.tsx
 *
 * One rendered row in the interleaved timeline: sender, timestamp, channel
 * badge, `sprk_body` (respecting `sprk_bodyformat`), attachment file cards,
 * reply-nesting indentation (task 060, FR-10), and — task 063, FR-13 —
 * "Quote into message" / "Quote into email" actions that read this row's
 * `sprk_body`/`sprk_bodyformat`, format it via `quoteBody()`, and hand the
 * result to the caller (`CommunicationTimeline.tsx` owns the actual prefill
 * wiring; this row only surfaces the click). Both actions are rendered on
 * every row (any `sprk_communication` row can be quoted either direction per
 * design §6.3) and only appear when the corresponding callback prop is
 * supplied, so standalone/test usage of `MessageRow` is unaffected.
 *
 * HTML bodies come from persisted email content (external senders included)
 * and are UNTRUSTED — sanitized via the shared hardened `sanitizeEmailHtml`
 * util (allow-list DOMPurify: no script/iframe/object, no `on*` handlers,
 * schemes restricted to http/https/mailto, anchors forced to
 * `rel="noopener noreferrer" target="_blank"`; task 001 / FR-16 / NFR-03)
 * before `dangerouslySetInnerHTML`. Plain-text bodies render as React text
 * content (auto-escaped, no sanitization needed).
 */
import * as React from 'react';
import { Button, Text, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import { ChatRegular, MailRegular } from '@fluentui/react-icons';
import { sanitizeEmailHtml } from '../../../utils/sanitizeEmailHtml';
import { ChannelBadge } from './ChannelBadge';
import { PrivacyMarkers } from './PrivacyMarkers';
import { MessageAttachments } from './MessageAttachments';
import type { TimelineAttachment, TimelineMessage } from '../CommunicationTimeline.types';

export interface IMessageRowProps {
  message: TimelineMessage;
  /** Reply-nesting depth from `buildTimeline` — rendered as left indentation. */
  depth: number;
  /** "Quote into message" action (task 063) — omit to hide the affordance. */
  onQuoteIntoMessage?: (message: TimelineMessage) => void;
  /** "Quote into email" action (task 063) — omit to hide the affordance. */
  onQuoteIntoEmail?: (message: TimelineMessage) => void;
  /**
   * Open/preview/download an attachment via the existing SPE document-viewer
   * path (task 042 / FR-20). Threaded from `CommunicationTimeline`; the host
   * mounts `<RichFilePreviewDialog />`. Omit it and attachments render as
   * passive chips.
   */
  onOpenAttachment?: (attachment: TimelineAttachment, message: TimelineMessage) => void;
}

const MAX_INDENT_DEPTH = 6;
const DEPTH_INDENT_PX = 20;

const useStyles = makeStyles({
  row: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  quoteActionsRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    marginLeft: 'auto',
  },
  sender: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  timestamp: {
    color: tokens.colorNeutralForeground3,
  },
  // Containment for the UNTRUSTED sanitized email HTML (ISS-003 / #1636; same set as the external SPA's
  // `RecordMessages` body). The sanitizer drops the CSS that paints outside a box, but a host must not rely on it alone:
  // `contain: paint` clips painting to this box (a huge shadow/outline/reflection stays inside) and makes it the
  // containing block for any fixed/absolute descendant; `position: relative` anchors absolute descendants here.
  // `overflowX: auto` keeps a wide table/image scrollable sideways instead of cut; `overflowY: hidden` because normal
  // flow never overflows an auto-height box, so only an out-of-flow descendant could, and that must be clipped.
  // `flexShrink: 0`: an overflow box has a flex automatic minimum size of 0, so without it a height-constrained flex
  // parent could shrink the body and clip the end of a long email with no scrollbar.
  bodyHtml: {
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
    position: 'relative',
    contain: 'paint',
    overflowX: 'auto',
    overflowY: 'hidden',
    flexShrink: 0,
  },
  bodyText: {
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    fontFamily: tokens.fontFamilyBase,
  },
  attachmentsRow: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
  },
});

export const MessageRow: React.FC<IMessageRowProps> = ({
  message,
  depth,
  onQuoteIntoMessage,
  onQuoteIntoEmail,
  onOpenAttachment,
}) => {
  const styles = useStyles();
  const cappedDepth = Math.min(Math.max(depth, 0), MAX_INDENT_DEPTH);

  const sanitizedHtml = React.useMemo(() => {
    if (message.bodyFormat !== 'html' || !message.body) return '';
    return sanitizeEmailHtml(message.body);
  }, [message.bodyFormat, message.body]);

  const timestampLabel = message.sentOn ? new Date(message.sentOn).toLocaleString() : '';

  return (
    <div
      className={styles.row}
      style={cappedDepth > 0 ? { marginLeft: cappedDepth * DEPTH_INDENT_PX } : undefined}
      role="article"
      aria-label={`Message from ${message.sender ?? 'unknown sender'}`}
    >
      <div className={styles.headerRow}>
        <ChannelBadge channelType={message.channelType} />
        <PrivacyMarkers
          privilege={message.privilege}
          isInternalOnly={message.isInternalOnly}
          isPrivate={message.isPrivate}
        />
        <Text className={styles.sender}>{message.sender ?? 'Unknown sender'}</Text>
        {timestampLabel && (
          <Text size={200} className={styles.timestamp}>
            {timestampLabel}
          </Text>
        )}
        {(onQuoteIntoMessage || onQuoteIntoEmail) && (
          <div className={styles.quoteActionsRow}>
            {onQuoteIntoMessage && (
              <Tooltip content="Quote into message" relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<ChatRegular />}
                  aria-label="Quote into message"
                  onClick={() => onQuoteIntoMessage(message)}
                />
              </Tooltip>
            )}
            {onQuoteIntoEmail && (
              <Tooltip content="Quote into email" relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<MailRegular />}
                  aria-label="Quote into email"
                  onClick={() => onQuoteIntoEmail(message)}
                />
              </Tooltip>
            )}
          </div>
        )}
      </div>

      {message.body ? (
        message.bodyFormat === 'html' ? (
          <div className={styles.bodyHtml} dangerouslySetInnerHTML={{ __html: sanitizedHtml }} />
        ) : (
          <div className={styles.bodyText}>{message.body}</div>
        )
      ) : null}

      {message.attachments.length > 0 && (
        <MessageAttachments
          className={styles.attachmentsRow}
          attachments={message.attachments}
          message={message}
          onOpenAttachment={onOpenAttachment}
        />
      )}
    </div>
  );
};

MessageRow.displayName = 'MessageRow';
