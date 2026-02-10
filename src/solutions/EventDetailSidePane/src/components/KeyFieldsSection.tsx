/**
 * KeyFieldsSection - Key fields section for Event Detail Side Pane
 *
 * Displays the always-visible key fields regardless of Event Type:
 * - Due Date (DatePicker)
 * - Priority (Dropdown)
 * - Owner (read-only display with lookup icon)
 *
 * Uses Fluent UI v9 components with proper theming support.
 *
 * @see projects/events-workspace-apps-UX-r1/design.md - Key Fields section spec
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Field,
  Dropdown,
  Option,
  Persona,
  Badge,
  Text,
  Spinner,
} from "@fluentui/react-components";
import { DatePicker } from "@fluentui/react-datepicker-compat";
import {
  CalendarMonthRegular,
  FlagRegular,
  PersonRegular,
} from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Priority option value (matches Dataverse sprk_priority optionset)
 * Note: Values may vary based on actual Dataverse configuration
 */
export type PriorityValue = 1 | 2 | 3;

/**
 * Priority option definition
 */
export interface PriorityOption {
  value: PriorityValue;
  label: string;
  color: "danger" | "warning" | "success";
}

/**
 * Owner information for display
 */
export interface OwnerInfo {
  /** Owner GUID */
  id: string;
  /** Owner display name */
  name: string;
  /** Owner type (systemuser or team) */
  type?: "systemuser" | "team";
}

/**
 * Props for the KeyFieldsSection component
 */
