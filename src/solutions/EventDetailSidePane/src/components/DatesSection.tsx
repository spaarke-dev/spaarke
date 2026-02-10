/**
 * DatesSection - Collapsible dates section for Event Detail Side Pane
 *
 * Displays date-related fields in a collapsible section:
 * - Base Date (DatePicker)
 * - Final Due Date (DatePicker)
 * - Remind At (DateTimePicker - date + time)
 *
 * This section is conditionally visible based on Event Type configuration.
 * Uses Fluent UI v9 components with proper theming support.
 *
 * @see projects/events-workspace-apps-UX-r1/design.md - Dates section spec
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Field,
  Input,
  Spinner,
} from "@fluentui/react-components";
import { DatePicker } from "@fluentui/react-datepicker-compat";
import {
  CalendarRegular,
  CalendarMonthRegular,
  AlertRegular,
  ClockRegular,
} from "@fluentui/react-icons";
import { CollapsibleSection } from "./CollapsibleSection";

// -----------------------------------------------------------------------------
// Types
// -----------------------------------------------------------------------------

/**
 * Date field value type - can be ISO string or null
 */
export type DateFieldValue = string | null;

/**
 * Props for the DatesSection component
 */
export interface DatesSectionProps {
  /** Base date value (ISO string or null) */
  baseDate: DateFieldValue;
  /** Callback when base date changes */
  onBaseDateChange: (date: Date | null) => void;
  /** Final due date value (ISO string or null) */
  finalDueDate: DateFieldValue;
  /** Callback when final due date changes */
  onFinalDueDateChange: (date: Date | null) => void;
  /** Remind at datetime value (ISO string or null) */
  remindAt: DateFieldValue;
  /** Callback when remind at changes */
  onRemindAtChange: (datetime: Date | null) => void;
  /** Whether the section starts expanded (default: false per design.md) */
  defaultExpanded?: boolean;
  /** Controlled expanded state */
  expanded?: boolean;
  /** Callback when expanded state changes */
  onExpandedChange?: (expanded: boolean) => void;
  /** Whether fields are disabled (read-only mode) */
  disabled?: boolean;
  /** Loading state */
  isLoading?: boolean;
}

// -----------------------------------------------------------------------------
// Styles
// -----------------------------------------------------------------------------

const useStyles = makeStyles({
  fieldsContainer: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("16px"),
  },
  fieldLabel: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("6px"),
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
  },
  fieldIcon: {
    fontSize: "14px",
    color: tokens.colorNeutralForeground3,
  },
  datePicker: {
    width: "100%",
  },
  dateTimeRow: {
    display: "flex",
    ...shorthands.gap("8px"),
    alignItems: "flex-start",
  },
  datePickerSmall: {
    flexGrow: 1,
    flexBasis: "60%",
    minWidth: 0,
  },
  timeInput: {
    flexGrow: 0,
    flexBasis: "40%",
    minWidth: "100px",
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("24px"),
  },
});

// -----------------------------------------------------------------------------
// Helper Functions
// -----------------------------------------------------------------------------

/**
 * Parse ISO date string to Date object
 */
function parseISODate(isoString: string | null | undefined): Date | null {
  if (!isoString) return null;
  try {
    const date = new Date(isoString);
    return isNaN(date.getTime()) ? null : date;
  } catch {
    return null;
  }
}

/**
 * Format Date object to display string (date only)
 */
