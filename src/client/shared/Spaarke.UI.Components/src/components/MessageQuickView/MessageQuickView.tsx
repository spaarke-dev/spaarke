/**
 * MessageQuickView - lightweight message-preview popover (task 023, FR-05).
 *
 * A Fluent v9 Popover that shows a COMPACT preview of a single message —
 * body truncated to 200 characters; for an email it also surfaces
 * to / from / date / subject — plus an "open→pin" action that jumps the
 * conversation to the full message and flashes a transient highlight.
 *
 * Popover MECHANICS (trigger, positioning, focus trap, Esc-dismiss, ARIA) are
 * COPIED from `AiSummaryPopover.tsx` — the proven popover pattern in this lib
 * (NFR-06); only the CONTENT differs (message preview + pin, not AI summary).
 * The message shape REUSES `TimelineMessage` from `CommunicationTimeline`
 * (NFR-06) — no invented message type. Email-only fields the timeline type
 * does not carry (`to`, `subject`) are accepted as optional props.
 *
 * Context-agnostic (ADR-012): no `Xrm`/`ComponentFramework`. There is no I/O —
 * the message is passed in as a prop; the "open→pin" jump is delegated to the
 * host via the `onPin` callback (the host wires it to a `ConversationView`
 * ref's `scrollToMessage`), so this popover is never hard-coupled to a
 * specific `ConversationView` instance.
 *
 * Fluent v9 + semantic tokens only; light + dark pass through the host
 * `FluentProvider` (ADR-021) — no hardcoded colors.
 *
 * @see AiSummaryPopover for the popover pattern this copies
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from 'react';
import { useCallback, useState } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  shorthands,
} from '@fluentui/react-components';
import { MailRegular, ChatRegular, ArrowCircleDownRegular } from '@fluentui/react-icons';
import type { TimelineMessage } from '../CommunicationTimeline/CommunicationTimeline.types';

/** Max characters of body shown in the preview before truncation (FR-05). Module-private. */
const MESSAGE_QUICK_VIEW_MAX_CHARS = 200;

/**
 * Props for `MessageQuickView`.
 */
export interface IMessageQuickViewProps {
  /** The trigger element that opens the popover (typically a Button). */
  trigger: React.ReactElement;
  /**
   * The message to preview. REUSES the timeline `TimelineMessage` shape
   * (NFR-06). `null`/`undefined` renders a graceful error state.
   */
  message: TimelineMessage | null | undefined;
  /**
   * Email `To` recipients. Not carried on `TimelineMessage`, so supplied
   * separately by the host when known. Only shown for email-channel messages.
   */
  to?: string | string[] | null;
  /**
   * Email `Subject`. Not carried on `TimelineMessage`, so supplied separately
   * by the host when known. Only shown for email-channel messages.
   */
  subject?: string | null;
  /**
   * "open→pin" action. Invoked with `message.id` when the user activates the
   * jump-to-message control. The host wires this to a `ConversationView`
   * ref's `scrollToMessage(id)`. When omitted, the pin control is hidden.
   */
  onPin?: (messageId: string) => void;
  /** Popover positioning relative to trigger. Default: "after". */
  positioning?: 'above' | 'below' | 'before' | 'after';
  /** Whether to show the arrow. Default: true. */
  withArrow?: boolean;
}

// ---------------------------------------------------------------------------
// Pure helpers (no I/O — ADR-012)
// ---------------------------------------------------------------------------

/** Strips HTML tags + collapses whitespace so the preview shows words, not markup. */
function toPlainText(message: TimelineMessage): string {
  const raw = message.body ?? '';
  const stripped = message.bodyFormat === 'html' ? raw.replace(/<[^>]*>/g, ' ') : raw;
  return stripped.replace(/\s+/g, ' ').trim();
}

/** Truncates to `max` characters, appending an ellipsis only when it actually cut text. Module-private. */
function truncatePreview(text: string, max: number = MESSAGE_QUICK_VIEW_MAX_CHARS): string {
  if (text.length <= max) return text;
  return `${text.slice(0, max)}…`;
}

