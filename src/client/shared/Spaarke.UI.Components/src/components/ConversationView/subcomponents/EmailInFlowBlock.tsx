/**
 * EmailInFlowBlock.tsx
 *
 * Compact in-flow presentation of an EMAIL-type communication inside
 * `<ConversationView />` (task 021, FR-04). Email is a first-class in-flow
 * object that is visually DISTINCT from a chat bubble: instead of a
 * `MessageBubble`, an email renders as a compact block showing subject +
 * from + to, a SINGLE "Email" indicator (never one-per-recipient), and an
 * open-icon affordance that hands the row back to the host via `onOpen`.
 *
 * The host wires `onOpen` to open the extended `<SendEmailDialog />` (task 020
 * gave it `threadId` / `regarding` / `recordLink`) in view/detail for that
 * email — this component NEVER mounts the dialog itself. ConversationView has
 * `authenticatedFetch` / `bffBaseUrl` / `threadId` but NOT the `regarding` /
 * recipient-directory / user context the dialog needs, so mounting it here
 * would bloat the prop surface and break ADR-012's context-agnostic contract.
 * The callback seam mirrors task 023's `onPin` and ADR-012's `renderConversation`.
 *
 * The single "Email" indicator REUSES `ChannelBadge` (NFR-06) — one per block.
 * Fluent v9 only — `makeStyles` + `tokens`, semantic tokens exclusively (no
 * hardcoded colors); dark mode passes through the host `FluentProvider`
 * (ADR-021). The open-icon is a keyboard-operable, ARIA-labeled `Button` and
 * the block is a labeled region (NFR-05).
 */
import * as React from 'react';
import { Button, Text, Tooltip, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { OpenRegular } from '@fluentui/react-icons';
import { ChannelBadge } from '../../CommunicationTimeline/subcomponents/ChannelBadge';
import { PrivacyMarkers } from '../../CommunicationTimeline/subcomponents/PrivacyMarkers';
import type { TimelineMessage } from '../../CommunicationTimeline/CommunicationTimeline.types';

export interface IEmailInFlowBlockProps {
  /** The email-type `sprk_communication` row to render as a compact block. */
  message: TimelineMessage;
  /** Mine (`true`) → right-aligned. Others (`false`) → left-aligned. Mirrors the bubble alignment decision. */
  isOwn: boolean;
  /**
   * Fired when the user activates the open-icon. The host opens the extended
   * `<SendEmailDialog />` (task 020: `threadId` / `regarding` / `recordLink`)
   * in view/detail for this email — this component never mounts the dialog.
   */
  onOpen?: (message: TimelineMessage) => void;
}

const SUBJECT_FALLBACK = '(no subject)';
const FROM_FALLBACK = 'Unknown sender';
const TO_FALLBACK = '—';

/** Max characters of the body preview shown on the card (R3 UAT 2026-07-22 item 2). */
const PREVIEW_MAX_CHARS = 100;

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
  block: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    maxWidth: '85%',
    minWidth: 0,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderRadius: tokens.borderRadiusMedium,
    // Per-side longhands (griffel rejects the `border*` shorthands) — a thin
    // stroke plus the distinct background2 sets the compact email block apart
    // from the chat bubbles without any hardcoded color (ADR-021).
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
  },
  // Meta header ABOVE the card (§B UAT 2026-07-27 item 4) — mirrors
  // MessageBubble's `header`: the single "Email" pill + privacy markers + sender
  // + sent/received time sit OUTSIDE the bordered card so the email row matches
  // the chat-bubble layout. The row's align-items (own→right / other→left) plus
  // this maxWidth keep the header edge-aligned with the card below.
  header: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginBottom: tokens.spacingVerticalXXS,
    maxWidth: '85%',
  },
  senderLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground2,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    minWidth: 0,
  },
  headerTimestamp: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  headerSpacer: {
    flex: 1,
    minWidth: 0,
  },
  // Item 2 (R3 UAT 2026-07-22): larger, less-compressed type — subject reads at
  // base400, meta/snippet at base300 (was base200). Semantic tokens only (ADR-021).
  subject: {
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },
  metaLine: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
  },
  metaLabel: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  metaValue: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground2,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    minWidth: 0,
  },
  // ~100-char body preview (item 2) — clamped to two lines so a long snippet
  // never blows out the card height.
  snippet: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground2,
    wordBreak: 'break-word',
  },
});

