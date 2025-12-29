/**
 * CalendarVisual Component
 * Custom calendar grid showing events by date
 * Supports click-to-drill for viewing records on a specific date
 */

import * as React from "react";
import { useState, useMemo } from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Badge,
  mergeClasses,
} from "@fluentui/react-components";
import {
  ChevronLeftRegular,
  ChevronRightRegular,
} from "@fluentui/react-icons";
import type { DrillInteraction } from "../types";

export interface ICalendarEvent {
  /** Event date */
  date: Date;
  /** Number of events on this date */
  count: number;
  /** Optional label */
  label?: string;
  /** Field value for drill interaction */
  fieldValue?: unknown;
}

export interface ICalendarVisualProps {
  /** Events to display */
  events: ICalendarEvent[];
  /** Initial month to display */
  initialMonth?: Date;
  /** Title */
  title?: string;
  /** Callback when a day is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Whether to show navigation buttons */
  showNavigation?: boolean;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    gap: tokens.spacingVerticalS,
  },
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: tokens.spacingVerticalXS,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase400,
  },
  navigation: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  monthLabel: {
    minWidth: "140px",
    textAlign: "center",
    fontWeight: tokens.fontWeightMedium,
    color: tokens.colorNeutralForeground1,
  },
  grid: {
    display: "grid",
    gridTemplateColumns: "repeat(7, 1fr)",
    gap: "2px",
  },
  weekdayHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXS,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightMedium,
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase",
  },
  day: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "48px",
    padding: tokens.spacingVerticalXXS,
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    cursor: "default",
    transition: "background-color 0.15s ease-in-out",
  },
  dayInteractive: {
    cursor: "pointer",
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    "&:active": {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
  },
  dayWithEvents: {
    backgroundColor: tokens.colorBrandBackground2,
    border: `1px solid ${tokens.colorBrandStroke1}`,
  },
  dayOutsideMonth: {
    opacity: 0.4,
    backgroundColor: tokens.colorNeutralBackground3,
  },
  dayToday: {
    border: `2px solid ${tokens.colorBrandBackground}`,
  },
  dayNumber: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightRegular,
  },
  dayNumberToday: {
    fontWeight: tokens.fontWeightBold,
    color: tokens.colorBrandForeground1,
  },
  eventBadge: {
    marginTop: "2px",
  },
  placeholder: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground3,
  },
});

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"
];

/**
 * Get all days to display in the calendar grid for a given month
 */
const getCalendarDays = (year: number, month: number): Date[] => {
  const firstDay = new Date(year, month, 1);
  const lastDay = new Date(year, month + 1, 0);
  const days: Date[] = [];

  // Add days from previous month to fill the first week
  const startPadding = firstDay.getDay();
  for (let i = startPadding - 1; i >= 0; i--) {
    days.push(new Date(year, month, -i));
  }

  // Add all days of the current month
  for (let d = 1; d <= lastDay.getDate(); d++) {
    days.push(new Date(year, month, d));
  }

  // Add days from next month to complete the grid (6 rows)
  const endPadding = 42 - days.length;
  for (let i = 1; i <= endPadding; i++) {
    days.push(new Date(year, month + 1, i));
  }

  return days;
};

/**
 * Check if two dates are the same day
 */
const isSameDay = (date1: Date, date2: Date): boolean => {
  return (
    date1.getFullYear() === date2.getFullYear() &&
    date1.getMonth() === date2.getMonth() &&
    date1.getDate() === date2.getDate()
  );
};

/**
 * CalendarVisual - Displays a monthly calendar grid with event indicators
 */
export const CalendarVisual: React.FC<ICalendarVisualProps> = ({
  events,
  initialMonth,
  title,
  onDrillInteraction,
  drillField,
  showNavigation = true,
}) => {
  const styles = useStyles();
  const today = new Date();
  const [currentMonth, setCurrentMonth] = useState(
    initialMonth || new Date(today.getFullYear(), today.getMonth(), 1)
  );

  const eventMap = useMemo(() => {
    const map = new Map<string, ICalendarEvent>();
    events.forEach((event) => {
      const key = `${event.date.getFullYear()}-${event.date.getMonth()}-${event.date.getDate()}`;
      map.set(key, event);
    });
    return map;
  }, [events]);

  const calendarDays = useMemo(
    () => getCalendarDays(currentMonth.getFullYear(), currentMonth.getMonth()),
    [currentMonth]
  );

  const handlePrevMonth = () => {
    setCurrentMonth(new Date(currentMonth.getFullYear(), currentMonth.getMonth() - 1, 1));
  };

  const handleNextMonth = () => {
    setCurrentMonth(new Date(currentMonth.getFullYear(), currentMonth.getMonth() + 1, 1));
  };

  const handleDayClick = (date: Date, event?: ICalendarEvent) => {
    if (onDrillInteraction && drillField) {
      // Create a date range for the entire day
      const startOfDay = new Date(date.getFullYear(), date.getMonth(), date.getDate());
      const endOfDay = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59);

      onDrillInteraction({
        field: drillField,
        operator: "between",
        value: [startOfDay.toISOString(), endOfDay.toISOString()],
        label: date.toLocaleDateString(),
      });
    }
  };

  const isInteractive = !!onDrillInteraction && !!drillField;

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        {title && <Text className={styles.title}>{title}</Text>}
        {showNavigation && (
          <div className={styles.navigation}>
            <Button
              appearance="subtle"
              icon={<ChevronLeftRegular />}
              onClick={handlePrevMonth}
              aria-label="Previous month"
            />
            <Text className={styles.monthLabel}>
              {MONTHS[currentMonth.getMonth()]} {currentMonth.getFullYear()}
            </Text>
            <Button
              appearance="subtle"
              icon={<ChevronRightRegular />}
              onClick={handleNextMonth}
              aria-label="Next month"
            />
          </div>
        )}
      </div>

      <div className={styles.grid}>
        {WEEKDAYS.map((day) => (
          <div key={day} className={styles.weekdayHeader}>
            {day}
          </div>
        ))}
        {calendarDays.map((date, index) => {
          const key = `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
          const event = eventMap.get(key);
          const isCurrentMonth = date.getMonth() === currentMonth.getMonth();
          const isToday = isSameDay(date, today);
          const hasEvents = event && event.count > 0;

          return (
            <div
              key={`day-${index}`}
              className={mergeClasses(
                styles.day,
                !isCurrentMonth && styles.dayOutsideMonth,
                isToday && styles.dayToday,
                hasEvents && styles.dayWithEvents,
                isInteractive && styles.dayInteractive
              )}
              onClick={isInteractive ? () => handleDayClick(date, event) : undefined}
              tabIndex={isInteractive ? 0 : undefined}
              role={isInteractive ? "button" : undefined}
              aria-label={`${date.toLocaleDateString()}${hasEvents ? `, ${event.count} events` : ""}`}
            >
              <Text
                className={mergeClasses(
                  styles.dayNumber,
                  isToday && styles.dayNumberToday
                )}
              >
                {date.getDate()}
              </Text>
              {hasEvents && (
                <Badge
                  className={styles.eventBadge}
                  size="small"
                  appearance="filled"
                  color="brand"
                >
                  {event.count}
                </Badge>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
};
