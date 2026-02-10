/**
 * StatusFilter Component
 *
 * Multi-select dropdown filter for filtering events by Event Status (sprk_eventstatus).
 * Uses Fluent UI v9 Combobox with multi-select capability.
 *
 * Event Status Values (sprk_eventstatus custom field):
 * - 0: Draft
 * - 1: Open
 * - 2: Completed
 * - 3: Closed
 * - 4: On Hold
 * - 5: Cancelled
 * - 6: Reassigned
 * - 7: Archived
 *
 * Default Selection: Actionable statuses (Draft, Open, On Hold) - excludes terminal statuses
 *
 * Features:
 * - Multi-select with chips display
 * - Actionable statuses selected by default
 * - Color-coded status badges in dropdown
 * - Dark mode support via design tokens
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/065-events-page-filters.poml
 * @see .claude/adr/ADR-021-fluent-design-system.md
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Combobox,
  Option,
  Badge,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Status option configuration
 */
export interface IStatusOption {
  /** Event status code (sprk_eventstatus value) */
  value: number;
  /** Display label */
  label: string;
  /** Badge color for visual distinction */
  color: "brand" | "success" | "warning" | "danger" | "informative" | "subtle";
  /** Badge appearance */
  appearance: "filled" | "outline" | "tint" | "ghost";
  /** Whether this status is considered "actionable" (not terminal) */
  isActionable: boolean;
}

/**
 * Props for StatusFilter component
 */
export interface StatusFilterProps {
  /** Currently selected status codes */
  selectedStatuses: number[];
  /** Callback when selection changes */
  onSelectionChange: (statusCodes: number[]) => void;
  /** Placeholder text when no selection */
  placeholder?: string;
  /** Disable the filter */
  disabled?: boolean;
  /** Auto-select actionable statuses on mount (default: true) */
  autoSelectActionable?: boolean;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * All available event status options
 * Matches Dataverse sprk_eventstatus values for sprk_event entity
 */
const STATUS_OPTIONS: IStatusOption[] = [
  {
    value: 0,
    label: "Draft",
    color: "subtle",
    appearance: "tint",
    isActionable: true,
  },
  {
    value: 1,
    label: "Open",
    color: "brand",
    appearance: "tint",
    isActionable: true,
  },
  {
    value: 2,
    label: "Completed",
    color: "success",
    appearance: "filled",
    isActionable: false,
  },
  {
    value: 3,
    label: "Closed",
    color: "informative",
    appearance: "ghost",
    isActionable: false,
  },
  {
    value: 4,
    label: "On Hold",
    color: "warning",
    appearance: "tint",
    isActionable: true,
  },
  {
    value: 5,
    label: "Cancelled",
    color: "danger",
    appearance: "ghost",
    isActionable: false,
  },
  {
    value: 6,
    label: "Reassigned",
    color: "informative",
    appearance: "tint",
    isActionable: false,
  },
  {
    value: 7,
    label: "Archived",
    color: "subtle",
    appearance: "ghost",
    isActionable: false,
  },
];

/**
 * Default selection: actionable statuses only
 * (Draft, Open, On Hold - excludes terminal statuses)
 */
const ACTIONABLE_STATUSES = STATUS_OPTIONS.filter((s) => s.isActionable).map(
  (s) => s.value
);

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
  },
  combobox: {
    minWidth: "160px",
    maxWidth: "250px",
  },
  optionContent: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
  },
  statusBadge: {
    textTransform: "capitalize",
    minWidth: "70px",
    justifyContent: "center",
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const StatusFilter: React.FC<StatusFilterProps> = ({
  selectedStatuses,
  onSelectionChange,
  placeholder = "Status...",
  disabled = false,
  autoSelectActionable = true,
}) => {
  const styles = useStyles();
  const [isInitialized, setIsInitialized] = React.useState(false);

  // Auto-select actionable statuses on mount if no selection provided
  React.useEffect(() => {
    if (!isInitialized && autoSelectActionable && selectedStatuses.length === 0) {
      onSelectionChange(ACTIONABLE_STATUSES);
    }
    setIsInitialized(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /**
   * Handle combobox selection change
   */
  const handleOptionSelect = React.useCallback(
    (
      _event: React.SyntheticEvent,
      data: { optionValue?: string; selectedOptions: string[] }
    ) => {
      // Convert string values back to numbers
      const selectedCodes = data.selectedOptions.map((v) => parseInt(v, 10));
      onSelectionChange(selectedCodes);
    },
    [onSelectionChange]
  );

  // Convert number[] to string[] for Combobox
  const selectedOptions = React.useMemo(
    () => selectedStatuses.map((s) => s.toString()),
    [selectedStatuses]
  );

  // Get display value for selected statuses
  const selectedValue = React.useMemo(() => {
    if (selectedStatuses.length === 0) return "";
    if (selectedStatuses.length === STATUS_OPTIONS.length) return "All statuses";
    if (
      selectedStatuses.length === ACTIONABLE_STATUSES.length &&
      ACTIONABLE_STATUSES.every((s) => selectedStatuses.includes(s))
    ) {
      return "Actionable";
    }
    const selectedLabels = STATUS_OPTIONS.filter((s) =>
      selectedStatuses.includes(s.value)
    ).map((s) => s.label);
    if (selectedLabels.length === 1) {
      return selectedLabels[0];
    }
    return `${selectedLabels.length} statuses`;
  }, [selectedStatuses]);

  return (
    <div className={styles.container}>
      <Combobox
        className={styles.combobox}
        placeholder={placeholder}
        multiselect
        selectedOptions={selectedOptions}
        onOptionSelect={handleOptionSelect}
        value={selectedValue}
        disabled={disabled}
        aria-label="Filter by event status"
      >
        {STATUS_OPTIONS.map((status) => (
          <Option
            key={status.value}
            value={status.value.toString()}
            text={status.label}
          >
            <div className={styles.optionContent}>
              <Badge
                appearance={status.appearance}
                color={status.color}
                className={styles.statusBadge}
              >
                {status.label}
              </Badge>
            </div>
          </Option>
        ))}
      </Combobox>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

export default StatusFilter;

/**
 * Get all available status options
 * Useful for external components that need status metadata
 */
export const getStatusOptions = (): IStatusOption[] => STATUS_OPTIONS;

/**
 * Get default actionable status codes
 * Use this when initializing filter state externally
 */
export const getActionableStatuses = (): number[] => [...ACTIONABLE_STATUSES];
