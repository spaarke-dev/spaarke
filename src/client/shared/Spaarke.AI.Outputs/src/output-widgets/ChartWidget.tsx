/**
 * ChartWidget
 *
 * Renders a bar or line chart using SVG primitives only — no third-party
 * charting libraries. The chart is responsive via a ResizeObserver attached
 * to the container div.
 *
 * Axis label colors use tokens.colorNeutralForeground2.
 * Default series colors cycle through Fluent v9 palette tokens.
 * User-supplied series.color overrides the default.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * Data shape injected via the AI streaming response (already parsed by the
 * calling code page). No direct API calls inside this widget.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Spinner,
} from "@fluentui/react-components";
import type { OutputWidgetProps } from "../types";

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

export interface ChartPoint {
  /** X-axis label or numeric value. */
  x: string | number;
  /** Y-axis numeric value. */
  y: number;
}

export interface ChartSeries {
  /** Display name for this series (shown in legend). */
  name: string;
  /**
   * Optional CSS color override for this series.
   * When omitted, the default Fluent token palette is used.
   */
  color?: string;
  /** Ordered array of data points. */
  points: ChartPoint[];
}

export interface ChartData {
  /** Chart type: "bar" renders vertical bar groups, "line" renders polylines. */
  chartType: "bar" | "line";
  /** Chart title rendered above the SVG. */
  title: string;
  /** Optional X-axis label. */
  xLabel?: string;
  /** Optional Y-axis label. */
  yLabel?: string;
  /** One or more data series. Multiple series are overlaid on the same axes. */
  series: ChartSeries[];
}

export type ChartWidgetProps = OutputWidgetProps<ChartData>;

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Default Fluent v9 token names used for series colors in cycle order. */
const SERIES_COLORS_CSS = [
  "var(--colorBrandForeground1)",
  "var(--colorPaletteBlueForeground2)",
  "var(--colorPaletteGreenForeground2)",
  "var(--colorPaletteRedForeground2)",
  "var(--colorPaletteYellowForeground2)",
  "var(--colorPalettePurpleForeground2)",
];

const CHART_MARGIN = { top: 24, right: 20, bottom: 48, left: 56 };
const AXIS_TICK_COUNT = 5;
const BAR_GROUP_GAP = 0.25; // fraction of group width used as gap

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    height: "100%",
    padding: tokens.spacingHorizontalM,
    boxSizing: "border-box",
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    textAlign: "center",
    flexShrink: 0,
  },
  chartContainer: {
    flexGrow: 1,
    minHeight: 0,
    position: "relative",
  },
  svg: {
    display: "block",
    width: "100%",
    height: "100%",
  },
  legend: {
    display: "flex",
    flexWrap: "wrap",
    gap: tokens.spacingHorizontalS,
    justifyContent: "center",
    flexShrink: 0,
  },
  legendItem: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function getSeriesColor(series: ChartSeries, index: number): string {
  return series.color ?? SERIES_COLORS_CSS[index % SERIES_COLORS_CSS.length];
}

/** Collect all unique X-axis labels across all series in order of first appearance. */
function collectXLabels(seriesList: ChartSeries[]): string[] {
  const seen = new Set<string>();
  const labels: string[] = [];
  for (const s of seriesList) {
    for (const p of s.points) {
      const key = String(p.x);
      if (!seen.has(key)) {
        seen.add(key);
        labels.push(key);
      }
    }
  }
  return labels;
}

/** Get a point's Y value by X label, or 0 if not found. */
function getY(series: ChartSeries, xLabel: string): number {
  const p = series.points.find((pt) => String(pt.x) === xLabel);
  return p ? p.y : 0;
}

/** Compute a nice round max Y value for the chart axis. */
function niceMax(rawMax: number): number {
  if (rawMax <= 0) return 10;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawMax)));
  const norm = rawMax / magnitude;
  let nice: number;
  if (norm <= 1) nice = 1;
  else if (norm <= 2) nice = 2;
  else if (norm <= 5) nice = 5;
  else nice = 10;
  return nice * magnitude;
}

// ---------------------------------------------------------------------------
// SVG chart renderers
// ---------------------------------------------------------------------------

interface SvgChartProps {
  data: ChartData;
  width: number;
  height: number;
}

