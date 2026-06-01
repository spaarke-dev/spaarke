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
import type { IAggregatedDataPoint, ICardConfig } from '../types';
import { formatValue } from '../utils/valueFormatters';
import { getTokenSetColors } from '../utils/tokenSetColors';

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

// v1.4.7 — bar height 24 → 32 to read closer to the donut arc thickness
// (donut at innerRadius 0.62 has ~38% of radius as arc width; on a typical
// 200px donut that's ~38px). The two visualizations now feel coordinated.
const DEFAULT_BAR_HEIGHT = 32;
const FILL_TRANSITION_MS = 400;

// ============= Color Token Resolution =============

/**
 * Resolve bar fill color based on fill ratio and optional color thresholds.
 *
 * v1.4.6 — uses the shared `getTokenSetColors().donutSegment` (Foreground2-tier
 * tokens) so HSBar uses the same softer palette as the Donut chart segments,
 * matching the SemanticSearchControl chip pattern. Falls back to `borderAccent`
 * for back-compat with any token set that hasn't been updated to include a
 * `donutSegment`. Falls back to brand blue when no thresholds match at all.
 */
function resolveBarColor(fillRatio: number, config?: ICardConfig): string {
  if (config?.colorThresholds) {
    for (const threshold of config.colorThresholds) {
      if (fillRatio >= threshold.range[0] && fillRatio <= threshold.range[1]) {
        const set = getTokenSetColors(threshold.tokenSet);
        return set.donutSegment ?? set.borderAccent ?? tokens.colorBrandBackground;
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
    position: 'relative', // anchor for the budget marker (v1.4.6)
  },
  // v1.4.7 — Over-budget marker: small diamond (rotated 45° square) anchored
  // at the boundary between the in-budget (green) and over-budget (red)
  // segments inside the actual-amount bar. Replaces the v1.4.6 line+label.
  budgetMarker: {
    position: 'absolute',
    top: '50%',
    width: '14px',
    height: '14px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `2px solid ${tokens.colorNeutralForeground1}`,
    transform: 'translate(-50%, -50%) rotate(45deg)',
    pointerEvents: 'none',
    boxSizing: 'border-box',
  },
  barFill: {
    height: '100%',
    borderRadius: tokens.borderRadiusMedium,
    transitionProperty: 'width',
    transitionDuration: `${FILL_TRANSITION_MS}ms`,
    transitionTimingFunction: 'ease-in-out',
    minWidth: 0,
  },
  // v1.4.7 — Two-segment fill for over-budget visualization. The bar fills
  // 100% of the container width, divided into:
  //   - in-budget segment (left): success/green, sized to (total/current)*100%
  //   - over-budget segment (right): danger/red, fills the remainder
  // Sub-elements share the bar's height and inherit the container's overflow:hidden.
  segmentRow: {
    display: 'flex',
    flexDirection: 'row',
    width: '100%',
    height: '100%',
  },
  segmentInBudget: {
    height: '100%',
  },
  segmentOver: {
    height: '100%',
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
  // v1.4.6 — track BOTH unclamped (for text display + over-budget detection)
  // and clamped (for bar fill width, since the bar can't exceed 100% of its
  // container).
  const rawRatio = hasTotalValue ? Math.max(currentValue / totalValue, 0) : 0;
  const isOverTotal = rawRatio > 1;
  const fillRatio = Math.min(rawRatio, 1);
  const fillPercent = fillRatio * 100;
  // Display percent in the sub-line text — uncamped, so $250K of $150K shows
  // as "167%", not "100%". Color thresholds still match on `fillRatio` (which
  // is clamped) so the [0.85, 1.0] danger band fires correctly when over.
  const displayPercentText = hasTotalValue ? `${Math.round(rawRatio * 100)}` : '0';
  // When over the total, render a vertical marker at the position where the
  // budget WOULD fall (100% line within the actual-amount bar). For $250K of
  // $150K: marker at (150/250)*100 = 60% from the left.
  const budgetMarkerLeftPercent =
    isOverTotal && currentValue > 0 ? (totalValue / currentValue) * 100 : null;

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
      const matched = dataPoints.find(
        dp => dp.fieldValue === headlineField || dp.label === headlineField
      );
      if (matched) {
        headlinePoint = matched;
      }
    }
    const headlineValue = headlinePoint?.value ?? 0;
    const isHeadlineNull = headlinePoint?.isNull === true;
    headlineText = isHeadlineNull
      ? nullDisplay
      : formatValue(headlineValue, headlinePoint?.valueFormat ?? valueFormat, nullDisplay);

    // v1.4.6 — use the UNCAMPED displayPercentText so "167% of $150,000" is
    // shown when the actual exceeds the total (the prior `fillPercent`-based
    // text capped at 100% which masked over-budget conditions).
    const percentText = displayPercentText;
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
        {/* v1.4.7 — Two rendering paths:
            (a) UNDER or AT budget: single fill segment using color thresholds
                (back-compat with chart def's `colorThresholds` semantics).
            (b) OVER budget: two segments — green (in-budget) on the left,
                red (over-budget) on the right — divided at the position where
                the budget falls inside the actual-amount bar. Diamond marker
                sits at the segment boundary. */}
        {isOverTotal && budgetMarkerLeftPercent !== null ? (
          <div className={styles.segmentRow}>
            <div
              className={styles.segmentInBudget}
              style={{
                width: `${budgetMarkerLeftPercent}%`,
                backgroundColor: getTokenSetColors('success').donutSegment,
              }}
            />
            <div
              className={styles.segmentOver}
              style={{
                width: `${100 - budgetMarkerLeftPercent}%`,
                backgroundColor: getTokenSetColors('danger').donutSegment,
              }}
            />
          </div>
        ) : (
          (hasTotalValue || currentValue > 0) && (
            <div
              className={styles.barFill}
              style={{
                width: hasTotalValue ? `${fillPercent}%` : '100%',
                backgroundColor: barColor,
              }}
            />
          )
        )}
        {/* Diamond marker — only rendered when over budget, positioned at
            the in-budget/over-budget segment boundary. */}
        {budgetMarkerLeftPercent !== null && (
          <div
            className={styles.budgetMarker}
            style={{ left: `${budgetMarkerLeftPercent}%` }}
            aria-hidden={true}
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
