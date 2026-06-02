/**
 * DonutChart Component
 * Renders donut/pie charts using Fluent UI Charting
 * Supports click-to-drill for viewing underlying records.
 *
 * FR-VH-01 / task 020 — Generic Custom Options additions (backward compatible):
 *   - donutLayout: "standard" (default, unchanged) | "matrixRight"
 *   - donutCenterMode: "total" (default, unchanged) | "meanOfFields"
 *   - donutCenterLabel: optional center label override
 *   - showBreakdownRows: optional breakdown rows next to the donut (matrixRight only)
 *   - breakdownValueFormat: "score" | "scoreOver100" | "percentage" | "ratio"
 *
 * Backward compat (NFR-05 binding): when `donutLayout` is undefined or "standard"
 * AND `donutCenterMode` is undefined or "total" AND `showBreakdownRows` is unset,
 * this component renders byte-identically to the pre-FR-VH-01 behavior.
 */

import * as React from 'react';
import { useRef, useState, useEffect } from 'react';
import { DonutChart as FluentDonutChart, IChartDataPoint, IChartProps } from '@fluentui/react-charting';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import type { DrillInteraction, IAggregatedDataPoint, ICardConfig, IColorThreshold } from '../types';
import { formatValue } from '../utils/valueFormatters';
import { getTokenSetColors } from '../utils/tokenSetColors';

export interface IDonutChartProps {
  /** Data points to display */
  data: IAggregatedDataPoint[];
  /** Chart title */
  title?: string;
  /** Inner radius ratio (0-1, 0 = pie chart, 0.5 = typical donut) */
  innerRadius?: number;
  /** Whether to show the legend */
  showLegend?: boolean;
  /** Callback when a slice is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Height of the chart in pixels */
  height?: number;
  /** Whether the chart should be responsive */
  responsive?: boolean;
  /** Whether to show value in center */
  showCenterValue?: boolean;
  /** Custom center label (legacy — kept for parity with existing chart defs) */
  centerLabel?: string;
  /**
   * Resolved card configuration. When provided, the new FR-VH-01 keys
   * (donutLayout, donutCenterMode, etc.) plus `colorThresholds` /
   * `valueFormat` flow through this object. Optional — when absent, the
   * component falls back to the legacy props above for byte-identical behavior.
   */
  cardConfig?: ICardConfig;
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    minHeight: '200px',
    alignItems: 'center',
  },
  title: {
    marginBottom: tokens.spacingVerticalS,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    textAlign: 'center',
  },
  chartWrapper: {
    flex: 1,
    position: 'relative',
    minHeight: '150px',
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
  },
  placeholder: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    color: tokens.colorNeutralForeground3,
  },
  // matrixRight layout — donut on the left, breakdown rows on the right
  matrixContainer: {
    display: 'grid',
    gridTemplateColumns: 'auto 1fr',
    columnGap: tokens.spacingHorizontalL,
    alignItems: 'center',
    width: '100%',
    height: '100%',
    minHeight: '200px',
    boxSizing: 'border-box',
  },
  matrixDonutCell: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: 0,
  },
  matrixRowsCell: {
    display: 'flex',
    flexDirection: 'column',
    justifyContent: 'center',
    rowGap: tokens.spacingVerticalS,
    minWidth: 0,
  },
  breakdownRow: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  breakdownLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    lineHeight: tokens.lineHeightBase200,
  },
  breakdownValue: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
  },
});

/**
 * Get Fluent-compatible color palette (used when no colorThresholds are configured).
 */
const getColorPalette = (): string[] => [
  tokens.colorBrandBackground,
  tokens.colorPaletteBlueBorderActive,
  tokens.colorPaletteTealBorderActive,
  tokens.colorPaletteGreenBorderActive,
  tokens.colorPaletteYellowBorderActive,
  tokens.colorPaletteDarkOrangeBorderActive,
  tokens.colorPaletteRedBorderActive,
  tokens.colorPalettePurpleBorderActive,
];

/**
 * Resolve a per-data-point color from `colorThresholds`. Returns undefined
 * when no threshold matches (caller falls back to the palette).
 */
function resolveSegmentColor(value: number, thresholds: IColorThreshold[] | undefined): string | undefined {
  if (!thresholds || thresholds.length === 0) return undefined;
  for (const t of thresholds) {
    const [min, max] = t.range;
    if (value >= min && value <= max) {
      return getTokenSetColors(t.tokenSet).borderAccent;
    }
  }
  return undefined;
}

/**
 * Format a breakdown row value per the `breakdownValueFormat` key.
 * Defaults to `"score"` (integer rounding) when no format is supplied.
 */
function formatBreakdownValue(value: number, format: ICardConfig['breakdownValueFormat']): string {
  switch (format) {
    case 'scoreOver100':
      return `${Math.round(value)}/100`;
    case 'percentage':
      return `${Math.round(value * 100)}%`;
    case 'ratio':
      return value.toFixed(2);
    case 'score':
    default:
      return `${Math.round(value)}`;
  }
}

/**
 * DonutChart - Renders proportional data as donut/pie chart
 */
