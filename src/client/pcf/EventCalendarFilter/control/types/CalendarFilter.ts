/**
 * CalendarFilter Type Definitions
 * Task 006: Standardized filter output JSON format for calendar-grid communication
 *
 * JSON Format Specification:
 * - Single date: {"type":"single","date":"YYYY-MM-DD"}
 * - Date range: {"type":"range","start":"YYYY-MM-DD","end":"YYYY-MM-DD"}
 * - Clear: {"type":"clear"}
 *
 * All dates use ISO 8601 format (YYYY-MM-DD).
 * The filter output is written to a hidden field on the form for the grid to read,
 * or passed via callback/props on Custom Pages.
 *
 * @see projects/events-workspace-apps-UX-r1/spec.md - FR-02 Event Calendar requirements
 * @see projects/events-workspace-apps-UX-r1/CLAUDE.md - Calendar-Grid Communication
 */

/**
 * Filter type discriminator for calendar output
 */
export type CalendarFilterType = "single" | "range" | "clear";

/**
 * Base filter output interface
 */
interface ICalendarFilterBase {
    /** Filter type discriminator */
    type: CalendarFilterType;
}

/**
 * Single date filter output
 * Emitted when user clicks a single date on the calendar
 *
 * Example: {"type":"single","date":"2026-02-10"}
 */
export interface ICalendarFilterSingle extends ICalendarFilterBase {
    type: "single";
    /** Selected date in ISO 8601 format (YYYY-MM-DD) */
    date: string;
}

/**
 * Date range filter output
 * Emitted when user selects a date range via Shift+click
 *
 * Example: {"type":"range","start":"2026-02-01","end":"2026-02-07"}
 *
 * Note: start is always <= end (sorted chronologically)
 */
export interface ICalendarFilterRange extends ICalendarFilterBase {
    type: "range";
    /** Range start date in ISO 8601 format (YYYY-MM-DD) */
    start: string;
    /** Range end date in ISO 8601 format (YYYY-MM-DD) */
    end: string;
}

/**
 * Clear filter output
 * Emitted when user clears selection (Clear button, click outside, etc.)
 *
 * Example: {"type":"clear"}
 */
export interface ICalendarFilterClear extends ICalendarFilterBase {
    type: "clear";
}

/**
 * Union type for all filter outputs
 * This is the type of value written to filterOutput property
 */
export type CalendarFilterOutput =
    | ICalendarFilterSingle
    | ICalendarFilterRange
    | ICalendarFilterClear;

/**
 * Type guard: Check if filter is a single date selection
 */
export function isSingleDateFilter(filter: CalendarFilterOutput): filter is ICalendarFilterSingle {
    return filter.type === "single";
}

/**
 * Type guard: Check if filter is a date range selection
 */
export function isRangeFilter(filter: CalendarFilterOutput): filter is ICalendarFilterRange {
    return filter.type === "range";
}

/**
 * Type guard: Check if filter is a clear action
 */
export function isClearFilter(filter: CalendarFilterOutput): filter is ICalendarFilterClear {
    return filter.type === "clear";
}

/**
 * Parse filter output JSON string to typed object
 *
 * @param json - JSON string from filterOutput property
 * @returns Parsed filter object or null if invalid/empty
 *
 * @example
 * const filter = parseFilterOutput('{"type":"single","date":"2026-02-10"}');
 * if (filter && isSingleDateFilter(filter)) {
 *   console.log(filter.date); // "2026-02-10"
 * }
 */
export function parseFilterOutput(json: string | null | undefined): CalendarFilterOutput | null {
    if (!json || json.trim() === "") {
        return null;
    }

    try {
        const parsed = JSON.parse(json);

        // Validate structure
        if (!parsed || typeof parsed !== "object" || !("type" in parsed)) {
            return null;
        }

        // Validate type field
        if (parsed.type === "single" && typeof parsed.date === "string") {
            return parsed as ICalendarFilterSingle;
        }

        if (
            parsed.type === "range" &&
            typeof parsed.start === "string" &&
            typeof parsed.end === "string"
        ) {
            return parsed as ICalendarFilterRange;
        }

        if (parsed.type === "clear") {
            return parsed as ICalendarFilterClear;
        }

        return null;
    } catch {
        return null;
    }
}
