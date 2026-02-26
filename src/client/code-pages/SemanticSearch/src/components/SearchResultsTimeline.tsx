/**
 * SearchResultsTimeline — SVG timeline visualization
 *
 * Renders search results on a time axis (X) vs relevance axis (Y) with
 * colored bubbles. Results without dates are shown in a "No Date" strip
 * below the main chart area.
 *
 * Features:
 *   - X-axis: time with formatted tick labels
 *   - Y-axis: relevance score (0-100%)
 *   - Zoom on X-axis via mouse wheel (double-click resets)
 *   - Hover tooltip with name, date, score, category
 *   - Click to navigate to result
 *   - Color legend in upper-left corner
 *   - "No Date" strip for undated results
 *   - Loading / empty states with Fluent Spinner / Text
 *
 * @see useTimelineLayout — layout hook
 * @see colorScale — getCategoryColor, buildColorLegend
 * @see ADR-021 — Fluent UI v9 design system
 */

import React, { useState, useCallback, useMemo, useRef, useEffect } from "react";
import {
    makeStyles,
    tokens,
    Spinner,
    Text,
} from "@fluentui/react-components";
import type {
    DocumentSearchResult,
    RecordSearchResult,
    SearchDomain,
    VisualizationColorBy,
    TimelineDateField,
} from "../types";
import { useTimelineLayout, type TimelinePoint } from "../hooks/useTimelineLayout";
import { getCategoryColor, buildColorLegend } from "../utils/colorScale";
import { getResultDomain } from "../utils/groupResults";

// =============================================
// Props
// =============================================

export interface SearchResultsTimelineProps {
    /** Search results to plot on the timeline. */
    results: (DocumentSearchResult | RecordSearchResult)[];
    /** Which date field to use for the X-axis. */
    dateField: TimelineDateField;
    /** Category field for color-coding points. */
    colorBy: VisualizationColorBy;
    /** Whether results are still loading. */
    isLoading: boolean;
    /** Active search domain tab. */
    activeDomain: SearchDomain;
    /** Callback when a result bubble is clicked. */
    onResultClick: (resultId: string, domain: SearchDomain) => void;
}

// =============================================
// Constants
// =============================================

const MARGIN = { top: 30, right: 30, bottom: 50, left: 60 };
const NO_DATE_STRIP_HEIGHT = 40;
const NO_DATE_GAP = 20;
const ZOOM_FACTOR = 0.002;
const DATE_FORMAT_OPTIONS: Intl.DateTimeFormatOptions = { month: "short", year: "2-digit" };

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    container: {
        position: "relative",
        flex: 1,
        overflow: "hidden",
        width: "100%",
        height: "100%",
    },
    svg: {
        width: "100%",
        height: "100%",
    },
    centerMessage: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: tokens.spacingVerticalM,
        color: tokens.colorNeutralForeground3,
    },
    legend: {
        position: "absolute",
        top: tokens.spacingVerticalS,
        left: tokens.spacingHorizontalS,
        display: "flex",
        flexDirection: "column",
        gap: "4px",
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        boxShadow: tokens.shadow4,
        maxHeight: "200px",
        overflowY: "auto",
    },
    legendItem: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    legendLabel: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground2,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
        maxWidth: "140px",
    },
    tooltip: {
        position: "absolute",
        pointerEvents: "none",
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        boxShadow: tokens.shadow8,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        zIndex: 20,
        maxWidth: "260px",
    },
    tooltipName: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
    },
    tooltipDetail: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
});

// =============================================
// Helpers
// =============================================

/** Format a date tick label. */
function formatTickLabel(timestamp: number): string {
    return new Date(timestamp).toLocaleDateString("en-US", DATE_FORMAT_OPTIONS);
}