/** Treats null/undefined/blank as "missing" so a whitespace-only server value still shows the fallback. */
function withFallback(value: string | null | undefined, fallback: string): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : fallback;
}

/**
 * ~100-char plain-text preview of the email body (item 2). Strips HTML tags from
 * html bodies and collapses whitespace so the snippet reads as running text, then
 * truncates on a character budget with an ellipsis. Returns '' when there's no body.
 */
function messagePreview(message: TimelineMessage, max = PREVIEW_MAX_CHARS): string {
  const raw = message.body ?? '';
  const text = (message.bodyFormat === 'html' ? raw.replace(/<[^>]*>/g, ' ') : raw).replace(/\s+/g, ' ').trim();
  if (text.length <= max) return text;
  return `${text.slice(0, max).trimEnd()}…`;
}

/**
 * "Sent/Received {date}" line (item 2), from `sentOn ?? createdOn`. The verb is
 * chosen from `sprk_direction` (outgoing → Sent, incoming → Received); when the
 * direction is unknown the bare date is shown. Returns null when no valid
 * timestamp is available so the line is simply omitted.
 */
function formatSentReceived(message: TimelineMessage): string | null {
  const raw = message.sentOn ?? message.createdOn;
  if (!raw) return null;
  const date = new Date(raw);
  if (Number.isNaN(date.getTime())) return null;
  const when = date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
  const verb = message.direction === 'incoming' ? 'Received ' : message.direction === 'outgoing' ? 'Sent ' : '';
  return `${verb}${when}`;
}

export const EmailInFlowBlock: React.FC<IEmailInFlowBlockProps> = ({ message, isOwn, onOpen }) => {
  const styles = useStyles();

  const subject = withFallback(message.subject, SUBJECT_FALLBACK);
  const from = withFallback(message.sender, FROM_FALLBACK);
  const recipients = (message.to ?? []).map(t => t.trim()).filter(Boolean);
  const to = recipients.length > 0 ? recipients.join(', ') : TO_FALLBACK;
  const preview = messagePreview(message);
  const sentReceived = formatSentReceived(message);

  const regionLabel = `Email: ${subject}, from ${from}`;

  return (
    <div className={mergeClasses(styles.row, isOwn ? styles.rowOwn : styles.rowOther)}>
      {/* Meta header OUTSIDE the card (§B UAT 2026-07-27 item 4) — mirrors
          MessageBubble: the SINGLE "Email" pill (never per-recipient, FR-04) +
          privacy markers + sender + sent/received time sit above the card, plus
          the open-icon affordance. */}
      <div className={styles.header}>
        <ChannelBadge channelType="email" />
        <PrivacyMarkers
          privilege={message.privilege}
          isInternalOnly={message.isInternalOnly}
          isPrivate={message.isPrivate}
        />
        <Text className={styles.senderLabel} title={from}>
          {from}
        </Text>
        {sentReceived && <Text className={styles.headerTimestamp}>{sentReceived}</Text>}
        <span className={styles.headerSpacer} />
        <Tooltip content="Open email" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<OpenRegular />}
            aria-label="Open email"
            onClick={() => onOpen?.(message)}
          />
        </Tooltip>
      </div>

      <div className={styles.block} role="group" aria-label={regionLabel}>
        <Text className={styles.subject}>{subject}</Text>

        <div className={styles.metaLine}>
          <Text className={styles.metaLabel}>To:</Text>
          <Text className={styles.metaValue} title={to}>
            {to}
          </Text>
        </div>

        {/* ~100-char body preview (item 2) — replaces the removed attachment list. */}
        {preview && <Text className={styles.snippet}>{preview}</Text>}
      </div>
    </div>
  );
};

EmailInFlowBlock.displayName = 'EmailInFlowBlock';
