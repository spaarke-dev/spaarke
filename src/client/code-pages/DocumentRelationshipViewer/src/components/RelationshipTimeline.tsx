/**
 * RelationshipTimeline — SVG timeline visualization for related documents
 *
 * Adapted from SemanticSearch's SearchResultsTimeline for the DocumentRelationshipViewer.
 * Documents are plotted on a time axis (X) vs similarity axis (Y).
 * Documents without dates are shown in a "No Date" strip.
 *
 * Features:
 *   - X-axis: document modified date
 *   - Y-axis: similarity score (0-100%)
 *   - Zoom on X-axis via mouse wheel (double-click resets)
 *   - Hover tooltip with name, date, similarity, type
 *   - Click to open FilePreviewDialog
 *   - Color-coded by relationship type
 */

import React, {
  useState,
  useCallback,
  useMemo,
  useRef,
  useEffect,
} from "react";
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import type { DocumentNode, DocumentNodeData } from "../types/graph";

// =============================================
// Types
// =============================================

interface TimelinePoint {
  id: string;
  name: string;
  date: Date | null;
  similarity: number;
  radius: number;
  color: string;
  borderColor: string;
  data: DocumentNodeData;
}

// =============================================
// Props
// =============================================

export interface RelationshipTimelineProps {
  nodes: DocumentNode[];
  isDarkMode?: boolean;
  onNodeClick?: (documentId: string, documentName: string) => void;
}

// =============================================
// Constants
// =============================================

const MARGIN = { top: 30, right: 30, bottom: 50, left: 60 };
const NO_DATE_STRIP_HEIGHT = 40;
const NO_DATE_GAP = 20;
const ZOOM_FACTOR = 0.002;
const DATE_FORMAT_OPTIONS: Intl.DateTimeFormatOptions = {
  month: "short",
  year: "2-digit",
};

// Relationship type → color mapping
const RELATIONSHIP_COLORS: Record<string, { bg: string; border: string }> = {
  semantic: {
    bg: tokens.colorBrandBackground2,
    border: tokens.colorBrandStroke1,
  },
  same_matter: {
    bg: tokens.colorPaletteGreenBackground2,
    border: tokens.colorPaletteGreenBorder2,
  },
  same_project: {
    bg: tokens.colorPaletteGreenBackground2,
    border: tokens.colorPaletteGreenBorder2,
  },
  same_email: {
    bg: tokens.colorPaletteYellowBackground2,
    border: tokens.colorPaletteYellowBorder2,
  },
  same_thread: {
    bg: tokens.colorPaletteYellowBackground2,
    border: tokens.colorPaletteYellowBorder2,
  },
  same_invoice: {
    bg: tokens.colorPaletteRedBackground2,
    border: tokens.colorPaletteRedBorder2,
  },
};

function getPointColor(data: DocumentNodeData): { bg: string; border: string } {
  if (data.isSource)
    return {
      bg: tokens.colorBrandBackground,
      border: tokens.colorBrandStroke1,
    };
  const relType = data.relationshipTypes?.[0]?.type ?? data.relationshipType;
  if (relType && RELATIONSHIP_COLORS[relType])
    return RELATIONSHIP_COLORS[relType];
  return {
    bg: tokens.colorNeutralBackground3,
    border: tokens.colorNeutralStroke1,
  };
}

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

function formatTickLabel(timestamp: number): string {
  return new Date(timestamp).toLocaleDateString("en-US", DATE_FORMAT_OPTIONS);
}

