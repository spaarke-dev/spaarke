/**
 * CalendarMonth Component
 *
 * Displays a single month calendar grid with:
 * - Month/year label header
 * - Weekday headers (S M T W T F S)
 * - Date grid (6 rows x 7 columns)
 * - Event indicators and selection highlighting
 *
 * Part of Phase 1: EventCalendarFilter PCF control
 * Uses React 16 APIs per ADR-022 and Fluent v9 tokens per ADR-021
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/002-implement-fluent-calendar-component.poml
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text,
    Badge,
    mergeClasses
} from "@fluentui/react-components";

/**
 * Props for CalendarMonth component
 */
export interface ICalendarMonthProps {
    /** Year to display */
    year: number;
    /** Month to display (0-11) */
    month: number;
    /**
     * Event counts per date (ISO date strings YYYY-MM-DD -> count)
     * Use Map for count-aware indicators, or Set for presence-only
     */
    eventCounts?: Map<string, number>;
    /**
     * @deprecated Use eventCounts for count-aware indicators
     * Dates with events (ISO date strings YYYY-MM-DD) - kept for backward compatibility
     */
    eventDates?: Set<string>;
    /** Selected dates (ISO date strings YYYY-MM-DD) */
    selectedDates?: Set<string>;
    /** Callback when a date is clicked */
    onDateClick?: (date: Date) => void;
    /**
     * Callback when a date is clicked with Shift key for range selection
     * Task 004: Range selection support
     */
    onDateShiftClick?: (date: Date) => void;
    /** Whether to show dates from adjacent months */
    showAdjacentMonths?: boolean;
    /** Currently focused date for keyboard navigation (ISO date string) */
    focusedDate?: string;
    /** Callback when keyboard navigation requests focus on a different date */
    onFocusDateChange?: (date: Date) => void;
    /**
     * Range start date for highlighting (ISO string YYYY-MM-DD)
     * Task 004: Range selection support
     */
    rangeStartDate?: string | null;
    /**
     * Range end date for highlighting (ISO string YYYY-MM-DD)
     * Task 004: Range selection support
     */
    rangeEndDate?: string | null;
}

const WEEKDAYS = ["S", "M", "T", "W", "T", "F", "S"];
const MONTHS = [
    "January", "February", "March", "April", "May", "June",
    "July", "August", "September", "October", "November", "December"
];

/**
 * Styles using Fluent design tokens per ADR-021
 * No hard-coded colors - all tokens for dark mode support
 */