function BarChart({ data, width, height }: SvgChartProps): React.ReactElement {
  const { series } = data;
  const xLabels = collectXLabels(series);
  const allYValues = series.flatMap((s) => s.points.map((p) => p.y));
  const maxY = niceMax(Math.max(...allYValues, 0));

  const m = CHART_MARGIN;
  const plotW = width - m.left - m.right;
  const plotH = height - m.top - m.bottom;

  const groupCount = xLabels.length;
  const seriesCount = series.length;
  const groupW = groupCount > 0 ? plotW / groupCount : plotW;
  const barW = (groupW * (1 - BAR_GROUP_GAP)) / Math.max(seriesCount, 1);

  // Y-axis ticks
  const yTicks: number[] = [];
  for (let i = 0; i <= AXIS_TICK_COUNT; i++) {
    yTicks.push((maxY * i) / AXIS_TICK_COUNT);
  }

  const yScale = (v: number) => plotH - (v / maxY) * plotH;
  const xGroupOffset = (i: number) =>
    m.left + i * groupW + (groupW * BAR_GROUP_GAP) / 2;
  const xBarOffset = (gi: number, si: number) =>
    xGroupOffset(gi) + si * barW;

  const axisColor = tokens.colorNeutralForeground3;
  const gridColor = tokens.colorNeutralStroke2;
  const labelColor = tokens.colorNeutralForeground2;

  return (
    <svg width={width} height={height} aria-label={data.title}>
      {/* Y-axis grid lines and labels */}
      {yTicks.map((tick, i) => {
        const y = m.top + yScale(tick);
        return (
          <React.Fragment key={i}>
            <line
              x1={m.left}
              y1={y}
              x2={m.left + plotW}
              y2={y}
              stroke={gridColor}
              strokeWidth={1}
              strokeDasharray={i === 0 ? undefined : "4 4"}
            />
            <text
              x={m.left - 8}
              y={y + 4}
              textAnchor="end"
              fontSize={10}
              fill={labelColor}
            >
              {tick % 1 === 0 ? tick.toFixed(0) : tick.toFixed(1)}
            </text>
          </React.Fragment>
        );
      })}

      {/* Bars */}
      {series.map((s, si) => {
        const color = getSeriesColor(s, si);
        return xLabels.map((label, gi) => {
          const yVal = getY(s, label);
          const barH = (yVal / maxY) * plotH;
          const x = xBarOffset(gi, si);
          const y = m.top + yScale(yVal);
          return (
            <rect
              key={`${si}-${gi}`}
              x={x}
              y={y}
              width={Math.max(barW - 1, 1)}
              height={Math.max(barH, 0)}
              fill={color}
              rx={2}
            >
              <title>{`${s.name}: ${yVal}`}</title>
            </rect>
          );
        });
      })}

      {/* X-axis labels */}
      {xLabels.map((label, gi) => {
        const cx = xGroupOffset(gi) + (groupW * (1 - BAR_GROUP_GAP)) / 2;
        return (
          <text
            key={gi}
            x={cx}
            y={m.top + plotH + 16}
            textAnchor="middle"
            fontSize={10}
            fill={labelColor}
          >
            {label.length > 10 ? label.slice(0, 10) + "…" : label}
          </text>
        );
      })}

      {/* Axes */}
      <line
        x1={m.left}
        y1={m.top}
        x2={m.left}
        y2={m.top + plotH}
        stroke={axisColor}
        strokeWidth={1.5}
      />
      <line
        x1={m.left}
        y1={m.top + plotH}
        x2={m.left + plotW}
        y2={m.top + plotH}
        stroke={axisColor}
        strokeWidth={1.5}
      />

      {/* Y-axis label */}
      {data.yLabel && (
        <text
          x={14}
          y={m.top + plotH / 2}
          textAnchor="middle"
          fontSize={11}
          fill={labelColor}
          transform={`rotate(-90, 14, ${m.top + plotH / 2})`}
        >
          {data.yLabel}
        </text>
      )}

      {/* X-axis label */}
      {data.xLabel && (
        <text
          x={m.left + plotW / 2}
          y={height - 6}
          textAnchor="middle"
          fontSize={11}
          fill={labelColor}
        >
          {data.xLabel}
        </text>
      )}
    </svg>
  );
}

