/**
 * CalendarStack Component
 *
 * Multi-month vertical stack calendar wrapper that:
 * - Renders 3 CalendarMonth components vertically
 * - Manages scroll state for month navigation
 * - Handles infinite scroll up/down behavior
 * - Supports date selection across month boundaries
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
    Button
} from "@fluentui/react-components";
import {
    ChevronUpRegular,
    ChevronDownRegular
} from "@fluentui/react-icons";
import { CalendarMonth } from "./CalendarMonth";

/**
 * Event date with count for indicator display
 */
export interface IEventDateInfo {
    /** ISO date string (YYYY-MM-DD) */
    date: string;
    /** Number of events on this date */
    count: number;
}

/**
 * Props for CalendarStack component
 */
export interface ICalendarStackProps {
    /** Initial date to center the calendar on (defaults to today) */
    initialDate?: Date;
    /** Number of months to display (default: 3) */
    monthsToShow?: number;
    /**
     * Event dates with counts for indicator display
     * Each entry specifies a date and how many events occur on that date
     */
    eventDateInfos?: IEventDateInfo[];
    /**
     * @deprecated Use eventDateInfos for count-aware indicators
     * Dates with events (ISO date strings YYYY-MM-DD) - kept for backward compatibility
     */
    eventDates?: string[];
    /** Currently selected dates (ISO date strings YYYY-MM-DD) */
    selectedDates?: string[];
    /** Callback when a date is clicked */
    onDateClick?: (date: Date) => void;
    /**
     * Callback when a date is clicked with Shift key for range selection
     * Task 004: Range selection support
     */
    onDateShiftClick?: (date: Date) => void;
    /** Callback when selection changes */
    onSelectionChange?: (dates: Date[]) => void;
    /**
     * Callback when range selection changes
     * Task 004: Range selection support
     */
    onRangeChange?: (startDate: Date | null, endDate: Date | null) => void;
    /** Height of the container (for scroll calculation) */
    height?: number;
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

/**
 * Internal state for tracking displayed months
 */
interface MonthInfo {
    year: number;
    month: number;
}

/**
 * Styles using Fluent design tokens per ADR-021
 */
const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        overflow: "hidden"
    },
    navigationTop: {
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`
    },
    navigationBottom: {
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`
    },
    scrollContainer: {
        flex: 1,
        overflowY: "auto",
        overflowX: "hidden",
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        // Hide scrollbar for cleaner look (navigation buttons provide control)
        scrollbarWidth: "thin",
        "::-webkit-scrollbar": {
            width: "4px"
        },
        "::-webkit-scrollbar-track": {
            backgroundColor: tokens.colorNeutralBackground2
        },
        "::-webkit-scrollbar-thumb": {
            backgroundColor: tokens.colorNeutralStroke1,
            borderRadius: "2px"
        }
    },
    monthsWrapper: {
        display: "flex",
        flexDirection: "column",
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS
    },
    navButton: {
        minWidth: "auto",
        padding: tokens.spacingHorizontalXS
    }
});

/**
 * Get array of month info objects starting from a given date
 */
const getMonthsArray = (startDate: Date, count: number): MonthInfo[] => {
    const months: MonthInfo[] = [];
    let year = startDate.getFullYear();
    let month = startDate.getMonth();

    for (let i = 0; i < count; i++) {
        months.push({ year, month });
        month++;
        if (month > 11) {
            month = 0;
            year++;
        }
    }

    return months;
};

/**
 * Navigate months forward or backward
 */
const navigateMonth = (current: Date, delta: number): Date => {
    const newDate = new Date(current);
    newDate.setMonth(newDate.getMonth() + delta);
    return newDate;
};

/**
 * Convert a Date to ISO date string (YYYY-MM-DD) for focus tracking
 */
const toIsoDateString = (date: Date): string => {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
};

/**
 * CalendarStack - Multi-month vertical stack calendar
 *
 * Features:
 * - Shows 3 months stacked vertically (configurable)
 * - Navigate up (earlier months) and down (later months)
 * - Scroll navigation for touch/mouse users
 * - Date selection across month boundaries
 * - Event indicators on dates with events
 * - Dark mode support via Fluent tokens
 */
