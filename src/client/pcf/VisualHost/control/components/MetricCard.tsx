/**
 * MetricCard Component
 * Displays a single aggregate value with optional trend indicator
 * Supports click-to-drill for viewing underlying records
 */

import * as React from "react";
import {
  Card,
  CardHeader,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import {
  ArrowUpRegular,
  ArrowDownRegular,
} from "@fluentui/react-icons";
import type { DrillInteraction } from "../types";

export type TrendDirection = "up" | "down" | "neutral";

export interface IMetricCardProps {
  /** The main metric value to display */
  value: string | number;
  /** Label describing what the metric represents */
  label: string;
  /** Optional description or subtitle */
  description?: string;
  /** Trend direction (up = positive, down = negative) */
  trend?: TrendDirection;
  /** Percentage change for trend display */
  trendValue?: number;
  /** Callback when card is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Value to filter by when drilling */
  drillValue?: unknown;
  /** Whether the card should be interactive */
  interactive?: boolean;
  /** Compact mode for smaller displays */
  compact?: boolean;
  /** Fill container width with 3:5 H:W ratio (aspect-ratio: 5/3) */
  fillContainer?: boolean;
  /** Content alignment: left, center, right */
  justification?: "left" | "center" | "right";
  /** Explicit width in pixels (overrides fillContainer when both width and height set) */
  explicitWidth?: number;
  /** Explicit height in pixels (overrides fillContainer when both width and height set) */
  explicitHeight?: number;
}

const useStyles = makeStyles({
  card: {
    minWidth: "200px",
    minHeight: "120px",
    cursor: "default",
    transition: "box-shadow 0.2s ease-in-out, transform 0.2s ease-in-out",
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
  cardCompact: {
    minHeight: "80px",
    minWidth: "150px",
  },
  cardFillContainer: {
    width: "100%",
    minWidth: "unset",
    minHeight: "unset",
    // 3:5 height-to-width ratio → CSS aspect-ratio is width/height = 5/3
    aspectRatio: "5 / 3",
  },
  content: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-start",
    padding: tokens.spacingVerticalM,
    gap: tokens.spacingVerticalXS,
  },
  contentCenter: {
    alignItems: "center",
  },
  contentRight: {
    alignItems: "flex-end",
  },
  contentCompact: {
    padding: tokens.spacingVerticalS,
  },
  valueContainer: {
    display: "flex",
    alignItems: "baseline",
    gap: tokens.spacingHorizontalS,
  },
  value: {
    fontSize: tokens.fontSizeHero800,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightHero800,
    color: tokens.colorNeutralForeground1,
  },
  valueCompact: {
    fontSize: tokens.fontSizeBase600,
    lineHeight: tokens.lineHeightBase600,
  },
  label: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightRegular,
  },
  labelCompact: {
    fontSize: tokens.fontSizeBase200,
  },
  description: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    marginTop: tokens.spacingVerticalXXS,
  },
  trendContainer: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightMedium,
  },
  trendUp: {
    color: tokens.colorPaletteGreenForeground1,
  },
  trendDown: {
    color: tokens.colorPaletteRedForeground1,
  },
  trendNeutral: {
    color: tokens.colorNeutralForeground3,
  },
  trendIcon: {
    fontSize: "16px",
  },
});

/**
 * Formats a number for display (e.g., 1000 -> 1K, 1000000 -> 1M)
 */
const formatValue = (value: string | number): string => {
  if (typeof value === "string") return value;

  if (value >= 1000000) {
    return `${(value / 1000000).toFixed(1)}M`;
  }
  if (value >= 1000) {
    return `${(value / 1000).toFixed(1)}K`;
  }
  return value.toLocaleString();
};

/**
 * MetricCard - Displays a single metric value with optional trend indicator
 */
export const MetricCard: React.FC<IMetricCardProps> = ({
  value,
  label,
  description,
  trend,
  trendValue,
  onDrillInteraction,
  drillField,
  drillValue,
  interactive = true,
  compact = false,
  fillContainer = false,
  justification = "left",
  explicitWidth,
  explicitHeight,
}) => {
  const styles = useStyles();

  // v1.2.21: If both explicit width and height are set, use those exact dimensions
  const hasExplicitDimensions = explicitWidth != null && explicitHeight != null;

  const handleClick = () => {
    if (interactive && onDrillInteraction && drillField) {
      onDrillInteraction({
        field: drillField,
        operator: "eq",
        value: drillValue ?? value,
        label: label,
      });
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (interactive && (e.key === "Enter" || e.key === " ")) {
      e.preventDefault();
      handleClick();
    }
  };

  const isInteractive = interactive && onDrillInteraction && drillField;

  const getTrendStyles = () => {
    switch (trend) {
      case "up":
        return styles.trendUp;
      case "down":
        return styles.trendDown;
      default:
        return styles.trendNeutral;
    }
  };

  const renderTrendIndicator = () => {
    if (!trend || trend === "neutral") return null;

    const TrendIcon = trend === "up" ? ArrowUpRegular : ArrowDownRegular;

    return (
      <div className={mergeClasses(styles.trendContainer, getTrendStyles())}>
        <TrendIcon className={styles.trendIcon} />
        {trendValue !== undefined && (
          <span>
            {trendValue > 0 ? "+" : ""}
            {trendValue.toFixed(1)}%
          </span>
        )}
      </div>
    );
  };

  return (
    <Card
      className={mergeClasses(
        styles.card,
        isInteractive && styles.cardInteractive,
        compact && styles.cardCompact,
        fillContainer && !hasExplicitDimensions && styles.cardFillContainer
      )}
      style={hasExplicitDimensions ? {
        width: `${explicitWidth}px`,
        height: `${explicitHeight}px`,
        minWidth: "unset",
        minHeight: "unset",
      } : undefined}
      onClick={isInteractive ? handleClick : undefined}
      onKeyDown={isInteractive ? handleKeyDown : undefined}
      tabIndex={isInteractive ? 0 : undefined}
      role={isInteractive ? "button" : undefined}
      aria-label={isInteractive ? `${label}: ${value}. Click to view details.` : undefined}
    >
      <div
        className={mergeClasses(
          styles.content,
          compact && styles.contentCompact,
          justification === "center" && styles.contentCenter,
          justification === "right" && styles.contentRight
        )}
      >
        <Text
          className={mergeClasses(styles.label, compact && styles.labelCompact)}
        >
          {label}
        </Text>
        <div className={styles.valueContainer}>
          <Text
            className={mergeClasses(
              styles.value,
              compact && styles.valueCompact
            )}
          >
            {formatValue(value)}
          </Text>
          {renderTrendIndicator()}
        </div>
        {description && (
          <Text className={styles.description}>{description}</Text>
        )}
      </div>
    </Card>
  );
};
