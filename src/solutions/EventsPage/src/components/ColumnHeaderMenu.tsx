/**
 * ColumnHeaderMenu Component
 *
 * A table header cell that matches OOB Power Apps grid column header behavior.
 * The entire header is clickable and shows a dropdown menu with:
 * - A to Z (sort ascending)
 * - Z to A (sort descending)
 * - Filter by (opens filter panel)
 * - Column width (placeholder)
 * - Move left / Move right (placeholder)
 *
 * Task 097: Column Header Menu OOB Parity
 * - Replaces ColumnFilterHeader with OOB-style menu
 * - Shows sort indicator next to sorted column name
 * - Shows filter indicator when filter is active
 * - Dark mode supported via Fluent UI tokens
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/097-column-header-menu-oob-parity.poml
 */

import * as React from "react";
import {
  makeStyles,
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
} from "@fluentui/react-components";
import {
  ArrowSortUp20Regular,
  ArrowSortDown20Regular,
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
} from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type ColumnFilterType = "text" | "choice" | "date" | "lookup";

export type SortDirection = "asc" | "desc" | null;

export interface ColumnFilterOption {
  /** Value to filter by */
  value: string | number;
  /** Display label */
  label: string;
}

export interface ColumnHeaderMenuProps {
  /** Column key/identifier for sorting and filtering */
  columnKey: string;
  /** Column display name */
  title: string;
  /** Type of filter to show */
  filterType: ColumnFilterType;
  /** Current filter value (for text/lookup) */
  filterValue?: string;
  /** Current selected values (for choice) */
  selectedValues?: (string | number)[];
  /** Available options (for choice filters) */
  options?: ColumnFilterOption[];
  /** Callback when filter changes */
  onFilterChange: (value: string | (string | number)[] | null) => void;
  /** Whether column has an active filter */
  hasActiveFilter?: boolean;
  /** Current sort direction for this column */
  sortDirection?: SortDirection;
  /** Callback when sort changes */
  onSortChange?: (direction: SortDirection) => void;
  /** Whether sorting is enabled for this column */
  sortable?: boolean;
  /** Additional className for the th element */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  th: {
    ...shorthands.padding("10px", "12px"),
    textAlign: "left",
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
    whiteSpace: "nowrap",
    position: "relative",
    cursor: "pointer",
    userSelect: "none",
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    fontSize: tokens.fontSizeBase200,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  headerContent: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.gap("4px"),
    width: "100%",
  },
  titleWrapper: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("4px"),
    flex: 1,
  },
  // Persistent dropdown chevron - always visible like OOB
  dropdownChevron: {
    color: tokens.colorNeutralForeground3,
    display: "flex",
    alignItems: "center",
    marginLeft: "2px",
    flexShrink: 0,
  },
  sortIndicator: {
    fontSize: "10px",
    color: tokens.colorBrandForeground1,
    marginLeft: "2px",
    fontWeight: tokens.fontWeightBold,
  },
  // Filter indicator - inline with column name like OOB
  filterIndicator: {
    display: "inline-flex",
    alignItems: "center",
    marginLeft: "4px",
    color: tokens.colorBrandForeground1,
    fontSize: "12px",
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
  menuItemIcon: {
    marginRight: "8px",
  },
  menuItemCheckmark: {
    marginRight: "8px",
    width: "16px",
    height: "16px",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  filterPopoverSurface: {
    ...shorthands.padding("12px"),
    minWidth: "220px",
    maxWidth: "300px",
  },
  filterHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: "12px",
  },
  filterTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
  },
  equalsDropdown: {
    width: "100%",
    marginBottom: "8px",
  },
  searchInput: {
    width: "100%",
    marginBottom: "8px",
  },
  optionsList: {
    maxHeight: "200px",
    overflowY: "auto",
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
  },
  optionItem: {
    display: "flex",
    alignItems: "center",
    ...shorthands.padding("4px", "0"),
  },
  clearButton: {
    marginTop: "8px",
    width: "100%",
  },
  buttonRow: {
    display: "flex",
    ...shorthands.gap("8px"),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ColumnHeaderMenu - A table header with OOB Power Apps grid behavior.
 *
 * Renders a th element with the column title. Clicking opens a dropdown menu
 * with sort options, filter option, and column management options.
 */
export const ColumnHeaderMenu: React.FC<ColumnHeaderMenuProps> = ({
  columnKey,
  title,
  filterType,
  filterValue = "",
  selectedValues = [],
  options = [],
  onFilterChange,
  hasActiveFilter = false,
  sortDirection = null,
  onSortChange,
  sortable = true,
  className,
}) => {
  const styles = useStyles();
  const [menuOpen, setMenuOpen] = React.useState(false);
  const [filterOpen, setFilterOpen] = React.useState(false);
  const [localFilterValue, setLocalFilterValue] = React.useState(filterValue);

  // Ref to the header cell for Popover positioning (Fix: anchor filter popover to th)
  const headerRef = React.useRef<HTMLTableCellElement>(null);

  // Sync local state with prop changes
  React.useEffect(() => {
    setLocalFilterValue(filterValue);
  }, [filterValue]);

  // ─────────────────────────────────────────────────────────────────────────
  // Sort Handlers
  // ─────────────────────────────────────────────────────────────────────────

  const handleSortAsc = React.useCallback(() => {
    onSortChange?.("asc");
    setMenuOpen(false);
  }, [onSortChange]);

  const handleSortDesc = React.useCallback(() => {
    onSortChange?.("desc");
    setMenuOpen(false);
  }, [onSortChange]);

  // ─────────────────────────────────────────────────────────────────────────
  // Filter Handlers
  // ─────────────────────────────────────────────────────────────────────────

  const handleFilterByClick = React.useCallback(() => {
    setMenuOpen(false);
    // Small delay to allow menu to close before opening filter popover
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
    [selectedValues, onFilterChange]
  );

  const handleClear = React.useCallback(() => {
    setLocalFilterValue("");
    onFilterChange(null);
    setFilterOpen(false);
  }, [onFilterChange]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Enter") {
        handleTextFilterApply();
      }
    },
    [handleTextFilterApply]
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Render Sort Indicator
  // ─────────────────────────────────────────────────────────────────────────

  const renderSortIndicator = () => {
    if (!sortDirection) return null;
    return (
      <span className={styles.sortIndicator}>
        {sortDirection === "asc" ? "\u25B2" : "\u25BC"}
      </span>
    );
  };

  // ─────────────────────────────────────────────────────────────────────────
  // Render Filter Content
  // ─────────────────────────────────────────────────────────────────────────

  const renderFilterContent = () => {
    switch (filterType) {
      case "text":
      case "lookup":
        return (
          <>
            <Dropdown
              className={styles.equalsDropdown}
              defaultSelectedOptions={["equals"]}
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
              <Button
                appearance="primary"
                size="small"
                onClick={handleTextFilterApply}
              >
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

      case "choice":
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
            <Divider style={{ margin: "8px 0" }} />
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

      case "date":
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
              <Button
                appearance="primary"
                size="small"
                onClick={handleTextFilterApply}
              >
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
    <th ref={headerRef} className={`${styles.th} ${className || ""}`}>
      <Menu open={menuOpen} onOpenChange={(_, data) => setMenuOpen(data.open)}>
        <MenuTrigger disableButtonEnhancement>
          <div
            className={styles.headerContent}
            role="button"
            tabIndex={0}
            aria-label={`${title} column options`}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") {
                setMenuOpen(!menuOpen);
              }
            }}
          >
            <div className={styles.titleWrapper}>
              <span>{title}</span>
              {renderSortIndicator()}
              {/* Filter indicator - inline with column name like OOB */}
              {hasActiveFilter && (
                <span className={styles.filterIndicator}>
                  <Filter16Filled />
                </span>
              )}
              {/* Always-visible dropdown chevron like OOB */}
              <span className={styles.dropdownChevron}>
                <ChevronDown16Regular />
              </span>
            </div>
          </div>
        </MenuTrigger>

        <MenuPopover>
          <MenuList>
            {/* Sort Options */}
            {sortable && (
              <>
                <MenuItem
                  className={styles.menuItem}
                  icon={
                    <span className={styles.menuItemCheckmark}>
                      {sortDirection === "asc" && <Checkmark16Regular />}
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
                      {sortDirection === "desc" && <Checkmark16Regular />}
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

            {/* Filter Option */}
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

            {/* Column Width - Placeholder (disabled) */}
            <MenuItem
              className={styles.menuItemDisabled}
              icon={
                <span className={styles.menuItemCheckmark} />
              }
              secondaryContent={<ResizeTable20Regular />}
              disabled
            >
              Column width
            </MenuItem>

            <MenuDivider />

            {/* Move Left/Right - Placeholders (disabled) */}
            <MenuItem
              className={styles.menuItemDisabled}
              icon={
                <span className={styles.menuItemCheckmark} />
              }
              secondaryContent={<ArrowLeft20Regular />}
              disabled
            >
              Move left
            </MenuItem>
            <MenuItem
              className={styles.menuItemDisabled}
              icon={
                <span className={styles.menuItemCheckmark} />
              }
              secondaryContent={<ArrowRight20Regular />}
              disabled
            >
              Move right
            </MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>

      {/* Filter Popover (opened when "Filter by" is clicked) */}
      {/* Position below header cell using target prop */}
      {filterOpen && (
        <Popover
          open={filterOpen}
          onOpenChange={(_, data) => setFilterOpen(data.open)}
          positioning={{
            target: headerRef.current,
            position: "below",
            align: "start",
            offset: { mainAxis: 4 },
          }}
          trapFocus
        >
          <PopoverSurface className={styles.filterPopoverSurface}>
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
          </PopoverSurface>
        </Popover>
      )}
    </th>
  );
};

export default ColumnHeaderMenu;