function formatDate(date: Date): string {
  return date.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

// =============================================
// Component
// =============================================

export const RelationshipTimeline: React.FC<RelationshipTimelineProps> = ({
  nodes: inputNodes,
  onNodeClick,
}) => {
  const styles = useStyles();

  // ─── Container sizing ───
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [dimensions, setDimensions] = useState({ width: 0, height: 0 });

  const containerCallbackRef = useCallback((node: HTMLDivElement | null) => {
    if (containerRef.current) {
      const obs = (containerRef.current as unknown as { __ro?: ResizeObserver })
        .__ro;
      obs?.disconnect();
      containerRef.current = null;
    }
    if (node) {
      containerRef.current = node;
      const rect = node.getBoundingClientRect();
      setDimensions({ width: rect.width, height: rect.height });
      const observer = new ResizeObserver((entries) => {
        for (const entry of entries) {
          setDimensions({
            width: entry.contentRect.width,
            height: entry.contentRect.height,
          });
        }
      });
      observer.observe(node);
      (node as unknown as { __ro?: ResizeObserver }).__ro = observer;
    }
  }, []);

  useEffect(() => {
    return () => {
      const node = containerRef.current;
      if (node)
        (node as unknown as { __ro?: ResizeObserver }).__ro?.disconnect();
    };
  }, []);

  // ─── Compute timeline points from DocumentNodes ───
  const { dated, undated } = useMemo(() => {
    const datedPoints: TimelinePoint[] = [];
    const undatedPoints: TimelinePoint[] = [];

    for (const n of inputNodes) {
      const d = n.data;
      const nodeType = d.nodeType;
      // Filter hub nodes
      if (
        nodeType === "matter" ||
        nodeType === "project" ||
        nodeType === "invoice" ||
        nodeType === "email"
      )
        continue;

      const colors = getPointColor(d);
      const sim = d.similarity ?? 0;
      const point: TimelinePoint = {
        id: n.id,
        name: d.name,
        date: d.modifiedOn
          ? new Date(d.modifiedOn)
          : d.createdOn
            ? new Date(d.createdOn)
            : null,
        similarity: d.isSource ? 1.0 : sim,
        radius: d.isSource ? 12 : 6 + sim * 8,
        color: colors.bg,
        borderColor: colors.border,
        data: d,
      };

      if (point.date && !isNaN(point.date.getTime())) {
        datedPoints.push(point);
      } else {
        undatedPoints.push(point);
      }
    }

    return { dated: datedPoints, undated: undatedPoints };
  }, [inputNodes]);

  // ─── Time domain ───
  const xDomain = useMemo<[number, number] | null>(() => {
    if (dated.length === 0) return null;
    let minT = Infinity,
      maxT = -Infinity;
    for (const p of dated) {
      const t = p.date!.getTime();
      if (t < minT) minT = t;
      if (t > maxT) maxT = t;
    }
    // Add 5% padding
    const range = maxT - minT || 1000 * 60 * 60 * 24 * 30; // min 30 days
    return [minT - range * 0.05, maxT + range * 0.05];
  }, [dated]);

  // ─── Zoom state ───
  const [zoomDomain, setZoomDomain] = useState<[number, number] | null>(null);

  useEffect(() => {
    setZoomDomain(null);
  }, [inputNodes]);

  // ─── Hover state ───
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  const [tooltipPos, setTooltipPos] = useState({ x: 0, y: 0 });

  const activeXDomain = useMemo<[number, number]>(() => {
    if (zoomDomain) return zoomDomain;
    if (!xDomain) return [0, 0];
    return xDomain;
  }, [xDomain, zoomDomain]);

  // ─── Chart dimensions ───
  const chartWidth = Math.max(0, dimensions.width - MARGIN.left - MARGIN.right);
  const hasUndated = undated.length > 0;
  const chartHeight = Math.max(
    0,
    dimensions.height -
      MARGIN.top -
      MARGIN.bottom -
      (hasUndated ? NO_DATE_STRIP_HEIGHT + NO_DATE_GAP : 0),
  );

  // ─── Scale functions ───
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
      return MARGIN.top + chartHeight * (1 - score);
    },
    [chartHeight],
  );

  // ─── Visible dated points ───
  const visibleDated = useMemo(() => {
    const [domMin, domMax] = activeXDomain;
    return dated
      .filter((p) => p.date!.getTime() >= domMin && p.date!.getTime() <= domMax)
      .map((p) => ({ ...p, x: scaleX(p.date!.getTime()) }));
  }, [dated, activeXDomain, scaleX]);

  // ─── Axis labels ───
  const yAxisLabels = useMemo(
    () => [0, 25, 50, 75, 100].map((pct) => ({ pct, y: scaleY(pct / 100) })),
    [scaleY],
  );

  const xAxisTicks = useMemo(() => {
    const [domMin, domMax] = activeXDomain;
    const range = domMax - domMin;
    if (range === 0) return [];
    const tickCount = Math.max(2, Math.min(8, Math.floor(chartWidth / 80)));
    const step = range / (tickCount - 1);
    const ticks: Array<{ value: number; x: number }> = [];
    for (let i = 0; i < tickCount; i++) {
      const t = domMin + step * i;
      ticks.push({ value: t, x: scaleX(t) });
    }
    return ticks;
  }, [activeXDomain, scaleX, chartWidth]);

  // ─── Wheel zoom ───
  const handleWheel = useCallback(
    (e: React.WheelEvent<SVGSVGElement>) => {
      e.preventDefault();
      const rect = e.currentTarget.getBoundingClientRect();
      const mouseX = e.clientX - rect.left;
      if (mouseX < MARGIN.left || mouseX > MARGIN.left + chartWidth) return;

      const [domMin, domMax] = activeXDomain;
      const range = domMax - domMin;
      const cursorFraction = (mouseX - MARGIN.left) / chartWidth;
      const cursorTime = domMin + cursorFraction * range;

      const delta = e.deltaY * ZOOM_FACTOR;
      const factor = 1 + delta;
      const originalRange = xDomain
        ? (xDomain[1] - xDomain[0]) * 1.5
        : range * 2;
      const newRange = Math.max(
        1000 * 60 * 60 * 24 * 7,
        Math.min(originalRange, range * factor),
      );

      setZoomDomain([
        cursorTime - cursorFraction * newRange,
        cursorTime + (1 - cursorFraction) * newRange,
      ]);
    },
    [activeXDomain, chartWidth, xDomain],
  );

  const handleDoubleClick = useCallback(() => {
    setZoomDomain(null);
  }, []);

  // ─── Point events ───
  const handlePointMouseEnter = useCallback(
    (e: React.MouseEvent, point: TimelinePoint) => {
      setHoveredId(point.id);
      const rect = e.currentTarget.closest("svg")?.getBoundingClientRect();
      if (rect)
        setTooltipPos({
          x: e.clientX - rect.left + 12,
          y: e.clientY - rect.top + 12,
        });
    },
    [],
  );

  const handlePointMouseMove = useCallback((e: React.MouseEvent) => {
    const rect = e.currentTarget.closest("svg")?.getBoundingClientRect();
    if (rect)
      setTooltipPos({
        x: e.clientX - rect.left + 12,
        y: e.clientY - rect.top + 12,
      });
  }, []);

  const handlePointMouseLeave = useCallback(() => {
    setHoveredId(null);
  }, []);

  const handlePointClick = useCallback(
    (e: React.MouseEvent, point: TimelinePoint) => {
      e.stopPropagation();
      if (point.data.documentId)
        onNodeClick?.(point.data.documentId, point.name);
    },
    [onNodeClick],
  );

  // ─── Legend ───
  const legendEntries = useMemo(
    () => [
      {
        key: "Source",
        bg: tokens.colorBrandBackground,
        border: tokens.colorBrandStroke1,
      },
      {
        key: "Semantic",
        bg: tokens.colorBrandBackground2,
        border: tokens.colorBrandStroke1,
      },
      {
        key: "Same Matter",
        bg: tokens.colorPaletteGreenBackground2,
        border: tokens.colorPaletteGreenBorder2,
      },
      {
        key: "Same Email",
        bg: tokens.colorPaletteYellowBackground2,
        border: tokens.colorPaletteYellowBorder2,
      },
    ],
    [],
  );

  // ─── Hovered point ───
  const hoveredPoint = useMemo<TimelinePoint | null>(() => {
    if (!hoveredId) return null;
    return (
      visibleDated.find((p) => p.id === hoveredId) ??
      undated.find((p) => p.id === hoveredId) ??
      null
    );
  }, [hoveredId, visibleDated, undated]);

  const noDateStripY = MARGIN.top + chartHeight + NO_DATE_GAP;

  // ─── Empty state ───
  if (dated.length === 0 && undated.length === 0) {
    return (
      <div className={styles.container} ref={containerCallbackRef}>
        <div className={styles.centerMessage}>
          <Text size={400} weight="semibold">
            No documents to visualize
          </Text>
          <Text size={200}>
            Load a document with AI embeddings to see the timeline
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
        aria-label={`Timeline showing ${dated.length + undated.length} related documents`}
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

        {/* X-Axis ticks */}
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
          Modified Date
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

        {/* Y-Axis labels + grid */}
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

        {/* Y-Axis label */}
        <text
          x={14}
          y={MARGIN.top + chartHeight / 2}
          textAnchor="middle"
          fill={tokens.colorNeutralForeground2}
          fontSize={12}
          transform={`rotate(-90, 14, ${MARGIN.top + chartHeight / 2})`}
        >
          Similarity
        </text>

        {/* Dated points */}
        {visibleDated.map((point) => {
          const isHovered = hoveredId === point.id;
          const cy = scaleY(point.similarity);
          return (
            <circle
              key={point.id}
              cx={point.x}
              cy={cy}
              r={isHovered ? point.radius * 1.3 : point.radius}
              fill={point.color}
              stroke={point.borderColor}
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
            <line
              x1={MARGIN.left}
              y1={noDateStripY - NO_DATE_GAP / 2}
              x2={MARGIN.left + chartWidth}
              y2={noDateStripY - NO_DATE_GAP / 2}
              stroke={tokens.colorNeutralStroke2}
              strokeWidth={1}
              strokeDasharray="6 3"
            />
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
            {undated.map((point, idx) => {
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
                  fill={point.color}
                  stroke={point.borderColor}
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

      {/* Legend */}
      <div className={styles.legend}>
        {legendEntries.map((entry) => (
          <div key={entry.key} className={styles.legendItem}>
            <svg width="10" height="10">
              <circle
                cx={5}
                cy={5}
                r={4}
                fill={entry.bg}
                stroke={entry.border}
                strokeWidth={1}
              />
            </svg>
            <span className={styles.legendLabel}>{entry.key}</span>
          </div>
        ))}
      </div>

      {/* Tooltip */}
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
          {!hoveredPoint.data.isSource && (
            <div className={styles.tooltipDetail}>
              Similarity: {Math.round(hoveredPoint.similarity * 100)}%
            </div>
          )}
          {hoveredPoint.data.documentType && (
            <div className={styles.tooltipDetail}>
              Type: {hoveredPoint.data.documentType}
            </div>
          )}
          {hoveredPoint.data.relationshipLabel && (
            <div className={styles.tooltipDetail}>
              Relationship: {hoveredPoint.data.relationshipLabel}
            </div>
          )}
          {hoveredPoint.data.isSource && (
            <div
              className={styles.tooltipDetail}
              style={{ fontStyle: "italic" }}
            >
              Source document
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default RelationshipTimeline;
