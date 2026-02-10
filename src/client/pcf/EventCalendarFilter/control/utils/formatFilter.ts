/**
 * Filter Output Formatting Utilities
 * Task 006: Standardized filter output JSON format for calendar-grid communication
 *
 * These functions create properly formatted filter output JSON strings
 * that conform to the spec defined in CalendarFilter.ts.
 *
 * @see types/CalendarFilter.ts for type definitions
 * @see projects/events-workspace-apps-UX-r1/spec.md for format specification
 */

import {
    CalendarFilterOutput,
    ICalendarFilterSingle,
    ICalendarFilterRange,
    ICalendarFilterClear
} from "../types/CalendarFilter";

/**
 * Convert a Date to ISO 8601 date string (YYYY-MM-DD)
 * Uses local time zone to avoid UTC conversion issues
 *
 * @param date - Date object to convert
 * @returns ISO date string in YYYY-MM-DD format
 *
 * @example
 * toIsoDateString(new Date(2026, 1, 10)) // "2026-02-10"
 */
export function toIsoDateString(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
}

/**
 * Parse ISO 8601 date string to Date object
 * Creates Date in local time zone (midnight)
 *
 * @param dateStr - Date string in YYYY-MM-DD format
 * @returns Date object
 *
 * @example
 * parseIsoDate("2026-02-10") // Date object for Feb 10, 2026
 */
export function parseIsoDate(dateStr: string): Date {
    const [year, month, day] = dateStr.split("-").map(Number);
    return new Date(year, month - 1, day);
}

/**
 * Format single date filter output
 *
 * @param date - Selected date (Date object or ISO string)
 * @returns Filter output object
 *
 * @example
 * formatSingleDateFilter(new Date(2026, 1, 10))
 * // { type: "single", date: "2026-02-10" }
 */
export function formatSingleDateFilter(date: Date | string): ICalendarFilterSingle {
    const dateStr = typeof date === "string" ? date : toIsoDateString(date);
    return {
        type: "single",
        date: dateStr
    };
}

/**
 * Format date range filter output
 * Automatically sorts dates so start <= end
 *
 * @param date1 - First date (Date object or ISO string)
 * @param date2 - Second date (Date object or ISO string)
 * @returns Filter output object with dates sorted chronologically
 *
 * @example
 * formatRangeFilter(new Date(2026, 1, 7), new Date(2026, 1, 1))
 * // { type: "range", start: "2026-02-01", end: "2026-02-07" }
 */
export function formatRangeFilter(date1: Date | string, date2: Date | string): ICalendarFilterRange {
    const dateStr1 = typeof date1 === "string" ? date1 : toIsoDateString(date1);
    const dateStr2 = typeof date2 === "string" ? date2 : toIsoDateString(date2);

    // Sort chronologically
    const d1 = parseIsoDate(dateStr1);
    const d2 = parseIsoDate(dateStr2);

    if (d1 <= d2) {
        return {
            type: "range",
            start: dateStr1,
            end: dateStr2
        };
    } else {
        return {
            type: "range",
            start: dateStr2,
            end: dateStr1
        };
    }
}

/**
 * Format clear filter output
 *
 * @returns Filter output object for clear action
 *
 * @example
 * formatClearFilter()
 * // { type: "clear" }
 */
export function formatClearFilter(): ICalendarFilterClear {
    return { type: "clear" };
}

/**
 * Format any filter output to JSON string
 * This is the primary function for creating the filterOutput property value
 *
 * @param filter - Filter output object
 * @returns JSON string
 *
 * @example
 * formatFilterOutput({ type: "single", date: "2026-02-10" })
 * // '{"type":"single","date":"2026-02-10"}'
 */
export function formatFilterOutputToJson(filter: CalendarFilterOutput): string {
    return JSON.stringify(filter);
}

/**
 * Create single date filter JSON string
 * Convenience function combining formatSingleDateFilter and JSON.stringify
 *
 * @param date - Selected date
 * @returns JSON string for filterOutput property
 *
 * @example
 * createSingleFilterJson(new Date(2026, 1, 10))
 * // '{"type":"single","date":"2026-02-10"}'
 */
export function createSingleFilterJson(date: Date | string): string {
    return formatFilterOutputToJson(formatSingleDateFilter(date));
}

/**
 * Create date range filter JSON string
 * Convenience function combining formatRangeFilter and JSON.stringify
 *
 * @param date1 - First date of range
 * @param date2 - Second date of range
 * @returns JSON string for filterOutput property
 *
 * @example
 * createRangeFilterJson("2026-02-01", "2026-02-07")
 * // '{"type":"range","start":"2026-02-01","end":"2026-02-07"}'
 */
export function createRangeFilterJson(date1: Date | string, date2: Date | string): string {
    return formatFilterOutputToJson(formatRangeFilter(date1, date2));
}

/**
 * Create clear filter JSON string
 * Convenience function for clear filter JSON
 *
 * @returns JSON string for filterOutput property
 *
 * @example
 * createClearFilterJson()
 * // '{"type":"clear"}'
 */
export function createClearFilterJson(): string {
    return formatFilterOutputToJson(formatClearFilter());
}
