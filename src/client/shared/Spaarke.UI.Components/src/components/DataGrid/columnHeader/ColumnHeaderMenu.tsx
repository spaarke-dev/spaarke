/**
 * `<ColumnHeaderMenu />` — DataGrid framework column header with OOB Power Apps menu parity.
 *
 * A `<th>` that wraps the column title in a clickable Fluent v9 `<Menu>` exposing:
 *   - "A to Z" / "Z to A" sort options
 *   - "Filter by" → opens a `<Popover>` with text / choice / date / lookup filter controls
 *   - "Column width" / "Move left" / "Move right" placeholders (disabled — wired in later phases)
 *
 * **Lifted from** `@spaarke/events-components/components/ColumnHeaderMenu/ColumnHeaderMenu.tsx`
 * (task 004 of spaarke-datagrid-framework-r1).
 *
 * **Two material changes from the Events version**:
 *   1. **Portal fix (NFR-03 / ADR-021)** — every `MenuPopover` and `PopoverSurface` body is
 *      re-wrapped in `<FluentProvider applyStylesToPortals theme={inheritedTheme}>` so the
 *      popover inherits the active dark/light theme. The Events version relied on the outer
 *      provider's `applyStylesToPortals` propagating through, which fails in MDA dark mode
 *      and inside Code Page iframe hosts (the #1 dark-mode regression — see
 *      `.claude/patterns/ui/fluent-v9-portal-gotcha.md`).
 *   2. **De-Event-typed API** — `columnKey` was previously a generic key but the prop was
 *      tightly bound to Event-specific field names in callers. Renamed to `columnLogicalName`
 *      to make the contract explicit: this is a Dataverse attribute logical name, not an
 *      Event-specific identifier. Generic across all entities the framework targets.
 *
 * **ADR**: ADR-021 (Fluent v9 + dark mode), ADR-022 (React-16-safe)
 * **FR**: FR-DG-09 (column header menu), NFR-03 (applyStylesToPortals on every popover surface)
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/004-column-header-menu-lift.poml
 * @see .claude/patterns/ui/fluent-v9-portal-gotcha.md
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  shorthands,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
  Popover,
  PopoverSurface,
  Button,
  Input,
  Checkbox,
  Text,
  Divider,
  Dropdown,
  Option,
  FluentProvider,
  webLightTheme,
  type PartialTheme,
} from '@fluentui/react-components';
import {
  ArrowSortUp20Regular as _ArrowSortUp20Regular,
  ArrowSortDown20Regular as _ArrowSortDown20Regular,
  Filter20Regular,
  Filter20Filled,
  Filter16Filled,
  TextSortAscending20Regular,
  TextSortDescending20Regular,
  ArrowLeft20Regular,
  ArrowRight20Regular,
  ResizeTable20Regular,
  Dismiss16Regular,
  Checkmark16Regular,
  ChevronDown16Regular,
} from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type ColumnFilterType = 'text' | 'choice' | 'date' | 'lookup';

export type SortDirection = 'asc' | 'desc' | null;

export interface ColumnFilterOption {
  /** Value to filter by (string, number, or option-set value). */
  value: string | number;
  /** User-visible label. */
  label: string;
}

