import * as React from "react";
import { makeStyles, tokens, Text } from "@fluentui/react-components";

export interface ISpendUtilizationBarProps {
  /** Total amount spent (numerator) */
  totalSpend: number;
  /** Total approved budget (denominator) */
  totalBudget: number;
  /** Pre-computed utilization 0–100. If omitted, derived from totalSpend/totalBudget. */
  utilizationPercent?: number;
}

// Utilization threshold boundaries (from spec)
const THRESHOLD_GREEN_MAX = 65; // < 65 % → green
const THRESHOLD_ORANGE_MAX = 85; // 65–85 % → orange; > 85 % → red

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    marginTop: tokens.spacingVerticalXS,
  },
  trackOuter: {
    width: "100%",
    height: "6px",
    backgroundColor: tokens.colorNeutralBackground4,
    borderRadius: tokens.borderRadiusCircular,
    overflow: "hidden",
  },
  // Fill variants — one class per threshold.
  // Fluent palette tokens adapt automatically to dark/high-contrast themes.
  fillGreen: {
    height: "100%",
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorPaletteGreenForeground1,
    transitionProperty: "width",
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
  },
  fillOrange: {
    height: "100%",
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorPaletteMarigoldForeground1,
    transitionProperty: "width",
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
  },
  fillRed: {
    height: "100%",
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorPaletteCranberryForeground2,
    transitionProperty: "width",
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
  },
  spendText: {
    color: tokens.colorNeutralForeground3,
  },
});

/** Format a dollar amount as a compact string, e.g. 1300000 → "$1.3M" */
function formatCurrency(amount: number): string {
  if (amount >= 1_000_000) {
    return `$${(amount / 1_000_000).toFixed(1)}M`;
  }
  if (amount >= 1_000) {
    return `$${(amount / 1_000).toFixed(0)}K`;
  }
  return `$${amount.toLocaleString("en-US")}`;
}

function resolveFillClass(
  pct: number,
  styles: ReturnType<typeof useStyles>
): string {
  if (pct < THRESHOLD_GREEN_MAX) return styles.fillGreen;
  if (pct <= THRESHOLD_ORANGE_MAX) return styles.fillOrange;
  return styles.fillRed;
}

function resolveAriaValueText(pct: number): string {
  if (pct < THRESHOLD_GREEN_MAX) return `${pct}% — within budget`;
  if (pct <= THRESHOLD_ORANGE_MAX) return `${pct}% — approaching budget limit`;
  return `${pct}% — over budget threshold`;
}

/**
 * SpendUtilizationBar — horizontal progress bar showing budget consumption.
 *
 * Color thresholds (spec):
 *   < 65 %  → green  (colorPaletteGreenForeground1)
 *   65–85 % → orange (colorPaletteMarigoldForeground1)
 *   > 85 %  → red    (colorPaletteCranberryForeground2)
 *
 * All colors use Fluent semantic tokens — no hardcoded hex/rgb.
 */
export const SpendUtilizationBar: React.FC<ISpendUtilizationBarProps> = ({
  totalSpend,
  totalBudget,
  utilizationPercent,
}) => {
  const styles = useStyles();

  // Derive utilization percentage; clamp to [0, 100]
  const pct = React.useMemo(() => {
    if (utilizationPercent !== undefined) {
      return Math.min(100, Math.max(0, Math.round(utilizationPercent)));
    }
    if (!totalBudget || totalBudget === 0) return 0;
    return Math.min(100, Math.max(0, Math.round((totalSpend / totalBudget) * 100)));
  }, [totalSpend, totalBudget, utilizationPercent]);

  const fillClass = resolveFillClass(pct, styles);
  const fillWidth = `${pct}%`;
  const ariaValueText = resolveAriaValueText(pct);
  const spendLabel = `${formatCurrency(totalSpend)} of ${formatCurrency(totalBudget)}`;

  return (
    <div className={styles.container}>
      {/* Progress track */}
      <div
        className={styles.trackOuter}
        role="progressbar"
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuetext={ariaValueText}
        aria-label="Budget utilization"
      >
        <div
          className={fillClass}
          style={{ width: fillWidth }}
        />
      </div>

      {/* Spend / Budget label */}
      <Text size={100} className={styles.spendText}>
        {spendLabel}
      </Text>
    </div>
  );
};
