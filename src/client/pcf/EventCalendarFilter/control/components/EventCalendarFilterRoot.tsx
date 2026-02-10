/**
 * EventCalendarFilterRoot Component
 *
 * Main component for the EventCalendarFilter PCF control.
 * Displays a multi-month calendar with event indicators and date selection.
 *
 * Task 002: Replaced placeholder with full CalendarStack implementation.
 * - Shows 3 months stacked vertically (Feb, Mar, Apr pattern per mockup)
 * - User scrolls up/down to navigate to previous/next months
 * - Selected dates highlighted in blue
 * - Date ranges can span across month boundaries
 *
 * Task 006: Standardized filter output JSON format
 * - Uses CalendarFilter types for consistent JSON structure
 * - Uses formatFilter utilities for JSON generation
 * - See types/CalendarFilter.ts for JSON format specification
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/002-implement-fluent-calendar-component.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/006-implement-filter-output-json.poml
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Button
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import { IInputs } from "../generated/ManifestTypes";
import { CalendarStack, IEventDateInfo } from "./CalendarStack";
import { CalendarFilterOutput } from "../types/CalendarFilter";
import {
    toIsoDateString,
    parseIsoDate,
    formatSingleDateFilter,
    formatRangeFilter,
    formatClearFilter,
    formatFilterOutputToJson
} from "../utils/formatFilter";

const CONTROL_VERSION = "1.0.4";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        boxSizing: "border-box",
        overflow: "hidden"
    },
    calendarWrapper: {
        flex: 1,
        minHeight: 0, // Allow flexbox to shrink content
        overflow: "hidden"
    },
    footer: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground1
    },
    /**
     * Task 004: Clear Range button styling
     * Uses subtle appearance with Fluent tokens per ADR-021
     */
    clearButton: {
        fontSize: tokens.fontSizeBase200,
        minWidth: "auto",
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS
    },
    versionText: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground4
    }
});

export interface IEventCalendarFilterRootProps {
    /** PCF context for accessing platform APIs */
    context: ComponentFramework.Context<IInputs>;
    /**
     * JSON string of event dates with counts
     * Format: [{"date": "2026-02-15", "count": 3}, ...]
     * or simple array: ["2026-02-15", "2026-02-16", ...]
     */
    eventDatesJson?: string;
    /**
     * @deprecated Use eventDatesJson for count support
     * Array of ISO date strings (YYYY-MM-DD) with events
     */
    eventDates?: string[];
    /** Display mode: single month or multi-month stack */
    displayMode: "month" | "multiMonth";
    /** Callback when filter output changes (JSON string for grid filtering) */
    onFilterOutputChange: (filterJson: string) => void;
    /** Callback when selected date changes (ISO date string) */
    onSelectedDateChange: (dateString: string) => void;
}

/**
 * Parse eventDatesJson string to IEventDateInfo array
 * Supports two formats:
 * 1. Array of objects: [{"date": "2026-02-15", "count": 3}, ...]
 * 2. Simple array of strings: ["2026-02-15", "2026-02-16", ...] (count defaults to 1)
 */
const parseEventDatesJson = (json: string | undefined): IEventDateInfo[] => {
    if (!json || json.trim() === "") {
        return [];
    }

    try {
        const parsed = JSON.parse(json);

        if (!Array.isArray(parsed)) {
            return [];
        }

        return parsed.map((item: string | { date: string; count?: number }) => {
            if (typeof item === "string") {
                // Simple string format - count defaults to 1
                return { date: item, count: 1 };
            } else if (item && typeof item.date === "string") {
                // Object format with date and optional count
                return {
                    date: item.date,
                    count: typeof item.count === "number" ? item.count : 1
                };
            }
            return null;
        }).filter((item): item is IEventDateInfo => item !== null);
    } catch (e) {
        // Invalid JSON - return empty array
        return [];
    }
};

/**
 * EventCalendarFilterRoot - Main calendar filter component
 *
 * Features (Task 002):
 * - Multi-month vertical stack (3 months visible)
 * - Scroll navigation for month browsing
 * - Single date selection
 * - Event indicators (blue dots on dates with events)
 * - Full dark mode support via Fluent tokens
 * - Version footer per PCF requirements
 *
 * Features (Task 004):
 * - Shift+click for range selection
 * - Range highlighting across month boundaries
 * - Clear Range button
 *
 * Features (Task 005):
 * - Event count indicators (dots for single, badges for multiple)
 * - Supports eventDatesJson with count per date
 */
