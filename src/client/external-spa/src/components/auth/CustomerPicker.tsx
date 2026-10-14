/**
 * CustomerPicker — choose which customer's backend to work in (task 041, spec FR-17).
 *
 * Shown by the bootstrap (main.tsx) only when MORE THAN ONE backend is known for the signed-in plane and none is
 * active (`resolveBackendSelection` → `pick`). The options are backends the directory / dev config returned; the
 * picker only reports which offered key was chosen (`chooseBackend` re-checks it against the offered list). It shows
 * display names only — never a URL or scope.
 *
 * Mirrors the RealmChooser landing (same centred card, Title2 + lead + large buttons). Fluent v9 primitives with
 * semantic tokens only (ADR-021: light / dark / Teams correct, no hard-coded colours).
 */
import * as React from 'react';
import { Button, Text, Title2, makeStyles, tokens } from '@fluentui/react-components';
import { Building24Regular } from '@fluentui/react-icons';

export interface CustomerPickerOption {
  customerKey: string;
  displayName: string;
}

export interface CustomerPickerProps {
  /** The backends to choose from (from the directory / dev config). */
  options: readonly CustomerPickerOption[];
  /** Invoked with the chosen option's customer key. */
  onChoose: (customerKey: string) => void;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    width: '100%',
    padding: tokens.spacingHorizontalXXL,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalL,
    maxWidth: '26rem',
    textAlign: 'center',
  },
  lead: {
    color: tokens.colorNeutralForeground3,
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    width: '100%',
    alignItems: 'center',
  },
  option: {
    minWidth: '16rem',
  },
});

export const CustomerPicker: React.FC<CustomerPickerProps> = ({ options, onChoose }) => {
  const s = useStyles();
  return (
    <div className={s.root}>
      <div className={s.card}>
        <Title2>Choose an organization</Title2>
        <Text className={s.lead} block>
          You have access to more than one organization in Spaarke. Choose the one to open.
        </Text>
        <div className={s.list} role="list">
          {options.map(option => (
            <div key={option.customerKey} role="listitem">
              <Button
                className={s.option}
                appearance="secondary"
                size="large"
                icon={<Building24Regular />}
                onClick={() => onChoose(option.customerKey)}
              >
                {option.displayName}
              </Button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};

export default CustomerPicker;
