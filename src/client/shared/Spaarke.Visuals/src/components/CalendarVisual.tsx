/**
 * CalendarVisual Component (presentational — @spaarke/visuals)
 * Custom calendar grid showing events by date.
 * Supports click-to-drill, and click-to-open-popover showing a day's events.
 *
 * VHVU-050: data-in only. The host (a PCF-side container) fetches detailed
 * event records and passes them via `detailedEvents` (already mapped) plus any
 * `fetchError`. This component contains NO webApi / Xrm / FetchXML — it renders
 * whatever data it is given. Falls back to the aggregated `events` prop for
 * badge counts when `detailedEvents` is null (no fetch was possible).
 */

import * as React from 'react';
import { useState, useMemo } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Badge,
  Tooltip,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  mergeClasses,
  shorthands,
} from '@fluentui/react-components';
import { ChevronLeftRegular, ChevronRightRegular, CopyRegular, CalendarLtrRegular } from '@fluentui/react-icons';
import type { DrillInteraction } from '../types';

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

/**
 * Detailed event record for the day-detail popover. The host container maps
 * raw Dataverse records to this presentational shape and passes them in via
 * the `detailedEvents` prop.
 */
export interface ICalendarEventRecord {
  id: string;
  name: string;
  date: Date;
  typeName?: string;
  typeColor?: string;
  description?: string;
  assignedTo?: string;
  entityName: string;
}

export interface ICalendarVisualProps {
  /** Aggregated events (badge counts) — used when detailedEvents is not supplied */
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
  /**
   * Detailed event records supplied by the host container (already fetched +
   * mapped). When present, badge counts + the day-detail popover use these.
   * `null` means no fetch was possible → fall back to `events` for badges and
   * disable the popover. `[]` means a fetch ran and returned nothing.
   */
  detailedEvents?: ICalendarEventRecord[] | null;
  /** Fetch error from the host container (shown in the day-detail popover). */
  fetchError?: string | null;
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    gap: tokens.spacingVerticalS,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: tokens.spacingVerticalXS,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase400,
  },
  navigation: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  monthLabel: {
    minWidth: '140px',
    textAlign: 'center',
    fontWeight: tokens.fontWeightMedium,
    color: tokens.colorNeutralForeground1,
  },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(7, 1fr)',
    gap: '2px',
  },
  weekdayHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXS,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightMedium,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
  },
  day: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '48px',
    padding: tokens.spacingVerticalXXS,
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    cursor: 'default',
    transition: 'background-color 0.15s ease-in-out',
  },
  dayInteractive: {
    cursor: 'pointer',
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    '&:active': {
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
    marginTop: '2px',
  },
  placeholder: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground3,
  },
  // v1.4.25 — day-detail POPOVER. Shape mirrors AiSummaryPopover
  // (@spaarke/ui-components): 480px wide, 400px max height, header row with
  // title + copy button, tabular body.
  popoverSurface: {
    width: '480px',
    maxHeight: '400px',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  popoverHeaderRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  popoverHeaderLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
  },
  // Hidden anchor for the controlled Popover — Fluent v9 requires a
  // PopoverTrigger child, but we drive positioning via `positioning.target`.
  popoverHiddenAnchor: {
    position: 'absolute',
    width: 0,
    height: 0,
    overflow: 'hidden',
    pointerEvents: 'none',
  },
  // Table styles
  eventsTable: {
    width: '100%',
    borderCollapse: 'collapse',
  },
  eventsTableHead: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.03em',
    textAlign: 'left',
    ...shorthands.padding('4px', '8px', '4px', 0),
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  eventsTableCell: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    ...shorthands.padding('8px', '8px', '8px', 0),
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke3,
    verticalAlign: 'top',
  },
  eventsTableRowLast: {
    '& td': {
      borderBottom: 'none',
    },
  },
  eventsNameCell: {
    fontWeight: tokens.fontWeightSemibold,
  },
  eventsTypeCell: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    whiteSpace: 'nowrap',
  },
  eventsTypeSwatch: {
    width: '10px',
    height: '10px',
    borderRadius: '2px',
    flexShrink: 0,
    display: 'inline-block',
  },
  eventsDateCell: {
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground2,
  },
  popoverEmpty: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    padding: tokens.spacingVerticalM,
  },
});

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

