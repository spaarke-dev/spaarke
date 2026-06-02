/**
 * SendEmailDialog.tsx
 * Reusable email composition dialog with user lookup, subject, and body fields.
 *
 * Fully callback-based — consumer provides search and send implementations.
 * No service dependencies; works in both PCF controls and Code Page solutions.
 *
 * Domain-agnostic: title, subject, and body are all consumer-supplied props,
 * so the same dialog can compose document emails, matter updates, invoice
 * notifications, or any other recipient-lookup-plus-message scenario.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │  {title}                                                            │
 *   │                                                                     │
 *   │  To *      [Search users...                             ]           │
 *   │                                                                     │
 *   │  Subject * [Document: Contract Agreement                ]           │
 *   │                                                                     │
 *   │  Message * [Dear Colleague,                              ]          │
 *   │            [Please find the following document...]                  │
 *   │                                                                     │
 *   │ ─────────────────────────────────────────────[Cancel]  [Send]──── │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * Constraints:
 *   - Fluent v9: Dialog, Input, Textarea, Field, Button, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Field,
  Input,
  Textarea,
  Button,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
// v1.1.59 — Dismiss24Regular import removed alongside the title-bar
// X close button (per UAT request for cross-modal consistency).
import { LookupField } from '../LookupField/LookupField';
import type { ILookupItem } from '../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Payload delivered to the onSend callback. */
export interface ISendEmailPayload {
  /** Selected recipient. */
  to: ILookupItem;
  /** Email subject line. */
  subject: string;
  /** Email body text. */
  body: string;
}

