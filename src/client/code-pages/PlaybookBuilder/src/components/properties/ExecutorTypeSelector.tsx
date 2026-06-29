/**
 * ExecutorTypeSelector — Dropdown for selecting an Executor Type Choice value.
 *
 * R7 Wave 8 task 086 (FR-24). Renders a Fluent v9 `Dropdown` populated from the
 * 33-entry EXECUTOR_METADATA catalog (task 082). Options are grouped by tier
 * (AI / Compute / Mutations / Control / Delivery / Capability) using Fluent v9
 * `OptionGroup` so makers can visually scan to the right family quickly.
 *
 * Bound to `node.data.executorType` (number, mirrors `sprk_executortype` Choice
 * value). The NodePalette (task 082) sets this at node-creation; this selector
 * lets the maker change it post-creation from the new Action tab on
 * NodePropertiesDialog (FR-24).
 *
 * Component Justification (CLAUDE.md §11):
 *   1. Existing: `NodePalette.tsx` (task 082) writes `executorType` at drop
 *      time via the drag payload — no UI exists to edit it on an existing node.
 *   2. Extension: NodePalette tile UI is a draggable Accordion tile — cannot be
 *      reused inside a Dropdown selector. A new component is required.
 *   3. Cost-of-doing-nothing: maker cannot change a node's executor type
 *      post-creation; FR-24 explicitly requires the side-by-side Action +
 *      Executor Type relationship view.
 *
 * @see R7 spec.md FR-24 (Action tab promotion — Action + Executor Type side-by-side)
 * @see R7 design.md §11 (6-tier executor categorization)
 * @see ADR-006 (Fluent UI v9 only)
 * @see ADR-021 (Dark mode — semantic tokens only, no hex)
 */

import React, { useCallback, useMemo } from 'react';
import {
  Dropdown,
  Option,
  OptionGroup,
  Label,
  makeStyles,
  tokens,
  type OptionOnSelectData,
  type SelectionEvents,
} from '@fluentui/react-components';
import {
  EXECUTOR_METADATA,
  TIER_LABEL,
  TIER_ORDER,
  getExecutorByValue,
  type ExecutorTier,
  type ExecutorMetadata,
} from '../../config/executorMetadata';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 makeStyles — semantic tokens only for dark mode binding)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  label: {
    fontWeight: tokens.fontWeightSemibold,
  },
  description: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    marginTop: tokens.spacingVerticalXXS,
  },
  dropdown: {
    width: '100%',
  },
  optionLine: {
    display: 'flex',
    flexDirection: 'column',
  },
  optionDescription: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ExecutorTypeSelectorProps {
  /** The currently selected ExecutorType Choice value (sprk_executortype). */
  value?: number;
  /** Callback when the selected executor type changes. */
  onChange: (newValue: number) => void;
  /** Whether the selector is disabled. */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ExecutorTypeSelector: React.FC<ExecutorTypeSelectorProps> = ({
  value,
  onChange,
  disabled = false,
}) => {
  const styles = useStyles();

  // Memoize tier grouping (stable since EXECUTOR_METADATA is module-scope constant).
  const grouped = useMemo<Record<ExecutorTier, ExecutorMetadata[]>>(() => {
    const groups: Record<ExecutorTier, ExecutorMetadata[]> = {
      AI: [],
      Compute: [],
      Mutations: [],
      Control: [],
      Delivery: [],
      Capability: [],
    };
    for (const entry of EXECUTOR_METADATA) {
      groups[entry.tier].push(entry);
    }
    return groups;
  }, []);

  const selectedEntry = useMemo(
    () => (typeof value === 'number' ? getExecutorByValue(value) : undefined),
    [value]
  );

  // Selected option display text — show "NN  Label" so the tier prefix is visible.
  const displayValue = selectedEntry
    ? `${selectedEntry.tierPrefix}  ${selectedEntry.label}`
    : '';

  // Fluent v9 Dropdown selectedOptions is a string[] of option values.
  const selectedOptions = selectedEntry ? [String(selectedEntry.value)] : [];

  const handleSelect = useCallback(
    (_event: SelectionEvents, data: OptionOnSelectData) => {
      const raw = data.optionValue;
      if (!raw) return;
      const parsed = Number(raw);
      if (!Number.isNaN(parsed)) {
        onChange(parsed);
      }
    },
    [onChange]
  );

  return (
    <div className={styles.container}>
      <Label className={styles.label} htmlFor="executor-type-selector">
        Executor Type
      </Label>
      <Dropdown
        id="executor-type-selector"
        className={styles.dropdown}
        placeholder="Select an executor type..."
        value={displayValue}
        selectedOptions={selectedOptions}
        onOptionSelect={handleSelect}
        disabled={disabled}
      >
        {TIER_ORDER.map(tier => {
          const entries = grouped[tier];
          if (entries.length === 0) return null;
          return (
            <OptionGroup key={tier} label={TIER_LABEL[tier]}>
              {entries.map(entry => {
                const optionText = `${entry.tierPrefix}  ${entry.label}`;
                return (
                  <Option
                    key={entry.value}
                    value={String(entry.value)}
                    text={optionText}
                  >
                    <div className={styles.optionLine}>
                      <div>{optionText}</div>
                      <div className={styles.optionDescription}>
                        {entry.description}
                      </div>
                    </div>
                  </Option>
                );
              })}
            </OptionGroup>
          );
        })}
      </Dropdown>
    </div>
  );
};

export default ExecutorTypeSelector;
