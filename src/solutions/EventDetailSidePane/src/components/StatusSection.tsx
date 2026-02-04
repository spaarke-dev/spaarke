/**
 * StatusSection - Status selection component for Event Detail Side Pane
 *
 * Displays status reason options as radio buttons for quick status changes.
 * Uses Fluent UI v9 RadioGroup component with proper theming.
 *
 * Status Reason Values:
 * - 1: Draft
 * - 2: Planned
 * - 3: Open
 * - 4: On Hold
 * - 5: Completed
 * - 6: Cancelled
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/033-create-status-section.poml
 */

import * as React from "react";
import {
  RadioGroup,
  Radio,
  Label,
  makeStyles,
  tokens,
  shorthands,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Status reason values from Dataverse sprk_event entity
 */
export type StatusReasonValue = 1 | 2 | 3 | 4 | 5 | 6;

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
 * Status reason options mapped from Dataverse statuscode values
 */
export const STATUS_REASON_OPTIONS: StatusReasonOption[] = [
  { value: 1, label: "Draft", description: "Event is in draft state" },
  { value: 2, label: "Planned", description: "Event is planned for future" },
  { value: 3, label: "Open", description: "Event is currently active" },
  { value: 4, label: "On Hold", description: "Event is temporarily paused" },
  { value: 5, label: "Completed", description: "Event has been completed" },
  { value: 6, label: "Cancelled", description: "Event has been cancelled" },
];

/**
 * Default label for the status section
 */
const DEFAULT_LABEL = "Status";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("8px"),
  },
  label: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    marginBottom: "4px",
  },
  radioGroup: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
  },
  radio: {
    // Allow radios to wrap naturally
  },
  // Horizontal layout variant for compact display
  radioGroupHorizontal: {
    display: "flex",
    flexDirection: "row",
    flexWrap: "wrap",
    ...shorthands.gap("12px", "8px"),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * StatusSection component for selecting event status reason.
 *
 * Uses Fluent UI v9 RadioGroup with vertical layout for clear selection.
 * Supports light, dark, and high-contrast themes via tokens.
 *
 * @example
 * ```tsx
 * const [status, setStatus] = useState<StatusReasonValue>(3);
 *
 * <StatusSection
 *   value={status}
 *   onChange={setStatus}
 *   disabled={!canEdit}
 * />
 * ```
 */
export const StatusSection: React.FC<StatusSectionProps> = ({
  value,
  onChange,
  disabled = false,
  label = DEFAULT_LABEL,
}) => {
  const styles = useStyles();

  /**
   * Handle radio selection change
   */
  const handleChange = React.useCallback(
    (_ev: React.FormEvent<HTMLDivElement>, data: { value: string }) => {
      const numericValue = parseInt(data.value, 10) as StatusReasonValue;
      onChange(numericValue);
    },
    [onChange]
  );

  return (
    <div className={styles.container}>
      <Label className={styles.label} htmlFor="status-radio-group">
        {label}
      </Label>
      <RadioGroup
        id="status-radio-group"
        className={styles.radioGroup}
        value={String(value)}
        onChange={handleChange}
        disabled={disabled}
        aria-label={`Select ${label.toLowerCase()}`}
      >
        {STATUS_REASON_OPTIONS.map((option) => (
          <Radio
            key={option.value}
            className={styles.radio}
            value={String(option.value)}
            label={option.label}
            aria-describedby={
              option.description ? `status-desc-${option.value}` : undefined
            }
          />
        ))}
      </RadioGroup>
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
export function isValidStatusReason(value: number): value is StatusReasonValue {
  return [1, 2, 3, 4, 5, 6].includes(value);
}

export default StatusSection;
