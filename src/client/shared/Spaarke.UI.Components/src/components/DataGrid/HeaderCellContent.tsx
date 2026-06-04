/**
 * HeaderCellContent — Power-Apps-OOB-style per-column header content.
 *
 * Renders inside a Fluent v9 `<DataGridHeaderCell>` `renderHeaderCell` slot
 * (or any header surface). Provides the same column-options menu as Power
 * Apps OOB grids: a Label + dropdown chevron whose entire span is the menu
 * trigger, opening a menu with sort + filter + (R2) column-width actions.
 *
 * Menu layout (matches `column-filter-drop-down-oob-style.jpg`):
 *
 * ```
 * Column Name ⌄
 *              │
 *              └─ ↑  A to Z
 *                 ↓  Z to A
 *                 ─────────
 *                 ▽  Filter by  ►   (opens nested filter chip popover)
 *                 ─────────
 *                 ⥋  Column width  (disabled in R1)
 * ```
 *
 * **Active-filter indicator**: when `state[descriptor.attribute] !== undefined`,
 * a small {@link Filter16Filled} glyph is rendered to the LEFT of the label in
 * `tokens.colorBrandForeground1` — matching MDA's active-filter affordance.
 *
 * **Active-sort indicator**: when the caller supplies `sortDirection`, a small
 * `▲` or `▼` glyph is rendered between the label and the dropdown chevron
 * (matches the indicator used by `ColumnHeaderMenu`). When `null` / undefined,
 * only the dropdown chevron `⌄` is shown.
 *
 * **Filter-by submenu**: clicking the "Filter by" menu item closes the menu and
 * opens a nested `Popover` containing the appropriate filter chip primitive for
 * `descriptor.kind`. The per-kind dispatch shape mirrors `FilterChipBar` and
 * the previous funnel implementation 1:1 — the only change is the trigger UX.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1 (DataGrid framework R1)
 * **ADRs**: ADR-021 (Fluent v9 + tokens-only), ADR-022 (React-16-safe)
 *
 * **NFR-02 compliance**: zero raw hex; layout uses `tokens.*` exclusively.
 *
 * **NFR-03 compliance**: BOTH the menu popover and the filter chip popover
 * surfaces are re-wrapped in `<FluentProvider applyStylesToPortals theme={...}>`
 * so dark mode + customer-tenant themes propagate through React portals (see
 * `.claude/patterns/ui/fluent-v9-portal-gotcha.md`).
 *
 * **React-16-safe**: no `useId`, no `useSyncExternalStore`, no `createRoot`.
 *
 * **Backwards compat**: the API contract (label, descriptor, state,
 * onStateChange, theme, className) is unchanged — DataGrid.tsx does not need
 * any updates. The new `sortDirection` + `onSortChange` props are optional;
 * when `onSortChange` is undefined the sort menu items are hidden so callers
 * that have not yet wired sort behave exactly as before.
 *
 * @see ChipDescriptor    — discriminated-union descriptor produced by `discoverChips`
 * @see ChipState         — attribute → applied filter value map
 * @see FilterChipBar     — horizontal-strip equivalent that uses the same `ChipState`
 * @see ColumnHeaderMenu  — th-level equivalent (used by the `<table>` host path)
 */

import * as React from 'react';
import {
  Button,
  Checkbox,
  Divider,
  Dropdown,
  FluentProvider,
  Input,
  Menu,
  MenuDivider,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Option,
  Popover,
  PopoverSurface,
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  webLightTheme,
  type Theme,
} from '@fluentui/react-components';
import {
  ChevronDown16Regular,
  Dismiss16Regular,
  Filter16Filled,
  Filter20Filled,
  Filter20Regular,
  FilterDismiss20Regular,
  ArrowAutofitWidth20Regular,
  ArrowUp20Regular,
  ArrowDown20Regular,
} from '@fluentui/react-icons';

import { TextFilterChip } from './chips/TextFilterChip';
import { OptionSetMultiFilterChip } from './chips/OptionSetMultiFilterChip';
import { DateRangeFilterChip, localDateToUtcBounds } from './chips/DateRangeFilterChip';
import type { UtcDateBounds } from './chips/DateRangeFilterChip';
import { BoolFilterChip } from './chips/BoolFilterChip';
import type { BoolFilterValue } from './chips/BoolFilterChip';
import type { EntityMetadata, OptionSetOption } from '../../services/IDataverseClient';

