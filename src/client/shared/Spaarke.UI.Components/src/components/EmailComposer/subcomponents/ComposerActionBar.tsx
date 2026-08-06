/**
 * ComposerActionBar.tsx
 *
 * Rendered ONLY in `dialog` + `page` mounts — returns `null` on `inline`
 * (the wizard frame owns Send/Cancel navigation; task 020 constraint).
 *
 * Compose/reply/forward/draft modes: Cancel / Save Draft / Send. The Send button
 * is a SplitButton whose caret menu carries the "send from" choice (Spaarke shared
 * mailbox vs the user's mailbox) — folding the former standalone SendModeRadio into
 * the primary action per the owner UAT mockup (2026-07-22). When the host fixes
 * `sendMode` (no choice offered), Send renders as a plain primary Button.
 * View mode: Edit (only when the record is a Draft) / Reply / Forward / Close
 * (design §5.6.7).
 */
import * as React from 'react';
import { Button, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import type { EmailComposerMode, EmailComposerMount } from '../EmailComposer.types';

export interface IComposerActionBarProps {
  mount: EmailComposerMount;
  mode: EmailComposerMode;
  /** Drives the busy state that disables Cancel / Save Draft while a send is in flight. */
  isSending: boolean;
  isSavingDraft: boolean;
  /** View mode only — whether the underlying record is still a Draft (enables Edit). */
  isDraftRecord?: boolean;
  onSaveDraft: () => void;
  onCancel: () => void;
  onEdit?: () => void;
  onReply?: () => void;
  onForward?: () => void;
}

const useStyles = makeStyles({
  // Cancel on the LEFT, Save Draft + Send on the RIGHT (owner UAT 2026-07-22 #7).
  bar: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalM,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },
  rightGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
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
  isDraftRecord,
  onSaveDraft,
  onCancel,
  onEdit,
  onReply,
  onForward,
}) => {
  const styles = useStyles();

  if (mount === 'inline') return null;

  const busy = isSending || isSavingDraft;

  if (mode === 'view') {
    return (
      <div className={styles.bar} role="region" aria-label="Composer actions">
        <Button appearance="secondary" onClick={onCancel}>
          Close
        </Button>
        <div className={styles.rightGroup}>
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
        </div>
      </div>
    );
  }

  // Send moved to the compose header's From row (owner UAT 2026-08-03 item 1 —
  // `ComposerSendButton`). This bar now owns Cancel (left) + Save Draft (right) only.
  return (
    <div className={styles.bar} role="region" aria-label="Composer actions">
      <Button appearance="secondary" onClick={onCancel} disabled={busy}>
        Cancel
      </Button>
      <div className={styles.rightGroup}>
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
      </div>
    </div>
  );
};

ComposerActionBar.displayName = 'ComposerActionBar';