export const DonutChart: React.FC<IDonutChartProps> = ({
  data,
  title,
  innerRadius = 0.5,
  showLegend = true,
  onDrillInteraction,
  drillField,
  height = 300,
  responsive = true,
  showCenterValue = true,
  centerLabel,
  cardConfig,
}) => {
  const styles = useStyles();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState<number>(400);

  useEffect(() => {
    if (!responsive || !containerRef.current) return;

    const resizeObserver = new ResizeObserver(entries => {
      for (const entry of entries) {
        setContainerWidth(entry.contentRect.width);
      }
    });

    resizeObserver.observe(containerRef.current);
    return () => resizeObserver.disconnect();
  }, [responsive]);

  const handleSliceClick = (dataPoint: IAggregatedDataPoint) => {
    if (onDrillInteraction && drillField) {
      onDrillInteraction({
        field: drillField,
        operator: 'eq',
        value: dataPoint.fieldValue,
        label: dataPoint.label,
      });
    }
  };

  if (!data || data.length === 0) {
    return (
      <div className={styles.container}>
        {title && <Text className={styles.title}>{title}</Text>}
        <div className={styles.placeholder}>
          <Text>No data available</Text>
        </div>
      </div>
    );
  }

  // ===== FR-VH-01: Resolve generic Custom Options keys =====
  // Backward compat (NFR-05): all five keys default to undefined → standard path,
  // which preserves byte-identical rendering for every existing Donut chart def.
  const donutLayout = cardConfig?.donutLayout ?? 'standard';
  const donutCenterMode = cardConfig?.donutCenterMode ?? 'total';
  const colorThresholds = cardConfig?.colorThresholds;

  const palette = getColorPalette();
  const total = data.reduce((sum, point) => sum + point.value, 0);

  // ===== Compute segment colors =====
  // When `colorThresholds` are configured, segment colors derive from
  // `tokenSet`. Otherwise the existing palette/per-point color is used.
  const chartData: IChartDataPoint[] = data.map((point, index) => {
    const thresholdColor = resolveSegmentColor(point.value, colorThresholds);
    return {
      legend: point.label,
      data: point.value,
      color: point.color || thresholdColor || palette[index % palette.length],
      onClick: () => handleSliceClick(point),
    };
  });

  // ===== Compute center value =====
  // donutCenterMode === "meanOfFields" → mean of values, formatted via
  // cardConfig.valueFormat (typically "letterGrade" → "B+").
  // Else total (current behavior).
  let centerValueText: string | undefined;
  if (donutCenterMode === 'meanOfFields' && data.length > 0) {
    const mean = data.reduce((sum, p) => sum + p.value, 0) / data.length;
    const fmt = cardConfig?.valueFormat;
    centerValueText = fmt ? formatValue(mean, fmt, cardConfig?.nullDisplay ?? '—') : `${Math.round(mean)}`;
  } else {
    // "total" mode (current behavior): show the sum
    centerValueText = total.toLocaleString();
  }

  // Center display string honored by the FluentDonutChart `valueInsideDonut` prop:
  //   1. donutCenterLabel from cardConfig (FR-VH-01 explicit override)
  //   2. centerLabel prop (legacy, kept for parity)
  //   3. computed centerValueText
  const effectiveCenterLabel = cardConfig?.donutCenterLabel ?? centerLabel;
  const valueInside = showCenterValue ? effectiveCenterLabel || centerValueText : undefined;

  const chartProps: IChartProps = {
    chartTitle: title,
  };

  // ===== matrixRight layout =====
  // Donut on the left + breakdown rows on the right. Used by FR-DV-01 once
  // the Matter Health chart def authors `donutLayout: "matrixRight"`.
  if (donutLayout === 'matrixRight') {
    // For the matrix layout, size the donut against the available height rather
    // than the (much larger) container width so the rows column has room.
    const donutSize = Math.max(120, Math.min(containerWidth * 0.45, height));
    const showRows = cardConfig?.showBreakdownRows ?? false;
    const breakdownFormat = cardConfig?.breakdownValueFormat;

    return (
      <div className={styles.container} ref={containerRef}>
        {title && <Text className={styles.title}>{title}</Text>}
        <div className={styles.matrixContainer}>
          <div className={styles.matrixDonutCell}>
            <FluentDonutChart
              data={{ chartData }}
              width={donutSize}
              height={donutSize}
              hideLegend
              hideTooltip={false}
              innerRadius={innerRadius * (donutSize / 2)}
              valueInsideDonut={valueInside}
              {...chartProps}
            />
          </div>
          {showRows && (
            <div className={styles.matrixRowsCell}>
              {data.map((dp, idx) => {
                const rowValue =
                  dp.value == null ? (cardConfig?.nullDisplay ?? '—') : formatBreakdownValue(dp.value, breakdownFormat);
                return (
                  <div key={`${dp.label}-${idx}`} className={styles.breakdownRow}>
                    <Text className={styles.breakdownLabel}>{dp.label}</Text>
                    <Text className={styles.breakdownValue} aria-live="polite">
                      {rowValue}
                    </Text>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    );
  }

  // ===== standard layout (DEFAULT — must be byte-identical to pre-FR-VH-01) =====
  // Note: when colorThresholds/cardConfig are unset, the only behavior change
  // in the standard path is that `centerValueText` falls through to the same
  // `total.toLocaleString()` as before. The chartData color fallback chain
  // also returns the same palette color when `thresholdColor === undefined`.
  const chartSize = Math.min(containerWidth, height);

  return (
    <div className={styles.container} ref={containerRef}>
      {title && <Text className={styles.title}>{title}</Text>}
      <div className={styles.chartWrapper}>
        <FluentDonutChart
          data={{ chartData }}
          width={chartSize}
          height={chartSize}
          hideLegend={!showLegend}
          hideTooltip={false}
          innerRadius={innerRadius * (chartSize / 2)}
          valueInsideDonut={valueInside}
          {...chartProps}
        />
      </div>
    </div>
  );
};
