/**
 * SimpleFilterChips.stories — Storybook stories for task 007's three filter
 * chip primitives: {@link DateRangeFilterChip}, {@link TextFilterChip},
 * {@link BoolFilterChip}.
 *
 * Each chip gets:
 *   - Default + multi-state (with-value) story
 *   - Light + dark theme variants via the `theme` argType
 *   - Cleared state visible by toggling the controlled value to its
 *     "no filter" sentinel (`null` for Date, `undefined` for Text, `null`
 *     for Bool)
 *
 * All stories are wrapped in `<FluentProvider applyStylesToPortals />` per
 * NFR-03 so Popover-bearing surfaces inherit the active theme.
 *
 * **Storybook config**: this project has no `.storybook/` configuration yet;
 * this file lives OUTSIDE `src/` so the TypeScript library build (`tsc`) does
 * not pick it up. When Storybook is wired in a later task, the standard
 * `main.ts` `stories: ['../storybook/**\/*.stories.@(ts|tsx)']` pattern will
 * include this file.
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/007-date-text-bool-chips.poml
 */

import * as React from 'react';
import { FluentProvider, webLightTheme, webDarkTheme, makeStyles, tokens } from '@fluentui/react-components';

import { DateRangeFilterChip, type UtcDateBounds } from '../src/components/DataGrid/chips/DateRangeFilterChip';
import { TextFilterChip } from '../src/components/DataGrid/chips/TextFilterChip';
import { BoolFilterChip, type BoolFilterValue } from '../src/components/DataGrid/chips/BoolFilterChip';

// ─────────────────────────────────────────────────────────────────────────────
// Shared layout + provider helper
// ─────────────────────────────────────────────────────────────────────────────

