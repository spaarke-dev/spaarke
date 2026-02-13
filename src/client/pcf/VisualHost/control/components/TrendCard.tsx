/**
 * TrendCard Component
 * Displays area name, historical average, trend indicator, and sparkline placeholder
 * Used on the Report Card tab to show performance trends per area
 */

import * as React from "react";
import {
  Card,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import {
  ArrowUpRegular,
  ArrowDownRegular,
  SubtractRegular,
} from "@fluentui/react-icons";
import type { DrillInteraction } from "../types";

export type TrendDirection = "up" | "down" | "flat";

export interface ITrendCardProps {
  /** Area name displayed as card label */
  areaName: string;
  /** Historical average grade value (0.00-1.00), null if no data */
  historicalAverage: number | null;
  /** Array of grade values for sparkline (last N assessments, chronological order) */
  trendData: number[];
  /** Trend direction calculated from linear regression */
  trendDirection: TrendDirection;
  /** Optional: callback for drill interaction */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Fill parent container width */
  fillContainer?: boolean;
}

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    padding: tokens.spacingVerticalL,
    gap: tokens.spacingVerticalS,
    minWidth: "200px",
    minHeight: "160px",
  },
  cardFillContainer: {
    width: "100%",
    minWidth: "unset",
    minHeight: "unset",
  },
  areaName: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  averageContainer: {
    display: "flex",
    alignItems: "baseline",
    gap: tokens.spacingHorizontalS,
  },
  averageValue: {
    fontSize: tokens.fontSizeHero800,
    fontWeight: tokens.fontWeightBold,
    lineHeight: tokens.lineHeightHero800,
    color: tokens.colorNeutralForeground1,
  },
  averageLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  trendContainer: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightMedium,
  },
  trendUp: {
    color: tokens.colorPaletteGreenForeground1,
  },
  trendDown: {
    color: tokens.colorPaletteRedForeground1,
  },
  trendFlat: {
    color: tokens.colorNeutralForeground3,
  },
  trendIcon: {
    fontSize: "16px",
  },
  sparklineContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "40px",
    color: tokens.colorBrandForeground1,
  },
  sparklineNoData: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "40px",
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase100,
    border: `1px dashed ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusSmall,
  },
  noData: {
    color: tokens.colorNeutralForeground3,
  },
});

/**
 * Formats a historical average value for display
 */
const formatAverage = (avg: number | null): string => {
  if (avg === null) return "N/A";
  return avg.toFixed(2);
};

/**
 * Returns the human-readable label for a trend direction
 */
const getTrendLabel = (dir: TrendDirection): string => {
  switch (dir) {
    case "up":
      return "Improving";
    case "down":
      return "Declining";
    case "flat":
      return "Stable";
  }
};

/**
 * Simple SVG sparkline component
 * Renders a mini line chart from an array of values
 */
const Sparkline: React.FC<{ data: number[]; width?: number; height?: number }> = ({
  data,
  width = 200,
  height = 40,
}) => {
  if (data.length < 2) return null;

  const padding = 4;
  const chartWidth = width - padding * 2;
  const chartHeight = height - padding * 2;

  const min = Math.min(...data);
  const max = Math.max(...data);
  const range = max - min || 1; // Avoid division by zero

  const points = data.map((value, index) => {
    const x = padding + (index / (data.length - 1)) * chartWidth;
    const y = padding + chartHeight - ((value - min) / range) * chartHeight;
    return `${x},${y}`;
  });

  const pathD = `M ${points.join(" L ")}`;

  return (
    <svg
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      role="img"
      aria-label={`Sparkline showing ${data.length} data points`}
      style={{ display: "block" }}
    >
      <path
        d={pathD}
        fill="none"
        stroke="currentColor"
        strokeWidth={2}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      {/* Dot on the last data point */}
      {data.length > 0 && (
        <circle
          cx={parseFloat(points[points.length - 1].split(",")[0])}
          cy={parseFloat(points[points.length - 1].split(",")[1])}
          r={3}
          fill="currentColor"
        />
      )}
    </svg>
  );
};

/**
 * TrendCard - Displays area performance trend with historical average and direction indicator
 */
export const TrendCard: React.FC<ITrendCardProps> = ({
  areaName,
  historicalAverage,
  trendData,
  trendDirection,
  onDrillInteraction,
  fillContainer = false,
}) => {
  const styles = useStyles();

  const getTrendStyle = (dir: TrendDirection): string => {
    switch (dir) {
      case "up":
        return styles.trendUp;
      case "down":
        return styles.trendDown;
      case "flat":
        return styles.trendFlat;
    }
  };

  const TrendIcon =
    trendDirection === "up"
      ? ArrowUpRegular
      : trendDirection === "down"
        ? ArrowDownRegular
        : SubtractRegular;

  const hasData = historicalAverage !== null;

  return (
    <Card
      className={mergeClasses(
        styles.card,
        fillContainer && styles.cardFillContainer,
      )}
      aria-label={`${areaName}: Average ${formatAverage(historicalAverage)}. Trend: ${getTrendLabel(trendDirection)}.`}
    >
      {/* Area name */}
      <Text className={styles.areaName}>{areaName}</Text>

      {/* Historical average */}
      <div className={styles.averageContainer}>
        <Text
          className={mergeClasses(
            styles.averageValue,
            !hasData && styles.noData,
          )}
        >
          {formatAverage(historicalAverage)}
        </Text>
        {hasData && <Text className={styles.averageLabel}>avg</Text>}
      </div>

      {/* Trend indicator */}
      {hasData && (
        <div
          className={mergeClasses(
            styles.trendContainer,
            getTrendStyle(trendDirection),
          )}
        >
          <TrendIcon className={styles.trendIcon} />
          <span>{getTrendLabel(trendDirection)}</span>
        </div>
      )}

      {/* Sparkline */}
      {trendData.length >= 2 ? (
        <div className={styles.sparklineContainer}>
          <Sparkline data={trendData} />
        </div>
      ) : (
        <div className={styles.sparklineNoData}>
          {trendData.length === 1 ? "Need 2+ data points for sparkline" : "No trend data"}
        </div>
      )}
    </Card>
  );
};
