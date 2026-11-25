/**
 * DateRangeFilter — inline date-range filter with quick-select presets, for search filter panes.
 *
 * Shared by the SemanticSearch code page (filter pane), the SemanticSearchControl PCF (inside its
 * command-bar popover) (#3093) and the Spaarke Console's Semantic Search criteria (#3131).
 *
 * Layout (stacked, fits a narrow pane):
 *   - label
 *   - preset dropdown: Off, Last 7 / 30 / 90 days, This year, Last year (shows "Custom" when
 *     the dates match no preset)
 *   - From / To date inputs, always shown so a custom range can be typed directly
 *   - Clear, once either date is set
 *
 * Values are LOCAL calendar dates (`YYYY-MM-DD`). `onChange(null)` means "no date filter".
 * A reversed range (From after To) shows a message and is not emitted.
 *
 * Not the DataGrid `DateRangeFilterChip`: that is a popover chip that emits UTC instants for a
 * FetchXML condition and has no presets; this one is a pane control that emits date strings for
 * the search API.
 *
 * ADR-021 (Fluent v9, tokens), ADR-022 (React 16-safe: Fluent's `useId`, not React's).
 */

import * as React from 'react';
import { makeStyles, tokens, Input, Label, Dropdown, Option, Button, useId } from '@fluentui/react-components';
import { formatDateOnly } from '../../utils/dateLocal';

/** A date range of LOCAL calendar dates (`YYYY-MM-DD`); either end may be open. */
export interface DateRangeFilterValue {
  from: string | null;
  to: string | null;
}

export interface DateRangeFilterProps {
  /** Label above the preset dropdown (default "Date Range") */
  label?: string;
  /** Current range; `null` or both ends null = no filter */
  value: DateRangeFilterValue | null;
  /**
   * Called with the new range, or `null` when the filter is cleared. `preset` says how it was set:
   * the preset picked, `'off'` for a clear, `'custom'` for typed dates. Hosts that store a preset
   * (to re-resolve it later) keep this id: inferring it from the dates is ambiguous when two
   * presets give the same range (early January, "This year" equals "Last 7 days").
   */
  onChange: (value: DateRangeFilterValue | null, preset: DateRangePresetId | 'custom') => void;
  /**
   * The preset the host stored for `value`, shown as the dropdown label while `value` still is that
   * preset's range. Without it the label is inferred from the dates.
   */
  preset?: DateRangePresetId | 'custom';
  /** Whether the inputs are disabled */
  disabled?: boolean;
}

/** Quick-select preset ids. */
export type DateRangePresetId = 'off' | 'last7' | 'last30' | 'last90' | 'thisYear' | 'lastYear';

/** Every preset id, in dropdown order (hosts that persist a preset id validate against it). */
export const DATE_RANGE_PRESET_IDS: readonly DateRangePresetId[] = [
  'off',
  'last7',
  'last30',
  'last90',
  'thisYear',
  'lastYear',
];

interface DatePreset {
  value: DateRangePresetId;
  label: string;
}

const DATE_PRESETS: DatePreset[] = [
  { value: 'off', label: 'Off' },
  { value: 'last7', label: 'Last 7 days' },
  { value: 'last30', label: 'Last 30 days' },
  { value: 'last90', label: 'Last 90 days' },
  { value: 'thisYear', label: 'This year' },
  { value: 'lastYear', label: 'Last year' },
];

/**
 * The range a preset stands for, in the user's LOCAL calendar (`now` defaults to today).
 *
 * LOCAL, not `toISOString()`: that is the UTC date, so after about 20:00 in a US zone "today"
 * became tomorrow, and east of UTC "This year" started on 31 December of the year before.
 */
export function calculatePresetRange(preset: DateRangePresetId, now: Date = new Date()): DateRangeFilterValue {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  switch (preset) {
    case 'off':
      return { from: null, to: null };
    case 'thisYear':
      return { from: formatDateOnly(new Date(today.getFullYear(), 0, 1)), to: formatDateOnly(today) };
    case 'lastYear':
      return {
        from: formatDateOnly(new Date(today.getFullYear() - 1, 0, 1)),
        to: formatDateOnly(new Date(today.getFullYear() - 1, 11, 31)),
      };
    case 'last7':
    case 'last30':
    case 'last90': {
      const from = new Date(today);
      from.setDate(from.getDate() - (preset === 'last7' ? 7 : preset === 'last30' ? 30 : 90));
      return { from: formatDateOnly(from), to: formatDateOnly(today) };
    }
  }
}

/** The preset a range matches: 'off' when empty, 'custom' when it matches none. */
export function detectActivePreset(
  value: DateRangeFilterValue | null,
  now: Date = new Date()
): DateRangePresetId | 'custom' {
  if (!value || (!value.from && !value.to)) return 'off';
  for (const preset of DATE_PRESETS) {
    if (preset.value === 'off') continue;
    const range = calculatePresetRange(preset.value, now);
    if (range.from === value.from && range.to === value.to) return preset.value;
  }
  return 'custom';
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  label: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  dropdown: {
    width: '100%',
    minWidth: 0,
  },
  dateFields: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'stretch',
    gap: tokens.spacingVerticalXXS,
  },
  fieldLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  dateInput: {
    width: '100%',
  },
  // Hide the native date placeholder text while the input is empty (Chromium/Edge)
  dateInputEmpty: {
    width: '100%',
    "& input[type='date']::-webkit-datetime-edit": {
      color: tokens.colorTransparentStroke,
    },
  },
  clearButton: {
    alignSelf: 'flex-start',
  },
  validationError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
});

