/**
 * SendModeRadio.tsx
 *
 * Rendered ONLY when the host leaves `sendMode` unset in `IEmailComposerProps`
 * (i.e. the host allows the user to choose) — the engine itself decides
 * whether to mount this component (design §5.6.5); this component just
 * renders the two-option choice once asked to.
 *
 * Defaults to `'sharedMailbox'` per `.claude/patterns/api/send-email-integration.md`
 * ("Default sender: fromMailbox: null uses shared mailbox").
 */
import * as React from 'react';
import { Radio, RadioGroup, Label, makeStyles, tokens } from '@fluentui/react-components';
import type { CommunicationSendMode } from '../../../services/communicationApi';

export interface ISendModeRadioProps {
  value: CommunicationSendMode;
  onChange: (value: CommunicationSendMode) => void;
  disabled?: boolean;
}

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
});

export const SendModeRadio: React.FC<ISendModeRadioProps> = ({ value, onChange, disabled }) => {
  const styles = useStyles();

  return (
    <div className={styles.wrapper} role="group" aria-label="Send from">
      <Label>Send from</Label>
      <RadioGroup
        value={value}
        onChange={(_, data) => onChange(data.value as CommunicationSendMode)}
        disabled={disabled}
        layout="horizontal"
      >
        <Radio value="sharedMailbox" label="Send from Spaarke" />
        <Radio value="user" label="Send from my mailbox" />
      </RadioGroup>
    </div>
  );
};

SendModeRadio.displayName = 'SendModeRadio';