/**
 * Get all days to display in the calendar grid for a given month.
 *
 * v1.4.24 — only pad the trailing week to complete it, instead of always
 * filling to 42 cells (6 rows). Avoids an entire trailing week of greyed-out
 * next-month days when the current month + leading padding already ends on a
 * week boundary. Result: 28, 35, or 42 cells depending on month.
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

  // Add only enough next-month days to complete the final week (0 to 6).
  const endPadding = (7 - (days.length % 7)) % 7;
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
 * v1.4.24 — derive a day-bucket key from a date.
 */
const dayKey = (date: Date): string => `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;

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
  detailedEvents = null,
  fetchError = null,
}) => {
  const styles = useStyles();
  const today = new Date();
  const [currentMonth, setCurrentMonth] = useState(initialMonth || new Date(today.getFullYear(), today.getMonth(), 1));

  // v1.4.25 — controlled popover. Tracks selected day AND the DOM anchor
  // element (the clicked day cell) so Fluent can position the popover next to it.
  const [selectedDay, setSelectedDay] = useState<Date | null>(null);
  const [popoverAnchor, setPopoverAnchor] = useState<HTMLElement | null>(null);
  const [copied, setCopied] = useState(false);

  // v1.4.24 — bucket detailed events by day. Used for both badge counts
  // (when detailed data is available) and the modal event list.
  const detailedByDay = useMemo(() => {
    if (!detailedEvents) return null;
    const map = new Map<string, ICalendarEventRecord[]>();
    for (const e of detailedEvents) {
      const k = dayKey(e.date);
      const arr = map.get(k);
      if (arr) arr.push(e);
      else map.set(k, [e]);
    }
    return map;
  }, [detailedEvents]);

  const eventMap = useMemo(() => {
    // Prefer fetched detailed data; fall back to the aggregated `events` prop.
    if (detailedByDay) {
      const m = new Map<string, ICalendarEvent>();
      detailedByDay.forEach((arr, key) => {
        const first = arr[0];
        m.set(key, { date: first.date, count: arr.length });
      });
      return m;
    }
    const map = new Map<string, ICalendarEvent>();
    events.forEach(event => {
      map.set(dayKey(event.date), event);
    });
    return map;
  }, [events, detailedByDay]);

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

  const dayEventsForPopover = useMemo(() => {
    if (!selectedDay || !detailedByDay) return [];
    return detailedByDay.get(dayKey(selectedDay)) || [];
  }, [selectedDay, detailedByDay]);

  const popoverOpen = !!selectedDay && !!popoverAnchor;
  const closePopover = () => {
    setSelectedDay(null);
    setPopoverAnchor(null);
    setCopied(false);
  };

  const handleDayClick = (date: Date, event: ICalendarEvent | undefined, anchor: HTMLElement) => {
    // v1.4.24 — when we have a detailed event list and the day has events,
    // open the day-detail popover instead of (or in addition to) the drill.
    if (detailedByDay && event && event.count > 0) {
      setSelectedDay(date);
      setPopoverAnchor(anchor);
      return;
    }

    if (onDrillInteraction && drillField) {
      // Create a date range for the entire day
      const startOfDay = new Date(date.getFullYear(), date.getMonth(), date.getDate());
      const endOfDay = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59);

      onDrillInteraction({
        field: drillField,
        operator: 'between',
        value: [startOfDay.toISOString(), endOfDay.toISOString()],
        label: date.toLocaleDateString(),
      });
    }
  };

  const handleCopyEvents = () => {
    if (dayEventsForPopover.length === 0) return;
    const dateHeader = selectedDay
      ? selectedDay.toLocaleDateString(undefined, {
          weekday: 'long',
          month: 'long',
          day: 'numeric',
          year: 'numeric',
        })
      : '';
    const header = 'Name\tType\tDue Date';
    const rows = dayEventsForPopover.map(ev => [ev.name, ev.typeName ?? '', ev.date.toLocaleDateString()].join('\t'));
    const text = [dateHeader, header, ...rows].join('\n');
    void navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  const isInteractive = (!!onDrillInteraction && !!drillField) || !!detailedByDay;

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
        {WEEKDAYS.map(day => (
          <div key={day} className={styles.weekdayHeader}>
            {day}
          </div>
        ))}
        {calendarDays.map((date, index) => {
          const key = dayKey(date);
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
              onClick={isInteractive ? e => handleDayClick(date, event, e.currentTarget as HTMLElement) : undefined}
              tabIndex={isInteractive ? 0 : undefined}
              role={isInteractive ? 'button' : undefined}
              aria-label={`${date.toLocaleDateString()}${hasEvents ? `, ${event.count} events` : ''}`}
            >
              <Text className={mergeClasses(styles.dayNumber, isToday && styles.dayNumberToday)}>{date.getDate()}</Text>
              {hasEvents && (
                <Badge className={styles.eventBadge} size="small" appearance="filled" color="brand">
                  {event.count}
                </Badge>
              )}
            </div>
          );
        })}
      </div>

      {/* v1.4.25 — day-detail POPOVER. Controlled by (selectedDay, popoverAnchor);
          positioned relative to the clicked day cell via `positioning.target`.
          Shape mirrors AiSummaryPopover (@spaarke/ui-components). */}
      <Popover
        open={popoverOpen}
        onOpenChange={(_ev, data) => {
          if (!data.open) closePopover();
        }}
        positioning={popoverAnchor ? { target: popoverAnchor, position: 'below', align: 'center' } : undefined}
        withArrow
      >
        <PopoverTrigger disableButtonEnhancement>
          <span className={styles.popoverHiddenAnchor} aria-hidden />
        </PopoverTrigger>
        <PopoverSurface className={styles.popoverSurface}>
          <div className={styles.popoverHeaderRow}>
            <Text className={styles.popoverHeaderLabel}>
              <CalendarLtrRegular aria-hidden="true" />
              {selectedDay
                ? selectedDay.toLocaleDateString(undefined, {
                    weekday: 'long',
                    month: 'long',
                    day: 'numeric',
                    year: 'numeric',
                  })
                : ''}
            </Text>
            {dayEventsForPopover.length > 0 && (
              <Tooltip content={copied ? 'Copied!' : 'Copy'} relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<CopyRegular />}
                  aria-label="Copy events"
                  onClick={handleCopyEvents}
                />
              </Tooltip>
            )}
          </div>
          {fetchError ? (
            <Text className={styles.popoverEmpty}>Unable to load events: {fetchError}</Text>
          ) : dayEventsForPopover.length === 0 ? (
            <Text className={styles.popoverEmpty}>No events on this day.</Text>
          ) : (
            <table className={styles.eventsTable}>
              <thead>
                <tr>
                  <th className={styles.eventsTableHead}>Name</th>
                  <th className={styles.eventsTableHead}>Type</th>
                  <th className={styles.eventsTableHead}>Due Date</th>
                </tr>
              </thead>
              <tbody>
                {dayEventsForPopover.map((ev, idx) => (
                  <tr
                    key={ev.id || idx}
                    className={idx === dayEventsForPopover.length - 1 ? styles.eventsTableRowLast : undefined}
                  >
                    <td className={mergeClasses(styles.eventsTableCell, styles.eventsNameCell)}>{ev.name}</td>
                    <td className={styles.eventsTableCell}>
                      <span className={styles.eventsTypeCell}>
                        {ev.typeColor && (
                          <span
                            className={styles.eventsTypeSwatch}
                            style={{ backgroundColor: ev.typeColor }}
                            aria-hidden
                          />
                        )}
                        {ev.typeName ?? ''}
                      </span>
                    </td>
                    <td className={mergeClasses(styles.eventsTableCell, styles.eventsDateCell)}>
                      {ev.date.toLocaleDateString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </PopoverSurface>
      </Popover>
    </div>
  );
};