const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        marginBottom: tokens.spacingVerticalM
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-start",
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalXS
    },
    monthLabel: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground1
    },
    weekdayRow: {
        display: "grid",
        gridTemplateColumns: "repeat(7, 1fr)",
        gap: "1px",
        marginBottom: tokens.spacingVerticalXS
    },
    weekdayCell: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "24px",
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightMedium,
        color: tokens.colorNeutralForeground3,
        textTransform: "uppercase"
    },
    grid: {
        display: "grid",
        gridTemplateColumns: "repeat(7, 1fr)",
        gap: "1px"
    },
    dayCell: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "32px",
        minWidth: "32px",
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground1,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusSmall,
        cursor: "pointer",
        transition: "background-color 0.1s ease-in-out",
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover
        },
        ":active": {
            backgroundColor: tokens.colorNeutralBackground1Pressed
        }
    },
    dayCellOutsideMonth: {
        color: tokens.colorNeutralForeground4,
        opacity: 0.5,
        cursor: "default",
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1
        }
    },
    dayCellToday: {
        fontWeight: tokens.fontWeightBold,
        color: tokens.colorBrandForeground1,
        border: `1px solid ${tokens.colorBrandStroke1}`
    },
    dayCellHasEvent: {
        position: "relative"
    },
    /**
     * Event indicator container - positioned at bottom of cell
     * Contains either a dot (single event) or badge (multiple events)
     */
    eventIndicatorContainer: {
        position: "absolute",
        bottom: "2px",
        left: "50%",
        transform: "translateX(-50%)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        pointerEvents: "none" // Don't interfere with date click
    },
    /**
     * Dot indicator for single event - uses Fluent brand color token
     * ADR-021: No hard-coded colors
     */
    eventDot: {
        width: "6px",
        height: "6px",
        borderRadius: "50%",
        backgroundColor: tokens.colorBrandBackground
    },
    /**
     * Dot indicator when cell is selected - uses contrasting color
     */
    eventDotSelected: {
        backgroundColor: tokens.colorNeutralForegroundOnBrand
    },
    /**
     * Badge for multiple events - shows count
     * Uses smaller badge size to fit in cell
     */
    eventBadge: {
        transform: "scale(0.85)", // Slightly smaller for cell fit
        minWidth: "16px"
    },
    dayCellSelected: {
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
        fontWeight: tokens.fontWeightSemibold,
        ":hover": {
            backgroundColor: tokens.colorBrandBackgroundHover
        },
        ":active": {
            backgroundColor: tokens.colorBrandBackgroundPressed
        }
    },
    /**
     * @deprecated - indicator now rendered as child component, not pseudo-element
     * Kept for potential fallback styling
     */
    dayCellSelectedHasEvent: {},
    dayCellInRange: {
        backgroundColor: tokens.colorBrandBackground2,
        borderRadius: 0,
        ":hover": {
            backgroundColor: tokens.colorBrandBackground2Hover
        }
    },
    dayCellRangeStart: {
        borderTopLeftRadius: tokens.borderRadiusSmall,
        borderBottomLeftRadius: tokens.borderRadiusSmall
    },
    dayCellRangeEnd: {
        borderTopRightRadius: tokens.borderRadiusSmall,
        borderBottomRightRadius: tokens.borderRadiusSmall
    },
    /**
     * Focus indicator for keyboard navigation
     * Uses Fluent focus ring pattern per ADR-021 accessibility requirements
     */
    dayCellFocused: {
        outline: `2px solid ${tokens.colorStrokeFocus2}`,
        outlineOffset: "1px"
    }
});

/**
 * Get all days to display in the calendar grid for a given month
 * Includes padding days from previous/next months to fill 6 rows
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

    // Add days from next month to complete the grid (6 rows = 42 cells)
    const endPadding = 42 - days.length;
    for (let i = 1; i <= endPadding; i++) {
        days.push(new Date(year, month + 1, i));
    }

    return days;
};

/**
 * Convert a Date to ISO date string (YYYY-MM-DD) for comparison
 */
