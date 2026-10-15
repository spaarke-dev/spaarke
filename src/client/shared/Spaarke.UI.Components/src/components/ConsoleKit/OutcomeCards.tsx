/**
 * OutcomeCards — the ways to decide, each as a card in one radio group; the suggested way is first (caller's order).
 * The chosen card shows its `body` (for example reason or amount fields) underneath. Choosing only reports the value
 * through `onChange`: nothing is recorded until the host's own confirm action.
 *
 * A Fluent `RadioGroup` with an accessible name, so the arrow keys move the selection and a disabled option is
 * skipped and cannot be chosen. Context-agnostic (ADR-012): labels, captions and bodies come from props. React-16-safe.
 *
 * Task: ebilling-r1, task 109.
 */

import * as React from 'react';
import { Caption1, Radio, RadioGroup, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

export interface OutcomeCardOption<V extends string = string> {
  value: V;
  label: string;
  /** A quiet line under the label. */
  caption?: string;
  disabled?: boolean;
  /** Shown under the card only while it is the chosen one. */
  body?: React.ReactNode;
}

export interface OutcomeCardsProps<V extends string = string> {
  /** In display order: put the suggestion first. */
  options: ReadonlyArray<OutcomeCardOption<V>>;
  /** The chosen value; `undefined` for none yet. */
  value: V | undefined;
  onChange: (value: V) => void;
  /** Accessible name of the radio group (required). */
  ariaLabel: string;
  className?: string;
}

const useStyles = makeStyles({
  group: { display: 'flex', flexDirection: 'column', alignItems: 'stretch', rowGap: tokens.spacingVerticalS },
  card: {
    display: 'flex',
    flexDirection: 'column',
    alignSelf: 'stretch',
    alignItems: 'flex-start',
    marginLeft: 0,
    marginRight: 0,
    borderRadius: tokens.borderRadiusMedium,
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  cardSelected: {
    borderTopWidth: tokens.strokeWidthThick,
    borderRightWidth: tokens.strokeWidthThick,
    borderBottomWidth: tokens.strokeWidthThick,
    borderLeftWidth: tokens.strokeWidthThick,
    borderTopColor: tokens.colorBrandStroke1,
    borderRightColor: tokens.colorBrandStroke1,
    borderBottomColor: tokens.colorBrandStroke1,
    borderLeftColor: tokens.colorBrandStroke1,
    backgroundColor: tokens.colorBrandBackground2,
  },
  label: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  caption: { color: tokens.colorNeutralForeground3 },
  body: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    paddingLeft: '36px',
    paddingRight: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalM,
  },
});

export function OutcomeCards<V extends string = string>({
  options,
  value,
  onChange,
  ariaLabel,
  className,
}: OutcomeCardsProps<V>): React.ReactElement {
  const styles = useStyles();
  return (
    <RadioGroup
      aria-label={ariaLabel}
      value={value ?? ''}
      onChange={(_, data) => onChange(data.value as V)}
      className={mergeClasses(styles.group, className)}
      data-testid="outcome-cards"
    >
      {options.map(option => {
        const chosen = value === option.value;
        return (
          <div
            key={option.value}
            className={mergeClasses(styles.card, chosen && styles.cardSelected)}
            data-testid={`outcome-card-${option.value}`}
            data-selected={chosen}
          >
            <Radio
              value={option.value}
              disabled={option.disabled}
              label={
                <span className={styles.label}>
                  <Text weight="semibold">{option.label}</Text>
                  {option.caption && <Caption1 className={styles.caption}>{option.caption}</Caption1>}
                </span>
              }
            />
            {chosen && option.body && (
              <div className={styles.body} data-testid={`outcome-body-${option.value}`}>
                {option.body}
              </div>
            )}
          </div>
        );
      })}
    </RadioGroup>
  );
}