const useChipStripStyles = makeStyles({
  page: {
    padding: tokens.spacingVerticalL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minHeight: '320px',
  },
  strip: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  caption: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

interface StoryArgs {
  theme: 'light' | 'dark';
}

const withFluentProvider = (theme: 'light' | 'dark', content: React.ReactNode): React.ReactNode => {
  return (
    <FluentProvider
      applyStylesToPortals
      theme={theme === 'dark' ? webDarkTheme : webLightTheme}
      style={{ minHeight: '360px', display: 'flex' }}
    >
      {content}
    </FluentProvider>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Storybook meta
// ─────────────────────────────────────────────────────────────────────────────

export default {
  title: 'DataGrid/SimpleFilterChips',
  parameters: {
    layout: 'fullscreen',
  },
  argTypes: {
    theme: {
      control: { type: 'radio' },
      options: ['light', 'dark'],
    },
  },
};

// ─────────────────────────────────────────────────────────────────────────────
// Story 1: DateRangeFilterChip — default (no value)
// ─────────────────────────────────────────────────────────────────────────────

export const DateRangeDefault = (args: StoryArgs) => {
  const styles = useChipStripStyles();
  const [value, setValue] = React.useState<UtcDateBounds | null>(null);

  return withFluentProvider(
    args.theme,
    <div className={styles.page}>
      <div className={styles.strip}>
        <DateRangeFilterChip label="Created on" value={value} onChange={setValue} />
      </div>
      <div className={styles.caption}>
        value: {value ? `${value.startUtc.toISOString()} → ${value.endUtc.toISOString()}` : '(cleared)'}
      </div>
    </div>
  );
};
DateRangeDefault.args = { theme: 'light' as const };
DateRangeDefault.storyName = 'DateRange — default (cleared)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 2: DateRangeFilterChip — with pre-set value
// ─────────────────────────────────────────────────────────────────────────────

export const DateRangeWithValue = (args: StoryArgs) => {
  const styles = useChipStripStyles();
  const initial: UtcDateBounds = React.useMemo(() => {
    // Initialise to LOCAL "first day of this month → today".
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth(), 1, 0, 0, 0, 0);
    const end = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 23, 59, 59, 999);
    return { startUtc: start, endUtc: end };
  }, []);
  const [value, setValue] = React.useState<UtcDateBounds | null>(initial);

  return withFluentProvider(
    args.theme,
    <div className={styles.page}>
      <div className={styles.strip}>
        <DateRangeFilterChip label="Created on" value={value} onChange={setValue} />
      </div>
      <div className={styles.caption}>
        value: {value ? `${value.startUtc.toISOString()} → ${value.endUtc.toISOString()}` : '(cleared)'}
      </div>
    </div>
  );
};
DateRangeWithValue.args = { theme: 'light' as const };
DateRangeWithValue.storyName = 'DateRange — with value (this month)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 3: TextFilterChip — default (no value)
// ─────────────────────────────────────────────────────────────────────────────

export const TextDefault = (args: StoryArgs) => {
  const styles = useChipStripStyles();
  const [value, setValue] = React.useState<string | undefined>(undefined);

  return withFluentProvider(
    args.theme,
    <div className={styles.page}>
      <div className={styles.strip}>
        <TextFilterChip label="Name contains" value={value} onChange={setValue} />
      </div>
      <div className={styles.caption}>value: {value === undefined ? '(cleared)' : JSON.stringify(value)}</div>
    </div>
  );
};
TextDefault.args = { theme: 'light' as const };
TextDefault.storyName = 'Text — default (cleared)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 4: TextFilterChip — with pre-set value
// ─────────────────────────────────────────────────────────────────────────────

export const TextWithValue = (args: StoryArgs) => {
  const styles = useChipStripStyles();
  const [value, setValue] = React.useState<string | undefined>('Acme');

  return withFluentProvider(
    args.theme,
    <div className={styles.page}>
      <div className={styles.strip}>
        <TextFilterChip label="Name contains" value={value} onChange={setValue} />
      </div>
      <div className={styles.caption}>value: {value === undefined ? '(cleared)' : JSON.stringify(value)}</div>
    </div>
  );
};
TextWithValue.args = { theme: 'light' as const };
TextWithValue.storyName = 'Text — with value ("Acme")';

// ─────────────────────────────────────────────────────────────────────────────
// Story 5: BoolFilterChip — default (Any)
// ─────────────────────────────────────────────────────────────────────────────

export const BoolDefault = (args: StoryArgs) => {
  const styles = useChipStripStyles();
  const [value, setValue] = React.useState<BoolFilterValue>(null);

  return withFluentProvider(
    args.theme,
    <div className={styles.page}>
      <div className={styles.strip}>
        <BoolFilterChip label="Is active" value={value} onChange={setValue} />
      </div>
      <div className={styles.caption}>value: {value === null ? 'null (Any)' : String(value)}</div>
    </div>
  );
};
BoolDefault.args = { theme: 'light' as const };
BoolDefault.storyName = 'Bool — default (Any)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 6: BoolFilterChip — pre-selected Yes
// ─────────────────────────────────────────────────────────────────────────────

export const BoolWithYes = (args: StoryArgs) => {
  const styles = useChipStripStyles();
  const [value, setValue] = React.useState<BoolFilterValue>(true);

  return withFluentProvider(
    args.theme,
    <div className={styles.page}>
      <div className={styles.strip}>
        <BoolFilterChip label="Is active" value={value} onChange={setValue} />
      </div>
      <div className={styles.caption}>value: {value === null ? 'null (Any)' : String(value)}</div>
    </div>
  );
};
BoolWithYes.args = { theme: 'light' as const };
BoolWithYes.storyName = 'Bool — pre-selected Yes';

// ─────────────────────────────────────────────────────────────────────────────
// Story 7: All three chips in one row (typical command-bar usage)
// ─────────────────────────────────────────────────────────────────────────────

export const AllThreeChipsInOneRow = (args: StoryArgs) => {
  const styles = useChipStripStyles();
  const [dateValue, setDateValue] = React.useState<UtcDateBounds | null>(null);
  const [textValue, setTextValue] = React.useState<string | undefined>(undefined);
  const [boolValue, setBoolValue] = React.useState<BoolFilterValue>(null);

  return withFluentProvider(
    args.theme,
    <div className={styles.page}>
      <div className={styles.strip}>
        <DateRangeFilterChip label="Created on" value={dateValue} onChange={setDateValue} />
        <TextFilterChip label="Name contains" value={textValue} onChange={setTextValue} />
        <BoolFilterChip label="Is active" value={boolValue} onChange={setBoolValue} />
      </div>
      <div className={styles.caption}>
        dateValue: {dateValue ? `${dateValue.startUtc.toISOString()} → ${dateValue.endUtc.toISOString()}` : '(cleared)'}
        <br />
        textValue: {textValue === undefined ? '(cleared)' : JSON.stringify(textValue)}
        <br />
        boolValue: {boolValue === null ? 'null (Any)' : String(boolValue)}
      </div>
    </div>
  );
};
AllThreeChipsInOneRow.args = { theme: 'light' as const };
AllThreeChipsInOneRow.storyName = 'All three chips together';
