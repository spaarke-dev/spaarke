/**
 * FilterChipBar — composite filter strip that renders the chip primitives
 * (`TextFilterChip`, `OptionSetMultiFilterChip`, `DateRangeFilterChip`,
 * `BoolFilterChip`) horizontally, controlled by a single {@link ChipState}.
 *
 * The bar is the binding integration point between the configjson-driven
 * {@link ChipDescriptor} list (produced by {@link discoverChips}) and the
 * Phase A primitive chips. Each chip is controlled — the bar receives
 * `state` + `onStateChange` and the primitives flow values through it.
 *
 * When at least one chip has a value, a "Clear all" subtle button appears
 * at the right of the strip; clicking it emits an empty `ChipState` to the
 * parent. The button is hidden when no chips are active so the strip stays
 * visually clean.
 *
 * **Lookup chip — R1 limitation**: the `LookupMultiFilterChip` primitive
 * requires an `IDataverseClient` instance to run async type-ahead. Threading
 * a client through the chip-bar API would couple it to a host concern and
 * complicate the configjson surface. For R1 the bar renders lookup-kind
 * descriptors as a disabled subtle button with the chip's label and the
 * suffix `"(lookup not wired in R1)"` so consumers see the slot but
 * understand it is non-functional. R2 will accept an optional
 * `dataverseClient` prop and wire the lookup chip end-to-end.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §6.4
 * **ADRs**: ADR-021 (Fluent v9 + tokens-only), ADR-022 (React-16-safe)
 *
 * **NFR-02 compliance**: zero raw hex; spacing + colors via `tokens.*`.
 *
 * **React-16-safe**: no `useId`, no `useSyncExternalStore`, no `createRoot`.
 *
 * @see chipDiscovery   — produces the `descriptors` prop
 * @see chipFetchXml    — converts (descriptors, state) → FetchXML
 * @see types.ts        — {@link ChipDescriptor} + {@link ChipState}
 */