export interface ColumnHeaderMenuProps {
  /**
   * Dataverse attribute logical name (e.g., `sprk_status`, `sprk_amount`).
   *
   * Renamed from the Events-component `columnKey` prop. This is intentionally
   * the **logical name** (not display name, not column index) — it identifies the
   * Dataverse field that sort/filter actions target.
   */
  columnLogicalName: string;
  /** Column display name shown in the header. */
  title: string;
  /** Which filter UI to render when "Filter by" is invoked. */
  filterType: ColumnFilterType;
  /** Current filter value (used by `text` and `lookup` filter types). */
  filterValue?: string;
  /** Currently selected option-set values (used by `choice` filter type). */
  selectedValues?: (string | number)[];
  /** Option list (used by `choice` filter type). */
  options?: ColumnFilterOption[];
  /** Invoked when the filter value changes (apply / clear). */
  onFilterChange: (value: string | (string | number)[] | null) => void;
  /** Whether the column currently has an active filter (drives the inline filter glyph). */
  hasActiveFilter?: boolean;
  /** Current sort direction (drives the inline sort arrow + menu checkmark). */
  sortDirection?: SortDirection;
  /** Invoked when the user picks A→Z / Z→A. */
  onSortChange?: (direction: SortDirection) => void;
  /** Whether sorting is enabled for this column (hides A→Z / Z→A when false). */
  sortable?: boolean;
  /**
   * Theme passed to the **inner** FluentProvider that re-wraps every portal surface
   * (Menu popover, filter popover). Required to close the Fluent v9 portal theming
   * gotcha — see component-level JSDoc. Defaults to `webLightTheme` for stories /
   * tests that don't pass a theme; in production hosts the parent's theme MUST be
   * passed in so light + dark + customer-tenant themes all resolve correctly.
   *
   * @default webLightTheme
   */
  theme?: PartialTheme;
  /** Additional class merged AFTER component classes (per Spaarke convention). */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles — `makeStyles` at MODULE SCOPE per ADR-021 + fluent-v9-component checklist
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  th: {
    ...shorthands.padding('10px', '12px'),
    textAlign: 'left',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke1),
    whiteSpace: 'nowrap',
    position: 'relative',
    cursor: 'pointer',
    userSelect: 'none',
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    fontSize: tokens.fontSizeBase200,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  headerContent: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.gap('4px'),
    width: '100%',
  },
  titleWrapper: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('4px'),
    flex: 1,
  },
  // Persistent dropdown chevron — always visible, matching OOB Power Apps grid.
  dropdownChevron: {
    color: tokens.colorNeutralForeground3,
    display: 'flex',
    alignItems: 'center',
    marginLeft: '2px',
    flexShrink: 0,
  },
  sortIndicator: {
    fontSize: '10px',
    color: tokens.colorBrandForeground1,
    marginLeft: '2px',
    fontWeight: tokens.fontWeightBold,
  },
  // Inline filter glyph next to column name (also OOB parity).
  filterIndicator: {
    display: 'inline-flex',
    alignItems: 'center',
    marginLeft: '4px',
    color: tokens.colorBrandForeground1,
    fontSize: '12px',
    flexShrink: 0,
  },
  menuItem: {
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    fontSize: tokens.fontSizeBase200,
  },
  menuItemDisabled: {
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForegroundDisabled,
  },
  menuItemCheckmark: {
    marginRight: '8px',
    width: '16px',
    height: '16px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  filterPopoverSurface: {
    ...shorthands.padding('12px'),
    minWidth: '220px',
    maxWidth: '300px',
  },
  filterHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: '12px',
  },
  filterTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
  },
  equalsDropdown: {
    width: '100%',
    marginBottom: '8px',
  },
  searchInput: {
    width: '100%',
    marginBottom: '8px',
  },
  optionsList: {
    maxHeight: '200px',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap('4px'),
  },
  optionItem: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.padding('4px', '0'),
  },
  clearButton: {
    marginTop: '8px',
    width: '100%',
  },
  buttonRow: {
    display: 'flex',
    ...shorthands.gap('8px'),
  },
  divider: {
    marginTop: '8px',
    marginBottom: '8px',
  },
});

// Suppress unused-import lint noise. These icons are part of the public Fluent v9
// sort-icon set and are kept imported so consumers can swap them in without touching
// the import block. They are intentionally not referenced in the render path because
// `TextSortAscending/Descending20Regular` are the OOB-parity choices.
void _ArrowSortUp20Regular;
void _ArrowSortDown20Regular;

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Configuration-agnostic column header with OOB Power Apps menu parity.
 *
 * Renders a `<th>` containing the column title; clicking the cell opens a dropdown
 * menu with sort + filter options. Both the menu popover and the per-type filter
 * popover are re-wrapped in `<FluentProvider applyStylesToPortals theme={...}>` so
 * dark mode + customer-tenant themes propagate through React portals (NFR-03).
 */