const toIsoDateString = (date: Date): string => {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
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
 * Parse ISO date string to Date object
 * Task 004: Range selection support
 */
const parseIsoDate = (dateStr: string): Date => {
    const [year, month, day] = dateStr.split("-").map(Number);
    return new Date(year, month - 1, day);
};

/**
 * Check if a date is within a range (inclusive)
 * Task 004: Range selection support
 */
const isDateInRange = (
    dateStr: string,
    rangeStart: string | null | undefined,
    rangeEnd: string | null | undefined
): boolean => {
    if (!rangeStart || !rangeEnd) return false;

    const date = parseIsoDate(dateStr);
    const start = parseIsoDate(rangeStart);
    const end = parseIsoDate(rangeEnd);

    return date >= start && date <= end;
};

/**
 * Check if a date is the start of a range
 * Task 004: Range selection support
 */
const isRangeStart = (dateStr: string, rangeStart: string | null | undefined): boolean => {
    return !!rangeStart && dateStr === rangeStart;
};

/**
 * Check if a date is the end of a range
 * Task 004: Range selection support
 */
const isRangeEnd = (dateStr: string, rangeEnd: string | null | undefined): boolean => {
    return !!rangeEnd && dateStr === rangeEnd;
};

/**
 * CalendarMonth - Renders a single month calendar grid
 *
 * Features:
 * - Month/year header
 * - Weekday abbreviation row
 * - 6-week date grid with proper alignment
 * - Event indicators (dots on dates with events)
 * - Selection highlighting (single date or range)
 * - Dark mode support via Fluent tokens
 */
/**
 * EventIndicator Component
 * Renders either a dot (single event) or badge (multiple events)
 * Uses Fluent tokens for colors per ADR-021
 */
interface IEventIndicatorProps {
    count: number;
    isSelected: boolean;
}

const EventIndicator: React.FC<IEventIndicatorProps> = ({ count, isSelected }) => {
    const styles = useStyles();

    if (count <= 0) {
        return null;
    }

    if (count === 1) {
        // Single event: show dot indicator
        return (
            <div className={styles.eventIndicatorContainer}>
                <div
                    className={mergeClasses(
                        styles.eventDot,
                        isSelected && styles.eventDotSelected
                    )}
                    aria-hidden="true"
                />
            </div>
        );
    }

    // Multiple events: show count badge
    return (
        <div className={styles.eventIndicatorContainer}>
            <Badge
                className={styles.eventBadge}
                size="small"
                appearance="filled"
                color={isSelected ? "informative" : "brand"}
            >
                {count > 99 ? "99+" : count}
            </Badge>
        </div>
    );
};

export const CalendarMonth: React.FC<ICalendarMonthProps> = ({
    year,
    month,
    eventCounts,
    eventDates = new Set(),
    selectedDates = new Set(),
    onDateClick,
    onDateShiftClick,
    showAdjacentMonths = false,
    focusedDate,
    onFocusDateChange,
    rangeStartDate,
    rangeEndDate
}) => {
    const styles = useStyles();
    const today = new Date();
    const gridRef = React.useRef<HTMLDivElement>(null);

    // Memoize calendar days calculation
    const calendarDays = React.useMemo(
        () => getCalendarDays(year, month),
        [year, month]
    );

    // Get dates that belong to current month (for keyboard navigation bounds)
    const currentMonthDays = React.useMemo(
        () => calendarDays.filter(d => d.getMonth() === month),
        [calendarDays, month]
    );

    /**
     * Handle day click - supports Shift+click for range selection
     * Task 004: Range selection via Shift+click
     */
    const handleDayClick = (date: Date, isCurrentMonth: boolean, event?: React.MouseEvent) => {
        if (!showAdjacentMonths && !isCurrentMonth) {
            return; // Don't allow clicking outside-month dates if not showing them
        }

        // Check for Shift key to trigger range selection
        if (event?.shiftKey && onDateShiftClick) {
            onDateShiftClick(date);
        } else if (onDateClick) {
            onDateClick(date);
        }
    };

    /**
     * Handle keyboard navigation for date cells
     * Arrow keys move focus, Enter/Space select date
     * Task 003: Keyboard accessibility per ADR-021
     */
    const handleKeyDown = React.useCallback((
        e: React.KeyboardEvent,
        date: Date,
        index: number,
        isCurrentMonth: boolean
    ) => {
        // Enter/Space selects the date
        if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            handleDayClick(date, isCurrentMonth);
            return;
        }

        // Arrow key navigation
        if (!["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"].includes(e.key)) {
            return;
        }

        e.preventDefault();

        let newDate: Date;
        const currentDate = new Date(date);

        switch (e.key) {
            case "ArrowLeft":
                newDate = new Date(currentDate);
                newDate.setDate(currentDate.getDate() - 1);
                break;
            case "ArrowRight":
                newDate = new Date(currentDate);
                newDate.setDate(currentDate.getDate() + 1);
                break;
            case "ArrowUp":
                newDate = new Date(currentDate);
                newDate.setDate(currentDate.getDate() - 7);
                break;
            case "ArrowDown":
                newDate = new Date(currentDate);
                newDate.setDate(currentDate.getDate() + 7);
                break;
            default:
                return;
        }

        // Notify parent of focus change (parent handles cross-month navigation)
        if (onFocusDateChange) {
            onFocusDateChange(newDate);
        }
    }, [onFocusDateChange, handleDayClick]);

    return (
        <div className={styles.container}>
            {/* Month/Year Header */}
            <div className={styles.header}>
                <Text className={styles.monthLabel}>
                    {MONTHS[month]} {year}
                </Text>
            </div>

            {/* Weekday Headers */}
            <div className={styles.weekdayRow}>
                {WEEKDAYS.map((day, index) => (
                    <div key={`weekday-${index}`} className={styles.weekdayCell}>
                        {day}
                    </div>
                ))}
            </div>

            {/* Date Grid */}
            <div ref={gridRef} className={styles.grid} role="grid" aria-label="Calendar dates">
                {calendarDays.map((date, index) => {
                    const dateStr = toIsoDateString(date);
                    const isCurrentMonth = date.getMonth() === month;
                    const isToday = isSameDay(date, today);
                    const isSelected = selectedDates.has(dateStr);
                    const isFocused = focusedDate === dateStr;

                    // Task 004: Range selection highlighting
                    const inRange = isDateInRange(dateStr, rangeStartDate, rangeEndDate);
                    const isStart = isRangeStart(dateStr, rangeStartDate);
                    const isEnd = isRangeEnd(dateStr, rangeEndDate);

                    // Get event count: prefer eventCounts Map, fallback to eventDates Set
                    const eventCount = eventCounts
                        ? (eventCounts.get(dateStr) || 0)
                        : (eventDates.has(dateStr) ? 1 : 0);
                    const hasEvent = eventCount > 0;

                    // Build class list based on date state
                    // Task 004: Added range styling classes
                    const cellClasses = mergeClasses(
                        styles.dayCell,
                        !isCurrentMonth && styles.dayCellOutsideMonth,
                        isToday && styles.dayCellToday,
                        hasEvent && styles.dayCellHasEvent,
                        // Range highlighting (in-range dates get background)
                        inRange && styles.dayCellInRange,
                        isStart && styles.dayCellRangeStart,
                        isEnd && styles.dayCellRangeEnd,
                        // Selected state (range endpoints are "selected")
                        (isSelected || isStart || isEnd) && styles.dayCellSelected,
                        isFocused && styles.dayCellFocused
                    );

                    // Skip rendering outside-month dates if not showing them
                    if (!showAdjacentMonths && !isCurrentMonth) {
                        return (
                            <div
                                key={`day-${index}`}
                                className={mergeClasses(styles.dayCell, styles.dayCellOutsideMonth)}
                                aria-hidden="true"
                            >
                                {date.getDate()}
                            </div>
                        );
                    }

                    // Build accessible label with event count and range info
                    const eventLabel = hasEvent
                        ? (eventCount === 1 ? ", has 1 event" : `, has ${eventCount} events`)
                        : "";
                    const rangeLabel = inRange
                        ? (isStart && isEnd ? ", selected (single date)" : isStart ? ", range start" : isEnd ? ", range end" : ", in selected range")
                        : "";

                    return (
                        <div
                            key={`day-${index}`}
                            className={cellClasses}
                            onClick={(e) => handleDayClick(date, isCurrentMonth, e)}
                            onKeyDown={(e) => handleKeyDown(e, date, index, isCurrentMonth)}
                            role="gridcell"
                            tabIndex={isFocused ? 0 : (isCurrentMonth && !focusedDate && isToday ? 0 : -1)}
                            aria-label={`${date.toLocaleDateString()}${eventLabel}${isSelected ? ", selected" : ""}${rangeLabel}`}
                            aria-selected={isSelected || inRange}
                            data-date={dateStr}
                        >
                            {date.getDate()}
                            {/* Event indicator: dot for 1 event, badge for 2+ */}
                            {hasEvent && (
                                <EventIndicator
                                    count={eventCount}
                                    isSelected={isSelected || isStart || isEnd}
                                />
                            )}
                        </div>
                    );
                })}
            </div>
        </div>
    );
};

export default CalendarMonth;
