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
import { Switch, Text, Tooltip, makeStyles, mergeClasses, shorthands, tokens, useId } from '@fluentui/react-components';

export const INTERNAL_ONLY_LABEL = 'Internal Only';
export const INTERNAL_ONLY_HINT = 'Only people in your organization can see this';
/** The "not internal" option of the tags variant (owner 2026-10-14: "(External) | (Internal Only)"). */
export const EXTERNAL_LABEL = 'External';
export const EXTERNAL_HINT = 'People outside your organization with access to this record can see this';

export interface IInternalOnlySwitchProps {
  /** The user's choice. Ignored (shown on) while `locked`. */
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** The target thread is known to be Private: shown on and disabled. */
  locked?: boolean;
  /** The host is busy (sending) or read-only. */
  disabled?: boolean;
  /** Render the helper text beside the switch. Default `true`; the compact chat bar turns it off. Switch variant only. */
  showHint?: boolean;
  /**
   * `switch` (default): a Fluent Switch labelled "Internal Only".
   * `tags` (owner 2026-10-14, Messages modal): two pill options, "External" (green when selected) and
   * "Internal Only" (red when selected), as a radio group. The hints are the pills' tooltips.
   */
  variant?: 'switch' | 'tags';
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
  tagGroup: {
    display: 'inline-flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
  tag: {
    display: 'inline-flex',
    alignItems: 'center',
    height: '24px',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusCircular,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground3,
    fontFamily: tokens.fontFamilyBase,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    lineHeight: tokens.lineHeightBase200,
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outlineWidth: tokens.strokeWidthThick,
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  tagExternalOn: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    ...shorthands.borderColor(tokens.colorPaletteGreenBorder2),
    color: tokens.colorPaletteGreenForeground2,
    fontWeight: tokens.fontWeightSemibold,
    ':hover': { backgroundColor: tokens.colorPaletteGreenBackground2 },
  },
  tagInternalOn: {
    backgroundColor: tokens.colorPaletteRedBackground2,
    ...shorthands.borderColor(tokens.colorPaletteRedBorder2),
    color: tokens.colorPaletteRedForeground2,
    fontWeight: tokens.fontWeightSemibold,
    ':hover': { backgroundColor: tokens.colorPaletteRedBackground2 },
  },
  tagDisabled: {
    cursor: 'not-allowed',
    opacity: 0.6,
  },
});

export const InternalOnlySwitch: React.FC<IInternalOnlySwitchProps> = ({
  checked,
  onChange,
  locked = false,
  disabled = false,
  showHint = true,
  variant = 'switch',
}) => {
  const styles = useStyles();
  const hintId = useId('internal-only-hint');

  if (variant === 'tags') {
    return <InternalOnlyTags checked={locked || checked} onChange={onChange} locked={locked} disabled={disabled} />;
  }

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

/**
 * The tags variant: a two-option radio group (WAI-ARIA radio pattern - one tab stop on the selected option,
 * arrow keys move and select). `locked` keeps "Internal Only" selected and "External" unavailable.
 */
const InternalOnlyTags: React.FC<{
  checked: boolean;
  onChange: (checked: boolean) => void;
  locked: boolean;
  disabled: boolean;
}> = ({ checked, onChange, locked, disabled }) => {
  const styles = useStyles();
  const externalRef = React.useRef<HTMLButtonElement>(null);
  const internalRef = React.useRef<HTMLButtonElement>(null);

  const select = (internal: boolean) => {
    if (disabled || (locked && !internal)) return;
    if (internal !== checked) onChange(internal);
    (internal ? internalRef : externalRef).current?.focus();
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLButtonElement>) => {
    if (['ArrowLeft', 'ArrowUp', 'ArrowRight', 'ArrowDown'].includes(e.key)) {
      e.preventDefault();
      select(!checked);
    }
  };

  const option = (internal: boolean) => {
    const selected = internal === checked;
    const unavailable = disabled || (locked && !internal);
    return (
      <Tooltip content={internal ? INTERNAL_ONLY_HINT : EXTERNAL_HINT} relationship="description">
        <button
          ref={internal ? internalRef : externalRef}
          type="button"
          role="radio"
          aria-checked={selected}
          aria-disabled={unavailable || undefined}
          tabIndex={selected ? 0 : -1}
          className={mergeClasses(
            styles.tag,
            selected && (internal ? styles.tagInternalOn : styles.tagExternalOn),
            unavailable && styles.tagDisabled
          )}
          onClick={() => select(internal)}
          onKeyDown={handleKeyDown}
        >
          {internal ? INTERNAL_ONLY_LABEL : EXTERNAL_LABEL}
        </button>
      </Tooltip>
    );
  };

  return (
    <div role="radiogroup" aria-label="Who can see this message" className={styles.tagGroup}>
      {option(false)}
      {option(true)}
    </div>
  );
};

InternalOnlyTags.displayName = 'InternalOnlyTags';
