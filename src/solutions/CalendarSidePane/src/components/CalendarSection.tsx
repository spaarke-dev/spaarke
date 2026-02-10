/**
 * CalendarSection Component for CalendarSidePane (Date Filter)
 *
 * Redesigned date filter with 3-month vertical stack, From/To fields,
 * and multi-select date field dropdown.
 *
 * Features:
 * - 3 vertical stacked calendars (current + 2 months)
 * - Month navigation (back/forward)
 * - From/To date fields (manual entry or click selection)
 * - Click to select: first click = From, second click = To
 * - Multi-select dropdown for date fields to filter
 * - Dark mode support via Fluent UI tokens
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/096-calendar-sidepane-webresource.poml
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Text,
  Button,
  Input,
  Dropdown,
  Option,
  Label,
} from "@fluentui/react-components";
import {
  DismissRegular,
  ChevronLeftRegular,
  ChevronRightRegular,
} from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Calendar filter output format
 */
export type CalendarFilterType = "single" | "range" | "clear";

export interface CalendarFilterSingle {
  type: "single";
  date: string;
  dateFields: string[];
}

export interface CalendarFilterRange {
  type: "range";
  start: string;
  end: string;
  dateFields: string[];
}

export interface CalendarFilterClear {
  type: "clear";
}

export type CalendarFilterOutput =
  | CalendarFilterSingle
  | CalendarFilterRange
  | CalendarFilterClear;

/**
 * Event date info for calendar indicators
 */
export interface IEventDateInfo {
  date: string;
  count: number;
}

/**
 * Date field option for the dropdown
 */
interface DateFieldOption {
  value: string;
  label: string;
}

/**
 * Props for CalendarSection component
 */
export interface CalendarSectionProps {
  /** Event dates with counts for showing indicators */
  eventDates?: IEventDateInfo[];
  /** Callback when filter changes */
  onFilterChange: (filter: CalendarFilterOutput | null) => void;
  /** Initial selected date (ISO string YYYY-MM-DD) */
  initialSelectedDate?: string;
  /** Initial range start (ISO string) */
  initialRangeStart?: string;
  /** Initial range end (ISO string) */
  initialRangeEnd?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const VERSION = "2.3.0"; // All Dates default, clear on page navigation

const DAYS_OF_WEEK = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

// Session storage key for filter persistence
const FILTER_STATE_KEY = "sprk_calendar_filter_state";

// Special value for "All Dates" option
const ALL_DATES_VALUE = "__ALL_DATES__";
const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"
];

/**
 * Individual date fields on the Event entity
 */
const INDIVIDUAL_DATE_FIELDS: DateFieldOption[] = [
  { value: "sprk_DueDate", label: "Due Date" },
  { value: "sprk_FinalDueDate", label: "Final Due Date" },
  { value: "CreatedOn", label: "Created On" },
  { value: "ModifiedOn", label: "Modified On" },
  { value: "sprk_ActualEnd", label: "Actual End" },
  { value: "sprk_ActualStart", label: "Actual Start" },
  { value: "sprk_ApprovedDate", label: "Approved Date" },
  { value: "sprk_BaseDate", label: "Base Date" },
  { value: "sprk_CompletedDate", label: "Completed Date" },
  { value: "sprk_MeetingDate", label: "Meeting Date" },
  { value: "sprk_PlannedEnd", label: "Planned End" },
  { value: "sprk_PlannedStart", label: "Planned Start" },
  { value: "sprk_ReassignedDate", label: "Reassigned Date" },
  { value: "sprk_RemindAt", label: "Remind At" },
  { value: "sprk_RescheduledDate", label: "Rescheduled Date" },
];

/**
 * All date field options including "All Dates" meta-option
 */
const DATE_FIELD_OPTIONS: DateFieldOption[] = [
  { value: ALL_DATES_VALUE, label: "All Dates" },
  ...INDIVIDUAL_DATE_FIELDS,
];

/**
 * Get all individual date field values (excludes ALL_DATES_VALUE)
 */
