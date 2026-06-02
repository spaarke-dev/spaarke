/**
 * `<ColumnFilterHeader />` — lightweight column header with an inline filter button.
 *
 * Renders a `<th>` containing the column title and a Fluent v9 filter `<Button>` that
 * opens a `<Popover>` with filter controls appropriate to the column's filter type.
 *
 * **Lifted from** `@spaarke/events-components/components/ColumnFilterHeader/ColumnFilterHeader.tsx`
 * (task 004 of spaarke-datagrid-framework-r1).
 *
 * **Two material changes from the Events version**:
 *   1. **Portal fix (NFR-03 / ADR-021)** — the `PopoverSurface` body is re-wrapped in
 *      `<FluentProvider applyStylesToPortals theme={inheritedTheme}>` so the popover
 *      inherits the active dark/light theme. See `.claude/patterns/ui/fluent-v9-portal-gotcha.md`.
 *   2. **De-Event-typed API** — accepts a new optional `columnLogicalName` prop to make
 *      the component identifiable to callers wiring sort/filter handlers (generic across
 *      all entities the framework targets). The title prop remains for display.
 *
 * **When to use this vs. `<ColumnHeaderMenu />`**:
 *   - `ColumnFilterHeader` — minimal: filter button only; no sort menu, no OOB chevron.
 *   - `ColumnHeaderMenu` — full OOB Power Apps parity: sort + filter + column-management menu.
 *   Most DataGrid framework consumers want `ColumnHeaderMenu`. `ColumnFilterHeader` exists
 *   for legacy / non-OOB layouts where the filter affordance is the only column action.
 *
 * **ADR**: ADR-021 (Fluent v9 + dark mode), ADR-022 (React-16-safe)
 * **FR**: FR-DG-09 (column header), NFR-03 (applyStylesToPortals on every popover surface)
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
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Button,
  Input,
  Checkbox,
  Text,
  Divider,
  FluentProvider,
  webLightTheme,
  type PartialTheme,
} from '@fluentui/react-components';
import {
  Filter20Regular,
  Filter20Filled,
  Dismiss16Regular,
} from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type ColumnFilterType = 'text' | 'choice' | 'date' | 'lookup';

export interface ColumnFilterOption {
  /** Value to filter by. */
  value: string | number;
  /** User-visible label. */
  label: string;
}

export interface ColumnFilterHeaderProps {
  /**
   * OPTIONAL Dataverse attribute logical name (e.g., `sprk_status`). Forwarded to
   * callers in onFilterChange handlers when they need a column identifier to wire
   * grid-state. Renamed from the Events-component implicit `columnKey` model. The
   * component itself does not display this — it is metadata for handlers.
   */
  columnLogicalName?: string;
  /** Column display name shown in the header. */
  title: string;
  /** Which filter UI to render. */
  filterType: ColumnFilterType;
  /** Current filter value (used by `text` and `lookup` filter types). */
  filterValue?: string;
  /** Currently selected option-set values (used by `choice` filter type). */
  selectedValues?: (string | number)[];
  /** Option list (used by `choice` filter type). */
  options?: ColumnFilterOption[];
  /** Invoked when the filter value changes (apply / clear). */
  onFilterChange: (value: string | (string | number)[] | null) => void;
  /** Whether the column currently has an active filter (toggles filled icon). */
  hasActiveFilter?: boolean;
  /**
   * Theme passed to the **inner** FluentProvider that re-wraps the popover surface.
   * Required to close the Fluent v9 portal theming gotcha. See component-level JSDoc.
   *
   * @default webLightTheme
   */
  theme?: PartialTheme;
  /** Additional class merged AFTER component classes. */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles — module scope per ADR-021 + fluent-v9-component checklist
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
  },
  headerContent: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.gap('8px'),
  },
  filterButton: {
    minWidth: '24px',
    width: '24px',
    height: '24px',
    ...shorthands.padding('0'),
    // Always visible at 60% opacity — hover-based opacity is unreliable across
    // makeStyles' :hover and the Fluent v9 button's internal hover stack, so we
    // simply lift to full opacity on hover.
    opacity: 0.6,
    ':hover': {
      opacity: 1,
    },
  },
  filterButtonActive: {
    opacity: 1,
    color: tokens.colorBrandForeground1,
  },
  popoverSurface: {
    ...shorthands.padding('12px'),
    minWidth: '200px',
    maxWidth: '280px',
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
  dateHint: {
    marginBottom: '8px',
    display: 'block',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Configuration-agnostic column header with an inline filter button. See module
 * JSDoc for the rationale on portal re-wrap and the de-Event-typed API.
 */
export const ColumnFilterHeader: React.FC<ColumnFilterHeaderProps> = ({
  columnLogicalName: _columnLogicalName,
  title,
  filterType,
  filterValue = '',
  selectedValues = [],
  options = [],
  onFilterChange,
  hasActiveFilter = false,
  theme = webLightTheme,
  className,
}) => {
  // `_columnLogicalName` is intentionally unused inside the component body — it is
  // forward-only metadata for callers' onFilterChange handlers.
  void _columnLogicalName;

  const styles = useStyles();
  const [isOpen, setIsOpen] = React.useState<boolean>(false);
  const [localFilterValue, setLocalFilterValue] = React.useState<string>(filterValue);

  React.useEffect(() => {
    setLocalFilterValue(filterValue);
  }, [filterValue]);

  const handleTextFilterApply = React.useCallback(() => {
    onFilterChange(localFilterValue || null);
    setIsOpen(false);
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
    setIsOpen(false);
  }, [onFilterChange]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter') {
        handleTextFilterApply();
      }
    },
    [handleTextFilterApply],
  );

  const renderFilterContent = (): React.ReactNode => {
    switch (filterType) {
      case 'text':
      case 'lookup':
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
        // Reuses native HTML date input — the dedicated DateRangeFilterChip
        // (task 007) is the richer date-range UX. ColumnFilterHeader keeps a
        // single-date fallback so callers that don't need range still work.
        return (
          <>
            <Text size={200} className={styles.dateHint}>
              Date filters coming soon
            </Text>
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
              <Button appearance="subtle" size="small" onClick={handleClear}>
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
    <th className={mergeClasses(styles.th, className)}>
      <div className={styles.headerContent}>
        <span>{title}</span>
        <Popover
          open={isOpen}
          onOpenChange={(_, data) => setIsOpen(data.open)}
          positioning="below-end"
        >
          <PopoverTrigger disableButtonEnhancement>
            <Button
              appearance="subtle"
              size="small"
              icon={hasActiveFilter ? <Filter20Filled /> : <Filter20Regular />}
              className={mergeClasses(
                styles.filterButton,
                hasActiveFilter ? styles.filterButtonActive : undefined,
              )}
              aria-label={`Filter ${title}`}
              aria-haspopup="dialog"
              aria-expanded={isOpen}
            />
          </PopoverTrigger>
          <PopoverSurface className={styles.popoverSurface}>
            {/* PORTAL FIX (NFR-03): inner FluentProvider re-wrap so the popover
                inherits the active dark/light theme through React portals. */}
            <FluentProvider applyStylesToPortals theme={theme}>
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
            </FluentProvider>
          </PopoverSurface>
        </Popover>
      </div>
    </th>
  );
};

export default ColumnFilterHeader;
