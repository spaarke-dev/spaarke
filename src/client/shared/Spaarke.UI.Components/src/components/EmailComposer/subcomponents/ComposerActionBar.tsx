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
import {
  Button,
  SplitButton,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItemRadio,
  Spinner,
  makeStyles,
  tokens,
  type MenuButtonProps,
} from '@fluentui/react-components';
import type { EmailComposerMode, EmailComposerMount } from '../EmailComposer.types';
import type { CommunicationSendMode } from '../../../services/communicationApi';

export interface IComposerActionBarProps {
  mount: EmailComposerMount;
  mode: EmailComposerMode;
  isSending: boolean;
  isSavingDraft: boolean;
  canSend: boolean;
  /** View mode only — whether the underlying record is still a Draft (enables Edit). */
  isDraftRecord?: boolean;
  /** Current send-from selection (drives the SplitButton caret menu). */
  sendMode?: CommunicationSendMode;
  /** True when the host lets the user choose the sender — renders the caret menu. */
  showSendModeChoice?: boolean;
  /** Called when the user picks a different sender from the caret menu. */
  onSendModeChange?: (value: CommunicationSendMode) => void;
  onSend: () => void;
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

const SEND_FROM_LABEL: Record<CommunicationSendMode, string> = {
  sharedMailbox: 'Send from Spaarke',
  user: 'Send from my mailbox',
};

export const ComposerActionBar: React.FC<IComposerActionBarProps> = ({
  mount,
  mode,
  isSending,
  isSavingDraft,
  canSend,
  isDraftRecord,
  sendMode = 'sharedMailbox',
  showSendModeChoice,
  onSendModeChange,
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
  const sendLabel = isSending ? (
    <span className={styles.spinnerRow}>
      <Spinner size="tiny" />
      Sending...
    </span>
  ) : (
    'Send'
  );

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

        {showSendModeChoice && onSendModeChange ? (
          <Menu
            positioning="below-end"
            checkedValues={{ sendFrom: [sendMode] }}
            onCheckedValueChange={(_e, data) => {
              const next = data.checkedItems[0] as CommunicationSendMode | undefined;
              if (next) onSendModeChange(next);
            }}
          >
            <MenuTrigger disableButtonEnhancement>
              {(triggerProps: MenuButtonProps) => (
                <SplitButton
                  menuButton={{ ...triggerProps, 'aria-label': 'Choose mailbox' }}
                  primaryActionButton={{ onClick: onSend, disabled: busy || !canSend }}
                  appearance="primary"
                  disabled={busy || !canSend}
                >
                  {sendLabel}
                </SplitButton>
              )}
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItemRadio name="sendFrom" value="sharedMailbox">
                  {SEND_FROM_LABEL.sharedMailbox}
                </MenuItemRadio>
                <MenuItemRadio name="sendFrom" value="user">
                  {SEND_FROM_LABEL.user}
                </MenuItemRadio>
              </MenuList>
            </MenuPopover>
          </Menu>
        ) : (
          <Button appearance="primary" onClick={onSend} disabled={busy || !canSend}>
            {sendLabel}
          </Button>
        )}
      </div>
    </div>
  );
};

ComposerActionBar.displayName = 'ComposerActionBar';
