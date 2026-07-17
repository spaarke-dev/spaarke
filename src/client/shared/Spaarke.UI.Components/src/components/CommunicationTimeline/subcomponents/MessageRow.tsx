/**
 * MessageRow.tsx
 *
 * One rendered row in the interleaved timeline: sender, timestamp, channel
 * badge, `sprk_body` (respecting `sprk_bodyformat`), attachment file cards,
 * and reply-nesting indentation (task 060, FR-10).
 *
 * HTML bodies come from persisted email content (external senders included)
 * and are UNTRUSTED — sanitized via DOMPurify before `dangerouslySetInnerHTML`
 * (same library `renderMarkdown.ts` uses elsewhere in this package). Plain-text
 * bodies render as React text content (auto-escaped, no sanitization needed).
 */
import * as React from 'react';
import { Badge, Text, makeStyles, tokens } from '@fluentui/react-components';
import { DocumentRegular } from '@fluentui/react-icons';
import DOMPurify from 'dompurify';
import { ChannelBadge } from './ChannelBadge';
import type { TimelineMessage } from '../CommunicationTimeline.types';

export interface IMessageRowProps {
  message: TimelineMessage;
  /** Reply-nesting depth from `buildTimeline` — rendered as left indentation. */
  depth: number;
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

export const MessageRow: React.FC<IMessageRowProps> = ({ message, depth }) => {
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
