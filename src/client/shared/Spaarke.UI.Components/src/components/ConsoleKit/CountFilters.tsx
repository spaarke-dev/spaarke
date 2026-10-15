/**
 * CountFilters — count cards that act as a filter on a list: a group of toggle buttons, one selected at a time.
 * The counts are the caller's and are always for the full list; selecting a card never changes them.
 *
 * Not `AggregateCard`: that is a "count and a link" with no selected state, so it cannot act as a filter.
 * Each button is named "label: count" (with the sub-line when given) and exposes `aria-pressed`.
 * Context-agnostic (ADR-012): labels come from props. React-16-safe.
 *
 * Task: ebilling-r1, task 109.
 */

import * as React from 'react';
import { Caption1, Caption2, ToggleButton, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

export interface CountFilterItem<K extends string = string> {
  key: K;
  label: string;
  count: number;
  /** A quiet second line, for example the first card's progress ("3 of 24 decided"). */
  sub?: string;
}

export interface CountFiltersProps<K extends string = string> {
  items: ReadonlyArray<CountFilterItem<K>>;
  /** The selected card's key. */
  value: K;
  onChange: (key: K) => void;
  /** Accessible name of the group (required: a group of buttons needs one). */
  ariaLabel: string;
  className?: string;
}

const useStyles = makeStyles({
  row: { display: 'flex', flexWrap: 'wrap', columnGap: tokens.spacingHorizontalS, rowGap: tokens.spacingVerticalS },
  card: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    justifyContent: 'flex-start',
    minWidth: '104px',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    rowGap: 0,
  },
  count: {
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
  },
  quiet: { color: tokens.colorNeutralForeground2 },
});

export function CountFilters<K extends string = string>({
  items,
  value,
  onChange,
  ariaLabel,
  className,
}: CountFiltersProps<K>): React.ReactElement {
  const styles = useStyles();
  return (
    <div
      className={mergeClasses(styles.row, className)}
      role="group"
      aria-label={ariaLabel}
      data-testid="count-filters"
    >
      {items.map(item => {
        const selected = value === item.key;
        return (
          <ToggleButton
            key={item.key}
            checked={selected}
            appearance={selected ? 'primary' : 'outline'}
            className={styles.card}
            onClick={() => onChange(item.key)}
            aria-label={`${item.label}: ${item.count}${item.sub ? `, ${item.sub}` : ''}`}
            data-testid={`count-filter-${item.key}`}
          >
            <span className={styles.count}>{item.count}</span>
            <Caption1 className={selected ? undefined : styles.quiet}>{item.label}</Caption1>
            {item.sub && <Caption2 className={selected ? undefined : styles.quiet}>{item.sub}</Caption2>}
          </ToggleButton>
        );
      })}
    </div>
  );
}
