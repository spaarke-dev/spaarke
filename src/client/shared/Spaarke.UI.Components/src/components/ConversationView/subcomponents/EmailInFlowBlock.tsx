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
import { MessageAttachments } from '../../CommunicationTimeline/subcomponents/MessageAttachments';
import type { TimelineAttachment, TimelineMessage } from '../../CommunicationTimeline/CommunicationTimeline.types';

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
  /**
   * Open/preview/download an email attachment via the existing SPE
   * document-viewer path (task 042 / FR-20). Threaded from `ConversationView`;
   * the host mounts `<RichFilePreviewDialog />`. Omit it and attachments render
   * as passive chips.
   */
  onOpenAttachment?: (attachment: TimelineAttachment, message: TimelineMessage) => void;
}

const SUBJECT_FALLBACK = '(no subject)';
const FROM_FALLBACK = 'Unknown sender';
const TO_FALLBACK = '—';

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
    gap: tokens.spacingVerticalXXS,
    maxWidth: '75%',
    minWidth: 0,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
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
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  headerSpacer: {
    flex: 1,
    minWidth: 0,
  },
  subject: {
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
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  metaValue: {
    color: tokens.colorNeutralForeground2,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    minWidth: 0,
  },
  attachmentsRow: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
  },
});

/** Treats null/undefined/blank as "missing" so a whitespace-only server value still shows the fallback. */
function withFallback(value: string | null | undefined, fallback: string): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : fallback;
}

export const EmailInFlowBlock: React.FC<IEmailInFlowBlockProps> = ({ message, isOwn, onOpen, onOpenAttachment }) => {
  const styles = useStyles();

  const subject = withFallback(message.subject, SUBJECT_FALLBACK);
  const from = withFallback(message.sender, FROM_FALLBACK);
  const recipients = (message.to ?? []).map(t => t.trim()).filter(Boolean);
  const to = recipients.length > 0 ? recipients.join(', ') : TO_FALLBACK;

  const regionLabel = `Email: ${subject}, from ${from}`;

  return (
    <div className={mergeClasses(styles.row, isOwn ? styles.rowOwn : styles.rowOther)}>
      <div className={styles.block} role="group" aria-label={regionLabel}>
        <div className={styles.headerRow}>
          {/* The SINGLE "Email" indicator — one per block, never per-recipient (FR-04). */}
          <ChannelBadge channelType="email" />
          <PrivacyMarkers
            privilege={message.privilege}
            isInternalOnly={message.isInternalOnly}
            isPrivate={message.isPrivate}
          />
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

        <Text className={styles.subject}>{subject}</Text>

        <div className={styles.metaLine}>
          <Text size={200} className={styles.metaLabel}>
            From:
          </Text>
          <Text size={200} className={styles.metaValue} title={from}>
            {from}
          </Text>
        </div>

        <div className={styles.metaLine}>
          <Text size={200} className={styles.metaLabel}>
            To:
          </Text>
          <Text size={200} className={styles.metaValue} title={to}>
            {to}
          </Text>
        </div>

        {message.attachments.length > 0 && (
          <MessageAttachments
            className={styles.attachmentsRow}
            attachments={message.attachments}
            message={message}
            onOpenAttachment={onOpenAttachment}
          />
        )}
      </div>
    </div>
  );
};

EmailInFlowBlock.displayName = 'EmailInFlowBlock';
