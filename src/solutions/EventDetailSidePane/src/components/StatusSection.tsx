/**
 * StatusSection - Status dropdown for Event Detail Side Pane
 *
 * Displays a compact dropdown for quick status changes.
 * Uses Fluent UI v9 Dropdown component.
 *
 * Status Reason Values (from Dataverse sprk_event.statuscode):
 * Active (statecode 0):
 *   1           = Draft
 *   659,490,001 = Open
 *   659,490,006 = On Hold
 * Inactive (statecode 1):
 *   659,490,002 = Completed
 *   659,490,003 = Closed
 *   659,490,004 = Cancelled
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/033-create-status-section.poml
 */

import * as React from "react";
import {
  Dropdown,
  Option,
  Label,
  makeStyles,
  tokens,
  shorthands,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Status reason values from Dataverse sprk_event.statuscode
 */
export type StatusReasonValue = number;

/**
 * Status reason option definition
 */
export interface StatusReasonOption {
  value: StatusReasonValue;
  label: string;
  description?: string;
}

/**
 * Props for the StatusSection component
 */
export interface StatusSectionProps {
  /** Currently selected status reason value */
  value: StatusReasonValue;
  /** Callback when status selection changes */
  onChange: (value: StatusReasonValue) => void;
  /** Whether the field is disabled (read-only mode) */
  disabled?: boolean;
  /** Optional label override */
  label?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Status reason options mapped from Dataverse sprk_event.statuscode values
 */
export const STATUS_REASON_OPTIONS: StatusReasonOption[] = [
  { value: 1, label: "Draft" },
  { value: 659490001, label: "Open" },
  { value: 659490002, label: "Completed" },
  { value: 659490003, label: "Closed" },
  { value: 659490006, label: "On Hold" },
  { value: 659490004, label: "Cancelled" },
];

const DEFAULT_LABEL = "Update Status";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
    ...shorthands.padding("8px", "20px"),
  },
  label: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const StatusSection: React.FC<StatusSectionProps> = ({
  value,
  onChange,
  disabled = false,
  label = DEFAULT_LABEL,
}) => {
  const styles = useStyles();

  const handleChange = React.useCallback(
    (_ev: unknown, data: { optionValue?: string }) => {
      if (data.optionValue) {
        const numericValue = parseInt(data.optionValue, 10) as StatusReasonValue;
        onChange(numericValue);
      }
    },
    [onChange]
  );

  const selectedLabel = React.useMemo(() => {
    return STATUS_REASON_OPTIONS.find((o) => o.value === value)?.label ?? "";
  }, [value]);

  return (
    <div className={styles.container}>
      <Label className={styles.label} htmlFor="status-dropdown">
        {label}
      </Label>
      <Dropdown
        id="status-dropdown"
        value={selectedLabel}
        selectedOptions={[String(value)]}
        onOptionSelect={handleChange}
        disabled={disabled}
        aria-label={`Select ${label.toLowerCase()}`}
        style={{ width: "100%" }}
      >
        {STATUS_REASON_OPTIONS.map((option) => (
          <Option key={option.value} value={String(option.value)}>
            {option.label}
          </Option>
        ))}
      </Dropdown>
    </div>
  );
};

/**
 * Get the label for a status reason value
 */
export function getStatusReasonLabel(value: StatusReasonValue): string {
  const option = STATUS_REASON_OPTIONS.find((opt) => opt.value === value);
  return option?.label ?? "Unknown";
}

/**
 * Check if a number is a valid status reason value
 */
export function isValidStatusReason(value: number): boolean {
  return STATUS_REASON_OPTIONS.some((opt) => opt.value === value);
}

export default StatusSection;