export const DateRangeFilter: React.FC<DateRangeFilterProps> = ({
  label = 'Date Range',
  value,
  onChange,
  preset,
  disabled = false,
}) => {
  const styles = useStyles();
  const presetId = useId('date-preset');
  const fromId = useId('date-from');
  const toId = useId('date-to');

  const [fromDate, setFromDate] = React.useState<string>(value?.from ?? '');
  const [toDate, setToDate] = React.useState<string>(value?.to ?? '');
  const [validationError, setValidationError] = React.useState<string | null>(null);
  // The preset picked here, for the label when two presets give the same range.
  const [pickedPreset, setPickedPreset] = React.useState<DateRangePresetId | null>(null);

  // Follow the host's value (a preset, a clear, a saved search).
  React.useEffect(() => {
    setFromDate(value?.from ?? '');
    setToDate(value?.to ?? '');
  }, [value?.from, value?.to]);

  const activePreset = React.useMemo(() => {
    const known = preset ?? pickedPreset;
    if (known === 'custom' && value && (value.from || value.to)) return 'custom';
    if (known && known !== 'custom' && known !== 'off') {
      const range = calculatePresetRange(known);
      if (range.from === value?.from && range.to === value?.to) return known;
    }
    return detectActivePreset(value);
  }, [value, preset, pickedPreset]);

  const updateRange = React.useCallback(
    (from: string, to: string) => {
      setValidationError(null);
      setPickedPreset(null);
      if (!from && !to) {
        onChange(null, 'off');
        return;
      }
      if (from && to && from > to) {
        setValidationError('From date must be before To date');
        return;
      }
      onChange({ from: from || null, to: to || null }, 'custom');
    },
    [onChange]
  );

  const handleFromChange = React.useCallback(
    (ev: React.ChangeEvent<HTMLInputElement>) => {
      const next = ev.target.value;
      setFromDate(next);
      updateRange(next, toDate);
    },
    [toDate, updateRange]
  );

  const handleToChange = React.useCallback(
    (ev: React.ChangeEvent<HTMLInputElement>) => {
      const next = ev.target.value;
      setToDate(next);
      updateRange(fromDate, next);
    },
    [fromDate, updateRange]
  );

  const clear = React.useCallback(() => {
    setFromDate('');
    setToDate('');
    setValidationError(null);
    setPickedPreset(null);
    onChange(null, 'off');
  }, [onChange]);

  const handlePresetSelect = React.useCallback(
    (_ev: unknown, data: { optionValue?: string }) => {
      const picked = DATE_PRESETS.find(p => p.value === data.optionValue);
      if (!picked) return;
      if (picked.value === 'off') {
        clear();
        return;
      }
      const range = calculatePresetRange(picked.value);
      setFromDate(range.from ?? '');
      setToDate(range.to ?? '');
      setValidationError(null);
      setPickedPreset(picked.value);
      onChange(range, picked.value);
    },
    [onChange, clear]
  );

  const presetLabel =
    activePreset === 'custom' ? 'Custom' : (DATE_PRESETS.find(p => p.value === activePreset)?.label ?? 'Off');

  return (
    <div className={styles.container}>
      <Label className={styles.label} htmlFor={presetId}>
        {label}
      </Label>
      <Dropdown
        id={presetId}
        className={styles.dropdown}
        size="small"
        value={presetLabel}
        selectedOptions={activePreset === 'custom' ? [] : [activePreset]}
        onOptionSelect={handlePresetSelect}
        disabled={disabled}
        aria-label="Date range preset"
      >
        {DATE_PRESETS.map(preset => (
          <Option key={preset.value} value={preset.value}>
            {preset.label}
          </Option>
        ))}
      </Dropdown>

      <div className={styles.dateFields}>
        <Label htmlFor={fromId} className={styles.fieldLabel} size="small">
          From
        </Label>
        <Input
          id={fromId}
          className={fromDate ? styles.dateInput : styles.dateInputEmpty}
          type="date"
          value={fromDate}
          onChange={handleFromChange}
          disabled={disabled}
          aria-label="From date"
        />
        <Label htmlFor={toId} className={styles.fieldLabel} size="small">
          To
        </Label>
        <Input
          id={toId}
          className={toDate ? styles.dateInput : styles.dateInputEmpty}
          type="date"
          value={toDate}
          onChange={handleToChange}
          disabled={disabled}
          aria-label="To date"
        />
        {(fromDate || toDate) && (
          <Button className={styles.clearButton} appearance="subtle" size="small" onClick={clear} disabled={disabled}>
            Clear
          </Button>
        )}
        {validationError && (
          <span className={styles.validationError} role="alert">
            {validationError}
          </span>
        )}
      </div>
    </div>
  );
};

export default DateRangeFilter;