import * as React from 'react';
import { Button, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';

import { TextFilterChip } from '../chips/TextFilterChip';
import { OptionSetMultiFilterChip } from '../chips/OptionSetMultiFilterChip';
import { DateRangeFilterChip, localDateToUtcBounds } from '../chips/DateRangeFilterChip';
import type { UtcDateBounds } from '../chips/DateRangeFilterChip';
import { BoolFilterChip } from '../chips/BoolFilterChip';
import type { BoolFilterValue } from '../chips/BoolFilterChip';
import type { EntityMetadata, OptionSetOption } from '../../../services/IDataverseClient';

import type { ChipDescriptor, ChipState, ChipValue } from './types';

// ─────────────────────────────────────────────────────────────────────────────
// Public types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for {@link FilterChipBar}.
 *
 * The bar is fully controlled — pass `state` + `onStateChange`. To clear a
 * single chip the matching primitive emits its own "cleared" value (the bar
 * translates that to deleting the key from {@link ChipState}).
 */
export interface FilterChipBarProps {
  /** Discovered chips to render. Order is preserved in the rendered strip. */
  descriptors: readonly ChipDescriptor[];
  /** Currently-applied filter values, keyed by attribute logical name. */
  state: ChipState;
  /** Fires whenever the user changes any chip (including "Clear all"). */
  onStateChange: (next: ChipState) => void;
  /**
   * OPTIONAL — when `false`, the "Clear all" button is suppressed even if
   * chips have values. Defaults to `true`.
   */
  showClearAll?: boolean;
  /**
   * OPTIONAL — entity metadata used by `OptionSetMultiFilterChip` primitives
   * (their options derive from `entityMetadata.attributes[X].optionSet`).
   * When omitted, option-set chips fall back to a metadata stub built from
   * `descriptor.options`.
   */
  entityMetadata?: EntityMetadata;
  /** OPTIONAL — additional class merged AFTER component classes. */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (module scope per ADR-021)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground2,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  spacer: {
    flexGrow: 1,
  },
  placeholder: {
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function hasAnyValue(state: ChipState): boolean {
  for (const key of Object.keys(state)) {
    if (state[key] !== undefined) return true;
  }
  return false;
}

/**
 * Immutable "set or unset" helper — returns a new {@link ChipState} with the
 * given attribute set to `value` (or with the key removed when `value` is
 * `undefined`).
 */
function setChipValue(state: ChipState, attribute: string, value: ChipValue | undefined): ChipState {
  const next: ChipState = { ...state };
  if (value === undefined) {
    delete next[attribute];
  } else {
    next[attribute] = value;
  }
  return next;
}

/**
 * Build a minimal `EntityMetadata` shim for `OptionSetMultiFilterChip` when
 * the caller doesn't pass full metadata — the chip only needs
 * `attributes[X].optionSet` to render options.
 */
function buildMetadataShim(descriptor: ChipDescriptor, fallback: EntityMetadata | undefined): EntityMetadata {
  if (fallback) return fallback;
  const options: OptionSetOption[] = (descriptor.options ?? []).map(o => ({
    value: o.value,
    label: o.label,
  }));
  return {
    primaryIdAttribute: '',
    primaryNameAttribute: '',
    attributes: {
      [descriptor.attribute]: {
        attributeType: 'Picklist',
        optionSet: options,
      },
    },
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Render the filter chip strip.
 *
 * @see FilterChipBarProps
 */
export const FilterChipBar: React.FC<FilterChipBarProps> = ({
  descriptors,
  state,
  onStateChange,
  showClearAll,
  entityMetadata,
  className,
}) => {
  const styles = useStyles();

  const hasValues = React.useMemo(() => hasAnyValue(state), [state]);
  const clearAllVisible = (showClearAll ?? true) && hasValues;

  const handleClearAll = React.useCallback(() => {
    onStateChange({});
  }, [onStateChange]);

  return (
    <div
      className={mergeClasses(styles.root, className)}
      data-testid="filter-chip-bar"
      role="group"
      aria-label="Filter chips"
    >
      {descriptors.map(descriptor => renderChip(descriptor, state, onStateChange, entityMetadata, styles.placeholder))}
      <div className={styles.spacer} aria-hidden="true" />
      {clearAllVisible && (
        <Button
          appearance="subtle"
          size="small"
          icon={<DismissRegular aria-hidden="true" />}
          onClick={handleClearAll}
          data-testid="filter-chip-bar-clear-all"
          aria-label="Clear all filters"
        >
          Clear all
        </Button>
      )}
    </div>
  );
};

export default FilterChipBar;

// ─────────────────────────────────────────────────────────────────────────────
// Per-chip rendering
// ─────────────────────────────────────────────────────────────────────────────

function renderChip(
  descriptor: ChipDescriptor,
  state: ChipState,
  onStateChange: (next: ChipState) => void,
  entityMetadata: EntityMetadata | undefined,
  placeholderClass: string
): React.ReactNode {
  const current = state[descriptor.attribute];

  switch (descriptor.kind) {
    case 'text':
      return (
        <TextFilterChip
          key={descriptor.attribute}
          label={descriptor.label}
          value={current?.kind === 'text' ? current.value : undefined}
          onChange={next =>
            onStateChange(
              setChipValue(state, descriptor.attribute, next === undefined ? undefined : { kind: 'text', value: next })
            )
          }
        />
      );

    case 'optionset': {
      const selected = new Set<number>();
      if (current?.kind === 'optionset') {
        for (const v of current.values) {
          const n = typeof v === 'number' ? v : Number.parseInt(String(v), 10);
          if (!Number.isNaN(n)) selected.add(n);
        }
      }
      const metadataForChip = buildMetadataShim(descriptor, entityMetadata);
      return (
        <OptionSetMultiFilterChip
          key={descriptor.attribute}
          columnLogicalName={descriptor.attribute}
          entityMetadata={metadataForChip}
          label={descriptor.label}
          value={selected}
          onChange={nextSet => {
            if (nextSet.size === 0) {
              onStateChange(setChipValue(state, descriptor.attribute, undefined));
              return;
            }
            const values: number[] = [];
            nextSet.forEach(v => values.push(v));
            onStateChange(
              setChipValue(state, descriptor.attribute, {
                kind: 'optionset',
                values,
              })
            );
          }}
        />
      );
    }

    case 'daterange': {
      const bounds: UtcDateBounds | null =
        current?.kind === 'daterange' && (current.from || current.to)
          ? boundsFromIsoState(current.from, current.to)
          : null;
      return (
        <DateRangeFilterChip
          key={descriptor.attribute}
          label={descriptor.label}
          value={bounds}
          onChange={next => {
            if (!next) {
              onStateChange(setChipValue(state, descriptor.attribute, undefined));
              return;
            }
            onStateChange(
              setChipValue(state, descriptor.attribute, {
                kind: 'daterange',
                from: next.startUtc.toISOString(),
                to: next.endUtc.toISOString(),
              })
            );
          }}
        />
      );
    }

    case 'bool': {
      const boolVal: BoolFilterValue = current?.kind === 'bool' ? current.value : null;
      return (
        <BoolFilterChip
          key={descriptor.attribute}
          label={descriptor.label}
          value={boolVal}
          onChange={next => {
            if (next === null) {
              onStateChange(setChipValue(state, descriptor.attribute, undefined));
              return;
            }
            onStateChange(setChipValue(state, descriptor.attribute, { kind: 'bool', value: next }));
          }}
        />
      );
    }

    case 'lookup':
      // R1 limitation — see component-level JSDoc. Render a disabled placeholder
      // chip so the slot is visible but non-functional.
      return (
        <Text
          key={descriptor.attribute}
          className={placeholderClass}
          data-testid={`filter-chip-bar-lookup-placeholder-${descriptor.attribute}`}
        >
          {`${descriptor.label} (lookup not wired in R1)`}
        </Text>
      );

    default:
      return null;
  }
}

/**
 * Rehydrate a {@link UtcDateBounds} from previously-emitted ISO strings.
 * The conversion is lossy in principle (the original LOCAL Y/M/D is implicit
 * in the UTC instants) but round-trip-safe within the same timezone — which
 * matches how `DateRangeFilterChip` derives bounds via
 * {@link localDateToUtcBounds}.
 */
function boundsFromIsoState(from: string | undefined, to: string | undefined): UtcDateBounds | null {
  if (!from && !to) return null;
  const startSrc = from ?? to!;
  const endSrc = to ?? from!;
  const startDate = new Date(startSrc);
  const endDate = new Date(endSrc);
  if (Number.isNaN(startDate.getTime()) || Number.isNaN(endDate.getTime())) {
    return null;
  }
  // Re-derive the bounds against the LOCAL Y/M/D of each date so the
  // re-emitted bounds line up cleanly when applied again.
  return localDateToUtcBounds(startDate, endDate);
}
