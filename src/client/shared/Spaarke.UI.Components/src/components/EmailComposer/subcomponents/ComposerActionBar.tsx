/**
 * ComposerActionBar.tsx
 *
 * Rendered ONLY in `dialog` + `page` mounts — returns `null` on `inline`
 * (the wizard frame owns Send/Cancel navigation; task 020 constraint).
 *
 * Compose/reply/forward/draft modes: Send / Save Draft / Cancel.
 * View mode: Edit (only when the record is a Draft) / Reply / Forward / Close
 * (design §5.6.7).
 */
import * as React from 'react';
import { Button, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import type { EmailComposerMode, EmailComposerMount } from '../EmailComposer.types';

export interface IComposerActionBarProps {
  mount: EmailComposerMount;
  mode: EmailComposerMode;
  isSending: boolean;
  isSavingDraft: boolean;
  canSend: boolean;
  /** View mode only — whether the underlying record is still a Draft (enables Edit). */
  isDraftRecord?: boolean;
  onSend: () => void;
  onSaveDraft: () => void;
  onCancel: () => void;
  onEdit?: () => void;
  onReply?: () => void;
  onForward?: () => void;
}

const useStyles = makeStyles({
  bar: {
    display: 'flex',
    justifyContent: 'flex-end',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalM,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },
  spinnerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
});

export const ComposerActionBar: React.FC<IComposerActionBarProps> = ({
  mount,
  mode,
  isSending,
  isSavingDraft,
  canSend,
  isDraftRecord,
  onSend,
  onSaveDraft,
  onCancel,
  onEdit,
  onReply,
  onForward,
}) => {
  const styles = useStyles();

  if (mount === 'inline') return null;

  const busy = isSending || isSavingDraft;

  return (
    <div className={styles.bar} role="region" aria-label="Composer actions">
      {mode === 'view' ? (
        <>
          <Button appearance="secondary" onClick={onCancel}>
            Close
          </Button>
          {onReply && (
            <Button appearance="secondary" onClick={onReply}>
              Reply
            </Button>
          )}
          {onForward && (
            <Button appearance="secondary" onClick={onForward}>
              Forward
            </Button>
          )}
          {isDraftRecord && onEdit && (
            <Button appearance="primary" onClick={onEdit}>
              Edit
            </Button>
          )}
        </>
      ) : (
        <>
          <Button appearance="secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button appearance="secondary" onClick={onSaveDraft} disabled={busy}>
            {isSavingDraft ? (
              <span className={styles.spinnerRow}>
                <Spinner size="tiny" />
                Saving...
              </span>
            ) : (
              'Save Draft'
            )}
          </Button>
          <Button appearance="primary" onClick={onSend} disabled={busy || !canSend}>
            {isSending ? (
              <span className={styles.spinnerRow}>
                <Spinner size="tiny" />
                Sending...
              </span>
            ) : (
              'Send'
            )}
          </Button>
        </>
      )}
    </div>
  );
};

ComposerActionBar.displayName = 'ComposerActionBar';
