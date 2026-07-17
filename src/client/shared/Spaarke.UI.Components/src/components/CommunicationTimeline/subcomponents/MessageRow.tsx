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
 * and are UNTRUSTED — sanitized via DOMPurify before `dangerouslySetInnerHTML`
 * (same library `renderMarkdown.ts` uses elsewhere in this package). Plain-text
 * bodies render as React text content (auto-escaped, no sanitization needed).
 */
import * as React from 'react';
import { Badge, Button, Text, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import { ChatRegular, DocumentRegular, MailRegular } from '@fluentui/react-icons';
import DOMPurify from 'dompurify';
import { ChannelBadge } from './ChannelBadge';
import type { TimelineMessage } from '../CommunicationTimeline.types';

export interface IMessageRowProps {
  message: TimelineMessage;
  /** Reply-nesting depth from `buildTimeline` — rendered as left indentation. */
  depth: number;
  /** "Quote into message" action (task 063) — omit to hide the affordance. */
  onQuoteIntoMessage?: (message: TimelineMessage) => void;
  /** "Quote into email" action (task 063) — omit to hide the affordance. */
  onQuoteIntoEmail?: (message: TimelineMessage) => void;
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
  bodyHtml: {
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
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

export const MessageRow: React.FC<IMessageRowProps> = ({ message, depth, onQuoteIntoMessage, onQuoteIntoEmail }) => {
  const styles = useStyles();
  const cappedDepth = Math.min(Math.max(depth, 0), MAX_INDENT_DEPTH);

  const sanitizedHtml = React.useMemo(() => {
    if (message.bodyFormat !== 'html' || !message.body) return '';
    return DOMPurify.sanitize(message.body, { USE_PROFILES: { html: true } });
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
        <div className={styles.attachmentsRow}>
          {message.attachments.map(a => (
            <Badge key={a.id} appearance="outline" icon={<DocumentRegular />} size="small">
              {a.fileName ?? 'Attachment'}
            </Badge>
          ))}
        </div>
      )}
    </div>
  );
};

MessageRow.displayName = 'MessageRow';