export interface KeyFieldsSectionProps {
  /** Due date value (ISO string or null) */
  dueDate: string | null;
  /** Callback when due date changes */
  onDueDateChange: (date: Date | null) => void;
  /** Priority value (1=High, 2=Normal, 3=Low) */
  priority: PriorityValue | null;
  /** Callback when priority changes */
  onPriorityChange: (priority: PriorityValue) => void;
  /** Owner information for display */
  owner: OwnerInfo | null;
  /** Callback when owner lookup is requested (future: opens lookup dialog) */
  onOwnerLookup?: () => void;
  /** Whether fields are disabled (read-only mode) */
  disabled?: boolean;
  /** Loading state */
  isLoading?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Priority options mapped from Dataverse sprk_priority optionset
 */
export const PRIORITY_OPTIONS: PriorityOption[] = [
  { value: 1, label: "High", color: "danger" },
  { value: 2, label: "Normal", color: "warning" },
  { value: 3, label: "Low", color: "success" },
];

/**
 * Default priority value when none selected
 */
export const DEFAULT_PRIORITY: PriorityValue = 2;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("16px"),
    ...shorthands.padding("16px", "20px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
  sectionTitle: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    marginBottom: "8px",
  },
  titleText: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  fieldRow: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
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
  dropdown: {
    width: "100%",
  },
  ownerContainer: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground3,
    cursor: "default",
  },
  ownerContainerClickable: {
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  priorityBadge: {
    marginLeft: "8px",
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("24px"),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

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
 * Format Date object to display string
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
 * Get priority option by value
 */
export function getPriorityOption(value: PriorityValue | null): PriorityOption | null {
  if (value === null) return null;
  return PRIORITY_OPTIONS.find((opt) => opt.value === value) ?? null;
}

/**
 * Get priority label by value
 */
export function getPriorityLabel(value: PriorityValue | null): string {
  const option = getPriorityOption(value);
  return option?.label ?? "Not Set";
}

/**
 * Check if a number is a valid priority value
 */
export function isValidPriority(value: number | null | undefined): value is PriorityValue {
  if (value === null || value === undefined) return false;
  return [1, 2, 3].includes(value);
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * KeyFieldsSection component for displaying and editing key event fields.
 *
 * These fields are always visible regardless of Event Type configuration:
 * - Due Date: DatePicker for selecting the event due date
 * - Priority: Dropdown for High/Normal/Low selection
 * - Owner: Read-only display with persona (future: lookup support)
 *
 * Uses Fluent UI v9 components with proper dark mode support via tokens.
 *
 * @example
 * ```tsx
 * <KeyFieldsSection
 *   dueDate={event.sprk_duedate}
 *   onDueDateChange={handleDueDateChange}
 *   priority={event.sprk_priority as PriorityValue}
 *   onPriorityChange={handlePriorityChange}
 *   owner={{ id: event._ownerid_value, name: ownerName }}
 *   disabled={!canEdit}
 * />
 * ```
 */
export const KeyFieldsSection: React.FC<KeyFieldsSectionProps> = ({
  dueDate,
  onDueDateChange,
  priority,
  onPriorityChange,
  owner,
  onOwnerLookup,
  disabled = false,
  isLoading = false,
}) => {
  const styles = useStyles();

  // Parse the ISO date string to Date object for DatePicker
  const dueDateValue = React.useMemo(() => parseISODate(dueDate), [dueDate]);

  // ─────────────────────────────────────────────────────────────────────────
  // Event Handlers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle DatePicker selection change
   */
  const handleDateSelect = React.useCallback(
    (date: Date | null | undefined) => {
      onDueDateChange(date ?? null);
    },
    [onDueDateChange]
  );

  /**
   * Handle Priority dropdown change
   */
  const handlePriorityChange = React.useCallback(
    (_ev: unknown, data: { optionValue?: string }) => {
      if (data.optionValue) {
        const numericValue = parseInt(data.optionValue, 10) as PriorityValue;
        if (isValidPriority(numericValue)) {
          onPriorityChange(numericValue);
        }
      }
    },
    [onPriorityChange]
  );

  /**
   * Handle Owner container click (for future lookup functionality)
   */
  const handleOwnerClick = React.useCallback(() => {
    if (onOwnerLookup && !disabled) {
      onOwnerLookup();
    }
  }, [onOwnerLookup, disabled]);

  // ─────────────────────────────────────────────────────────────────────────
  // Render Loading State
  // ─────────────────────────────────────────────────────────────────────────

  if (isLoading) {
    return (
      <section className={styles.container}>
        <div className={styles.loadingContainer}>
          <Spinner size="small" label="Loading fields..." />
        </div>
      </section>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Compute display values
  // ─────────────────────────────────────────────────────────────────────────

  const selectedPriority = getPriorityOption(priority);
  const priorityDropdownValue = priority !== null ? String(priority) : "";

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <section className={styles.container}>
      {/* Due Date Field */}
      <Field
        label={
          <span className={styles.fieldLabel}>
            <CalendarMonthRegular className={styles.fieldIcon} />
            Due Date
          </span>
        }
      >
        <DatePicker
          className={styles.datePicker}
          placeholder="Select due date..."
          value={dueDateValue}
          onSelectDate={handleDateSelect}
          disabled={disabled}
          formatDate={formatDateForDisplay}
          aria-label="Due date"
        />
      </Field>

      {/* Priority Field */}
      <Field
        label={
          <span className={styles.fieldLabel}>
            <FlagRegular className={styles.fieldIcon} />
            Priority
            {selectedPriority && (
              <Badge
                className={styles.priorityBadge}
                appearance="filled"
                color={selectedPriority.color}
                size="small"
              >
                {selectedPriority.label}
              </Badge>
            )}
          </span>
        }
      >
        <Dropdown
          className={styles.dropdown}
          placeholder="Select priority..."
          value={selectedPriority?.label ?? ""}
          selectedOptions={priorityDropdownValue ? [priorityDropdownValue] : []}
          onOptionSelect={handlePriorityChange}
          disabled={disabled}
          aria-label="Priority"
        >
          {PRIORITY_OPTIONS.map((option) => (
            <Option key={option.value} value={String(option.value)}>
              {option.label}
            </Option>
          ))}
        </Dropdown>
      </Field>

      {/* Owner Field (Read-only display) */}
      <Field
        label={
          <span className={styles.fieldLabel}>
            <PersonRegular className={styles.fieldIcon} />
            Owner
          </span>
        }
      >
        <div
          className={`${styles.ownerContainer} ${
            onOwnerLookup && !disabled ? styles.ownerContainerClickable : ""
          }`}
          onClick={handleOwnerClick}
          role={onOwnerLookup && !disabled ? "button" : undefined}
          tabIndex={onOwnerLookup && !disabled ? 0 : undefined}
          aria-label={owner ? `Owner: ${owner.name}` : "Owner not assigned"}
          onKeyDown={
            onOwnerLookup && !disabled
              ? (e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    handleOwnerClick();
                  }
                }
              : undefined
          }
        >
          {owner ? (
            <Persona
              name={owner.name}
              size="small"
              avatar={{ color: "colorful" }}
              secondaryText={owner.type === "team" ? "Team" : undefined}
            />
          ) : (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Not assigned
            </Text>
          )}
        </div>
      </Field>
    </section>
  );
};

export default KeyFieldsSection;