/** Props for SendEmailDialog. */
export interface ISendEmailDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Called when the dialog should close. */
  onClose: () => void;
  /** Pre-populated subject line. */
  defaultSubject?: string;
  /** Pre-populated email body. */
  defaultBody?: string;
  /** Async user search for the To field. */
  onSearchUsers: (query: string) => Promise<ILookupItem[]>;
  /** Called when user clicks Send. Consumer handles the BFF call. */
  onSend: (payload: ISendEmailPayload) => Promise<void>;
  /**
   * Override the dialog surface maxWidth. Defaults to `'520px'`.
   *
   * Pass a larger value (e.g. `'720px'`) when launching from inside a
   * wide host dialog so the email composer has visual presence over the
   * host. Backward-compatible: omitting the prop preserves the original
   * 520px sizing used by existing consumers.
   *
   * @since v1.1.52 (SemanticSearchControl UAT polish round 7)
   */
  maxWidth?: string;

  /**
   * Override the dialog surface height. Defaults to `'auto'` (content-sized,
   * preserves original consumer behavior). Pass a viewport-relative value
   * like `'85vh'` to give the composer a tall presence matching a sibling
   * host dialog (FilePreviewDialog uses `85vh`). When set, the Message
   * textarea grows to fill the available vertical space.
   *
   * @since v1.1.56 (SemanticSearchControl UAT polish round 11)
   */
  height?: string;

  /**
   * Dialog title text shown in the DialogTitle slot. Defaults to
   * `'Email Document'` for backward compatibility with the three
   * original consumers (SemanticSearchControl, SpeDocumentViewer,
   * LegalWorkspace FilePreview) which all email a single document.
   *
   * Override for other domains — e.g. `'Email Matter'`,
   * `'Email Selected Records'`, `'Send Update'` — so the same
   * dialog can be reused anywhere an email-with-recipient-lookup
   * composer is needed.
   *
   * @since v1.1.60 (SemanticSearchControl UAT polish round 15)
   */
  title?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    maxWidth: '520px',
    width: '100%',
    // v1.1.58 — when the consumer passes an explicit `height` (e.g. '85vh'),
    // the surface needs to be a flex column so DialogBody can flex-grow
    // to fill it. Fluent v9 DialogSurface is already display: flex by
    // default; we just ensure the direction is column.
    display: 'flex',
    flexDirection: 'column',
  },
  // v1.1.58 — DialogBody is the inner region that holds Title +
  // Content + Actions. To make Content grow and Actions pin to the
  // bottom, DialogBody must flex-grow within the surface AND lay out
  // its own children as a flex column.
  dialogBody: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  // v1.1.58 — DialogContent now flex-grows within DialogBody and
  // scrolls internally when content overflows (so the form stays
  // visible and the footer stays anchored).
  dialogContent: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
    overflowY: 'auto',
  },
  // v1.1.58 — DialogActions becomes a real footer: visible top
  // border, right-aligned button cluster, anchored to the bottom of
  // the DialogBody by being the last grid/flex row after the
  // grow-to-fill DialogContent.
  dialogActions: {
    flexShrink: 0,
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    ...shorthands.borderTop(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
  },
  // v1.1.58 — `form` fills the DialogContent so the Message Field can
  // flex-grow to take all remaining vertical space.
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
    flex: 1,
    minHeight: 0,
  },
  messageField: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
  },
  // v1.1.59 — the descendant selector `& > textarea` (v1.1.58) didn't
  // override Fluent v9 Textarea's inner-element height — UAT confirmed
  // the inner <textarea> stayed at its `rows` natural height (~260px
  // on rows=10). Switched to the SLOT-PROP approach:
  // `<Textarea textarea={{ className: styles.messageTextareaInner }}>`
  // applies the className directly to the inner element via Fluent's
  // slot rendering, which bypasses Fluent's internal styling entirely.
  messageTextarea: {
    flex: 1,
    minHeight: '180px',
    display: 'flex',
    flexDirection: 'column',
  },
  // v1.1.59 — applied to the inner `<textarea>` via slot prop.
  // `height: 100%` + `flex: 1` makes it fill the messageTextarea
  // wrapper (which itself fills the form via flex:1). minHeight:0
  // lets it shrink without forcing overflow.
  messageTextareaInner: {
    flex: 1,
    minHeight: 0,
    height: '100%',
    boxSizing: 'border-box',
  },
  labelRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  requiredMark: {
    color: tokens.colorPaletteRedForeground1,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
    paddingTop: tokens.spacingVerticalXS,
  },
  spinnerOverlay: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SendEmailDialog: React.FC<ISendEmailDialogProps> = ({
  open,
  onClose,
  defaultSubject,
  defaultBody,
  onSearchUsers,
  onSend,
  maxWidth = '520px',
  height = 'auto',
  title = 'Email Document',
}) => {
  const styles = useStyles();

  // Form state
  const [selectedUser, setSelectedUser] = React.useState<ILookupItem | null>(null);
  const [subject, setSubject] = React.useState('');
  const [body, setBody] = React.useState('');
  const [sending, setSending] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Reset form when dialog opens with new defaults
  React.useEffect(() => {
    if (open) {
      setSelectedUser(null);
      setSubject(defaultSubject ?? '');
      setBody(defaultBody ?? '');
      setSending(false);
      setError(null);
    }
  }, [open, defaultSubject, defaultBody]);

  // ── Handlers ──────────────────────────────────────────────────────────

  const handleSend = React.useCallback(async () => {
    if (!selectedUser) return;
    if (!subject.trim()) return;

    setSending(true);
    setError(null);

    try {
      await onSend({ to: selectedUser, subject: subject.trim(), body });
      onClose();
    } catch (err) {
      console.error('[SendEmailDialog] Send failed:', err);
      setError(err instanceof Error ? err.message : 'Failed to send email. Please try again.');
    } finally {
      setSending(false);
    }
  }, [selectedUser, subject, body, onSend, onClose]);

  const canSend = !!selectedUser && !!subject.trim() && !sending;

  const renderLabel = (text: string, required?: boolean): React.ReactElement => (
    <span className={styles.labelRow}>
      {text}
      {required && (
        <span aria-hidden="true" className={styles.requiredMark}>
          {' *'}
        </span>
      )}
    </span>
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(_, data) => {
        if (!data.open) onClose();
      }}
    >
      {/* v1.1.57 — Inline style now sets BOTH `height` and `minHeight`.
          Fluent v9 DialogSurface internally applies `block-size:
          fit-content` (or equivalent) at a specificity that overrides
          our inline `height` alone — empirically observed in v1.1.56
          UAT: surface remained content-sized (540px) even with
          `style={{ height: '85vh' }}`. Adding `minHeight` forces the
          surface to grow to at least the requested height regardless
          of Fluent's content-sizing rule, because `min-height`
          semantics override `block-size: fit-content`.
          Default of 'auto' produces { height: 'auto', minHeight: 'auto' }
          which is a no-op for back-compat consumers (LegalWorkspace +
          SpeDocumentViewer). */}
      <DialogSurface className={styles.surface} style={{ maxWidth, height, minHeight: height }}>
        {/* v1.1.59 — title-bar X close icon removed per UAT.
            The Cancel button in the footer is the single close
            affordance, matching FilePreviewDialog's pattern
            (v1.1.46) for consistency across our shared modals.
            v1.1.60 — title text is now consumer-configurable via
            the `title` prop (default 'Email Document' for back-
            compat) so the dialog can be reused for non-document
            email scenarios. */}
        <DialogTitle>{title}</DialogTitle>

        <DialogBody className={styles.dialogBody}>
          <DialogContent className={styles.dialogContent}>
            <div className={styles.form}>
              {/* To — user lookup */}
              <LookupField
                label="To"
                required
                placeholder="Search users..."
                value={selectedUser}
                onChange={setSelectedUser}
                onSearch={onSearchUsers}
                minSearchLength={2}
              />

              {/* Subject */}
              <Field label={renderLabel('Subject', true)} required>
                <Input
                  value={subject}
                  onChange={e => setSubject(e.target.value)}
                  placeholder="Email subject"
                  aria-label="Subject"
                  disabled={sending}
                />
              </Field>

              {/* Body — v1.1.59: the inner <textarea> is styled via the
                  `textarea` slot prop (NOT a descendant selector on the
                  wrapper className) so Fluent v9's slot rendering
                  applies our class directly to the inner element. This
                  is the only reliable way to override Fluent's
                  rows-based natural height on the inner textarea. */}
              <Field label={renderLabel('Message')} className={styles.messageField}>
                <Textarea
                  className={styles.messageTextarea}
                  textarea={{ className: styles.messageTextareaInner }}
                  value={body}
                  onChange={e => setBody(e.target.value)}
                  placeholder="Compose your message..."
                  rows={10}
                  resize="vertical"
                  aria-label="Message body"
                  disabled={sending}
                />
              </Field>

              {/* Error message */}
              {error && (
                <Text size={200} className={styles.errorText}>
                  {error}
                </Text>
              )}
            </div>
          </DialogContent>
        </DialogBody>

        <DialogActions className={styles.dialogActions}>
          <Button appearance="secondary" onClick={onClose} disabled={sending}>
            Cancel
          </Button>
          <Button appearance="primary" onClick={handleSend} disabled={!canSend}>
            {sending ? (
              <span className={styles.spinnerOverlay}>
                <Spinner size="tiny" />
                Sending...
              </span>
            ) : (
              'Send'
            )}
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

SendEmailDialog.displayName = 'SendEmailDialog';
