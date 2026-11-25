/**
 * FilterDropdown — labelled Fluent v9 multi-select dropdown for a search filter
 * (Document Type, Matter Type, File Type, ...).
 *
 * Shared by the SemanticSearch code page and the SemanticSearchControl PCF (#3093).
 * Shows "All" when nothing is selected, the option label for one selection, "{N} selected"
 * for several, and a spinner while the host is still loading options.
 *
 * Fits narrow side panes: the trigger button never grows past its container.
 *
 * ADR-021 (Fluent v9, tokens), ADR-022 (React 16-safe: Fluent's `useId`, not React's).
 */

import * as React from 'react';
import { makeStyles, tokens, Dropdown, Option, Label, Spinner, useId } from '@fluentui/react-components';

/** One selectable filter option, with an optional result count. */
export interface FilterDropdownOption {
  value: string;
  label: string;
  count?: number;
}

export interface FilterDropdownProps {
  /** Label text for the dropdown */
  label: string;
  /** Available filter options */
  options: FilterDropdownOption[];
  /** Currently selected option values */
  selectedValues: string[];
  /** Called with the new selection */
  onChange: (selected: string[]) => void;
  /** Whether the host is still loading options (a spinner shows while there are none yet) */
  isLoading?: boolean;
  /** Whether the dropdown is disabled */
  disabled?: boolean;
  /** Allow several selections (default: true) */
  multiSelect?: boolean;
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    width: '100%',
    minWidth: 0,
    overflow: 'hidden',
  },
  label: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
  },
  dropdown: {
    width: '100%',
    maxWidth: '100%',
    minWidth: 0,
    boxSizing: 'border-box',
    '& > button': {
      width: '100%',
      maxWidth: '100%',
      minWidth: 0,
      boxSizing: 'border-box',
    },
  },
  loading: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

/** Text the closed dropdown shows for a selection. */
export function formatFilterSelection(selectedValues: string[], options: FilterDropdownOption[]): string {
  if (selectedValues.length === 0) return 'All';
  if (selectedValues.length === 1) {
    return options.find(o => o.value === selectedValues[0])?.label ?? selectedValues[0];
  }
  return `${selectedValues.length} selected`;
}

export const FilterDropdown: React.FC<FilterDropdownProps> = ({
  label,
  options,
  selectedValues,
  onChange,
  isLoading = false,
  disabled = false,
  multiSelect = true,
}) => {
  const styles = useStyles();
  const dropdownId = useId('filter-dropdown');

  const handleOptionSelect = React.useCallback(
    (_ev: unknown, data: { optionValue?: string; selectedOptions: string[] }) => {
      onChange(data.selectedOptions);
    },
    [onChange]
  );

  const selectedValue = React.useMemo(() => formatFilterSelection(selectedValues, options), [selectedValues, options]);

  // Spinner only while loading: an option set that loaded empty shows an empty dropdown, not a
  // spinner that never stops.
  if (isLoading && options.length === 0) {
    return (
      <div className={styles.container}>
        <Label className={styles.label}>{label}</Label>
        <div className={styles.loading}>
          <Spinner size="tiny" />
          <span>Loading...</span>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <Label className={styles.label} htmlFor={dropdownId}>
        {label}
      </Label>
      <Dropdown
        id={dropdownId}
        className={styles.dropdown}
        button={{ style: { width: '100%', minWidth: 0, boxSizing: 'border-box' } }}
        placeholder="All"
        value={selectedValue}
        selectedOptions={selectedValues}
        onOptionSelect={handleOptionSelect}
        disabled={disabled}
        multiselect={multiSelect}
      >
        {options.map(option => (
          <Option key={option.value} value={option.value} text={option.label}>
            {option.label}
            {option.count !== undefined ? ` (${option.count})` : ''}
          </Option>
        ))}
      </Dropdown>
    </div>
  );
};

export default FilterDropdown;
