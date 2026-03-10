/**
 * GaugeVisual Component
 * Renders semicircular arc gauges in a responsive CSS Grid layout.
 * Each gauge displays a foreground arc proportional to its value,
 * a center-formatted value (letter grade, percentage, currency, etc.),
 * and a label below the arc.
 *
 * Visual: SVG-based semicircular arcs with colored fill and endpoint dot.
 * Color logic mirrors MetricCardMatrix (value threshold / token set resolution).
 * Layout: Pure CSS Grid with auto-fill + minmax() — same pattern as MetricCardMatrix.
 */

import * as React from "react";
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import type {
  IAggregatedDataPoint,
  ICardConfig,
  ColorTokenSet,
  IColorThreshold,
} from "../types";
import { formatValue } from "../utils/valueFormatters";

// ============= Props =============

export interface IGaugeVisualProps {
  /** Title displayed above the gauge grid */
  title?: string;
  /** Data points to render as individual gauges */
  dataPoints: IAggregatedDataPoint[];
  /** Resolved card configuration from cardConfigResolver */
  cardConfig?: ICardConfig;
  /** Fixed columns override (legacy — prefer cardConfig.columns) */
  columns?: number;
  /** Available width in pixels (from PCF property) — unused, kept for interface compat */
  width?: number;
  /** Minimum height in pixels (from PCF property) */
  height?: number;
}

// ============= Constants =============

/** Gap between gauge cells in pixels */
const GAUGE_GAP = 8;

/** Card size -> CSS min-width for auto-fill grid */
const CARD_SIZE_MAP: Record<string, string> = {
  small: "80px",
  medium: "100px",
  large: "160px",
};

// SVG geometry constants
const SVG_VIEWBOX_WIDTH = 200;
const SVG_VIEWBOX_HEIGHT = 120;
const ARC_CENTER_X = 100;
const ARC_CENTER_Y = 100;
const ARC_RADIUS = 80;
const ARC_STROKE_WIDTH = 18;
const DOT_RADIUS = 10;

// ============= Color Token Resolution =============

/**
 * Resolved color values for a gauge arc
 */
interface IGaugeColorTokens {
  arcColor: string;
  valueTextColor?: string;
}

/**
 * Map a ColorTokenSet name to Fluent UI v9 semantic token values.
 * All tokens auto-adapt to light/dark mode via FluentProvider.
 */
function getTokenSetColors(tokenSet: ColorTokenSet): IGaugeColorTokens {
  switch (tokenSet) {
    case "brand":
      return {
        arcColor: tokens.colorBrandBackground,
        valueTextColor: tokens.colorBrandForeground1,
      };
    case "warning":
      return {
        arcColor: tokens.colorPaletteYellowBorderActive,
        valueTextColor: tokens.colorPaletteYellowForeground2,
      };
    case "danger":
      return {
        arcColor: tokens.colorPaletteRedBorderActive,
        valueTextColor: tokens.colorPaletteRedForeground1,
      };
    case "success":
      return {
        arcColor: tokens.colorPaletteGreenBorderActive,
        valueTextColor: tokens.colorPaletteGreenForeground1,
      };
    case "neutral":
    default:
      return {
        arcColor: tokens.colorNeutralStroke1,
        valueTextColor: tokens.colorNeutralForeground3,
      };
  }
}

/**
 * Resolve gauge arc color based on config and data point value.
 * Mirrors the resolveCardColors logic from MetricCardMatrix.
 */
function resolveGaugeColor(
  dp: IAggregatedDataPoint,
  config?: ICardConfig
): IGaugeColorTokens {
  if (!config) {
    return { arcColor: tokens.colorBrandBackground };
  }

  switch (config.colorSource) {
    case "optionSetColor": {
      if (dp.color) {
        return { arcColor: dp.color };
      }
      return { arcColor: tokens.colorNeutralStroke1 };
    }

    case "valueThreshold": {
      if (!config.colorThresholds || config.colorThresholds.length === 0) {
        return { arcColor: tokens.colorBrandBackground };
      }
      const normalizedValue = dp.value;
      for (const threshold of config.colorThresholds) {
        const [min, max] = threshold.range;
        if (normalizedValue >= min && normalizedValue <= max) {
          return getTokenSetColors(threshold.tokenSet);
        }
      }
      return getTokenSetColors("neutral");
    }

    case "signBased": {
      const invert = config.invertSign ?? false;
      if (dp.value < 0) {
        return getTokenSetColors(invert ? "success" : "danger");
      } else if (dp.value > 0) {
        return getTokenSetColors(invert ? "danger" : "success");
      }
      return getTokenSetColors("neutral");
    }

    case "none":
    default:
      return { arcColor: tokens.colorBrandBackground };
  }
}