function LineChart({ data, width, height }: SvgChartProps): React.ReactElement {
  const { series } = data;
  const xLabels = collectXLabels(series);
  const allYValues = series.flatMap((s) => s.points.map((p) => p.y));
  const maxY = niceMax(Math.max(...allYValues, 0));

  const m = CHART_MARGIN;
  const plotW = width - m.left - m.right;
  const plotH = height - m.top - m.bottom;

  // Y-axis ticks
  const yTicks: number[] = [];
  for (let i = 0; i <= AXIS_TICK_COUNT; i++) {
    yTicks.push((maxY * i) / AXIS_TICK_COUNT);
  }

  const labelCount = xLabels.length;
  const xScale = (i: number) =>
    labelCount > 1 ? m.left + (i / (labelCount - 1)) * plotW : m.left + plotW / 2;
  const yScale = (v: number) => m.top + plotH - (v / maxY) * plotH;

  const axisColor = tokens.colorNeutralForeground3;
  const gridColor = tokens.colorNeutralStroke2;
  const labelColor = tokens.colorNeutralForeground2;

  return (
    <svg width={width} height={height} aria-label={data.title}>
      {/* Y-axis grid lines and labels */}
      {yTicks.map((tick, i) => {
        const y = yScale(tick);
        return (
          <React.Fragment key={i}>
            <line
              x1={m.left}
              y1={y}
              x2={m.left + plotW}
              y2={y}
              stroke={gridColor}
              strokeWidth={1}
              strokeDasharray={i === 0 ? undefined : "4 4"}
            />
            <text
              x={m.left - 8}
              y={y + 4}
              textAnchor="end"
              fontSize={10}
              fill={labelColor}
            >
              {tick % 1 === 0 ? tick.toFixed(0) : tick.toFixed(1)}
            </text>
          </React.Fragment>
        );
      })}

      {/* Lines + dots per series */}
      {series.map((s, si) => {
        const color = getSeriesColor(s, si);
        const pts = xLabels.map((label, gi) => ({
          cx: xScale(gi),
          cy: yScale(getY(s, label)),
          label,
          y: getY(s, label),
        }));

        const polyPoints = pts.map((p) => `${p.cx},${p.cy}`).join(" ");

        return (
          <React.Fragment key={si}>
            <polyline
              points={polyPoints}
              fill="none"
              stroke={color}
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
            />
            {pts.map((p, gi) => (
              <circle key={gi} cx={p.cx} cy={p.cy} r={4} fill={color}>
                <title>{`${s.name} (${p.label}): ${p.y}`}</title>
              </circle>
            ))}
          </React.Fragment>
        );
      })}

      {/* X-axis labels */}
      {xLabels.map((label, gi) => (
        <text
          key={gi}
          x={xScale(gi)}
          y={m.top + plotH + 16}
          textAnchor="middle"
          fontSize={10}
          fill={labelColor}
        >
          {label.length > 10 ? label.slice(0, 10) + "…" : label}
        </text>
      ))}

      {/* Axes */}
      <line
        x1={m.left}
        y1={m.top}
        x2={m.left}
        y2={m.top + plotH}
        stroke={axisColor}
        strokeWidth={1.5}
      />
      <line
        x1={m.left}
        y1={m.top + plotH}
        x2={m.left + plotW}
        y2={m.top + plotH}
        stroke={axisColor}
        strokeWidth={1.5}
      />

      {/* Y-axis label */}
      {data.yLabel && (
        <text
          x={14}
          y={m.top + plotH / 2}
          textAnchor="middle"
          fontSize={11}
          fill={labelColor}
          transform={`rotate(-90, 14, ${m.top + plotH / 2})`}
        >
          {data.yLabel}
        </text>
      )}

      {/* X-axis label */}
      {data.xLabel && (
        <text
          x={m.left + plotW / 2}
          y={height - 6}
          textAnchor="middle"
          fontSize={11}
          fill={labelColor}
        >
          {data.xLabel}
        </text>
      )}
    </svg>
  );
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ChartWidget renders a bar or line chart using SVG primitives only (no
 * third-party charting library). The chart is responsive via a ResizeObserver
 * on the container div, re-rendering the SVG whenever the container size
 * changes. All colors use Fluent v9 design tokens.
 */
export default function ChartWidget({
  data,
  isLoading,
  error,
  className,
}: ChartWidgetProps): React.ReactElement {
  const styles = useStyles();
  const containerRef = React.useRef<HTMLDivElement>(null);
  const [size, setSize] = React.useState({ width: 0, height: 0 });

  // ResizeObserver: update SVG dimensions when container resizes
  React.useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    const observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (!entry) return;
      const { width, height } = entry.contentRect;
      setSize({ width: Math.floor(width), height: Math.floor(height) });
    });

    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading chart..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  const hasData =
    data.series.length > 0 &&
    data.series.some((s) => s.points.length > 0);

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Title */}
      <Text size={300} className={styles.title}>
        {data.title}
      </Text>

      {/* SVG container — ResizeObserver watches this element */}
      <div className={styles.chartContainer} ref={containerRef}>
        {hasData && size.width > 0 && size.height > 0 ? (
          data.chartType === "bar" ? (
            <BarChart data={data} width={size.width} height={size.height} />
          ) : (
            <LineChart data={data} width={size.width} height={size.height} />
          )
        ) : (
          !hasData && (
            <Text className={styles.errorText}>No chart data available.</Text>
          )
        )}
      </div>

      {/* Legend */}
      {data.series.length > 1 && (
        <div className={styles.legend}>
          {data.series.map((s, si) => {
            const color = getSeriesColor(s, si);
            return (
              <div key={si} className={styles.legendItem}>
                <svg width={12} height={12}>
                  <circle cx={6} cy={6} r={5} fill={color} />
                </svg>
                <Text size={200}>{s.name}</Text>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
