/**
 * BoolFilterChip — Fluent v9 three-state boolean filter chip.
 *
 * Renders a single Fluent v9 `<Dropdown>` with three options:
 *   - `"Any"`   → `onChange(null)`   (no filter applied)
 *   - `"Yes"`   → `onChange(true)`   (FetchXML `eq` true)
 *   - `"No"`    → `onChange(false)`  (FetchXML `eq` false)
 *
 * Unlike the date / text chips this one has NO Popover — Fluent v9's
 * `<Dropdown>` already renders its listbox into a portal and the Fluent
 * library handles that portal's theme propagation natively, so NFR-03's
 * `applyStylesToPortals` re-wrap is NOT needed here.
 *
 * **Spec**: projects/spaarke-datagrid-framework-r1/spec.md FR-DG-07 + FR-DG-13
 * **Design**: design.md §6.4 (filter chip primitives)
 * **ADRs**: ADR-021 (Fluent v9 + dark mode), ADR-022 (React-16-safe)
 *
 * **NFR-02 compliance**: zero raw hex; all sizing via `tokens.*` and
 * `dataGridTokens`.
 *
 * **React-16-safe**: no `useId`, no `useSyncExternalStore`, no `createRoot`.
 */

import * as React from 'react';
import { Dropdown, Option, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import type { DropdownProps } from '@fluentui/react-components';

import { dataGridTokens } from '../tokens';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The three-state value emitted by the chip.
 *
 *   - `null`  → "Any" — no filter applied (filter cleared)
 *   - `true`  → "Yes" — match records where the boolean attribute is true
 *   - `false` → "No"  — match records where the boolean attribute is false
 */
export type BoolFilterValue = null | true | false;

/**
 * Props for {@link BoolFilterChip}.
 *
 * Fully controlled: pass `null` for "Any", `true` for "Yes", `false` for "No".
 */
export interface BoolFilterChipProps {
  /** Currently selected value. */
  value: BoolFilterValue;

  /** Fires when the user picks a different option. */
  onChange: (next: BoolFilterValue) => void;

  /**
   * Chip label, used as the dropdown's `aria-label`. Also rendered as the
   * dropdown placeholder when `value === null` is mapped to "Any" by the
   * caller.
   */
  label: string;

  /**
   * OPTIONAL — override the three option labels. Defaults to
   * `{ any: 'Any', yes: 'Yes', no: 'No' }`. Useful for domain-specific
   * boolean attributes (e.g. `{ any: 'Any', yes: 'Active', no: 'Inactive' }`).
   */
  optionLabels?: { any: string; yes: string; no: string };

  /** OPTIONAL — additional class merged AFTER component classes. */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal value <-> string mapping
// ─────────────────────────────────────────────────────────────────────────────

/** Stable string keys for Fluent Dropdown options. */
const VALUE_ANY = '__any__';
const VALUE_YES = '__yes__';
const VALUE_NO = '__no__';

function valueToKey(value: BoolFilterValue): string {
  if (value === null) return VALUE_ANY;
  if (value === true) return VALUE_YES;
  return VALUE_NO;
}

function keyToValue(key: string): BoolFilterValue {
  if (key === VALUE_YES) return true;
  if (key === VALUE_NO) return false;
  return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (module scope per ADR-021)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'inline-flex',
    alignItems: 'center',
  },
  dropdown: {
    fontSize: dataGridTokens.filterChip.fontSize,
    minWidth: '120px',
    // Allow the dropdown to shrink to its content + chevron — chip strips
    // need this to avoid one chip dominating the row width.
    width: 'auto',
  },
  listbox: {
    // No portal re-wrap needed: Dropdown handles its own portal theme.
    // Token-derived spacing only.
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const BoolFilterChip: React.FC<BoolFilterChipProps> = ({ value, onChange, label, optionLabels, className }) => {
  const styles = useStyles();

  const labels = optionLabels ?? { any: 'Any', yes: 'Yes', no: 'No' };

  const selectedKey = valueToKey(value);
  const displayValue: string = value === null ? labels.any : value === true ? labels.yes : labels.no;

  const handleOptionSelect: DropdownProps['onOptionSelect'] = React.useCallback(
    (_event, data) => {
      // Fluent Dropdown emits `selectedOptions: string[]` even in single-select;
      // we take the first (and only) element.
      const key = data.optionValue ?? data.selectedOptions[0] ?? VALUE_ANY;
      onChange(keyToValue(key));
    },
    [onChange]
  );

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="bool-filter-chip">
      <Dropdown
        className={styles.dropdown}
        listbox={{ className: styles.listbox }}
        value={displayValue}
        selectedOptions={[selectedKey]}
        onOptionSelect={handleOptionSelect}
        aria-label={label}
        data-testid="bool-filter-chip-dropdown"
      >
        <Option value={VALUE_ANY} text={labels.any} data-testid="bool-filter-chip-option-any">
          {labels.any}
        </Option>
        <Option value={VALUE_YES} text={labels.yes} data-testid="bool-filter-chip-option-yes">
          {labels.yes}
        </Option>
        <Option value={VALUE_NO} text={labels.no} data-testid="bool-filter-chip-option-no">
          {labels.no}
        </Option>
      </Dropdown>
    </div>
  );
};

export default BoolFilterChip;
