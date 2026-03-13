/**
 * HorizontalStackedBar Component
 * Renders a single horizontal progress bar for financial/budget visualization.
 *
 * Layout:
 *                                            $50k budget
 *   [████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░]
 *   $18.2k spent                          $31.8k remaining
 *
 * Data binding:
 *   dataPoints[0] = current/spent value
 *   dataPoints[1] = total/budget value
 *   remaining     = total - current (computed)
 *   fill %        = current / total * 100
 */

import * as React from "react";
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import type {
  IAggregatedDataPoint,
  ICardConfig,
  ColorTokenSet,
} from "../types";
import { formatValue } from "../utils/valueFormatters";

// ============= Props =============

export interface IHorizontalStackedBarProps {
  /** Optional title displayed above the bar */
  title?: string;
  /** Data points: [0] = current/spent, [1] = total/budget */
  dataPoints: IAggregatedDataPoint[];
  /** Card configuration for formatting and color thresholds */
  cardConfig?: ICardConfig;
  /** Bar height in pixels (default: 14) */
  height?: number;
}

// ============= Constants =============

const DEFAULT_BAR_HEIGHT = 20;
const FILL_TRANSITION_MS = 400;

// ============= Color Token Resolution =============

/**
 * Map a ColorTokenSet name to Fluent UI v9 semantic token values.
 * Mirrors the pattern in MetricCardMatrix for consistency.
 */
function getTokenSetColors(tokenSet: ColorTokenSet): { borderAccent?: string } {
  switch (tokenSet) {
    case "brand":
      return { borderAccent: tokens.colorBrandBackground };
    case "warning":
      return { borderAccent: tokens.colorPaletteYellowBorderActive };
    case "danger":
      return { borderAccent: tokens.colorPaletteRedBorderActive };
    case "success":
      return { borderAccent: tokens.colorPaletteGreenBorderActive };
    case "neutral":
    default:
      return { borderAccent: tokens.colorNeutralStroke1 };
  }
}

/**
 * Resolve bar fill color based on fill ratio and optional color thresholds.
 * Falls back to brand blue when no thresholds match.
 */
function resolveBarColor(fillRatio: number, config?: ICardConfig): string {
  if (config?.colorThresholds) {
    for (const threshold of config.colorThresholds) {
      if (fillRatio >= threshold.range[0] && fillRatio <= threshold.range[1]) {
        return (
          getTokenSetColors(threshold.tokenSet).borderAccent ||
          tokens.colorBrandBackground
        );
      }
    }
  }
  return tokens.colorBrandBackground;
}

// ============= Styles =============

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    gap: tokens.spacingVerticalXS,
    marginTop: "20px",
    marginBottom: "20px",
  },
  title: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
  },
  headerRow: {
    display: "flex",
    flexDirection: "row",
    justifyContent: "flex-end",
    alignItems: "baseline",
  },
  barContainer: {
    width: "100%",
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground3,
  },
  barFill: {
    height: "100%",
    borderRadius: tokens.borderRadiusMedium,
    transitionProperty: "width",
    transitionDuration: `${FILL_TRANSITION_MS}ms`,
    transitionTimingFunction: "ease-in-out",
    minWidth: 0,
  },
  footerRow: {
    display: "flex",
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "baseline",
  },
  labelText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    lineHeight: tokens.lineHeightBase200,
  },
  valueText: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase300,
  },
  placeholder: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingVerticalM,
  },
});

// ============= Component =============

/**
 * HorizontalStackedBar - Financial progress bar showing current vs. total
 * with three positioned labels: total (top-right), spent (bottom-left),
 * remaining (bottom-right).
 */
export const HorizontalStackedBar: React.FC<IHorizontalStackedBarProps> = ({
  title,
  dataPoints,
  cardConfig,
  height = DEFAULT_BAR_HEIGHT,
}) => {
  const styles = useStyles();

  // --- No-data state ---
  if (!dataPoints || dataPoints.length === 0) {
    return (
      <div className={styles.container}>
        {title && <Text className={styles.title}>{title}</Text>}
        <div className={styles.placeholder}>
          <Text>No data available</Text>
        </div>
      </div>
    );
  }

  // --- Resolve values ---
  const valueFormat = cardConfig?.valueFormat ?? "currency";
  const nullDisplay = cardConfig?.nullDisplay ?? "\u2014"; // em dash

  const currentPoint = dataPoints[0];
  const totalPoint = dataPoints.length > 1 ? dataPoints[1] : undefined;

  const currentValue = currentPoint.value ?? 0;
  const totalValue = totalPoint ? (totalPoint.value ?? 0) : 0;
  const hasTotalValue = totalPoint !== undefined && totalValue > 0;

  const remaining = hasTotalValue ? totalValue - currentValue : 0;
  const fillRatio = hasTotalValue
    ? Math.min(Math.max(currentValue / totalValue, 0), 1)
    : 0;
  const fillPercent = fillRatio * 100;

  // --- Resolve colors ---
  const barColor = resolveBarColor(fillRatio, cardConfig);

  // --- Format display values ---
  const currentLabel = currentPoint.label || "spent";
  const totalLabel = totalPoint?.label || "budget";
  const remainingLabel = "remaining";

  const formattedCurrent = formatValue(currentValue, valueFormat, nullDisplay);
  const formattedTotal = hasTotalValue
    ? formatValue(totalValue, valueFormat, nullDisplay)
    : null;
  const formattedRemaining = hasTotalValue
    ? formatValue(remaining, valueFormat, nullDisplay)
    : null;

  // --- Accessibility ---
  const ariaLabel = hasTotalValue
    ? `${formattedCurrent} ${currentLabel} of ${formattedTotal} ${totalLabel}. ${formattedRemaining} ${remainingLabel}. ${Math.round(fillPercent)}% used.`
    : `${formattedCurrent} ${currentLabel}.`;

  return (
    <div className={styles.container}>
      {title && <Text className={styles.title}>{title}</Text>}

      {/* Top-right: total value + label */}
      {hasTotalValue && (
        <div className={styles.headerRow}>
          <Text>
            <span className={styles.valueText}>{formattedTotal}</span>{" "}
            <span className={styles.labelText}>{totalLabel}</span>
          </Text>
        </div>
      )}

      {/* Progress bar */}
      <div
        className={styles.barContainer}
        style={{ height: `${height}px` }}
        role="progressbar"
        aria-valuenow={Math.round(fillPercent)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={ariaLabel}
      >
        {(hasTotalValue || currentValue > 0) && (
          <div
            className={styles.barFill}
            style={{
              width: hasTotalValue ? `${fillPercent}%` : "100%",
              backgroundColor: barColor,
            }}
          />
        )}
      </div>

      {/* Bottom row: spent (left) and remaining (right) */}
      <div className={styles.footerRow}>
        <Text>
          <span className={styles.valueText}>{formattedCurrent}</span>{" "}
          <span className={styles.labelText}>{currentLabel}</span>
        </Text>
        {hasTotalValue && formattedRemaining && (
          <Text>
            <span className={styles.valueText}>{formattedRemaining}</span>{" "}
            <span className={styles.labelText}>{remainingLabel}</span>
          </Text>
        )}
      </div>
    </div>
  );
};
