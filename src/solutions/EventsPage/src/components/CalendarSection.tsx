/**
 * CalendarSection Component
 *
 * Wrapper component that embeds the EventCalendarFilter component
 * within the Events Custom Page. Provides date selection and filtering
 * capabilities for the Events grid.
 *
 * This component adapts the EventCalendarFilter (originally designed for PCF)
 * to work in a Custom Page context by:
 * - Providing mock context for non-PCF environment
 * - Handling filter output to parent component
 * - Supporting event date indicators from fetched data
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/061-events-page-integrate-calendar.poml
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Text,
  Button,
} from "@fluentui/react-components";
import { DismissRegular, Calendar24Regular } from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Calendar filter output format (matches EventCalendarFilter output)
 */
export type CalendarFilterType = "single" | "range" | "clear";

export interface CalendarFilterSingle {
  type: "single";
  date: string;
}

export interface CalendarFilterRange {
  type: "range";
  start: string;
  end: string;
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
 * Props for CalendarSection component
 */
export interface CalendarSectionProps {
  /** Event dates with counts for showing indicators */
  eventDates?: IEventDateInfo[];
  /** Callback when filter changes */
  onFilterChange: (filter: CalendarFilterOutput | null) => void;
  /** Initial selected date (ISO string) */
  initialDate?: string;
  /** Height of the calendar section */
  height?: number;
}

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
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  calendarContent: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    ...shorthands.padding("8px"),
    overflowY: "auto",
  },
  monthContainer: {
    marginBottom: "16px",
  },
  monthHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("8px"),
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
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
    minHeight: "36px",
    ...shorthands.padding("4px"),
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
  clearButton: {
    fontSize: tokens.fontSizeBase200,
    minWidth: "auto",
  },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
  selectionInfo: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    ...shorthands.padding("8px", "16px"),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

const DAYS_OF_WEEK = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"
];

/**
 * Convert Date to ISO date string (YYYY-MM-DD)
 */
function toIsoDateString(date: Date): string {
  return date.toISOString().split("T")[0];
}

/**
 * Parse ISO date string to Date
 */
function parseIsoDate(dateStr: string): Date {
  const [year, month, day] = dateStr.split("-").map(Number);
  return new Date(year, month - 1, day);
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

  // Add padding days for next month to complete the grid
  const endPadding = 42 - days.length; // 6 rows of 7 days
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
// Component
// ─────────────────────────────────────────────────────────────────────────────

const VERSION = "1.0.0";

export const CalendarSection: React.FC<CalendarSectionProps> = ({
  eventDates = [],
  onFilterChange,
  initialDate,
  height,
}) => {
  const styles = useStyles();
  const today = new Date();

  // State for current view (which months to display)
  const [viewDate, setViewDate] = React.useState<Date>(
    initialDate ? parseIsoDate(initialDate) : today
  );

  // Selection state
  const [selectedDate, setSelectedDate] = React.useState<string | null>(
    initialDate ?? null
  );
  const [rangeStart, setRangeStart] = React.useState<string | null>(null);
  const [rangeEnd, setRangeEnd] = React.useState<string | null>(null);

  // Build event date lookup for O(1) access
  const eventDateMap = React.useMemo(() => {
    const map = new Map<string, number>();
    eventDates.forEach(({ date, count }) => {
      map.set(date, count);
    });
    return map;
  }, [eventDates]);

  /**
   * Handle date click
   */
  const handleDateClick = React.useCallback(
    (date: Date, isShiftKey: boolean) => {
      const dateStr = toIsoDateString(date);

      if (isShiftKey && selectedDate) {
        // Range selection
        const start = selectedDate;
        const end = dateStr;
        setRangeStart(start);
        setRangeEnd(end);

        // Emit range filter
        const filter: CalendarFilterRange = {
          type: "range",
          start: start < end ? start : end,
          end: start < end ? end : start,
        };
        onFilterChange(filter);
      } else {
        // Single date selection
        if (selectedDate === dateStr && !rangeStart) {
          // Toggle off
          setSelectedDate(null);
          setRangeStart(null);
          setRangeEnd(null);
          onFilterChange({ type: "clear" });
        } else {
          // Select new date
          setSelectedDate(dateStr);
          setRangeStart(null);
          setRangeEnd(null);
          onFilterChange({ type: "single", date: dateStr });
        }
      }
    },
    [selectedDate, rangeStart, onFilterChange]
  );

  /**
   * Clear selection
   */
  const handleClearSelection = React.useCallback(() => {
    setSelectedDate(null);
    setRangeStart(null);
    setRangeEnd(null);
    onFilterChange({ type: "clear" });
  }, [onFilterChange]);

  /**
   * Check if a date is selected or in range
   */
  const getDateState = React.useCallback(
    (date: Date): "selected" | "in-range" | null => {
      const dateStr = toIsoDateString(date);

      if (rangeStart && rangeEnd) {
        const start = parseIsoDate(rangeStart);
        const end = parseIsoDate(rangeEnd);
        if (isSameDay(date, start) || isSameDay(date, end)) {
          return "selected";
        }
        if (isDateInRange(date, start, end)) {
          return "in-range";
        }
      } else if (selectedDate === dateStr) {
        return "selected";
      }

      return null;
    },
    [selectedDate, rangeStart, rangeEnd]
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
      <div key={`${year}-${month}`} className={styles.monthContainer}>
        <div className={styles.monthHeader}>
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
                  onClick={(e) => handleDateClick(day, e.shiftKey)}
                  role="button"
                  tabIndex={0}
                  aria-label={`${day.toDateString()}${hasEvents ? " - has events" : ""}`}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      handleDateClick(day, e.shiftKey);
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

  // Generate months to display (current month + next 2 months)
  const monthsToShow = React.useMemo(() => {
    const months: { year: number; month: number }[] = [];
    for (let i = 0; i < 3; i++) {
      const d = new Date(viewDate.getFullYear(), viewDate.getMonth() + i, 1);
      months.push({ year: d.getFullYear(), month: d.getMonth() });
    }
    return months;
  }, [viewDate]);

  const hasSelection = selectedDate !== null || rangeStart !== null;

  // Format selection info
  const selectionInfo = React.useMemo(() => {
    if (rangeStart && rangeEnd) {
      return `Selected: ${rangeStart} to ${rangeEnd}`;
    }
    if (selectedDate) {
      return `Selected: ${selectedDate}`;
    }
    return null;
  }, [selectedDate, rangeStart, rangeEnd]);

  return (
    <div className={styles.container} style={{ height }}>
      {/* Header */}
      <div className={styles.header}>
        <div className={styles.headerTitle}>
          <Calendar24Regular />
          <span>Calendar</span>
        </div>
      </div>

      {/* Selection info banner */}
      {selectionInfo && (
        <div className={styles.selectionInfo}>{selectionInfo}</div>
      )}

      {/* Calendar content */}
      <div className={styles.calendarContent}>
        {monthsToShow.map(({ year, month }) => renderMonth(year, month))}
      </div>

      {/* Footer */}
      <div className={styles.footer}>
        {hasSelection ? (
          <Button
            className={styles.clearButton}
            appearance="subtle"
            size="small"
            icon={<DismissRegular />}
            onClick={handleClearSelection}
            aria-label="Clear selection"
          >
            Clear
          </Button>
        ) : (
          <span />
        )}
        <Text className={styles.versionText}>v{VERSION}</Text>
      </div>
    </div>
  );
};

export default CalendarSection;
