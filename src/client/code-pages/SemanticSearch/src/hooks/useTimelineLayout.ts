/**
 * useTimelineLayout — Timeline scatter layout hook
 *
 * Positions search results along a horizontal time axis (x) with vertical
 * placement proportional to relevance score (y). Results without a parseable
 * date are placed in a separate "undated" strip at the bottom of the chart.
 *
 * Uses d3-scale (scaleTime, scaleLinear) for axis mapping and d3-array
 * (extent) for domain computation. All computation runs synchronously
 * inside useMemo.
 */

import { useMemo } from "react";
import { scaleTime, scaleLinear } from "d3-scale";
import { extent } from "d3-array";
import type { VisualizationColorBy, TimelineDateField } from "../types";
import {
    type SearchResult,
    getScore,
    getResultId,
    getResultName,
    getResultDate,
    extractClusterKey,
} from "../utils/groupResults";

// =============================================
// Public types
// =============================================

/** A single result positioned on the timeline. */
export interface TimelinePoint {
    /** Unique result ID (documentId or recordId). */
    id: string;
    /** Parsed date (null for undated results). */
    date: Date | null;
    /** Horizontal pixel position. */
    x: number;
    /** Vertical pixel position (higher score = higher up). */
    y: number;
    /** Dot radius: 3 + score * 10. */
    radius: number;
    /** Relevance score (0-1). */
    score: number;
    /** Display name of the result. */
    name: string;
    /** Category key for color-coding. */
    category: string;
    /** Original search result for drill-down. */
    result: SearchResult;
}

// =============================================
// Constants
// =============================================

const MARGIN = { top: 20, right: 20, bottom: 40, left: 50 } as const;
const UNDATED_STRIP_HEIGHT = 30;
const TICK_COUNT = 6;

// =============================================
// Helpers
// =============================================

interface ParsedResult {
    result: SearchResult;
    id: string;
    name: string;
    score: number;
    category: string;
    date: Date | null;
}

/**
 * Attempt to parse a date string. Returns null if the string is
 * missing, empty, or produces an invalid Date.
 */
function tryParseDate(dateStr: string | undefined): Date | null {
    if (!dateStr) return null;
    const d = new Date(dateStr);
    return isNaN(d.getTime()) ? null : d;
}

// =============================================
// Hook
// =============================================

/**
 * Compute timeline positions for search results.
 *
 * @param results   - All available search results (documents + records)
 * @param dateField - Which date field to use for the time axis
 * @param colorBy   - Category field for color-coding dots
 * @param width     - Available container width in pixels
 * @param height    - Available container height in pixels
 * @returns Dated points, undated points, x-axis domain, and tick marks.
 */
export function useTimelineLayout(
    results: SearchResult[],
    dateField: TimelineDateField,
    colorBy: VisualizationColorBy,
    width: number,
    height: number,
): {
    dated: TimelinePoint[];
    undated: TimelinePoint[];
    xDomain: [Date, Date] | null;
    ticks: Date[];
} {
    return useMemo(() => {
        const empty: {
            dated: TimelinePoint[];
            undated: TimelinePoint[];
            xDomain: [Date, Date] | null;
            ticks: Date[];
        } = { dated: [], undated: [], xDomain: null, ticks: [] };

        // Guard: invalid dimensions or empty results
        if (width <= 0 || height <= 0 || results.length === 0) {
            return empty;
        }

        // 1. Parse dates and enrich each result
        const parsed: ParsedResult[] = results.map((result) => ({
            result,
            id: getResultId(result),
            name: getResultName(result),
            score: getScore(result),
            category: extractClusterKey(result, colorBy),
            date: tryParseDate(getResultDate(result, dateField)),
        }));

        // 2. Separate into dated and undated
        const datedParsed: ParsedResult[] = [];
        const undatedParsed: ParsedResult[] = [];

        for (const item of parsed) {
            if (item.date !== null) {
                datedParsed.push(item);
            } else {
                undatedParsed.push(item);
            }
        }

        // 3. If no dated results, return all as undated with evenly distributed x
        if (datedParsed.length === 0) {
            const undated: TimelinePoint[] = undatedParsed.map((item, index) => ({
                id: item.id,
                date: null,
                x:
                    undatedParsed.length > 1
                        ? MARGIN.left +
                          (index / (undatedParsed.length - 1)) *
                              (width - MARGIN.left - MARGIN.right)
                        : width / 2,
                y: height - 15,
                radius: 5 + item.score * 18,
                score: item.score,
                name: item.name,
                category: item.category,
                result: item.result,
            }));

            return { dated: [], undated, xDomain: null, ticks: [] };
        }

        // 4. Compute x domain from dated results
        const [minDate, maxDate] = extent(datedParsed, (d) => d.date as Date) as [
            Date,
            Date,
        ];
        const xDomain: [Date, Date] = [minDate, maxDate];

        // 5. Create scales
        const xScale = scaleTime()
            .domain(xDomain)
            .range([MARGIN.left, width - MARGIN.right]);

        const yScale = scaleLinear()
            .domain([0, 1])
            .range([height - MARGIN.bottom - UNDATED_STRIP_HEIGHT, MARGIN.top]);

        // 6. Map dated results to TimelinePoint[]
        const dated: TimelinePoint[] = datedParsed.map((item) => ({
            id: item.id,
            date: item.date,
            x: xScale(item.date as Date),
            y: yScale(item.score),
            radius: 5 + item.score * 18,
            score: item.score,
            name: item.name,
            category: item.category,
            result: item.result,
        }));

        // 7. Map undated results: spread evenly along x, pinned to bottom strip
        const undated: TimelinePoint[] = undatedParsed.map((item, index) => ({
            id: item.id,
            date: null,
            x:
                undatedParsed.length > 1
                    ? MARGIN.left +
                      (index / (undatedParsed.length - 1)) *
                          (width - MARGIN.left - MARGIN.right)
                    : width / 2,
            y: height - 15,
            radius: 5 + item.score * 18,
            score: item.score,
            name: item.name,
            category: item.category,
            result: item.result,
        }));

        // 8. Generate tick marks for the x axis
        const ticks: Date[] = xScale.ticks(TICK_COUNT);

        return { dated, undated, xDomain, ticks };
    }, [results, dateField, colorBy, width, height]);
}

export default useTimelineLayout;
