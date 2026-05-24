/**
 * ColumnFilterHeader Component
 *
 * A table header cell that includes a filter icon and popover.
 * Matches OOB Power Apps grid column filtering behavior.
 *
 * Task 094: Add Column Filters to Grid Headers
 * - Shows filter icon on hover
 * - Opens filter popover on click
 * - Supports text, choice, date, and lookup column types
 * - Dark mode supported via Fluent UI tokens
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/094-add-column-filters-to-grid.poml
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Button,
  Input,
  Checkbox,
  Text,
  Divider,
} from "@fluentui/react-components";
import {
  Filter20Regular,
  Filter20Filled,
  Dismiss16Regular,
} from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type ColumnFilterType = "text" | "choice" | "date" | "lookup";

export interface ColumnFilterOption {
  /** Value to filter by */
  value: string | number;
  /** Display label */
  label: string;
}

export interface ColumnFilterHeaderProps {
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
  },
  headerContent: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.gap("8px"),
  },
  filterButton: {
    minWidth: "24px",
    width: "24px",
    height: "24px",
    ...shorthands.padding("0"),
    // Always visible - hover-based visibility doesn't work reliably in makeStyles
    opacity: 0.6,
    ":hover": {
      opacity: 1,
    },
  },
  filterButtonActive: {
    opacity: 1,
    color: tokens.colorBrandForeground1,
  },
  popoverSurface: {
    ...shorthands.padding("12px"),
    minWidth: "200px",
    maxWidth: "280px",
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
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ColumnFilterHeader - A table header with filtering capability.
 *
 * Renders a th element with the column title and a filter icon that opens
 * a popover with filter controls appropriate for the column type.
 */
export const ColumnFilterHeader: React.FC<ColumnFilterHeaderProps> = ({
  title,
  filterType,
  filterValue = "",
  selectedValues = [],
  options = [],
  onFilterChange,
  hasActiveFilter = false,
  className,
}) => {
  const styles = useStyles();
  const [isOpen, setIsOpen] = React.useState(false);
  const [localFilterValue, setLocalFilterValue] = React.useState(filterValue);

  // Sync local state with prop changes
  React.useEffect(() => {
    setLocalFilterValue(filterValue);
  }, [filterValue]);

  /**
   * Handle text filter apply
   */
  const handleTextFilterApply = React.useCallback(() => {
    onFilterChange(localFilterValue || null);
    setIsOpen(false);
  }, [localFilterValue, onFilterChange]);

  /**
   * Handle choice filter change
   */
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

  /**
   * Handle clear filter
   */
  const handleClear = React.useCallback(() => {
    setLocalFilterValue("");
    onFilterChange(null);
    setIsOpen(false);
  }, [onFilterChange]);

  /**
   * Handle key press in text input
   */
  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Enter") {
        handleTextFilterApply();
      }
    },
    [handleTextFilterApply]
  );

  // Render filter content based on type
  const renderFilterContent = () => {
    switch (filterType) {
      case "text":
      case "lookup":
        return (
          <>
            <Input
              className={styles.searchInput}
              placeholder={`Filter by ${title.toLowerCase()}...`}
              value={localFilterValue}
              onChange={(_, data) => setLocalFilterValue(data.value)}
              onKeyDown={handleKeyDown}
              appearance="outline"
            />
            <div style={{ display: "flex", gap: "8px" }}>
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
        // For now, use text input for date filtering
        // Future: Implement date range picker
        return (
          <>
            <Text size={200} style={{ marginBottom: "8px", display: "block" }}>
              Date filters coming soon
            </Text>
            <Input
              className={styles.searchInput}
              placeholder="YYYY-MM-DD"
              value={localFilterValue}
              onChange={(_, data) => setLocalFilterValue(data.value)}
              onKeyDown={handleKeyDown}
              appearance="outline"
            />
            <div style={{ display: "flex", gap: "8px" }}>
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
    <th className={`${styles.th} ${className || ""}`}>
      <div className={styles.headerContent}>
        <span>{title}</span>
        <Popover
          open={isOpen}
          onOpenChange={(_, data) => setIsOpen(data.open)}
          positioning="below-end"
        >
          <PopoverTrigger>
            <Button
              appearance="subtle"
              size="small"
              icon={hasActiveFilter ? <Filter20Filled /> : <Filter20Regular />}
              className={`${styles.filterButton} ${
                hasActiveFilter ? styles.filterButtonActive : ""
              }`}
              aria-label={`Filter ${title}`}
            />
          </PopoverTrigger>
          <PopoverSurface className={styles.popoverSurface}>
            <div className={styles.filterHeader}>
              <Text className={styles.filterTitle}>Filter: {title}</Text>
              <Button
                appearance="subtle"
                size="small"
                icon={<Dismiss16Regular />}
                onClick={() => setIsOpen(false)}
                aria-label="Close filter"
              />
            </div>
            {renderFilterContent()}
          </PopoverSurface>
        </Popover>
      </div>
    </th>
  );
};

export default ColumnFilterHeader;
