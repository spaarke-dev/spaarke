/**
 * MetricCardMatrix Component
 * Renders multiple MetricCards in a responsive CSS Grid layout
 * Used when a MetricCard visual type has a groupByField producing multiple data points
 *
 * Layout approach: Pure CSS Grid — no pixel calculations, fully responsive.
 *   - `columns` prop → grid-template-columns: repeat(N, 1fr)
 *   - Cards fill available width equally, height follows via aspect-ratio: 5/3
 *   - Automatically adapts when form section resizes
 */

import * as React from "react";
import {
  Card,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import type { IAggregatedDataPoint, DrillInteraction } from "../types";

export type MatrixJustification = "left" | "center" | "right";

export interface IMetricCardMatrixProps {
  /** Title displayed above the card grid */
  title?: string;
  /** Data points to render as individual cards */
  dataPoints: IAggregatedDataPoint[];
  /** Number of cards per row */
  columns?: number;
  /** Available width in pixels (from PCF property) — unused, kept for interface compat */
  width?: number;
  /** Minimum height in pixels (from PCF property) */
  height?: number;
  /** How to justify the card grid within the container */
  justification?: MatrixJustification;
  /** Callback for drill-through interaction */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interactions */
  drillField?: string;
}

/** Gap between cards in pixels */
const CARD_GAP = 8;

const useStyles = makeStyles({
  wrapper: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    boxSizing: "border-box",
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
    gap: `${CARD_GAP}px`,
  },
  card: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    boxSizing: "border-box",
    cursor: "default",
    transition: "box-shadow 0.2s ease-in-out, transform 0.2s ease-in-out",
    padding: tokens.spacingVerticalS,
    gap: tokens.spacingVerticalXXS,
    // 3:5 height-to-width ratio → CSS aspect-ratio is width/height = 5/3
    aspectRatio: "5 / 3",
  },
  cardInteractive: {
    cursor: "pointer",
    "&:hover": {
      boxShadow: tokens.shadow8,
      transform: "translateY(-2px)",
    },
    "&:active": {
      transform: "translateY(0)",
    },
  },
  cardLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightRegular,
    textAlign: "center",
    lineHeight: tokens.lineHeightBase200,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    width: "100%",
  },
  cardValue: {
    fontSize: tokens.fontSizeHero700,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightHero700,
    color: tokens.colorNeutralForeground1,
    textAlign: "center",
  },
});

/**
 * Formats a number for display (e.g., 1000 -> 1K, 1000000 -> 1M)
 */
const formatValue = (value: number): string => {
  if (value >= 1000000) {
    return `${(value / 1000000).toFixed(1)}M`;
  }
  if (value >= 1000) {
    return `${(value / 1000).toFixed(1)}K`;
  }
  return value.toLocaleString();
};

/**
 * MetricCardMatrix - Renders grouped data as individual metric cards in a responsive grid
 */
export const MetricCardMatrix: React.FC<IMetricCardMatrixProps> = ({
  title,
  dataPoints,
  columns: columnsProp,
  height,
  justification = "left",
  onDrillInteraction,
  drillField,
}) => {
  const styles = useStyles();

  const count = dataPoints.length;
  // Columns: use prop, or default to data count (all in one row)
  const cols = columnsProp && columnsProp > 0
    ? Math.min(columnsProp, count)
    : count;

  const isInteractive = !!onDrillInteraction && !!drillField;

  const handleCardClick = (dp: IAggregatedDataPoint) => {
    if (isInteractive && onDrillInteraction && drillField) {
      onDrillInteraction({
        field: drillField,
        operator: "eq",
        value: dp.fieldValue ?? dp.label,
        label: dp.label,
      });
    }
  };

  // Grid style: columns via repeat(N, 1fr), justify via justify-items
  const gridStyle: React.CSSProperties = {
    gridTemplateColumns: `repeat(${cols}, 1fr)`,
    justifyItems:
      justification === "center" ? "center"
        : justification === "right" ? "end"
          : "stretch",
  };

  // Wrapper min-height from Height prop
  const wrapperStyle: React.CSSProperties = height
    ? { minHeight: `${height}px` }
    : {};

  return (
    <div className={styles.wrapper} style={wrapperStyle}>
      {title && (
        <Text className={styles.title}>{title}</Text>
      )}
      <div className={styles.grid} style={gridStyle}>
        {dataPoints.map((dp, idx) => (
          <Card
            key={`${dp.label}-${idx}`}
            className={mergeClasses(
              styles.card,
              isInteractive && styles.cardInteractive
            )}
            onClick={isInteractive ? () => handleCardClick(dp) : undefined}
            tabIndex={isInteractive ? 0 : undefined}
            role={isInteractive ? "button" : undefined}
            aria-label={
              isInteractive
                ? `${dp.label}: ${dp.value}. Click to view details.`
                : `${dp.label}: ${dp.value}`
            }
          >
            <Text className={styles.cardLabel}>{dp.label}</Text>
            <Text className={styles.cardValue}>{formatValue(dp.value)}</Text>
          </Card>
        ))}
      </div>
    </div>
  );
};