/** Format a full date for the tooltip. */
function formatDate(date: Date): string {
    return date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

// =============================================
// Component
// =============================================

export const SearchResultsTimeline: React.FC<SearchResultsTimelineProps> = ({
    results,
    dateField,
    colorBy,
    isLoading,
    activeDomain,
    onResultClick,
}) => {
    const styles = useStyles();

    // Container sizing via ResizeObserver
    const containerRef = useRef<HTMLDivElement | null>(null);
    const [dimensions, setDimensions] = useState<{ width: number; height: number }>({
        width: 0,
        height: 0,
    });

    const containerCallbackRef = useCallback(
        (node: HTMLDivElement | null) => {
            if (containerRef.current) {
                const obs = (containerRef.current as unknown as { __resizeObserver?: ResizeObserver }).__resizeObserver;
                obs?.disconnect();
                containerRef.current = null;
            }

            if (node) {
                containerRef.current = node;
                const rect = node.getBoundingClientRect();
                setDimensions({ width: rect.width, height: rect.height });

                const observer = new ResizeObserver((entries) => {
                    for (const entry of entries) {
                        const { width, height } = entry.contentRect;
                        setDimensions({ width, height });
                    }
                });
                observer.observe(node);
                (node as unknown as { __resizeObserver?: ResizeObserver }).__resizeObserver = observer;
            }
        },
        [],
    );

    // Cleanup ResizeObserver on unmount
    useEffect(() => {
        return () => {
            const node = containerRef.current;
            if (node) {
                const obs = (node as unknown as { __resizeObserver?: ResizeObserver }).__resizeObserver;
                obs?.disconnect();
            }
        };
    }, []);

    // Layout hook
    const { dated, undated, xDomain, ticks } = useTimelineLayout(
        results,
        dateField,
        colorBy,
        dimensions.width,
        dimensions.height,
    );

    // Zoom state: narrowed time range
    const [zoomDomain, setZoomDomain] = useState<[number, number] | null>(null);

    // Reset zoom when results, dateField, or colorBy change
    useEffect(() => {
        setZoomDomain(null);
    }, [results, dateField, colorBy]);

    // Hover state
    const [hoveredId, setHoveredId] = useState<string | null>(null);
    const [tooltipPos, setTooltipPos] = useState<{ x: number; y: number }>({ x: 0, y: 0 });

    // Active X-axis domain as timestamps (original or zoomed)
    const activeXDomain = useMemo<[number, number]>(() => {
        if (zoomDomain) return zoomDomain;
        if (!xDomain) return [0, 0];
        return [xDomain[0].getTime(), xDomain[1].getTime()];
    }, [xDomain, zoomDomain]);

    // Chart dimensions
    const chartWidth = Math.max(0, dimensions.width - MARGIN.left - MARGIN.right);
    const hasUndated = undated.length > 0;
    const chartHeight = Math.max(
        0,
        dimensions.height -
            MARGIN.top -
            MARGIN.bottom -
            (hasUndated ? NO_DATE_STRIP_HEIGHT + NO_DATE_GAP : 0),
    );

    // Scale functions
    const scaleX = useCallback(
        (timestamp: number): number => {
            const [domMin, domMax] = activeXDomain;
            const range = domMax - domMin;
            if (range === 0) return MARGIN.left + chartWidth / 2;
            return MARGIN.left + ((timestamp - domMin) / range) * chartWidth;
        },
        [activeXDomain, chartWidth],
    );

    const scaleY = useCallback(
        (score: number): number => {
            // Score 0 = bottom, score 1 = top
            return MARGIN.top + chartHeight * (1 - score);
        },
        [chartHeight],
    );

    // Recompute X positions for dated points when zoomed
    const visibleDated = useMemo(() => {
        const [domMin, domMax] = activeXDomain;
        return dated
            .filter((p) => p.date !== null && p.date.getTime() >= domMin && p.date.getTime() <= domMax)
            .map((p) => ({
                ...p,
                x: scaleX(p.date!.getTime()),
            }));
    }, [dated, activeXDomain, scaleX]);

    // Generate Y-axis labels
    const yAxisLabels = useMemo(
        () => [0, 25, 50, 75, 100].map((pct) => ({ pct, y: scaleY(pct / 100) })),
        [scaleY],
    );

    // Generate X-axis tick positions (use hook ticks, but recalculate positions for zoom)
    const xAxisTicks = useMemo(() => {
        const [domMin, domMax] = activeXDomain;
        const range = domMax - domMin;

        // When zoomed, generate our own ticks
        if (zoomDomain) {
            const tickCount = Math.max(2, Math.min(8, Math.floor(chartWidth / 80)));
            const step = range / (tickCount - 1);
            const generatedTicks: number[] = [];
            for (let i = 0; i < tickCount; i++) {
                generatedTicks.push(domMin + step * i);
            }
            return generatedTicks.map((t) => ({ value: t, x: scaleX(t) }));
        }

        // Use layout-provided ticks (convert Date → timestamp)
        return ticks
            .filter((t) => t.getTime() >= domMin && t.getTime() <= domMax)
            .map((t) => ({ value: t.getTime(), x: scaleX(t.getTime()) }));
    }, [activeXDomain, zoomDomain, ticks, scaleX, chartWidth]);

    // Wheel handler — zoom X-axis around cursor
    const handleWheel = useCallback(
        (e: React.WheelEvent<SVGSVGElement>) => {
            e.preventDefault();

            const rect = e.currentTarget.getBoundingClientRect();
            const mouseX = e.clientX - rect.left;

            // Only zoom if cursor is within chart area
            if (mouseX < MARGIN.left || mouseX > MARGIN.left + chartWidth) return;

            const [domMin, domMax] = activeXDomain;
            const range = domMax - domMin;
            const cursorFraction = (mouseX - MARGIN.left) / chartWidth;
            const cursorTime = domMin + cursorFraction * range;

            const delta = e.deltaY * ZOOM_FACTOR;
            const factor = 1 + delta;
            const originalRange = xDomain
                ? (xDomain[1].getTime() - xDomain[0].getTime()) * 1.5
                : range * 2;
            const newRange = Math.max(
                1000 * 60 * 60 * 24 * 7, // min 1 week
                Math.min(originalRange, range * factor),
            );

            const newMin = cursorTime - cursorFraction * newRange;
            const newMax = cursorTime + (1 - cursorFraction) * newRange;

            setZoomDomain([newMin, newMax]);
        },
        [activeXDomain, chartWidth, xDomain],
    );

    // Double-click resets zoom
    const handleDoubleClick = useCallback(() => {
        setZoomDomain(null);
    }, []);

    // Point event handlers
    const handlePointMouseEnter = useCallback(
        (e: React.MouseEvent, point: TimelinePoint) => {
            setHoveredId(point.id);
            const rect = e.currentTarget.closest("svg")?.getBoundingClientRect();
            if (rect) {
                setTooltipPos({ x: e.clientX - rect.left + 12, y: e.clientY - rect.top + 12 });
            }
        },
        [],
    );

    const handlePointMouseMove = useCallback(
        (e: React.MouseEvent) => {
            const rect = e.currentTarget.closest("svg")?.getBoundingClientRect();
            if (rect) {
                setTooltipPos({ x: e.clientX - rect.left + 12, y: e.clientY - rect.top + 12 });
            }
        },
        [],
    );

    const handlePointMouseLeave = useCallback(() => {
        setHoveredId(null);
    }, []);

    const handlePointClick = useCallback(
        (e: React.MouseEvent, point: TimelinePoint) => {
            e.stopPropagation();
            onResultClick(point.id, getResultDomain(point.result));
        },
        [onResultClick],
    );

    // Color legend
    const legendEntries = useMemo(() => {
        const allCategories = [...dated, ...undated].map((p) => p.category);
        return buildColorLegend(allCategories);
    }, [dated, undated]);

    // Hovered point for tooltip
    const hoveredPoint = useMemo<TimelinePoint | null>(() => {
        if (!hoveredId) return null;
        return (
            visibleDated.find((p) => p.id === hoveredId) ??
            undated.find((p) => p.id === hoveredId) ??
            null
        );
    }, [hoveredId, visibleDated, undated]);

    // No Date strip Y position
    const noDateStripY = MARGIN.top + chartHeight + NO_DATE_GAP;

    // Loading state
    if (isLoading) {
        return (
            <div className={styles.container} ref={containerCallbackRef}>
                <div className={styles.centerMessage}>
                    <Spinner size="medium" label="Computing timeline layout..." />
                </div>
            </div>
        );
    }

    // Empty state
    if (dated.length === 0 && undated.length === 0) {
        return (
            <div className={styles.container} ref={containerCallbackRef}>
                <div className={styles.centerMessage}>
                    <Text size={400} weight="semibold">
                        No results to visualize
                    </Text>
                    <Text size={200}>
                        Run a search to see results on the timeline
                    </Text>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container} ref={containerCallbackRef}>
            <svg
                className={styles.svg}
                onWheel={handleWheel}
                onDoubleClick={handleDoubleClick}
                aria-label={`Timeline showing ${dated.length + undated.length} results`}
            >
                {/* X-Axis line */}
                <line
                    x1={MARGIN.left}
                    y1={MARGIN.top + chartHeight}
                    x2={MARGIN.left + chartWidth}
                    y2={MARGIN.top + chartHeight}
                    stroke={tokens.colorNeutralStroke1}
                    strokeWidth={1}
                />

                {/* X-Axis ticks and labels */}
                {xAxisTicks.map((tick, i) => (
                    <g key={`x-tick-${i}`}>
                        <line
                            x1={tick.x}
                            y1={MARGIN.top + chartHeight}
                            x2={tick.x}
                            y2={MARGIN.top + chartHeight + 6}
                            stroke={tokens.colorNeutralStroke1}
                            strokeWidth={1}
                        />
                        <text
                            x={tick.x}
                            y={MARGIN.top + chartHeight + 20}
                            textAnchor="middle"
                            fill={tokens.colorNeutralForeground3}
                            fontSize={11}
                        >
                            {formatTickLabel(tick.value)}
                        </text>
                    </g>
                ))}

                {/* X-Axis label */}
                <text
                    x={MARGIN.left + chartWidth / 2}
                    y={dimensions.height - 8}
                    textAnchor="middle"
                    fill={tokens.colorNeutralForeground2}
                    fontSize={12}
                >
                    Date
                </text>

                {/* Y-Axis line */}
                <line
                    x1={MARGIN.left}
                    y1={MARGIN.top}
                    x2={MARGIN.left}
                    y2={MARGIN.top + chartHeight}
                    stroke={tokens.colorNeutralStroke1}
                    strokeWidth={1}
                />

                {/* Y-Axis labels and grid lines */}
                {yAxisLabels.map((label) => (
                    <g key={`y-${label.pct}`}>
                        <line
                            x1={MARGIN.left - 4}
                            y1={label.y}
                            x2={MARGIN.left}
                            y2={label.y}
                            stroke={tokens.colorNeutralStroke1}
                            strokeWidth={1}
                        />
                        <text
                            x={MARGIN.left - 8}
                            y={label.y + 4}
                            textAnchor="end"
                            fill={tokens.colorNeutralForeground3}
                            fontSize={11}
                        >
                            {label.pct}%
                        </text>
                        {/* Light grid line */}
                        {label.pct > 0 && label.pct < 100 && (
                            <line
                                x1={MARGIN.left + 1}
                                y1={label.y}
                                x2={MARGIN.left + chartWidth}
                                y2={label.y}
                                stroke={tokens.colorNeutralStroke2}
                                strokeWidth={0.5}
                                strokeDasharray="4 4"
                            />
                        )}
                    </g>
                ))}

                {/* Y-Axis label (rotated) */}
                <text
                    x={14}
                    y={MARGIN.top + chartHeight / 2}
                    textAnchor="middle"
                    fill={tokens.colorNeutralForeground2}
                    fontSize={12}
                    transform={`rotate(-90, 14, ${MARGIN.top + chartHeight / 2})`}
                >
                    Relevance
                </text>

                {/* Dated points */}
                {visibleDated.map((point) => {
                    const colors = getCategoryColor(point.category);
                    const isHovered = hoveredId === point.id;
                    const cy = scaleY(point.score);

                    return (
                        <circle
                            key={point.id}
                            cx={point.x}
                            cy={cy}
                            r={isHovered ? point.radius * 1.3 : point.radius}
                            fill={colors.background}
                            stroke={colors.foreground}
                            strokeWidth={isHovered ? 2 : 1}
                            style={{
                                cursor: "pointer",
                                opacity: hoveredId && !isHovered ? 0.4 : 1,
                                transition: "r 0.15s ease, opacity 0.15s ease",
                            }}
                            onMouseEnter={(e) => handlePointMouseEnter(e, point)}
                            onMouseMove={handlePointMouseMove}
                            onMouseLeave={handlePointMouseLeave}
                            onClick={(e) => handlePointClick(e, point)}
                        />
                    );
                })}

                {/* No Date strip */}
                {hasUndated && (
                    <>
                        {/* Separator line */}
                        <line
                            x1={MARGIN.left}
                            y1={noDateStripY - NO_DATE_GAP / 2}
                            x2={MARGIN.left + chartWidth}
                            y2={noDateStripY - NO_DATE_GAP / 2}
                            stroke={tokens.colorNeutralStroke2}
                            strokeWidth={1}
                            strokeDasharray="6 3"
                        />

                        {/* "No Date" label */}
                        <text
                            x={MARGIN.left - 8}
                            y={noDateStripY + NO_DATE_STRIP_HEIGHT / 2 + 4}
                            textAnchor="end"
                            fill={tokens.colorNeutralForeground3}
                            fontSize={11}
                            fontStyle="italic"
                        >
                            No Date
                        </text>

                        {/* Undated points in a horizontal row */}
                        {undated.map((point, idx) => {
                            const colors = getCategoryColor(point.category);
                            const isHovered = hoveredId === point.id;
                            const spacing = Math.min(
                                30,
                                chartWidth / Math.max(1, undated.length + 1),
                            );
                            const cx = MARGIN.left + spacing * (idx + 1);
                            const cy = noDateStripY + NO_DATE_STRIP_HEIGHT / 2;

                            return (
                                <circle
                                    key={point.id}
                                    cx={cx}
                                    cy={cy}
                                    r={isHovered ? point.radius * 1.3 : point.radius}
                                    fill={colors.background}
                                    stroke={colors.foreground}
                                    strokeWidth={isHovered ? 2 : 1}
                                    style={{
                                        cursor: "pointer",
                                        opacity: hoveredId && !isHovered ? 0.4 : 1,
                                        transition: "r 0.15s ease, opacity 0.15s ease",
                                    }}
                                    onMouseEnter={(e) => handlePointMouseEnter(e, point)}
                                    onMouseMove={handlePointMouseMove}
                                    onMouseLeave={handlePointMouseLeave}
                                    onClick={(e) => handlePointClick(e, point)}
                                />
                            );
                        })}
                    </>
                )}
            </svg>

            {/* Color legend — upper-left */}
            {legendEntries.length > 0 && (
                <div className={styles.legend}>
                    {legendEntries.map((entry) => (
                        <div key={entry.key} className={styles.legendItem}>
                            <svg width="10" height="10">
                                <circle
                                    cx={5}
                                    cy={5}
                                    r={4}
                                    fill={entry.background}
                                    stroke={entry.foreground}
                                    strokeWidth={1}
                                />
                            </svg>
                            <span className={styles.legendLabel}>{entry.key}</span>
                        </div>
                    ))}
                </div>
            )}

            {/* Hover tooltip */}
            {hoveredPoint && (
                <div
                    className={styles.tooltip}
                    style={{ left: tooltipPos.x, top: tooltipPos.y }}
                >
                    <div className={styles.tooltipName}>{hoveredPoint.name}</div>
                    {hoveredPoint.date && (
                        <div className={styles.tooltipDetail}>
                            Date: {formatDate(hoveredPoint.date)}
                        </div>
                    )}
                    <div className={styles.tooltipDetail}>
                        Score: {Math.round(hoveredPoint.score * 100)}%
                    </div>
                    <div className={styles.tooltipDetail}>
                        Category: {hoveredPoint.category}
                    </div>
                    <div className={styles.tooltipDetail}>
                        Domain: {getResultDomain(hoveredPoint.result)}
                    </div>
                </div>
            )}
        </div>
    );
};

export default SearchResultsTimeline;