/** Locale date/time for the email `Date` row; empty string when unresolvable (graceful degrade). */
function formatDate(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Joins a to-field that may be a string or an array into one display string. */
function joinRecipients(to: string | string[] | null | undefined): string {
  if (!to) return '';
  return Array.isArray(to) ? to.filter(Boolean).join(', ') : to;
}

// ---------------------------------------------------------------------------
// Styles (semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    width: '360px',
    maxHeight: '400px',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
  },
  fields: {
    display: 'grid',
    gridTemplateColumns: 'auto 1fr',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXXS,
  },
  fieldLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
  fieldValue: {
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },
  body: {
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    color: tokens.colorNeutralForeground1,
  },
  emptyBody: {
    color: tokens.colorNeutralForeground3,
  },
  errorState: {
    color: tokens.colorNeutralForeground3,
    ...shorthands.padding(tokens.spacingVerticalS),
  },
  footerRow: {
    display: 'flex',
    justifyContent: 'flex-end',
    paddingTop: tokens.spacingVerticalXS,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },
});

// ---------------------------------------------------------------------------
// Field row
// ---------------------------------------------------------------------------

const FieldRow: React.FC<{ label: string; value: string; fallback: string }> = ({ label, value, fallback }) => {
  const styles = useStyles();
  return (
    <React.Fragment>
      <Text className={styles.fieldLabel} size={200}>
        {label}
      </Text>
      <Text className={styles.fieldValue} size={200}>
        {value || fallback}
      </Text>
    </React.Fragment>
  );
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const MessageQuickView: React.FC<IMessageQuickViewProps> = ({
  trigger,
  message,
  to,
  subject,
  onPin,
  positioning = 'after',
  withArrow = true,
}) => {
  const styles = useStyles();
  // Controlled so the "open→pin" action can dismiss the popover as it jumps.
  const [open, setOpen] = useState(false);

  const handleOpenChange = useCallback((_ev: unknown, data: { open: boolean }) => {
    setOpen(data.open);
  }, []);

  const handlePin = useCallback(() => {
    if (message && onPin) onPin(message.id);
    setOpen(false); // jump + close; focus returns to the trigger (Fluent Popover)
  }, [message, onPin]);

  const isEmail = message?.channelType === 'email';
  const plainBody = message ? toPlainText(message) : '';
  const preview = truncatePreview(plainBody);
  const fromValue = message ? (message.senderName ?? message.sender ?? '') : '';
  const toValue = joinRecipients(to);
  const dateValue = formatDate(message?.sentOn);
  const subjectValue = subject ?? '';

  return (
    <Popover
      open={open}
      onOpenChange={handleOpenChange}
      positioning={positioning}
      withArrow={withArrow}
      trapFocus
    >
      <PopoverTrigger disableButtonEnhancement>{trigger}</PopoverTrigger>
      <PopoverSurface className={styles.surface} aria-label="Message preview">
        {!message ? (
          <Text role="alert" className={styles.errorState}>
            Message preview unavailable.
          </Text>
        ) : (
          <React.Fragment>
            <div className={styles.headerRow}>
              {isEmail ? <MailRegular aria-hidden="true" /> : <ChatRegular aria-hidden="true" />}
              <Text as="span">{isEmail ? 'Email preview' : 'Message preview'}</Text>
            </div>

            {isEmail && (
              <div className={styles.fields}>
                <FieldRow label="From" value={fromValue} fallback="Unknown sender" />
                <FieldRow label="To" value={toValue} fallback="—" />
                <FieldRow label="Date" value={dateValue} fallback="Unknown date" />
                <FieldRow label="Subject" value={subjectValue} fallback="(no subject)" />
              </div>
            )}

            {preview ? (
              <Text className={styles.body} size={300}>
                {preview}
              </Text>
            ) : (
              <Text className={styles.emptyBody} size={300} italic>
                No preview available.
              </Text>
            )}

            {onPin && (
              <div className={styles.footerRow}>
                <Button
                  appearance="primary"
                  size="small"
                  icon={<ArrowCircleDownRegular />}
                  onClick={handlePin}
                  aria-label="Go to message in conversation"
                >
                  Go to message
                </Button>
              </div>
            )}
          </React.Fragment>
        )}
      </PopoverSurface>
    </Popover>
  );
};

MessageQuickView.displayName = 'MessageQuickView';

export default MessageQuickView;
