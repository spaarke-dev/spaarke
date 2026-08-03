/**
 * ComposerSendButton.tsx
 *
 * The primary Send control, relocated to the compose header's "From:" row
 * (Outlook-style — owner UAT 2026-08-03 item 1: Send sits in the address section
 * next to From:, not in a bottom action bar). A `SplitButton` whose caret menu
 * carries the send-from choice (Spaarke shared mailbox vs the user's mailbox)
 * when the host offers it; a plain primary `Button` when the sender is fixed.
 *
 * Extracted VERBATIM from the former `ComposerActionBar` Send rendering so the
 * send-from semantics are unchanged — only the placement moved. Fluent v9
 * semantic tokens only (ADR-021).
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
import type { CommunicationSendMode } from '../../../services/communicationApi';

export interface IComposerSendButtonProps {
  isSending: boolean;
  canSend: boolean;
  /** Current send-from selection (drives the SplitButton caret menu). */
  sendMode?: CommunicationSendMode;
  /** True when the host lets the user choose the sender — renders the caret menu. */
  showSendModeChoice?: boolean;
  /** Called when the user picks a different sender from the caret menu. */
  onSendModeChange?: (value: CommunicationSendMode) => void;
  onSend: () => void;
}

const useStyles = makeStyles({
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

export const ComposerSendButton: React.FC<IComposerSendButtonProps> = ({
  isSending,
  canSend,
  sendMode = 'sharedMailbox',
  showSendModeChoice,
  onSendModeChange,
  onSend,
}) => {
  const styles = useStyles();
  const busy = isSending;
  const sendLabel = isSending ? (
    <span className={styles.spinnerRow}>
      <Spinner size="tiny" />
      Sending...
    </span>
  ) : (
    'Send'
  );

  if (showSendModeChoice && onSendModeChange) {
    return (
      <Menu
        positioning="below-start"
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
    );
  }

  return (
    <Button appearance="primary" onClick={onSend} disabled={busy || !canSend}>
      {sendLabel}
    </Button>
  );
};

ComposerSendButton.displayName = 'ComposerSendButton';
