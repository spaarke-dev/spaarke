/**
 * HorizontalStackedBar Component
 * Renders a single horizontal progress bar for financial/budget visualization.
 *
 * Layout (default — backward-compatible, used when `cardConfig.layoutMode` is undefined or "default"):
 *                                            $50k budget
 *   [████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░]
 *   $18.2k spent                          $31.8k remaining
 *
 * Layout ("headlineAboveBar" — FR-VH-04, gated on cardConfig.layoutMode):
 *   $50K
 *   33% of $150K
 *   [████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░]
 *   (top-right total and bottom-right remaining labels are suppressed)
 *
 * Data binding:
 *   dataPoints[0] = current/spent value
 *   dataPoints[1] = total/budget value
 *   remaining     = total - current (computed)
 *   fill %        = current / total * 100
 *
 * NFR-05: chart defs WITHOUT `layoutMode` (or `layoutMode: "default"`) MUST render
 * byte-identically to the pre-FR-VH-04 component. The new render path is gated
 * entirely on `cardConfig.layoutMode === 'headlineAboveBar'`.
 */

import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import type { IAggregatedDataPoint, ICardConfig, ColorTokenSet } from '../types';
import { formatValue } from '../utils/valueFormatters';

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
    case 'brand':
      return { borderAccent: tokens.colorBrandBackground };
    case 'warning':
      return { borderAccent: tokens.colorPaletteYellowBorderActive };
    case 'danger':
      return { borderAccent: tokens.colorPaletteRedBorderActive };
    case 'success':
      return { borderAccent: tokens.colorPaletteGreenBorderActive };
    case 'neutral':
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
        return getTokenSetColors(threshold.tokenSet).borderAccent || tokens.colorBrandBackground;
      }
    }
  }
  return tokens.colorBrandBackground;
}

// ============= Styles =============

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    gap: tokens.spacingVerticalXS,
    marginTop: '20px',
    marginBottom: '20px',
  },
  title: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
  },
  headerRow: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'flex-end',
    alignItems: 'baseline',
  },
  barContainer: {
    width: '100%',
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground3,
  },
  barFill: {
    height: '100%',
    borderRadius: tokens.borderRadiusMedium,
    transitionProperty: 'width',
    transitionDuration: `${FILL_TRANSITION_MS}ms`,
    transitionTimingFunction: 'ease-in-out',
    minWidth: 0,
  },
  footerRow: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'baseline',
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
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingVerticalM,
  },
  // ----- headlineAboveBar layout (FR-VH-04) -----
  headlineStack: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalXXS,
    marginBottom: tokens.spacingVerticalXS,
  },
  headline: {
    fontSize: tokens.fontSizeHero700,
    lineHeight: tokens.lineHeightHero700,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  subLine: {
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
  },
});

// ============= Template substitution helper (FR-VH-04) =============

/**
 * Substitute `{remaining}`, `{percent}`, `{total}` placeholders in a template string.
 * Each value is pre-formatted (currency / percentage / etc.) before substitution.
 * Local + non-exported — this is generic but only used by HSBar today.
 */
