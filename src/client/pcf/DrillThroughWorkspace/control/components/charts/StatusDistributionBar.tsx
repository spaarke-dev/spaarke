/**
 * StatusDistributionBar Component
 * Renders a horizontal stacked bar showing status distribution
 * Supports click-to-drill for viewing underlying records
 */

import * as React from "react";
import { makeStyles, tokens, Text, mergeClasses } from "@fluentui/react-components";
import type { DrillInteraction } from "../../types";

export interface IStatusSegment {
  /** Segment label */
  label: string;
  /** Segment value/count */
  value: number;
  /** Segment color (uses Fluent tokens if not specified) */
  color?: string;
  /** Field value for drill interaction */
  fieldValue: unknown;
}

export interface IStatusDistributionBarProps {
  /** Segments to display */
  segments: IStatusSegment[];
  /** Bar title */
  title?: string;
  /** Whether to show labels on segments */
  showLabels?: boolean;
  /** Whether to show counts (vs percentages) */
  showCounts?: boolean;
  /** Callback when a segment is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Height of the bar in pixels */
  height?: number;
  /** Whether segments should be interactive */
  interactive?: boolean;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    gap: tokens.spacingVerticalXS,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
  },
  barContainer: {
    display: "flex",
    width: "100%",
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground3,
  },
  segment: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minWidth: "24px",
    transition: "opacity 0.2s ease-in-out, filter 0.2s ease-in-out",
    overflow: "hidden",
  },
  segmentInteractive: {
    cursor: "pointer",
    "&:hover": {
      filter: "brightness(1.1)",
    },
    "&:active": {
      filter: "brightness(0.95)",
    },
  },
  segmentLabel: {
    color: tokens.colorNeutralForegroundOnBrand,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightMedium,
    textShadow: "0 1px 2px rgba(0,0,0,0.3)",
    whiteSpace: "nowrap",
    overflow: "hidden",
    textOverflow: "ellipsis",
    padding: `0 ${tokens.spacingHorizontalXS}`,
  },
  legend: {
    display: "flex",
    flexWrap: "wrap",
    gap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalXS,
  },
  legendItem: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
  },
  legendColor: {
    width: "12px",
    height: "12px",
    borderRadius: tokens.borderRadiusSmall,
  },
  legendText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  placeholder: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingVerticalM,
  },
});

/**
 * Default color palette for status segments
 */
const getDefaultColors = (): string[] => [
  tokens.colorPaletteGreenBackground3,
  tokens.colorPaletteYellowBackground3,
  tokens.colorPaletteDarkOrangeBackground3,
  tokens.colorPaletteRedBackground3,
  tokens.colorPaletteBlueBorderActive,
  tokens.colorPalettePurpleBorderActive,
];

/**
 * StatusDistributionBar - Renders a horizontal stacked bar for status distribution
 */
export const StatusDistributionBar: React.FC<IStatusDistributionBarProps> = ({
  segments,
  title,
  showLabels = true,
  showCounts = true,
  onDrillInteraction,
  drillField,
  height = 32,
  interactive = true,
}) => {
  const styles = useStyles();

  const handleSegmentClick = (segment: IStatusSegment) => {
    if (interactive && onDrillInteraction && drillField) {
      onDrillInteraction({
        field: drillField,
        operator: "eq",
        value: segment.fieldValue,
        label: segment.label,
      });
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent, segment: IStatusSegment) => {
    if (interactive && (e.key === "Enter" || e.key === " ")) {
      e.preventDefault();
      handleSegmentClick(segment);
    }
  };

  if (!segments || segments.length === 0) {
    return (
      <div className={styles.container}>
        {title && <Text className={styles.title}>{title}</Text>}
        <div className={styles.placeholder}>
          <Text>No data available</Text>
        </div>
      </div>
    );
  }

  const total = segments.reduce((sum, segment) => sum + segment.value, 0);
  const colors = getDefaultColors();
  const isInteractive = interactive && onDrillInteraction && drillField;

  return (
    <div className={styles.container}>
      {title && <Text className={styles.title}>{title}</Text>}
      <div className={styles.barContainer} style={{ height }}>
        {segments.map((segment, index) => {
          const percentage = total > 0 ? (segment.value / total) * 100 : 0;
          const segmentColor = segment.color || colors[index % colors.length];
          const displayText = showCounts
            ? segment.value.toString()
            : `${percentage.toFixed(0)}%`;

          return (
            <div
              key={`${segment.label}-${index}`}
              className={mergeClasses(
                styles.segment,
                isInteractive && styles.segmentInteractive
              )}
              style={{
                width: `${percentage}%`,
                backgroundColor: segmentColor,
              }}
              onClick={isInteractive ? () => handleSegmentClick(segment) : undefined}
              onKeyDown={isInteractive ? (e) => handleKeyDown(e, segment) : undefined}
              tabIndex={isInteractive ? 0 : undefined}
              role={isInteractive ? "button" : undefined}
              aria-label={`${segment.label}: ${segment.value}`}
              title={`${segment.label}: ${segment.value} (${percentage.toFixed(1)}%)`}
            >
              {showLabels && percentage > 8 && (
                <span className={styles.segmentLabel}>{displayText}</span>
              )}
            </div>
          );
        })}
      </div>
      <div className={styles.legend}>
        {segments.map((segment, index) => (
          <div key={`legend-${segment.label}-${index}`} className={styles.legendItem}>
            <div
              className={styles.legendColor}
              style={{ backgroundColor: segment.color || colors[index % colors.length] }}
            />
            <span className={styles.legendText}>
              {segment.label}: {segment.value}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
};