export const EventCalendarFilterRoot: React.FC<IEventCalendarFilterRootProps> = ({
    context,
    eventDatesJson,
    eventDates = [],
    displayMode,
    onFilterOutputChange,
    onSelectedDateChange
}) => {
    const styles = useStyles();

    // Track selected dates (single selection for now, range in Task 004)
    const [selectedDates, setSelectedDates] = React.useState<string[]>([]);

    /**
     * Task 004: Range selection state
     * - anchorDate: First date clicked (becomes range anchor for Shift+click)
     * - rangeStart: Start of selected range (always <= rangeEnd)
     * - rangeEnd: End of selected range (always >= rangeStart)
     */
    const [anchorDate, setAnchorDate] = React.useState<string | null>(null);
    const [rangeStart, setRangeStart] = React.useState<string | null>(null);
    const [rangeEnd, setRangeEnd] = React.useState<string | null>(null);

    // Parse eventDatesJson to IEventDateInfo array (memoized)
    const eventDateInfos = React.useMemo(
        () => parseEventDatesJson(eventDatesJson),
        [eventDatesJson]
    );

    // Get container dimensions for responsive layout
    const width = context.mode.allocatedWidth || 300;
    const height = context.mode.allocatedHeight || 400;

    /**
     * Check if there's an active selection (single or range)
     * Task 004: Used to show/hide Clear button
     */
    const hasSelection = rangeStart !== null || selectedDates.length > 0;

    /**
     * Clear all selection state and notify parent
     * Task 004: Clear Range button handler
     * Task 006: Uses formatClearFilter for consistent JSON output
     */
    const handleClearSelection = React.useCallback(() => {
        setSelectedDates([]);
        setAnchorDate(null);
        setRangeStart(null);
        setRangeEnd(null);

        // Task 006: Use standardized filter format
        const filterOutput: CalendarFilterOutput = formatClearFilter();
        onFilterOutputChange(formatFilterOutputToJson(filterOutput));
        onSelectedDateChange("");
    }, [onFilterOutputChange, onSelectedDateChange]);

    /**
     * Handle date click - update selection and notify parent
     * Task 004: Sets anchor date for subsequent Shift+click
     */
    const handleDateClick = React.useCallback((date: Date) => {
        const dateStr = toIsoDateString(date);

        // Clear any existing range selection
        setRangeEnd(null);

        // Check if clicking the same date (toggle off)
        if (anchorDate === dateStr && rangeStart === dateStr && rangeEnd === null) {
            // Deselect - clear filter
            handleClearSelection();
            return;
        }

        // Set as new anchor and single selection
        setAnchorDate(dateStr);
        setRangeStart(dateStr);
        setSelectedDates([dateStr]);

        // Task 006: Use standardized filter format for single date
        const filterOutput: CalendarFilterOutput = formatSingleDateFilter(dateStr);
        onFilterOutputChange(formatFilterOutputToJson(filterOutput));
        onSelectedDateChange(dateStr);
    }, [anchorDate, rangeStart, rangeEnd, handleClearSelection, onFilterOutputChange, onSelectedDateChange]);

    /**
     * Handle Shift+click for range selection
     * Task 004: Extends selection from anchor to clicked date
     */
    const handleDateShiftClick = React.useCallback((date: Date) => {
        const dateStr = toIsoDateString(date);

        // If no anchor, treat as regular click
        if (!anchorDate) {
            handleDateClick(date);
            return;
        }

        // Calculate range (anchor to clicked date)
        const anchorDateObj = parseIsoDate(anchorDate);
        const clickedDateObj = date;

        // Determine start and end (start is always the earlier date)
        let start: string;
        let end: string;
        if (anchorDateObj <= clickedDateObj) {
            start = anchorDate;
            end = dateStr;
        } else {
            start = dateStr;
            end = anchorDate;
        }

        setRangeStart(start);
        setRangeEnd(end);

        // Build selected dates array (all dates in range)
        // For now, just mark start and end - the highlighting uses rangeStart/rangeEnd
        setSelectedDates([start, end]);

        // Task 006: Use standardized filter format for date range
        const filterOutput: CalendarFilterOutput = formatRangeFilter(start, end);
        onFilterOutputChange(formatFilterOutputToJson(filterOutput));
        onSelectedDateChange(`${start} - ${end}`);
    }, [anchorDate, handleDateClick, onFilterOutputChange, onSelectedDateChange]);

    /**
     * Handle selection change from CalendarStack
     * (Kept for compatibility, but primary handling is via handleDateClick/handleDateShiftClick)
     * Task 006: Updated to use standardized filter format utilities
     */
    const handleSelectionChange = React.useCallback((dates: Date[]) => {
        const dateStrs = dates.map(toIsoDateString);
        setSelectedDates(dateStrs);

        let filterOutput: CalendarFilterOutput;

        if (dates.length === 0) {
            filterOutput = formatClearFilter();
            onFilterOutputChange(formatFilterOutputToJson(filterOutput));
            onSelectedDateChange("");
        } else if (dates.length === 1) {
            filterOutput = formatSingleDateFilter(dateStrs[0]);
            onFilterOutputChange(formatFilterOutputToJson(filterOutput));
            onSelectedDateChange(dateStrs[0]);
        } else {
            // Range selection - sort dates chronologically
            const sorted = [...dates].sort((a, b) => a.getTime() - b.getTime());
            const startDate = toIsoDateString(sorted[0]);
            const endDate = toIsoDateString(sorted[sorted.length - 1]);
            filterOutput = formatRangeFilter(startDate, endDate);
            onFilterOutputChange(formatFilterOutputToJson(filterOutput));
            onSelectedDateChange(`${startDate} - ${endDate}`);
        }
    }, [onFilterOutputChange, onSelectedDateChange]);

    // Calculate calendar height (container minus footer)
    const calendarHeight = height - 32; // Approximate footer height

    return (
        <div className={styles.container} style={{ width, height }}>
            <div className={styles.calendarWrapper}>
                <CalendarStack
                    monthsToShow={displayMode === "multiMonth" ? 3 : 1}
                    eventDateInfos={eventDateInfos}
                    eventDates={eventDates}
                    selectedDates={selectedDates}
                    onDateClick={handleDateClick}
                    onDateShiftClick={handleDateShiftClick}
                    onSelectionChange={handleSelectionChange}
                    height={calendarHeight}
                    rangeStartDate={rangeStart}
                    rangeEndDate={rangeEnd}
                />
            </div>

            <div className={styles.footer}>
                {/* Task 004: Clear Range button - only shown when selection exists */}
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
                    <span /> // Spacer to maintain layout
                )}
                <span className={styles.versionText}>
                    v{CONTROL_VERSION}
                </span>
            </div>
        </div>
    );
};
