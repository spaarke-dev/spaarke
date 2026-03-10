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
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    maxWidth: '520px',
    width: '90vw',
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
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
      <DialogSurface className={styles.surface}>
        <DialogTitle
          action={
            <Button
              appearance="subtle"
              icon={<Dismiss24Regular />}
              aria-label="Close"
              onClick={onClose}
            />
          }
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
                  onChange={(e) => setSubject(e.target.value)}
                  placeholder="Email subject"
                  aria-label="Subject"
                  disabled={sending}
                />
              </Field>

              {/* Body */}
              <Field label={renderLabel('Message')}>
                <Textarea
                  value={body}
                  onChange={(e) => setBody(e.target.value)}
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
