/**
 * Type definitions for EventCalendarFilter PCF control
 *
 * Task 006: Added CalendarFilter types and utilities export
 */

// Export standardized filter types (Task 006)
export {
    CalendarFilterType,
    CalendarFilterOutput,
    ICalendarFilterSingle,
    ICalendarFilterRange,
    ICalendarFilterClear,
    isSingleDateFilter,
    isRangeFilter,
    isClearFilter,
    parseFilterOutput
} from "./CalendarFilter";

/**
 * @deprecated Use CalendarFilterOutput from "./CalendarFilter" instead
 * Legacy calendar filter output format - kept for backward compatibility
 */
export interface ICalendarFilterOutput {
    /** Filter type: single date, range, or clear */
    type: "single" | "range" | "clear";
    /** Single date selection (ISO format) */
    date?: string;
    /** Range start date (ISO format) */
    start?: string;
    /** Range end date (ISO format) */
    end?: string;
}

/**
 * Event date indicator
 * Represents dates that have events for showing indicators
 */
export interface IEventDateIndicator {
    /** Date in ISO format (YYYY-MM-DD) */
    date: string;
    /** Number of events on this date */
    count?: number;
    /** Optional color for the indicator */
    color?: string;
}

/**
 * Calendar display configuration
 */
export interface ICalendarConfig {
    /** Display mode: single month or multi-month stack */
    displayMode: "month" | "multiMonth";
    /** Number of months to display in multiMonth mode */
    monthsToShow?: number;
    /** Start day of week (0 = Sunday, 1 = Monday) */
    firstDayOfWeek?: 0 | 1 | 2 | 3 | 4 | 5 | 6;
    /** Show week numbers */
    showWeekNumbers?: boolean;
}

/**
 * Selection state for the calendar
 */
export interface ICalendarSelection {
    /** Selection mode: single date or range */
    mode: "single" | "range";
    /** Selected date(s) */
    selectedDate?: Date;
    /** Range start */
    rangeStart?: Date;
    /** Range end */
    rangeEnd?: Date;
}

/**
 * Range selection state for Shift+click behavior
 * Task 004: Add range selection
 */
export interface IRangeSelection {
    /** Anchor date - first date clicked (start of potential range) */
    anchorDate: string | null;
    /** Start date of the range (ISO format) - always <= endDate */
    startDate: string | null;
    /** End date of the range (ISO format) - always >= startDate */
    endDate: string | null;
}

/**
 * Control version constant
 * Task 006: Updated to 1.0.4
 */
export const CONTROL_VERSION = "1.0.4";
