/**
 * InternalOnlySwitch.tsx
 *
 * The one "Internal Only" control every compose surface shows (task 068; owner decision 2026-10-14).
 * Rendered by `<TimelineComposeBox/>`, `<EmailComposer/>` and the Conversation chat bar so the label,
 * the helper text and the locked state are defined once.
 *
 * Wording is the owner's and is not to be changed: the label is exactly "Internal Only", the optional
 * helper text is "Only people in your organization can see this".
 *
 * `locked` is for a surface that already KNOWS its target thread is Private: the switch shows on and is
 * disabled. It never reads Dataverse to learn that — the BFF floor (task 065) covers every case the
 * surface does not know.
 */
import * as React from 'react';
import { Switch, Text, makeStyles, tokens, useId } from '@fluentui/react-components';

export const INTERNAL_ONLY_LABEL = 'Internal Only';
export const INTERNAL_ONLY_HINT = 'Only people in your organization can see this';

export interface IInternalOnlySwitchProps {
  /** The user's choice. Ignored (shown on) while `locked`. */
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** The target thread is known to be Private: shown on and disabled. */
  locked?: boolean;
  /** The host is busy (sending) or read-only. */
  disabled?: boolean;
  /** Render the helper text beside the switch. Default `true`; the compact chat bar turns it off. */
  showHint?: boolean;
}

const useStyles = makeStyles({
  root: {
    display: 'inline-flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalS,
  },
  hint: {
    color: tokens.colorNeutralForeground3,
  },
});

export const InternalOnlySwitch: React.FC<IInternalOnlySwitchProps> = ({
  checked,
  onChange,
  locked = false,
  disabled = false,
  showHint = true,
}) => {
  const styles = useStyles();
  const hintId = useId('internal-only-hint');

  return (
    <div className={styles.root}>
      <Switch
        label={INTERNAL_ONLY_LABEL}
        checked={locked || checked}
        disabled={locked || disabled}
        onChange={(_e, data) => onChange(data.checked)}
        aria-describedby={showHint ? hintId : undefined}
      />
      {showHint && (
        <Text id={hintId} size={200} className={styles.hint}>
          {INTERNAL_ONLY_HINT}
        </Text>
      )}
    </div>
  );
};

InternalOnlySwitch.displayName = 'InternalOnlySwitch';