// ============= SVG Arc Utilities =============

/**
 * Convert polar coordinates to cartesian for SVG path commands.
 * Angle 0 = right (3 o'clock), increases counter-clockwise.
 */
function polarToCartesian(
  cx: number,
  cy: number,
  radius: number,
  angleDegrees: number
): { x: number; y: number } {
  const angleRad = (angleDegrees * Math.PI) / 180;
  return {
    x: cx + radius * Math.cos(angleRad),
    y: cy - radius * Math.sin(angleRad),
  };
}

/**
 * Build an SVG arc path from startAngle to endAngle (degrees).
 * Angles: 180 = left (9 o'clock), 0 = right (3 o'clock).
 * Arc sweeps from left to right (180 -> 0) for a bottom-up semicircle.
 */
function describeArc(
  cx: number,
  cy: number,
  radius: number,
  startAngleDeg: number,
  endAngleDeg: number
): string {
  const start = polarToCartesian(cx, cy, radius, startAngleDeg);
  const end = polarToCartesian(cx, cy, radius, endAngleDeg);

  // Determine if arc is > 180 degrees (large-arc-flag)
  const angleDiff = Math.abs(startAngleDeg - endAngleDeg);
  const largeArcFlag = angleDiff > 180 ? 1 : 0;

  // sweep-flag = 1 means clockwise in SVG (y-axis down), which draws
  // the arc UPWARD from left to right — correct for a gauge semicircle.
  return [
    "M", start.x, start.y,
    "A", radius, radius, 0, largeArcFlag, 1, end.x, end.y,
  ].join(" ");
}

// ============= Sort Utilities =============

/**
 * Sort data points according to config sortBy (same logic as MetricCardMatrix)
 */
function sortDataPoints(
  points: IAggregatedDataPoint[],
  sortBy: string
): IAggregatedDataPoint[] {
  const sorted = [...points];
  switch (sortBy) {
    case "value":
      sorted.sort((a, b) => b.value - a.value);
      break;
    case "valueAsc":
      sorted.sort((a, b) => a.value - b.value);
      break;
    case "optionSetOrder":
      sorted.sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
      break;
    case "label":
    default:
      sorted.sort((a, b) => a.label.localeCompare(b.label));
      break;
  }
  return sorted;
}

// ============= Styles =============

const useStyles = makeStyles({
  wrapper: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    boxSizing: "border-box",
    marginBottom: "20px",
  },
  title: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    paddingBottom: "4px",
    flexShrink: 0,
  },
  grid: {
    display: "grid",
    gap: `${GAUGE_GAP}px`,
  },
  gaugeContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    boxSizing: "border-box",
    padding: "2px",
  },
  gaugeSvg: {
    width: "100%",
    height: "auto",
  },
  gaugeLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
    textAlign: "center",
    marginTop: "2px",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "100%",
  },
  noDataWrapper: {
    display: "flex",
    alignItems: "center",
    justifyContent: "flex-start",
    width: "100%",
    padding: tokens.spacingVerticalM,
    boxSizing: "border-box",
  },
  noDataText: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground3,
  },
});

// ============= Sub-Components =============

interface ISingleGaugeProps {
  dataPoint: IAggregatedDataPoint;
  config?: ICardConfig;
}

/**
 * SingleGauge renders one semicircular arc gauge with value and label.
 */