function formatDateForDisplay(date: Date | null): string {
  if (!date) return "";
  return date.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

/**
 * Extract time string (HH:MM) from Date object
 */
function extractTimeString(date: Date | null): string {
  if (!date) return "";
  const hours = date.getHours().toString().padStart(2, "0");
  const minutes = date.getMinutes().toString().padStart(2, "0");
  return `${hours}:${minutes}`;
}

/**
 * Combine date and time string into a single Date object
 */
function combineDateAndTime(date: Date | null, timeString: string): Date | null {
  if (!date) return null;

  const combined = new Date(date);

  if (timeString) {
    const [hours, minutes] = timeString.split(":").map(Number);
    if (!isNaN(hours) && !isNaN(minutes)) {
      combined.setHours(hours, minutes, 0, 0);
    }
  } else {
    // Default to 9:00 AM if no time specified
    combined.setHours(9, 0, 0, 0);
  }

  return combined;
}

// -----------------------------------------------------------------------------
// Component
// -----------------------------------------------------------------------------

/**
 * DatesSection component for displaying and editing date-related event fields.
 *
 * Rendered as a collapsible section that can be expanded/collapsed.
 * Contains three date fields:
 * - Base Date: The base/trigger date for the event
 * - Final Due Date: The final/hard deadline for the event
 * - Remind At: DateTime for when to send reminders
 *
 * Uses Fluent UI v9 components with proper dark mode support via tokens.
 *
 * @example
 * ```tsx
 * <DatesSection
 *   baseDate={event.sprk_basedate}
 *   onBaseDateChange={handleBaseDateChange}
 *   finalDueDate={event.sprk_finalduedate}
 *   onFinalDueDateChange={handleFinalDueDateChange}
 *   remindAt={event.sprk_remindat}
 *   onRemindAtChange={handleRemindAtChange}
 *   defaultExpanded={true}
 *   disabled={!canEdit}
 * />
 * ```
 */
export const DatesSection: React.FC<DatesSectionProps> = ({
  baseDate,
  onBaseDateChange,
  finalDueDate,
  onFinalDueDateChange,
  remindAt,
  onRemindAtChange,
  defaultExpanded = false,
  expanded,
  onExpandedChange,
  disabled = false,
  isLoading = false,
}) => {
  const styles = useStyles();

  // Parse ISO date strings to Date objects
  const baseDateValue = React.useMemo(() => parseISODate(baseDate), [baseDate]);
  const finalDueDateValue = React.useMemo(
    () => parseISODate(finalDueDate),
    [finalDueDate]
  );
  const remindAtValue = React.useMemo(() => parseISODate(remindAt), [remindAt]);

  // Track time separately for Remind At field
  const [remindAtTime, setRemindAtTime] = React.useState<string>(() =>
    extractTimeString(remindAtValue)
  );

  // Update time when remindAt prop changes
  React.useEffect(() => {
    setRemindAtTime(extractTimeString(remindAtValue));
  }, [remindAtValue]);

  // ---------------------------------------------------------------------------
  // Event Handlers
  // ---------------------------------------------------------------------------

  /**
   * Handle Base Date selection
   */
  const handleBaseDateSelect = React.useCallback(
    (date: Date | null | undefined) => {
      onBaseDateChange(date ?? null);
    },
    [onBaseDateChange]
  );

  /**
   * Handle Final Due Date selection
   */
  const handleFinalDueDateSelect = React.useCallback(
    (date: Date | null | undefined) => {
      onFinalDueDateChange(date ?? null);
    },
    [onFinalDueDateChange]
  );

  /**
   * Handle Remind At date selection
   */
  const handleRemindAtDateSelect = React.useCallback(
    (date: Date | null | undefined) => {
      if (date) {
        const combined = combineDateAndTime(date, remindAtTime);
        onRemindAtChange(combined);
      } else {
        onRemindAtChange(null);
      }
    },
    [onRemindAtChange, remindAtTime]
  );

  /**
   * Handle Remind At time change
   */
  const handleRemindAtTimeChange = React.useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const newTime = event.target.value;
      setRemindAtTime(newTime);

      // Update the combined datetime if we have a date
      if (remindAtValue) {
        const combined = combineDateAndTime(remindAtValue, newTime);
        onRemindAtChange(combined);
      }
    },
    [remindAtValue, onRemindAtChange]
  );

  // ---------------------------------------------------------------------------
  // Render Loading State
  // ---------------------------------------------------------------------------

  if (isLoading) {
    return (
      <CollapsibleSection
        title="Dates"
        icon={<CalendarRegular />}
        defaultExpanded={defaultExpanded}
        expanded={expanded}
        onExpandedChange={onExpandedChange}
        disabled
      >
        <div className={styles.loadingContainer}>
          <Spinner size="small" label="Loading dates..." />
        </div>
      </CollapsibleSection>
    );
  }

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <CollapsibleSection
      title="Dates"
      icon={<CalendarRegular />}
      defaultExpanded={defaultExpanded}
      expanded={expanded}
      onExpandedChange={onExpandedChange}
      disabled={disabled}
      ariaLabel="Dates section"
    >
      <div className={styles.fieldsContainer}>
        {/* Base Date Field */}
        <Field
          label={
            <span className={styles.fieldLabel}>
              <CalendarMonthRegular className={styles.fieldIcon} />
              Base Date
            </span>
          }
        >
          <DatePicker
            className={styles.datePicker}
            placeholder="Select base date..."
            value={baseDateValue}
            onSelectDate={handleBaseDateSelect}
            disabled={disabled}
            formatDate={formatDateForDisplay}
            aria-label="Base date"
          />
        </Field>

        {/* Final Due Date Field */}
        <Field
          label={
            <span className={styles.fieldLabel}>
              <AlertRegular className={styles.fieldIcon} />
              Final Due Date
            </span>
          }
        >
          <DatePicker
            className={styles.datePicker}
            placeholder="Select final due date..."
            value={finalDueDateValue}
            onSelectDate={handleFinalDueDateSelect}
            disabled={disabled}
            formatDate={formatDateForDisplay}
            aria-label="Final due date"
          />
        </Field>

        {/* Remind At DateTime Field */}
        <Field
          label={
            <span className={styles.fieldLabel}>
              <ClockRegular className={styles.fieldIcon} />
              Remind At
            </span>
          }
        >
          <div className={styles.dateTimeRow}>
            {/* Date picker */}
            <DatePicker
              className={styles.datePickerSmall}
              placeholder="Select date..."
              value={remindAtValue}
              onSelectDate={handleRemindAtDateSelect}
              disabled={disabled}
              formatDate={formatDateForDisplay}
              aria-label="Remind at date"
            />
            {/* Time input */}
            <Input
              className={styles.timeInput}
              type="time"
              value={remindAtTime}
              onChange={handleRemindAtTimeChange}
              disabled={disabled}
              aria-label="Remind at time"
            />
          </div>
        </Field>
      </div>
    </CollapsibleSection>
  );
};

export default DatesSection;