const ALL_DATE_FIELD_VALUES = INDIVIDUAL_DATE_FIELDS.map((f) => f.value);

// Default: "All Dates" selected (filters by all date fields)
const DEFAULT_DATE_FIELDS: string[] = [ALL_DATES_VALUE];

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("12px", "16px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
  headerTitle: {
    fontSize: "18px",
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
  },
  // Date field dropdown section
  dateFieldSection: {
    ...shorthands.padding("12px", "16px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
  dateFieldLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    marginBottom: "4px",
    display: "block",
  },
  dateFieldDropdown: {
    width: "100%",
  },
  // From/To input section
  dateInputSection: {
    display: "flex",
    ...shorthands.gap("12px"),
    ...shorthands.padding("12px", "16px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
  dateInputField: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
  },
  dateInputLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  dateInput: {
    width: "100%",
  },
  dateInputFocused: {
    ...shorthands.outline("2px", "solid", tokens.colorBrandStroke1),
  },
  // Navigation header
  navHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("8px", "16px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
  navTitle: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    minWidth: "140px",
    textAlign: "center",
  },
  navButtons: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("4px"),
  },
  // Calendar content
  calendarContent: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    ...shorthands.padding("8px", "16px"),
    overflowY: "auto",
  },
  monthSection: {
    marginBottom: "16px",
  },
  monthTitle: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    ...shorthands.padding("8px", "0"),
    textAlign: "center",
  },
  weekRow: {
    display: "grid",
    gridTemplateColumns: "repeat(7, 1fr)",
    ...shorthands.gap("2px"),
  },
  dayHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    ...shorthands.padding("4px", "0"),
  },
  dayCell: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    position: "relative",
    minHeight: "32px",
    ...shorthands.padding("2px"),
    ...shorthands.borderRadius("4px"),
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  dayCellOtherMonth: {
    color: tokens.colorNeutralForeground4,
  },
  dayCellToday: {
    fontWeight: tokens.fontWeightBold,
    color: tokens.colorBrandForeground1,
  },
  dayCellSelected: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    ":hover": {
      backgroundColor: tokens.colorBrandBackgroundHover,
    },
  },
  dayCellInRange: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    ":hover": {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  dayNumber: {
    fontSize: tokens.fontSizeBase200,
  },
  eventIndicator: {
    position: "absolute",
    bottom: "2px",
    width: "4px",
    height: "4px",
    ...shorthands.borderRadius("50%"),
    backgroundColor: tokens.colorBrandBackground,
  },
  eventIndicatorSelected: {
    backgroundColor: tokens.colorNeutralForegroundOnBrand,
  },
  footer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("8px", "16px"),
    ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  applyButton: {
    marginRight: "8px",
  },
  clearButton: {
    fontSize: tokens.fontSizeBase200,
    minWidth: "auto",
  },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Convert Date to ISO date string (YYYY-MM-DD)
 */
function toIsoDateString(date: Date): string {
  return date.toISOString().split("T")[0];
}

/**
 * Parse ISO date string to Date
 */
function parseIsoDate(dateStr: string): Date | null {
  if (!dateStr || !/^\d{4}-\d{2}-\d{2}$/.test(dateStr)) {
    return null;
  }
  const [year, month, day] = dateStr.split("-").map(Number);
  return new Date(year, month - 1, day);
}

/**
 * Format date for display (MMM D, YYYY)
 */
function formatDateDisplay(dateStr: string): string {
  const date = parseIsoDate(dateStr);
  if (!date) return dateStr;
  return date.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

/**
 * Get all days to display for a month (including padding days from prev/next months)
 */
function getMonthDays(year: number, month: number): Date[] {
  const firstDay = new Date(year, month, 1);
  const lastDay = new Date(year, month + 1, 0);
  const days: Date[] = [];

  // Add padding days from previous month
  const startPadding = firstDay.getDay();
  for (let i = startPadding - 1; i >= 0; i--) {
    const d = new Date(year, month, -i);
    days.push(d);
  }

  // Add days of current month
  for (let d = 1; d <= lastDay.getDate(); d++) {
    days.push(new Date(year, month, d));
  }

  // Add padding days for next month to complete the grid (6 rows)
  const endPadding = 42 - days.length;
  for (let d = 1; d <= endPadding; d++) {
    days.push(new Date(year, month + 1, d));
  }

  return days;
}

/**
 * Check if a date is between two dates (inclusive)
 */
function isDateInRange(date: Date, start: Date, end: Date): boolean {
  const d = date.getTime();
  const s = start.getTime();
  const e = end.getTime();
  return d >= Math.min(s, e) && d <= Math.max(s, e);
}

/**
 * Check if two dates are the same day
 */
function isSameDay(date1: Date, date2: Date): boolean {
  return (
    date1.getFullYear() === date2.getFullYear() &&
    date1.getMonth() === date2.getMonth() &&
    date1.getDate() === date2.getDate()
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Session Storage Persistence
// ─────────────────────────────────────────────────────────────────────────────

interface PersistedFilterState {
  fromDate: string;
  toDate: string;
  dateFields: string[];
  viewMonth: number;
  viewYear: number;
}

/**
 * Load filter state from session storage
 */
function loadFilterState(): PersistedFilterState | null {
  try {
    const stored = sessionStorage.getItem(FILTER_STATE_KEY);
    if (stored) {
      return JSON.parse(stored) as PersistedFilterState;
    }
  } catch (e) {
    console.warn("[CalendarSection] Failed to load filter state:", e);
  }
  return null;
}

/**
 * Save filter state to session storage
 */
function saveFilterState(state: PersistedFilterState): void {
  try {
    sessionStorage.setItem(FILTER_STATE_KEY, JSON.stringify(state));
  } catch (e) {
    console.warn("[CalendarSection] Failed to save filter state:", e);
  }
}

/**
 * Clear filter state from session storage
 */
function clearFilterState(): void {
  try {
    sessionStorage.removeItem(FILTER_STATE_KEY);
  } catch (e) {
    console.warn("[CalendarSection] Failed to clear filter state:", e);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const CalendarSection: React.FC<CalendarSectionProps> = ({
  eventDates = [],
  onFilterChange,
  initialRangeStart,
  initialRangeEnd,
}) => {
  const styles = useStyles();
  const today = new Date();

  // Load persisted state on mount
  const persistedState = React.useMemo(() => loadFilterState(), []);

  // State for current view (base month to display)
  // Priority: URL params > persisted state > today
  const [viewDate, setViewDate] = React.useState<Date>(() => {
    if (initialRangeStart) {
      const d = parseIsoDate(initialRangeStart);
      return d || today;
    }
    if (persistedState) {
      return new Date(persistedState.viewYear, persistedState.viewMonth, 1);
    }
    return today;
  });

  // From/To date values
  // Priority: URL params > persisted state > empty
  const [fromDate, setFromDate] = React.useState<string>(
    initialRangeStart ?? persistedState?.fromDate ?? ""
  );
  const [toDate, setToDate] = React.useState<string>(
    initialRangeEnd ?? persistedState?.toDate ?? ""
  );

  // Which input field is currently focused (for click-to-select)
  const [focusedField, setFocusedField] = React.useState<"from" | "to" | null>(null);

  // Selected date fields to filter
  // Priority: persisted state > default (empty)
  const [selectedDateFields, setSelectedDateFields] = React.useState<string[]>(
    persistedState?.dateFields ?? DEFAULT_DATE_FIELDS
  );

  // Persist filter state when it changes
  React.useEffect(() => {
    // Only persist if there's something to persist
    if (fromDate || toDate || selectedDateFields.length > 0) {
      saveFilterState({
        fromDate,
        toDate,
        dateFields: selectedDateFields,
        viewMonth: viewDate.getMonth(),
        viewYear: viewDate.getFullYear(),
      });
    }
  }, [fromDate, toDate, selectedDateFields, viewDate]);

  // On mount, if there's persisted state with dates, notify parent to apply filter
  React.useEffect(() => {
    if (persistedState && (persistedState.fromDate || persistedState.toDate)) {
      // Small delay to ensure parent is ready to receive
      const timer = setTimeout(() => {
        // Convert "All Dates" to actual field values
        const dateFields = persistedState.dateFields.includes(ALL_DATES_VALUE)
          ? ALL_DATE_FIELD_VALUES
          : persistedState.dateFields;

        if (persistedState.fromDate && persistedState.toDate) {
          const start = persistedState.fromDate < persistedState.toDate
            ? persistedState.fromDate : persistedState.toDate;
          const end = persistedState.fromDate < persistedState.toDate
            ? persistedState.toDate : persistedState.fromDate;
          onFilterChange({
            type: "range",
            start,
            end,
            dateFields,
          });
        } else if (persistedState.fromDate) {
          onFilterChange({
            type: "single",
            date: persistedState.fromDate,
            dateFields,
          });
        }
        console.log("[CalendarSection] Restored persisted filter:", persistedState);
      }, 200);
      return () => clearTimeout(timer);
    }
  }, []); // Only on mount

  // Build event date lookup for O(1) access
  const eventDateMap = React.useMemo(() => {
    const map = new Map<string, number>();
    eventDates.forEach(({ date, count }) => {
      map.set(date, count);
    });
    return map;
  }, [eventDates]);

  /**
   * Navigate to previous month (moves all 3 calendars)
   */
  const handlePrevMonth = React.useCallback(() => {
    setViewDate((prev) => new Date(prev.getFullYear(), prev.getMonth() - 1, 1));
  }, []);

  /**
   * Navigate to next month (moves all 3 calendars)
   */
  const handleNextMonth = React.useCallback(() => {
    setViewDate((prev) => new Date(prev.getFullYear(), prev.getMonth() + 1, 1));
  }, []);

  /**
   * Handle date click from calendar
   * First click sets From, second click sets To
   * If a field is focused, sets that field
   */
  const handleDateClick = React.useCallback(
    (date: Date) => {
      const dateStr = toIsoDateString(date);

      if (focusedField === "from") {
        setFromDate(dateStr);
        setFocusedField("to"); // Move focus to To field
      } else if (focusedField === "to") {
        setToDate(dateStr);
        setFocusedField(null);
      } else {
        // No field focused - default behavior
        if (!fromDate) {
          // First click sets From
          setFromDate(dateStr);
          setFocusedField("to"); // Auto-focus To for next click
        } else if (!toDate) {
          // Second click sets To
          setToDate(dateStr);
          setFocusedField(null);
        } else {
          // Both set - start over with From
          setFromDate(dateStr);
          setToDate("");
          setFocusedField("to");
        }
      }
    },
    [focusedField, fromDate, toDate]
  );

  /**
   * Handle date field dropdown change
   * Implements mutual exclusivity between "All Dates" and individual fields
   */
  const handleDateFieldChange = React.useCallback(
    (_: unknown, data: { selectedOptions: string[] }) => {
      const newSelection = data.selectedOptions;
      const hadAllDates = selectedDateFields.includes(ALL_DATES_VALUE);
      const hasAllDates = newSelection.includes(ALL_DATES_VALUE);

      if (hasAllDates && !hadAllDates) {
        // User just selected "All Dates" - make it the only selection
        setSelectedDateFields([ALL_DATES_VALUE]);
      } else if (hadAllDates && newSelection.length > 1) {
        // User selected individual field while "All Dates" was selected - remove "All Dates"
        setSelectedDateFields(newSelection.filter((v) => v !== ALL_DATES_VALUE));
      } else if (newSelection.length === 0) {
        // Nothing selected - default back to "All Dates"
        setSelectedDateFields([ALL_DATES_VALUE]);
      } else {
        setSelectedDateFields(newSelection);
      }
    },
    [selectedDateFields]
  );

  /**
   * Get actual date field values for filtering
   * Converts "All Dates" to all individual field values
   */
  const getActualDateFields = React.useCallback((): string[] => {
    if (selectedDateFields.includes(ALL_DATES_VALUE)) {
      return ALL_DATE_FIELD_VALUES;
    }
    return selectedDateFields;
  }, [selectedDateFields]);

  /**
   * Apply the filter
   */
  const handleApplyFilter = React.useCallback(() => {
    if (!fromDate && !toDate) {
      onFilterChange({ type: "clear" });
      return;
    }

    const dateFields = getActualDateFields();

    if (fromDate && toDate) {
      // Range filter
      const start = fromDate < toDate ? fromDate : toDate;
      const end = fromDate < toDate ? toDate : fromDate;
      onFilterChange({
        type: "range",
        start,
        end,
        dateFields,
      });
    } else if (fromDate) {
      // Single date filter
      onFilterChange({
        type: "single",
        date: fromDate,
        dateFields,
      });
    }
  }, [fromDate, toDate, getActualDateFields, onFilterChange]);

  /**
   * Clear selection and persisted state
   */
  const handleClearSelection = React.useCallback(() => {
    setFromDate("");
    setToDate("");
    setFocusedField(null);
    clearFilterState(); // Clear session storage
    onFilterChange({ type: "clear" });
  }, [onFilterChange]);

  /**
   * Check if a date is selected or in range
   */
  const getDateState = React.useCallback(
    (date: Date): "selected" | "in-range" | null => {
      const dateStr = toIsoDateString(date);

      const fromParsed = parseIsoDate(fromDate);
      const toParsed = parseIsoDate(toDate);

      if (fromParsed && toParsed) {
        if (isSameDay(date, fromParsed) || isSameDay(date, toParsed)) {
          return "selected";
        }
        if (isDateInRange(date, fromParsed, toParsed)) {
          return "in-range";
        }
      } else if (fromParsed && isSameDay(date, fromParsed)) {
        return "selected";
      } else if (toParsed && isSameDay(date, toParsed)) {
        return "selected";
      }

      return null;
    },
    [fromDate, toDate]
  );

  /**
   * Render a single month calendar
   */
  const renderMonth = (year: number, month: number) => {
    const days = getMonthDays(year, month);
    const weeks: Date[][] = [];

    for (let i = 0; i < days.length; i += 7) {
      weeks.push(days.slice(i, i + 7));
    }

    return (
      <div key={`${year}-${month}`} className={styles.monthSection}>
        <div className={styles.monthTitle}>
          {MONTHS[month]} {year}
        </div>

        {/* Day headers */}
        <div className={styles.weekRow}>
          {DAYS_OF_WEEK.map((day) => (
            <div key={day} className={styles.dayHeader}>
              {day}
            </div>
          ))}
        </div>

        {/* Weeks */}
        {weeks.map((week, weekIdx) => (
          <div key={weekIdx} className={styles.weekRow}>
            {week.map((day, dayIdx) => {
              const isOtherMonth = day.getMonth() !== month;
              const isToday = isSameDay(day, today);
              const dateState = getDateState(day);
              const dateStr = toIsoDateString(day);
              const hasEvents = eventDateMap.has(dateStr);

              return (
                <div
                  key={dayIdx}
                  className={`${styles.dayCell} ${
                    isOtherMonth ? styles.dayCellOtherMonth : ""
                  } ${isToday ? styles.dayCellToday : ""} ${
                    dateState === "selected" ? styles.dayCellSelected : ""
                  } ${dateState === "in-range" ? styles.dayCellInRange : ""}`}
                  onClick={() => handleDateClick(day)}
                  role="button"
                  tabIndex={0}
                  aria-label={`${day.toDateString()}${hasEvents ? " - has events" : ""}`}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      handleDateClick(day);
                    }
                  }}
                >
                  <span className={styles.dayNumber}>{day.getDate()}</span>
                  {hasEvents && (
                    <span
                      className={`${styles.eventIndicator} ${
                        dateState === "selected"
                          ? styles.eventIndicatorSelected
                          : ""
                      }`}
                    />
                  )}
                </div>
              );
            })}
          </div>
        ))}
      </div>
    );
  };

  const hasSelection = fromDate !== "" || toDate !== "";

  // Get the 3 months to display (current view + next 2)
  const month1 = { year: viewDate.getFullYear(), month: viewDate.getMonth() };
  const month2Date = new Date(viewDate.getFullYear(), viewDate.getMonth() + 1, 1);
  const month2 = { year: month2Date.getFullYear(), month: month2Date.getMonth() };
  const month3Date = new Date(viewDate.getFullYear(), viewDate.getMonth() + 2, 1);
  const month3 = { year: month3Date.getFullYear(), month: month3Date.getMonth() };

  return (
    <div className={styles.container}>
      {/* Header removed - title comes from Xrm side pane */}

      {/* Date Field Dropdown - Multi-select */}
      <div className={styles.dateFieldSection}>
        <Label className={styles.dateFieldLabel}>Filter by Date Field</Label>
        <Dropdown
          className={styles.dateFieldDropdown}
          multiselect
          selectedOptions={selectedDateFields}
          onOptionSelect={handleDateFieldChange}
          placeholder="Select date fields..."
        >
          {DATE_FIELD_OPTIONS.map((option) => (
            <Option key={option.value} value={option.value}>
              {option.label}
            </Option>
          ))}
        </Dropdown>
      </div>

      {/* From/To Input Fields */}
      <div className={styles.dateInputSection}>
        <div className={styles.dateInputField}>
          <Label className={styles.dateInputLabel}>From</Label>
          <Input
            className={`${styles.dateInput} ${focusedField === "from" ? styles.dateInputFocused : ""}`}
            value={fromDate}
            onChange={(_, data) => setFromDate(data.value)}
            onFocus={() => setFocusedField("from")}
            placeholder="YYYY-MM-DD"
            appearance="outline"
          />
        </div>
        <div className={styles.dateInputField}>
          <Label className={styles.dateInputLabel}>To</Label>
          <Input
            className={`${styles.dateInput} ${focusedField === "to" ? styles.dateInputFocused : ""}`}
            value={toDate}
            onChange={(_, data) => setToDate(data.value)}
            onFocus={() => setFocusedField("to")}
            placeholder="YYYY-MM-DD"
            appearance="outline"
          />
        </div>
      </div>

      {/* Navigation */}
      <div className={styles.navHeader}>
        <div className={styles.navButtons}>
          <Button
            appearance="subtle"
            size="small"
            icon={<ChevronLeftRegular />}
            onClick={handlePrevMonth}
            aria-label="Previous month"
          />
        </div>
        <Text className={styles.navTitle}>Navigate Months</Text>
        <div className={styles.navButtons}>
          <Button
            appearance="subtle"
            size="small"
            icon={<ChevronRightRegular />}
            onClick={handleNextMonth}
            aria-label="Next month"
          />
        </div>
      </div>

      {/* Calendar content - 3 months stacked */}
      <div className={styles.calendarContent}>
        {renderMonth(month1.year, month1.month)}
        {renderMonth(month2.year, month2.month)}
        {renderMonth(month3.year, month3.month)}
      </div>

      {/* Footer */}
      <div className={styles.footer}>
        <div>
          <Button
            className={styles.applyButton}
            appearance="primary"
            size="small"
            onClick={handleApplyFilter}
            disabled={!hasSelection || selectedDateFields.length === 0}
          >
            Apply
          </Button>
          <Button
            className={styles.clearButton}
            appearance="subtle"
            size="small"
            icon={<DismissRegular />}
            onClick={handleClearSelection}
            disabled={!hasSelection}
            aria-label="Clear selection"
          >
            Clear
          </Button>
        </div>
        <Text className={styles.versionText}>v{VERSION}</Text>
      </div>
    </div>
  );
};

export default CalendarSection;