export const CalendarStack: React.FC<ICalendarStackProps> = ({
    initialDate,
    monthsToShow = 3,
    eventDateInfos = [],
    eventDates = [],
    selectedDates = [],
    onDateClick,
    onDateShiftClick,
    onSelectionChange,
    onRangeChange,
    height,
    rangeStartDate,
    rangeEndDate
}) => {
    const styles = useStyles();
    const scrollContainerRef = React.useRef<HTMLDivElement>(null);

    // State: First month being displayed
    const [baseDate, setBaseDate] = React.useState<Date>(() => {
        const now = initialDate || new Date();
        // Start with the previous month so current month is in the middle
        return new Date(now.getFullYear(), now.getMonth() - 1, 1);
    });

    // State: Currently focused date for keyboard navigation (ISO string)
    // Task 003: Keyboard accessibility per ADR-021
    const [focusedDate, setFocusedDate] = React.useState<string | undefined>(undefined);

    // Convert eventDateInfos to Map<string, number> for O(1) lookup in CalendarMonth
    const eventCountsMap = React.useMemo(() => {
        const map = new Map<string, number>();

        // Prefer eventDateInfos (new API with counts)
        if (eventDateInfos.length > 0) {
            eventDateInfos.forEach(info => {
                map.set(info.date, info.count);
            });
        } else if (eventDates.length > 0) {
            // Fallback to legacy eventDates (count = 1 for each)
            eventDates.forEach(date => {
                map.set(date, 1);
            });
        }

        return map;
    }, [eventDateInfos, eventDates]);

    // Convert arrays to Sets for O(1) lookup in CalendarMonth (legacy support)
    const eventDatesSet = React.useMemo(
        () => new Set(eventDates),
        [eventDates]
    );

    const selectedDatesSet = React.useMemo(
        () => new Set(selectedDates),
        [selectedDates]
    );

    // Generate array of months to display
    const displayedMonths = React.useMemo(
        () => getMonthsArray(baseDate, monthsToShow),
        [baseDate, monthsToShow]
    );

    /**
     * Navigate to earlier months (scroll up)
     */
    const handleNavigateUp = React.useCallback(() => {
        setBaseDate(prev => navigateMonth(prev, -1));
    }, []);

    /**
     * Navigate to later months (scroll down)
     */
    const handleNavigateDown = React.useCallback(() => {
        setBaseDate(prev => navigateMonth(prev, 1));
    }, []);

    /**
     * Handle date click - pass to parent callback
     */
    const handleDateClick = React.useCallback((date: Date) => {
        if (onDateClick) {
            onDateClick(date);
        }
    }, [onDateClick]);

    /**
     * Handle date Shift+click - pass to parent callback for range selection
     * Task 004: Range selection via Shift+click
     */
    const handleDateShiftClick = React.useCallback((date: Date) => {
        if (onDateShiftClick) {
            onDateShiftClick(date);
        }
    }, [onDateShiftClick]);

    /**
     * Handle focus change from keyboard navigation in CalendarMonth
     * Task 003: Arrow key navigation across months
     */
    const handleFocusDateChange = React.useCallback((date: Date) => {
        const dateStr = toIsoDateString(date);
        setFocusedDate(dateStr);

        // Check if the focused date is outside current displayed months
        const focusedYear = date.getFullYear();
        const focusedMonth = date.getMonth();

        const firstDisplayed = displayedMonths[0];
        const lastDisplayed = displayedMonths[displayedMonths.length - 1];

        // If focused date is before first displayed month, navigate up
        if (
            focusedYear < firstDisplayed.year ||
            (focusedYear === firstDisplayed.year && focusedMonth < firstDisplayed.month)
        ) {
            handleNavigateUp();
        }
        // If focused date is after last displayed month, navigate down
        else if (
            focusedYear > lastDisplayed.year ||
            (focusedYear === lastDisplayed.year && focusedMonth > lastDisplayed.month)
        ) {
            handleNavigateDown();
        }
    }, [displayedMonths, handleNavigateUp, handleNavigateDown]);

    /**
     * Handle scroll events for infinite scroll behavior
     */
    const handleScroll = React.useCallback((e: React.UIEvent<HTMLDivElement>) => {
        const container = e.currentTarget;
        const { scrollTop, scrollHeight, clientHeight } = container;

        // Near top - load earlier months
        if (scrollTop < 50) {
            handleNavigateUp();
            // Adjust scroll position to keep view consistent
            if (scrollContainerRef.current) {
                // This will be called after render, giving approximate position
                setTimeout(() => {
                    if (scrollContainerRef.current) {
                        scrollContainerRef.current.scrollTop = 150;
                    }
                }, 0);
            }
        }

        // Near bottom - load later months
        if (scrollTop + clientHeight > scrollHeight - 50) {
            handleNavigateDown();
        }
    }, [handleNavigateUp, handleNavigateDown]);

    // Keyboard navigation support
    const handleKeyDown = React.useCallback((e: React.KeyboardEvent) => {
        if (e.key === "PageUp") {
            e.preventDefault();
            handleNavigateUp();
        } else if (e.key === "PageDown") {
            e.preventDefault();
            handleNavigateDown();
        }
    }, [handleNavigateUp, handleNavigateDown]);

    return (
        <div
            className={styles.container}
            style={height ? { height } : undefined}
            onKeyDown={handleKeyDown}
        >
            {/* Top Navigation - Navigate to Earlier Months */}
            <div className={styles.navigationTop}>
                <Button
                    className={styles.navButton}
                    appearance="subtle"
                    icon={<ChevronUpRegular />}
                    onClick={handleNavigateUp}
                    aria-label="Show earlier months"
                    title="Earlier months"
                />
            </div>

            {/* Scrollable Month Stack */}
            <div
                ref={scrollContainerRef}
                className={styles.scrollContainer}
                onScroll={handleScroll}
                role="application"
                aria-label="Calendar navigation"
            >
                <div className={styles.monthsWrapper}>
                    {displayedMonths.map((monthInfo, index) => (
                        <CalendarMonth
                            key={`${monthInfo.year}-${monthInfo.month}`}
                            year={monthInfo.year}
                            month={monthInfo.month}
                            eventCounts={eventCountsMap}
                            eventDates={eventDatesSet}
                            selectedDates={selectedDatesSet}
                            onDateClick={handleDateClick}
                            onDateShiftClick={handleDateShiftClick}
                            showAdjacentMonths={false}
                            focusedDate={focusedDate}
                            onFocusDateChange={handleFocusDateChange}
                            rangeStartDate={rangeStartDate}
                            rangeEndDate={rangeEndDate}
                        />
                    ))}
                </div>
            </div>

            {/* Bottom Navigation - Navigate to Later Months */}
            <div className={styles.navigationBottom}>
                <Button
                    className={styles.navButton}
                    appearance="subtle"
                    icon={<ChevronDownRegular />}
                    onClick={handleNavigateDown}
                    aria-label="Show later months"
                    title="Later months"
                />
            </div>
        </div>
    );
};

export default CalendarStack;