import type { ChipDescriptor, ChipState, ChipValue } from './filterChips/types';

// ─────────────────────────────────────────────────────────────────────────────
// Public types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Active sort direction for this column. `null` (or `undefined`) means unsorted.
 *
 * Kept structurally identical to `SortDirection` in `columnHeader/ColumnHeaderMenu`
 * so callers can share a single sort-state map across both header surfaces.
 */
export type HeaderSortDirection = 'asc' | 'desc' | null;

/**
 * Props for {@link HeaderCellContent}.
 *
 * Designed to live inside Fluent v9 `<DataGridHeaderCell>`'s `renderHeaderCell`
 * slot. The component has no Fluent v9 DataGrid dependency itself and can be
 * reused in any header surface.
 */
export interface HeaderCellContentProps {
  /** Display label rendered as the column header text. */
  label: string;
  /**
   * Chip descriptor for this column, if filterable. When `undefined`, the
   * "Filter by" menu item is hidden. If `onSortChange` is also undefined and
   * `descriptor` is undefined, the component renders ONLY the plain label
   * (no chevron, no menu) so non-actionable columns stay quiet.
   */
  descriptor?: ChipDescriptor;
  /** Current {@link ChipState} (shared with the {@link FilterChipBar}). */
  state: ChipState;
  /** Same `onStateChange` the {@link FilterChipBar} uses. */
  onStateChange: (next: ChipState) => void;
  /**
   * Active theme — passed to the inner {@link FluentProvider} instances that
   * re-wrap the menu popover AND the filter-chip popover so dark mode resolves
   * correctly through React portals (NFR-03). Defaults to `webLightTheme`.
   */
  theme?: Theme;
  /** Optional className appended (Spaarke convention). */
  className?: string;
  /**
   * Current sort direction for this column, or `null` / `undefined` for
   * unsorted. Drives the inline sort arrow next to the label and the menu-item
   * checkmark when present. When `undefined`, treated as `null`.
   */
  sortDirection?: HeaderSortDirection;
  /**
   * Invoked when the user picks "A to Z" or "Z to A". When `undefined`, the
   * sort menu items are hidden (back-compat for callers that haven't wired
   * sort yet — e.g. the DataGrid host that today relies on Fluent v9 native
   * header-click sorting).
   */
  onSortChange?: (next: HeaderSortDirection) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (module scope per ADR-021)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'inline-flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    minWidth: 0,
    width: '100%',
  },
  // Entire trigger surface for the menu (label + active indicators + chevron).
  triggerButton: {
    display: 'inline-flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    minWidth: 0,
    flex: 1,
    justifyContent: 'flex-start',
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
  },
  label: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
    // Match OOB Power Apps grid column header — 14px semibold Segoe UI.
    fontSize: tokens.fontSizeBase300,
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    minWidth: 0,
  },
  plainLabel: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    minWidth: 0,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
  },
  // Inline filter glyph to the LEFT of the label when an active filter is set.
  filterIndicator: {
    display: 'inline-flex',
    alignItems: 'center',
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase200,
    flexShrink: 0,
  },
  // Inline sort glyph between the label and the dropdown chevron.
  sortIndicator: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightBold,
    flexShrink: 0,
  },
  // Persistent dropdown chevron — always visible when the menu is available.
  dropdownChevron: {
    color: tokens.colorNeutralForeground3,
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
  },
  /**
   * Outer wrapper for Fluent v9 `MenuPopover` — neutralized the same way as
   * `filterPopoverSurface`. Fluent's `MenuPopover` has hardcoded default
   * padding/border/shadow that beats our Griffel className on CSS specificity,
   * so the visible card styling lives on `menuCard` below.
   */
  menuPopoverSurface: {
    padding: '0',
    boxShadow: 'none',
    backgroundColor: 'transparent',
    ...shorthands.border('0'),
  },
  /**
   * The actual visible menu card. Inline `filter: drop-shadow` on the
   * MenuPopover (via style prop) is the brute-force shadow — `box-shadow`
   * on this card was being clipped by Fluent's portal wrapper's
   * `overflow: hidden`. Both layers are kept for defense in depth.
   */
  menuCard: {
    minWidth: '220px',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke3),
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: '0 8px 24px rgba(0, 0, 0, 0.22), 0 2px 8px rgba(0, 0, 0, 0.16)',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  /**
   * Wrapper for Fluent v9 `PopoverSurface` — we make IT transparent (zero
   * padding, no border, no shadow) and put all real card styling on an inner
   * `filterCard` div. Reason: Fluent v9's `PopoverSurface` has hardcoded
   * default padding/border/shadow that beats our Griffel className on CSS
   * specificity, so styling at this level was silently ignored.
   */
  filterPopoverSurface: {
    padding: '0',
    boxShadow: 'none',
    backgroundColor: 'transparent',
    ...shorthands.border('0'),
  },
  /**
   * The actual visible filter card. Owns the border, shadow, padding, and
   * background color. Hosted inside FluentProvider so portal dark-mode
   * styles propagate (NFR-03).
   */
  filterCard: {
    // Wider so the Operator dropdown's trigger button + clear glyph fits
    // cleanly inside the card without horizontal overflow.
    minWidth: '260px',
    maxWidth: '300px',
    backgroundColor: tokens.colorNeutralBackground1,
    // Subtle gray hairline — Stroke3 is the lightest visible stroke token.
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke3),
    borderRadius: tokens.borderRadiusMedium,
    // shadow28 = Fluent v9's standard dropdown/popover elevation tier.
    boxShadow: tokens.shadow28,
    paddingTop: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    paddingBottom: tokens.spacingVerticalL,
  },
  /**
   * Inner flex column INSIDE FluentProvider — direct parent of the
   * `filterHeader` + `Dropdown` + `Input` + `oobButtonRow` siblings. Without
   * this wrapper, `PopoverSurface`'s `rowGap` would only see ONE direct child
   * (FluentProvider) and the form elements would stack flush together.
   */
  filterPopoverBody: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
  },
  filterHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalS,
    // Visible vertical padding around the "Filter by" + close button so the
    // title row isn't visually flush with the dropdown below.
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalS,
  },
  filterTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase400,
  },
  /** OOB structured filter form — operator dropdown above the value field. */
  oobOperator: { width: '100%' },
  oobInput: { width: '100%' },
  /**
   * Apply / Clear buttons — explicit border-radius so they match the OOB
   * Power Apps "Filter by" pill shape (Fluent v9 default is too square).
   */
  oobApplyButton: {
    borderRadius: tokens.borderRadiusLarge,
  },
  oobClearButton: {
    // Subtle gray fill — `secondary` appearance gives the disabled-looking
    // background the user wants for Clear when no filter is active.
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorNeutralBackground3,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  oobButtonRow: {
    display: 'flex',
    alignItems: 'center',
    // Apply / Clear right-aligned at the bottom of the popover.
    justifyContent: 'flex-end',
    columnGap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalS,
  },
  oobOptionList: {
    display: 'flex',
    flexDirection: 'column',
    maxHeight: '240px',
    overflowY: 'auto',
    ...shorthands.padding(tokens.spacingVerticalXS, 0),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers (per-kind dispatch — mirrors FilterChipBar.renderChip)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Returns `true` when the parent {@link ChipState} has any non-`undefined`
 * value for the descriptor's attribute (i.e. this column has an active
 * filter). Drives the inline filter indicator.
 */
function isFilterActive(state: ChipState, descriptor: ChipDescriptor): boolean {
  return state[descriptor.attribute] !== undefined;
}

/**
 * Immutable "set or unset" helper — returns a new {@link ChipState} with the
 * given attribute set to `value` (or with the key removed when `value` is
 * `undefined`). Same semantics as the private helper in
 * {@link FilterChipBar}.
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
 * Build a minimal `EntityMetadata` shim for `OptionSetMultiFilterChip` from
 * the descriptor's `options`. The chip only needs
 * `attributes[X].optionSet` to render options, so when the host doesn't pass
 * full metadata we still get a functional chip from the descriptor alone.
 */
function buildMetadataShim(descriptor: ChipDescriptor): EntityMetadata {
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

/**
 * Rehydrate a {@link UtcDateBounds} from previously-emitted ISO strings —
 * mirrors the private helper in {@link FilterChipBar}.
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
  return localDateToUtcBounds(startDate, endDate);
}

// ─────────────────────────────────────────────────────────────────────────────
// PopoverFilterControl — internal per-kind dispatcher
// ─────────────────────────────────────────────────────────────────────────────

interface PopoverFilterControlProps {
  descriptor: ChipDescriptor;
  state: ChipState;
  onStateChange: (next: ChipState) => void;
}

/**
 * OOB Power Apps grid "Filter by" form. Renders a structured filter UI per
 * descriptor kind:
 *
 * - `text` / `lookup`: Operator dropdown (Equals / Contains / Begins with)
 *   + value Input + Apply / Clear buttons
 * - `optionset`: list of Checkboxes per option + Clear all
 * - `daterange`: From + To date inputs + Apply / Clear
 * - `bool`: Yes / No / All radio-style buttons + Apply / Clear
 *
 * Implementation parity with the proven `ColumnHeaderMenu.renderFilterContent`
 * pattern lifted from Events (task 004) — same JSX shape, same Fluent v9
 * primitives, same "draft local state → commit on Apply" UX.
 */
const PopoverFilterControl: React.FC<PopoverFilterControlProps> = ({ descriptor, state, onStateChange }) => {
  const styles = useStyles();
  const current = state[descriptor.attribute];

  // Local draft state — committed to chipState only when Apply is clicked.
  const initialText = current?.kind === 'text' ? current.value : '';
  const initialOp: 'equals' | 'contains' | 'begins' = (current?.kind === 'text' && current.op) || 'contains';
  const [textDraft, setTextDraft] = React.useState<string>(initialText);
  const [textOp, setTextOp] = React.useState<'equals' | 'contains' | 'begins'>(initialOp);
  React.useEffect(() => {
    setTextDraft(initialText);
    setTextOp(initialOp);
  }, [initialText, initialOp]);

  const initialDateFrom = current?.kind === 'daterange' ? (current.from ?? '').slice(0, 10) : '';
  const initialDateTo = current?.kind === 'daterange' ? (current.to ?? '').slice(0, 10) : '';
  const [dateFromDraft, setDateFromDraft] = React.useState<string>(initialDateFrom);
  const [dateToDraft, setDateToDraft] = React.useState<string>(initialDateTo);
  React.useEffect(() => {
    setDateFromDraft(initialDateFrom);
    setDateToDraft(initialDateTo);
  }, [initialDateFrom, initialDateTo]);

  const initialChoices = new Set<number>(
    current?.kind === 'optionset'
      ? current.values
          .map(v => (typeof v === 'number' ? v : Number.parseInt(String(v), 10)))
          .filter(n => !Number.isNaN(n))
      : []
  );
  const [choiceDraft, setChoiceDraft] = React.useState<Set<number>>(initialChoices);

  const hasActiveFilter = current !== undefined;

  // Apply / Clear handlers — write through to chipState.
  const applyText = () => {
    const next = textDraft.trim();
    onStateChange(
      setChipValue(state, descriptor.attribute, next ? { kind: 'text', value: next, op: textOp } : undefined)
    );
  };
  const applyDate = () => {
    if (!dateFromDraft && !dateToDraft) {
      onStateChange(setChipValue(state, descriptor.attribute, undefined));
      return;
    }
    onStateChange(
      setChipValue(state, descriptor.attribute, {
        kind: 'daterange',
        from: dateFromDraft ? `${dateFromDraft}T00:00:00.000Z` : undefined,
        to: dateToDraft ? `${dateToDraft}T23:59:59.999Z` : undefined,
      })
    );
  };
  const applyChoice = () => {
    if (choiceDraft.size === 0) {
      onStateChange(setChipValue(state, descriptor.attribute, undefined));
      return;
    }
    const values: number[] = [];
    choiceDraft.forEach(v => values.push(v));
    onStateChange(setChipValue(state, descriptor.attribute, { kind: 'optionset', values }));
  };
  const clearAll = () => {
    setTextDraft('');
    setDateFromDraft('');
    setDateToDraft('');
    setChoiceDraft(new Set<number>());
    onStateChange(setChipValue(state, descriptor.attribute, undefined));
  };

  switch (descriptor.kind) {
    case 'text':
    case 'lookup':
      return (
        <>
          <Dropdown
            className={styles.oobOperator}
            value={textOp === 'equals' ? 'Equals' : textOp === 'begins' ? 'Begins with' : 'Contains'}
            selectedOptions={[textOp]}
            onOptionSelect={(_e, data) => {
              const next = data.optionValue as 'equals' | 'contains' | 'begins' | undefined;
              if (next) setTextOp(next);
            }}
            appearance="outline"
          >
            <Option value="equals">Equals</Option>
            <Option value="contains">Contains</Option>
            <Option value="begins">Begins with</Option>
          </Dropdown>
          <Input
            className={styles.oobInput}
            value={textDraft}
            onChange={(_, data) => setTextDraft(data.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') applyText();
            }}
            appearance="outline"
          />
          <div className={styles.oobButtonRow}>
            <Button appearance="primary" size="small" className={styles.oobApplyButton} onClick={applyText}>
              Apply
            </Button>
            <Button
              appearance="secondary"
              size="small"
              className={styles.oobClearButton}
              onClick={clearAll}
              disabled={!textDraft && !hasActiveFilter}
            >
              Clear
            </Button>
          </div>
        </>
      );

    case 'optionset': {
      const options = descriptor.options ?? [];
      return (
        <>
          <div className={styles.oobOptionList}>
            {options.map(o => (
              <Checkbox
                key={String(o.value)}
                checked={choiceDraft.has(o.value)}
                onChange={(_, data) => {
                  const next = new Set(choiceDraft);
                  if (data.checked === true) next.add(o.value);
                  else next.delete(o.value);
                  setChoiceDraft(next);
                }}
                label={o.label}
              />
            ))}
          </div>
          <Divider />
          <div className={styles.oobButtonRow}>
            <Button appearance="primary" size="small" className={styles.oobApplyButton} onClick={applyChoice}>
              Apply
            </Button>
            <Button
              appearance="secondary"
              size="small"
              className={styles.oobClearButton}
              onClick={clearAll}
              disabled={choiceDraft.size === 0 && !hasActiveFilter}
            >
              Clear
            </Button>
          </div>
        </>
      );
    }

    case 'daterange':
      return (
        <>
          <Input
            className={styles.oobInput}
            type="date"
            value={dateFromDraft}
            placeholder="From"
            onChange={(_, data) => setDateFromDraft(data.value)}
            appearance="outline"
          />
          <Input
            className={styles.oobInput}
            type="date"
            value={dateToDraft}
            placeholder="To"
            onChange={(_, data) => setDateToDraft(data.value)}
            appearance="outline"
          />
          <div className={styles.oobButtonRow}>
            <Button appearance="primary" size="small" className={styles.oobApplyButton} onClick={applyDate}>
              Apply
            </Button>
            <Button
              appearance="secondary"
              size="small"
              className={styles.oobClearButton}
              onClick={clearAll}
              disabled={!dateFromDraft && !dateToDraft && !hasActiveFilter}
            >
              Clear
            </Button>
          </div>
        </>
      );

    case 'bool': {
      const boolVal: BoolFilterValue = current?.kind === 'bool' ? current.value : null;
      return (
        <BoolFilterChip
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

    default:
      return null;
  }
};

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Render a Power-Apps-OOB-style column header with chevron-menu sort + filter.
 *
 * @see HeaderCellContentProps
 */
export const HeaderCellContent: React.FC<HeaderCellContentProps> = ({
  label,
  descriptor,
  state,
  onStateChange,
  theme,
  className,
  sortDirection,
  onSortChange,
}) => {
  const styles = useStyles();
  const effectiveTheme = theme ?? webLightTheme;
  const effectiveSort: HeaderSortDirection = sortDirection ?? null;

  const filterable = descriptor !== undefined;
  const sortable = onSortChange !== undefined;
  // If nothing is actionable we render a plain label (no chevron, no menu).
  const hasMenu = filterable || sortable;

  const active = descriptor ? isFilterActive(state, descriptor) : false;

  // Menu open/close + filter sub-popover open/close.
  const [menuOpen, setMenuOpen] = React.useState<boolean>(false);
  const [filterOpen, setFilterOpen] = React.useState<boolean>(false);

  // Menu-item handlers.
  const handleSortAsc = React.useCallback(() => {
    onSortChange?.('asc');
    setMenuOpen(false);
  }, [onSortChange]);

  const handleSortDesc = React.useCallback(() => {
    onSortChange?.('desc');
    setMenuOpen(false);
  }, [onSortChange]);

  const handleFilterByClick = React.useCallback(() => {
    setMenuOpen(false);
    // Small delay so the menu can close before the filter popover opens —
    // avoids focus-trap fights between two simultaneously-mounted portal
    // surfaces. Same technique used by `ColumnHeaderMenu`.
    setTimeout(() => setFilterOpen(true), 50);
  }, []);

  const handleClearFilter = React.useCallback(() => {
    if (!descriptor) return;
    onStateChange(setChipValue(state, descriptor.attribute, undefined));
    setMenuOpen(false);
  }, [descriptor, onStateChange, state]);

  // ─────────────────────────────────────────────────────────────────────────
  // Render path 1 — plain label (no menu)
  // ─────────────────────────────────────────────────────────────────────────
  if (!hasMenu) {
    return (
      <span className={mergeClasses(styles.root, className)}>
        <Text className={styles.plainLabel} title={label}>
          {label}
        </Text>
      </span>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Render path 2 — Power-Apps-OOB chevron menu
  // ─────────────────────────────────────────────────────────────────────────
  // Ref to the column's chevron Button — used as the positioning target for
  // the Filter Popover so it anchors to the column header instead of floating
  // at the document root (Fluent v9 default when no PopoverTrigger exists).
  const anchorRef = React.useRef<HTMLButtonElement>(null);

  return (
    // Stop click propagation at the outer span so clicks on the chevron menu
    // trigger Button don't bubble up to the Fluent v9 <DataGridHeaderCell>,
    // which has its own onClick that toggles column sort when the column
    // definition is `sortable: true`. Without this, opening the chevron menu
    // also re-sorts the column — the long-standing "filter chevron also
    // sorts" bug. Task 035 UAT iteration 3 — confirmed root cause + fix.
    // eslint-disable-next-line jsx-a11y/no-static-element-interactions, jsx-a11y/click-events-have-key-events
    <span className={mergeClasses(styles.root, className)} onClick={e => e.stopPropagation()}>
      <Menu open={menuOpen} onOpenChange={(_e, data) => setMenuOpen(data.open)}>
        <MenuTrigger disableButtonEnhancement>
          <Button
            ref={anchorRef}
            appearance="transparent"
            size="small"
            className={styles.triggerButton}
            aria-label={`${label} column options`}
            data-testid={
              descriptor ? `header-cell-content-trigger-${descriptor.attribute}` : 'header-cell-content-trigger'
            }
          >
            <Text className={styles.label} title={label}>
              {label}
            </Text>
            {effectiveSort !== null && (
              <span className={styles.sortIndicator} aria-hidden="true">
                {effectiveSort === 'asc' ? '▲' : '▼'}
              </span>
            )}
            {/* Active-filter glyph lives inside the chevron group (between
                sort indicator and dropdown chevron) so the column looks like
                `Name ↑ ▼ ⌄` — Power Apps OOB pattern. */}
            {active && (
              <span className={styles.filterIndicator} aria-hidden="true">
                <Filter16Filled />
              </span>
            )}
            <span className={styles.dropdownChevron} aria-hidden="true">
              <ChevronDown16Regular />
            </span>
          </Button>
        </MenuTrigger>

        <MenuPopover
          className={styles.menuPopoverSurface}
          // Inline `filter: drop-shadow` so the elevation is visible even when
          // Fluent's portal wrapper otherwise clips `box-shadow`. `filter` is
          // applied to the popover surface itself (a transparent layer), so
          // its drop-shadow follows the rounded `menuCard` silhouette.
          style={{
            filter: 'drop-shadow(0 6px 12px rgba(0,0,0,0.18)) drop-shadow(0 2px 4px rgba(0,0,0,0.12))',
          }}
        >
          {/* PORTAL FIX (NFR-03): re-wrap popover body in FluentProvider so
              dark mode + customer-tenant themes propagate through React portals. */}
          <FluentProvider applyStylesToPortals theme={effectiveTheme}>
            <div className={styles.menuCard}>
              <MenuList>
                {sortable && (
                  <>
                    <MenuItem
                      icon={<ArrowUp20Regular />}
                      onClick={handleSortAsc}
                      data-testid="header-cell-content-menu-sort-asc"
                    >
                      A to Z
                    </MenuItem>
                    <MenuItem
                      icon={<ArrowDown20Regular />}
                      onClick={handleSortDesc}
                      data-testid="header-cell-content-menu-sort-desc"
                    >
                      Z to A
                    </MenuItem>
                    {(filterable || true) && <MenuDivider />}
                  </>
                )}

                {filterable && (
                  <MenuItem
                    icon={<Filter20Filled />}
                    onClick={handleFilterByClick}
                    data-testid={`header-cell-content-menu-filter-${descriptor!.attribute}`}
                  >
                    Filter by
                  </MenuItem>
                )}

                {/* Clear filter — only rendered when this column has an
                    active filter. Drops the chipState entry for the column's
                    attribute, restoring all rows for the current view. */}
                {filterable && active && (
                  <MenuItem
                    icon={<FilterDismiss20Regular />}
                    onClick={handleClearFilter}
                    data-testid={`header-cell-content-menu-clear-filter-${descriptor!.attribute}`}
                  >
                    Clear filter
                  </MenuItem>
                )}

                <MenuDivider />

                {/* Column width — R1 placeholder, disabled. */}
                <Tooltip content="Coming in R2" relationship="description">
                  <MenuItem
                    icon={<ArrowAutofitWidth20Regular />}
                    disabled
                    data-testid="header-cell-content-menu-column-width"
                  >
                    Column width
                  </MenuItem>
                </Tooltip>
              </MenuList>
            </div>
          </FluentProvider>
        </MenuPopover>
      </Menu>

      {/* Filter Popover — opens when the "Filter by" menu item is clicked.
          Held outside the Menu so it can survive the menu's own portal
          unmount. Re-wrapped in FluentProvider for the same NFR-03 reason. */}
      {filterable && filterOpen && (
        <Popover
          open={filterOpen}
          onOpenChange={(_e, data) => setFilterOpen(data.open)}
          trapFocus
          // Anchor the Popover to the column's chevron button so it appears
          // INSIDE the header (below the trigger), not at the document root.
          // Fluent v9 falls back to floating-at-origin when there's no
          // PopoverTrigger AND no positioning.target — both omissions explain
          // the previous "popover in upper-left of viewport" bug.
          positioning={{
            target: anchorRef.current,
            position: 'below',
            align: 'start',
            // 8px gap between the column-header chevron and the popover top edge
            // so the popover reads as a clearly-floating card rather than a
            // continuation of the header row.
            offset: { mainAxis: 8 },
          }}
        >
          <PopoverSurface
            className={styles.filterPopoverSurface}
            // Stop click + keydown propagation so interactions inside the
            // filter popover don't reach the underlying Fluent v9 header
            // cell (which would trigger native column sort).
            onClick={e => e.stopPropagation()}
            onMouseDown={e => e.stopPropagation()}
            onKeyDown={e => e.stopPropagation()}
          >
            <FluentProvider applyStylesToPortals theme={effectiveTheme}>
              <div className={styles.filterCard}>
                <div className={styles.filterPopoverBody}>
                  <div className={styles.filterHeader}>
                    <Text className={styles.filterTitle}>Filter by</Text>
                    <Button
                      appearance="subtle"
                      size="small"
                      icon={<Dismiss16Regular />}
                      onClick={() => setFilterOpen(false)}
                      aria-label="Close filter"
                      data-testid={`header-cell-content-filter-close-${descriptor!.attribute}`}
                    />
                  </div>
                  <PopoverFilterControl descriptor={descriptor!} state={state} onStateChange={onStateChange} />
                </div>
              </div>
            </FluentProvider>
          </PopoverSurface>
        </Popover>
      )}
    </span>
  );
};

export default HeaderCellContent;