const SingleGauge: React.FC<ISingleGaugeProps> = ({ dataPoint, config }) => {
  const styles = useStyles();
  const dp = dataPoint;

  // Resolve display format
  const effectiveFormat = dp.valueFormat ?? config?.valueFormat ?? "shortNumber";
  const effectiveNullDisplay = config?.nullDisplay ?? "\u2014";
  const formattedValue = formatValue(dp.value, effectiveFormat, effectiveNullDisplay);

  // Resolve color
  const colorTokens = resolveGaugeColor(dp, config);

  // Compute fill fraction (clamp 0-1)
  // For values already normalized to 0-1 (grades, percentages): use directly
  // For raw values: value is already the ratio from FieldPivotService
  const fillFraction = Math.max(0, Math.min(1, dp.value));

  // Arc angles: background spans 180 -> 0 (full semicircle, left to right)
  // Foreground spans 180 -> (180 - fillFraction * 180)
  const bgStartAngle = 180;
  const bgEndAngle = 0;
  const fgStartAngle = 180;
  const fgEndAngle = 180 - fillFraction * 180;

  // Background arc path (always full semicircle)
  const bgArcPath = describeArc(
    ARC_CENTER_X, ARC_CENTER_Y, ARC_RADIUS,
    bgStartAngle, bgEndAngle
  );

  // Foreground arc path (partial, proportional to value)
  // Only render if there is measurable fill
  const hasFill = fillFraction > 0.005;
  const fgArcPath = hasFill
    ? describeArc(ARC_CENTER_X, ARC_CENTER_Y, ARC_RADIUS, fgStartAngle, fgEndAngle)
    : "";

  // Endpoint dot position (at the end of the foreground arc)
  const dotPos = hasFill
    ? polarToCartesian(ARC_CENTER_X, ARC_CENTER_Y, ARC_RADIUS, fgEndAngle)
    : null;

  // Value text position (centered in the semicircle)
  const valueFontSize = formattedValue.length <= 2 ? 28 : formattedValue.length <= 4 ? 22 : 18;

  return (
    <div
      className={styles.gaugeContainer}
      role="meter"
      aria-label={`${dp.label}: ${formattedValue}`}
      aria-valuenow={dp.value}
      aria-valuemin={0}
      aria-valuemax={1}
    >
      <svg
        className={styles.gaugeSvg}
        viewBox={`0 0 ${SVG_VIEWBOX_WIDTH} ${SVG_VIEWBOX_HEIGHT}`}
        preserveAspectRatio="xMidYMid meet"
        aria-hidden="true"
        focusable="false"
      >
        {/* Background arc — full semicircle in neutral gray */}
        <path
          d={bgArcPath}
          fill="none"
          stroke={tokens.colorNeutralStroke2}
          strokeWidth={ARC_STROKE_WIDTH}
          strokeLinecap="round"
        />

        {/* Foreground arc — partial fill proportional to value */}
        {hasFill && (
          <path
            d={fgArcPath}
            fill="none"
            stroke={colorTokens.arcColor}
            strokeWidth={ARC_STROKE_WIDTH}
            strokeLinecap="round"
          />
        )}

        {/* Endpoint dot at the tip of the foreground arc */}
        {hasFill && dotPos && (
          <circle
            cx={dotPos.x}
            cy={dotPos.y}
            r={DOT_RADIUS}
            fill={colorTokens.arcColor}
          />
        )}

        {/* Center value text */}
        <text
          x={ARC_CENTER_X}
          y={ARC_CENTER_Y - 10}
          textAnchor="middle"
          dominantBaseline="central"
          fontSize={valueFontSize}
          fontWeight="600"
          fill={colorTokens.valueTextColor ?? tokens.colorNeutralForeground1}
        >
          {formattedValue}
        </text>
      </svg>

      {/* Label below the arc */}
      <Text className={styles.gaugeLabel} title={dp.label}>
        {dp.label}
      </Text>
    </div>
  );
};

// ============= Main Component =============

/**
 * GaugeVisual - Renders grouped data as semicircular arc gauges in a responsive grid
 */
export const GaugeVisual: React.FC<IGaugeVisualProps> = ({
  title,
  dataPoints,
  cardConfig,
  columns: columnsProp,
  height,
}) => {
  const styles = useStyles();

  const config = cardConfig;
  const effectiveCardSize = config?.cardSize ?? "medium";
  const effectiveSortBy = config?.sortBy ?? "label";
  const effectiveColumns = config?.columns ?? columnsProp;
  const effectiveMaxCards = config?.maxCards;
  const showTitle = config?.showTitle ?? false;

  // Sort and limit data points
  let sortedPoints = sortDataPoints(dataPoints, effectiveSortBy);
  if (effectiveMaxCards && effectiveMaxCards > 0) {
    sortedPoints = sortedPoints.slice(0, effectiveMaxCards);
  }

  const count = sortedPoints.length;

  // Wrapper min-height from height prop
  const wrapperStyle: React.CSSProperties = height
    ? { minHeight: `${height}px` }
    : {};

  // No-data state
  if (count === 0) {
    return (
      <div className={styles.wrapper} style={wrapperStyle}>
        {showTitle && title && (
          <Text className={styles.title}>{title}</Text>
        )}
        <div className={styles.noDataWrapper}>
          <Text className={styles.noDataText}>Not yet assessed</Text>
        </div>
      </div>
    );
  }

  // Grid layout: use exact count for even distribution, or fixed columns if specified
  const gridStyle: React.CSSProperties = {
    gridTemplateColumns:
      effectiveColumns && effectiveColumns > 0
        ? `repeat(${Math.min(effectiveColumns, count)}, 1fr)`
        : `repeat(${count}, 1fr)`,
    alignContent: "start",
  };

  return (
    <div className={styles.wrapper} style={wrapperStyle}>
      {showTitle && title && (
        <Text className={styles.title}>{title}</Text>
      )}
      <div className={styles.grid} style={gridStyle}>
        {sortedPoints.map((dp, idx) => (
          <SingleGauge
            key={`${dp.label}-${idx}`}
            dataPoint={dp}
            config={config}
          />
        ))}
      </div>
    </div>
  );
};
