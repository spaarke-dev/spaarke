/**
 * SendEmailDialog.tsx
 * Reusable email composition dialog with user lookup, subject, and body fields.
 *
 * Fully callback-based — consumer provides search and send implementations.
 * No service dependencies; works in both PCF controls and Code Page solutions.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │  Email Document                                              [X]    │
 *   │                                                                     │
 *   │  To *      [Search users...                             ]           │
 *   │                                                                     │
 *   │  Subject * [Document: Contract Agreement                ]           │
 *   │                                                                     │
 *   │  Message * [Dear Colleague,                              ]          │
 *   │            [Please find the following document...]                  │
 *   │                                                                     │
 *   │                                     [Cancel]  [Send]               │
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
  tokens,
} from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';
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
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // v1.1.56 — `width: '90vw'` was the binding width constraint, which
  // capped the surface below the `maxWidth` value on common laptop
  // viewports (90% of 1366 = 1229, which clips a passed maxWidth=1280).
  // Switched to `width: '100%'` so the surface always grows to its
  // maxWidth, matching FilePreviewDialog's pattern (which reliably
  // hits 1280px on every viewport). The Dialog's portal still constrains
  // the surface within the viewport, so no overflow on smaller screens.
  surface: {
    maxWidth: '520px',
    width: '100%',
  },
  // v1.1.56 — `form` is now a flex column with `flex: 1` so it fills
  // the surface when an explicit height is set. `minHeight: 0` lets
  // children with `flex: 1` shrink instead of pushing the form out of
  // the dialog (default flex behavior would otherwise force overflow).
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
    flex: 1,
    minHeight: 0,
  },
  // v1.1.56 — Message Field grows to fill remaining vertical space when
  // the surface has an explicit height; flat content layout otherwise.
  messageField: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
  },
  // v1.1.56 — Textarea inside the Message Field grows with the field.
  // `minHeight` keeps the writing area usable even on short modals.
  messageTextarea: {
    flex: 1,
    minHeight: '180px',
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
      <DialogSurface
        className={styles.surface}
        style={{ maxWidth, height, minHeight: height }}
      >
        <DialogTitle
          action={<Button appearance="subtle" icon={<Dismiss24Regular />} aria-label="Close" onClick={onClose} />}
        >
          Email Document
        </DialogTitle>

        <DialogBody>
          <DialogContent>
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

              {/* Body — v1.1.56: Field + Textarea flex-grow to fill any
                  explicit surface height (host passes height="85vh" on
                  the SemanticSearchControl). `rows` still provides a
                  sane default height when surface height is 'auto'. */}
              <Field label={renderLabel('Message')} className={styles.messageField}>
                <Textarea
                  className={styles.messageTextarea}
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

        <DialogActions>
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