function substituteSubLineTemplate(
  template: string,
  values: { remaining: string; percent: string; total: string }
): string {
  return template
    .replace(/\{remaining\}/g, values.remaining)
    .replace(/\{percent\}/g, values.percent)
    .replace(/\{total\}/g, values.total);
}

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
  // Either no data points at all, or all source fields are null/undefined.
  // Distinguishes "no data available" from "real zero values" so the bar doesn't
  // misleadingly show "$0 spent" for missing source data.
  const allNull = !!dataPoints && dataPoints.length > 0 && dataPoints.every(dp => dp.isNull === true);
  if (!dataPoints || dataPoints.length === 0 || allNull) {
    return (
      <div className={styles.container}>
        {title && <Text className={styles.title}>{title}</Text>}
        <div className={styles.placeholder}>
          <Text>No data available for this measure</Text>
        </div>
      </div>
    );
  }

  // --- Resolve values ---
  const valueFormat = cardConfig?.valueFormat ?? 'currency';
  const nullDisplay = cardConfig?.nullDisplay ?? '\u2014'; // em dash

  const currentPoint = dataPoints[0];
  const totalPoint = dataPoints.length > 1 ? dataPoints[1] : undefined;

  const currentValue = currentPoint.value ?? 0;
  const totalValue = totalPoint ? (totalPoint.value ?? 0) : 0;
  const hasTotalValue = totalPoint !== undefined && totalValue > 0;

  const remaining = hasTotalValue ? totalValue - currentValue : 0;
  const fillRatio = hasTotalValue ? Math.min(Math.max(currentValue / totalValue, 0), 1) : 0;
  const fillPercent = fillRatio * 100;

  // --- Resolve colors ---
  const barColor = resolveBarColor(fillRatio, cardConfig);

  // --- Format display values ---
  const currentLabel = currentPoint.label || 'spent';
  const totalLabel = totalPoint?.label || 'budget';
  const remainingLabel = 'remaining';

  const formattedCurrent = formatValue(currentValue, valueFormat, nullDisplay);
  const formattedTotal = hasTotalValue ? formatValue(totalValue, valueFormat, nullDisplay) : null;
  const formattedRemaining = hasTotalValue ? formatValue(remaining, valueFormat, nullDisplay) : null;

  // --- Accessibility ---
  const ariaLabel = hasTotalValue
    ? `${formattedCurrent} ${currentLabel} of ${formattedTotal} ${totalLabel}. ${formattedRemaining} ${remainingLabel}. ${Math.round(fillPercent)}% used.`
    : `${formattedCurrent} ${currentLabel}.`;

  // --- Layout selection (FR-VH-04) ---
  // Default = current/existing layout (NFR-05 backward compat).
  // 'headlineAboveBar' = headline + sub-line ABOVE the bar; top-right total + bottom-right
  // remaining labels suppressed; bottom-left current label also suppressed (replaced by headline).
  const isHeadlineLayout = cardConfig?.layoutMode === 'headlineAboveBar';

  // --- Headline + sub-line resolution (FR-VH-04, only when isHeadlineLayout) ---
  // Resolve the headline source data point. Match `headlineFromField` against
  // `fieldValue` (preferred — FieldPivotService populates this from the Dataverse
  // field name) or `label` (fallback). If unmatched, default to dataPoints[0]
  // (current/spent), which is the conceptual headline for budget visualizations.
  let headlineText = '';
  let subLineText = '';
  if (isHeadlineLayout) {
    const headlineField = cardConfig?.headlineFromField;
    let headlinePoint: IAggregatedDataPoint | undefined = currentPoint;
    if (headlineField) {
      const matched = dataPoints.find(dp => dp.fieldValue === headlineField || dp.label === headlineField);
      if (matched) {
        headlinePoint = matched;
      }
    }
    const headlineValue = headlinePoint?.value ?? 0;
    const isHeadlineNull = headlinePoint?.isNull === true;
    headlineText = isHeadlineNull
      ? nullDisplay
      : formatValue(headlineValue, headlinePoint?.valueFormat ?? valueFormat, nullDisplay);

    const percentText = hasTotalValue ? `${Math.round(fillPercent)}` : '0';
    const remainingText = formattedRemaining ?? nullDisplay;
    const totalText = formattedTotal ?? nullDisplay;
    const template = cardConfig?.subLineTemplate ?? '';
    subLineText = template
      ? substituteSubLineTemplate(template, {
          remaining: remainingText,
          percent: percentText,
          total: totalText,
        })
      : '';
  }

  return (
    <div className={styles.container}>
      {title && <Text className={styles.title}>{title}</Text>}

      {/* FR-VH-04: headlineAboveBar layout — headline + sub-line above the bar */}
      {isHeadlineLayout && (
        <div className={styles.headlineStack}>
          <Text block className={styles.headline}>
            {headlineText}
          </Text>
          {subLineText && (
            <Text block className={styles.subLine}>
              {subLineText}
            </Text>
          )}
        </div>
      )}

      {/* Top-right: total value + label (suppressed in headlineAboveBar layout) */}
      {!isHeadlineLayout && hasTotalValue && (
        <div className={styles.headerRow}>
          <Text>
            <span className={styles.valueText}>{formattedTotal}</span>{' '}
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
              width: hasTotalValue ? `${fillPercent}%` : '100%',
              backgroundColor: barColor,
            }}
          />
        )}
      </div>

      {/* Bottom row: spent (left) and remaining (right) — suppressed in headlineAboveBar layout */}
      {!isHeadlineLayout && (
        <div className={styles.footerRow}>
          <Text>
            <span className={styles.valueText}>{formattedCurrent}</span>{' '}
            <span className={styles.labelText}>{currentLabel}</span>
          </Text>
          {hasTotalValue && formattedRemaining && (
            <Text>
              <span className={styles.valueText}>{formattedRemaining}</span>{' '}
              <span className={styles.labelText}>{remainingLabel}</span>
            </Text>
          )}
        </div>
      )}
    </div>
  );
};