export const ColumnHeaderMenu: React.FC<ColumnHeaderMenuProps> = ({
  columnLogicalName: _columnLogicalName,
  title,
  filterType,
  filterValue = '',
  selectedValues = [],
  options = [],
  onFilterChange,
  hasActiveFilter = false,
  sortDirection = null,
  onSortChange,
  sortable = true,
  theme = webLightTheme,
  className,
}) => {
  // `_columnLogicalName` is intentionally unused inside this component — it is the
  // public contract for callers (sort/filter handler wiring) and useful for any future
  // analytics/telemetry, but the menu UI itself is purely title-driven.
  void _columnLogicalName;

  const styles = useStyles();
  const [menuOpen, setMenuOpen] = React.useState<boolean>(false);
  const [filterOpen, setFilterOpen] = React.useState<boolean>(false);
  const [localFilterValue, setLocalFilterValue] = React.useState<string>(filterValue);

  // Ref to the header cell — used to position the filter popover below the th.
  const headerRef = React.useRef<HTMLTableCellElement>(null);

  // Sync local input state when parent prop changes.
  React.useEffect(() => {
    setLocalFilterValue(filterValue);
  }, [filterValue]);

  // ─────────────────────────────────────────────────────────────────────────
  // Sort handlers
  // ─────────────────────────────────────────────────────────────────────────

  const handleSortAsc = React.useCallback(() => {
    onSortChange?.('asc');
    setMenuOpen(false);
  }, [onSortChange]);

  const handleSortDesc = React.useCallback(() => {
    onSortChange?.('desc');
    setMenuOpen(false);
  }, [onSortChange]);

  // ─────────────────────────────────────────────────────────────────────────
  // Filter handlers
  // ─────────────────────────────────────────────────────────────────────────

  const handleFilterByClick = React.useCallback(() => {
    setMenuOpen(false);
    // Small delay so the menu can close before the filter popover opens — avoids
    // focus-trap fights between two simultaneously-mounted portal surfaces.
    setTimeout(() => setFilterOpen(true), 50);
  }, []);

  const handleTextFilterApply = React.useCallback(() => {
    onFilterChange(localFilterValue || null);
    setFilterOpen(false);
  }, [localFilterValue, onFilterChange]);

  const handleChoiceToggle = React.useCallback(
    (value: string | number, checked: boolean) => {
      const currentSet = new Set(selectedValues);
      if (checked) {
        currentSet.add(value);
      } else {
        currentSet.delete(value);
      }
      onFilterChange(Array.from(currentSet));
    },
    [selectedValues, onFilterChange],
  );

  const handleClear = React.useCallback(() => {
    setLocalFilterValue('');
    onFilterChange(null);
    setFilterOpen(false);
  }, [onFilterChange]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter') {
        handleTextFilterApply();
      }
    },
    [handleTextFilterApply],
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Render helpers
  // ─────────────────────────────────────────────────────────────────────────

  const renderSortIndicator = (): React.ReactNode => {
    if (!sortDirection) return null;
    return (
      <span className={styles.sortIndicator}>
        {sortDirection === 'asc' ? '▲' : '▼'}
      </span>
    );
  };

  const renderFilterContent = (): React.ReactNode => {
    switch (filterType) {
      case 'text':
      case 'lookup':
        return (
          <>
            <Dropdown
              className={styles.equalsDropdown}
              defaultSelectedOptions={['equals']}
              defaultValue="Equals"
              appearance="outline"
            >
              <Option value="equals">Equals</Option>
              <Option value="contains">Contains</Option>
              <Option value="begins">Begins with</Option>
            </Dropdown>
            <Input
              className={styles.searchInput}
              placeholder=""
              value={localFilterValue}
              onChange={(_, data) => setLocalFilterValue(data.value)}
              onKeyDown={handleKeyDown}
              appearance="outline"
            />
            <div className={styles.buttonRow}>
              <Button appearance="primary" size="small" onClick={handleTextFilterApply}>
                Apply
              </Button>
              <Button
                appearance="subtle"
                size="small"
                onClick={handleClear}
                disabled={!localFilterValue && !hasActiveFilter}
              >
                Clear
              </Button>
            </div>
          </>
        );

      case 'choice':
        return (
          <>
            <div className={styles.optionsList}>
              {options.map((option) => (
                <div key={String(option.value)} className={styles.optionItem}>
                  <Checkbox
                    checked={selectedValues.includes(option.value)}
                    onChange={(_, data) =>
                      handleChoiceToggle(option.value, data.checked === true)
                    }
                    label={option.label}
                  />
                </div>
              ))}
            </div>
            <Divider className={styles.divider} />
            <Button
              appearance="subtle"
              size="small"
              className={styles.clearButton}
              onClick={handleClear}
              disabled={selectedValues.length === 0}
            >
              Clear all
            </Button>
          </>
        );

      case 'date':
        return (
          <>
            <Input
              className={styles.searchInput}
              placeholder="YYYY-MM-DD"
              value={localFilterValue}
              onChange={(_, data) => setLocalFilterValue(data.value)}
              onKeyDown={handleKeyDown}
              appearance="outline"
              type="date"
            />
            <div className={styles.buttonRow}>
              <Button appearance="primary" size="small" onClick={handleTextFilterApply}>
                Apply
              </Button>
              <Button
                appearance="subtle"
                size="small"
                onClick={handleClear}
                disabled={!localFilterValue && !hasActiveFilter}
              >
                Clear
              </Button>
            </div>
          </>
        );

      default:
        return null;
    }
  };

  return (
    <th ref={headerRef} className={mergeClasses(styles.th, className)}>
      <Menu open={menuOpen} onOpenChange={(_, data) => setMenuOpen(data.open)}>
        <MenuTrigger disableButtonEnhancement>
          <div
            className={styles.headerContent}
            role="button"
            tabIndex={0}
            aria-label={`${title} column options`}
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                setMenuOpen((prev) => !prev);
              }
            }}
          >
            <div className={styles.titleWrapper}>
              <span>{title}</span>
              {renderSortIndicator()}
              {hasActiveFilter && (
                <span className={styles.filterIndicator}>
                  <Filter16Filled />
                </span>
              )}
              <span className={styles.dropdownChevron}>
                <ChevronDown16Regular />
              </span>
            </div>
          </div>
        </MenuTrigger>

        <MenuPopover>
          {/* PORTAL FIX (NFR-03): re-wrap popover body in FluentProvider so
              dark mode + customer-tenant themes propagate through React portals. */}
          <FluentProvider applyStylesToPortals theme={theme}>
            <MenuList>
              {sortable && (
                <>
                  <MenuItem
                    className={styles.menuItem}
                    icon={
                      <span className={styles.menuItemCheckmark}>
                        {sortDirection === 'asc' && <Checkmark16Regular />}
                      </span>
                    }
                    secondaryContent={<TextSortAscending20Regular />}
                    onClick={handleSortAsc}
                  >
                    A to Z
                  </MenuItem>
                  <MenuItem
                    className={styles.menuItem}
                    icon={
                      <span className={styles.menuItemCheckmark}>
                        {sortDirection === 'desc' && <Checkmark16Regular />}
                      </span>
                    }
                    secondaryContent={<TextSortDescending20Regular />}
                    onClick={handleSortDesc}
                  >
                    Z to A
                  </MenuItem>
                  <MenuDivider />
                </>
              )}

              <MenuItem
                className={styles.menuItem}
                icon={
                  <span className={styles.menuItemCheckmark}>
                    {hasActiveFilter && <Filter20Filled />}
                  </span>
                }
                secondaryContent={<Filter20Regular />}
                onClick={handleFilterByClick}
              >
                Filter by
              </MenuItem>

              <MenuDivider />

              <MenuItem
                className={styles.menuItemDisabled}
                icon={<span className={styles.menuItemCheckmark} />}
                secondaryContent={<ResizeTable20Regular />}
                disabled
              >
                Column width
              </MenuItem>

              <MenuDivider />

              <MenuItem
                className={styles.menuItemDisabled}
                icon={<span className={styles.menuItemCheckmark} />}
                secondaryContent={<ArrowLeft20Regular />}
                disabled
              >
                Move left
              </MenuItem>
              <MenuItem
                className={styles.menuItemDisabled}
                icon={<span className={styles.menuItemCheckmark} />}
                secondaryContent={<ArrowRight20Regular />}
                disabled
              >
                Move right
              </MenuItem>
            </MenuList>
          </FluentProvider>
        </MenuPopover>
      </Menu>

      {/* Filter Popover (opened when "Filter by" is clicked).
          Anchored to the header cell via the `target` prop so it tracks the
          header position even when the table scrolls. */}
      {filterOpen && (
        <Popover
          open={filterOpen}
          onOpenChange={(_, data) => setFilterOpen(data.open)}
          positioning={{
            target: headerRef.current,
            position: 'below',
            align: 'start',
            offset: { mainAxis: 4 },
          }}
          trapFocus
        >
          <PopoverSurface className={styles.filterPopoverSurface}>
            {/* PORTAL FIX (NFR-03): same re-wrap as MenuPopover above. */}
            <FluentProvider applyStylesToPortals theme={theme}>
              <div className={styles.filterHeader}>
                <Text className={styles.filterTitle}>Filter by</Text>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<Dismiss16Regular />}
                  onClick={() => setFilterOpen(false)}
                  aria-label="Close filter"
                />
              </div>
              {renderFilterContent()}
            </FluentProvider>
          </PopoverSurface>
        </Popover>
      )}
    </th>
  );
};

export default ColumnHeaderMenu;
