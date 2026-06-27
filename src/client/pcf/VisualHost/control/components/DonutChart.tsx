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
import { makeStyles, shorthands, tokens, Text } from '@fluentui/react-components';
import type {
  DrillInteraction,
  IAggregatedDataPoint,
  ICardConfig,
  IChartLegendConfig,
  IColorThreshold,
} from '../types';
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
  // v1.4.4 — placement-driven container styles. One of these is selected by
  // `effectiveLegend.placement` at render time. All produce a CSS grid with
  // a donut cell + a legend cell; only the track orientation/order differs.
  layoutRight: {
    display: 'grid',
    gridTemplateColumns: 'auto 1fr',
    columnGap: tokens.spacingHorizontalL,
    alignItems: 'center',
    width: '100%',
    height: '100%',
    minHeight: '200px',
    boxSizing: 'border-box',
  },
  layoutLeft: {
    display: 'grid',
    gridTemplateColumns: '1fr auto',
    columnGap: tokens.spacingHorizontalL,
    alignItems: 'center',
    width: '100%',
    height: '100%',
    minHeight: '200px',
    boxSizing: 'border-box',
  },
  layoutTop: {
    display: 'grid',
    gridTemplateRows: 'auto 1fr',
    rowGap: tokens.spacingVerticalM,
    justifyItems: 'center',
    width: '100%',
    height: '100%',
    minHeight: '200px',
    boxSizing: 'border-box',
  },
  layoutBottom: {
    display: 'grid',
    gridTemplateRows: '1fr auto',
    rowGap: tokens.spacingVerticalM,
    justifyItems: 'center',
    width: '100%',
    height: '100%',
    minHeight: '200px',
    boxSizing: 'border-box',
  },
  layoutHidden: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
    height: '100%',
    minHeight: '200px',
    boxSizing: 'border-box',
  },
  donutCell: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: 0,
    // Position context for the absolute-positioned center letter-grade overlay.
    position: 'relative',
  },
  // v1.4.4 — Legend cell. `legendRows` is a CSS grid whose columns are
  // computed at render time per (itemFormat, valueAlignment); see
  // `computeLegendGridColumns()`. `legendInline` is a wrapping flex row.
  legendRows: {
    display: 'grid',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalS,
    alignItems: 'baseline',
    justifyContent: 'start',
    minWidth: 0,
  },
  legendInline: {
    display: 'flex',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXS,
    alignItems: 'baseline',
    minWidth: 0,
  },
  legendInlineItem: {
    display: 'inline-flex',
    alignItems: 'baseline',
    columnGap: tokens.spacingHorizontalXS,
  },
  // Small filled square in column 1, color-matched to the corresponding
  // donut segment for visual cross-reference.
  breakdownSwatch: {
    width: '10px',
    height: '10px',
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    // alignSelf:center so the swatch sits visually centered next to the
    // baseline-aligned text in columns 2 and 3.
    alignSelf: 'center',
  },
  breakdownLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  breakdownValue: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  // Absolute-positioned overlay for the center value (typically a letter
  // grade). Sized as a fraction of the donut diameter — Fluent's built-in
  // `valueInsideDonut` prop renders at a fixed-small size that doesn't read
  // well at a glance.
  centerOverlay: {
    position: 'absolute',
    top: '50%',
    left: '50%',
    transform: 'translate(-50%, -50%)',
    pointerEvents: 'none',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    textAlign: 'center',
    lineHeight: 1,
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
      // Prefer the soft Foreground2-tier `donutSegment` token (matches SSC chip
      // pattern — saturated but readable, not vibrating). Fall back to the
      // vibrant `borderAccent` only when the token set has no donutSegment set.
      const set = getTokenSetColors(t.tokenSet);
      return set.donutSegment ?? set.borderAccent;
    }
  }
  return undefined;
}

/**
 * Format a breakdown row value per the `breakdownValueFormat` key.
 *
 * Format options:
 *  - `score`         → "85"           (integer round, no suffix)
 *  - `scoreOver100`  → "85/100"
 *  - `percentage`    → "85%"          (multiplies 0-1 input by 100)
 *  - `percentScore`  → "85%"          (treats 0-100 input as already a percent;
 *                                      use when chart def supplies score values
 *                                      already on a 0-100 scale)
 *  - `ratio`         → "0.85"         (2-decimal float)
 *
 * Defaults to `"score"` when no format is supplied.
 */
/**
 * Resolve the effective legend configuration for a render.
 *
 * Priority:
 *   1. `cardConfig.legend` (explicit v1.4.4 schema) — defaults applied per-field.
 *   2. Legacy back-compat: `donutLayout === 'matrixRight'` + `showBreakdownRows === true`
 *      → derive `{ placement: 'right', orientation: 'rows', itemFormat: 'swatchLabelValue',
 *      valueAlignment: 'near', swatchSize: 10 }` so every pre-v1.4.4 chart def
 *      renders identically (NFR-05).
 *   3. Otherwise: `undefined` — no legend, donut renders alone.
 */
function resolveLegendConfig(
  legend: IChartLegendConfig | undefined,
  donutLayout: string | undefined,
  showBreakdownRows: boolean | undefined
): Required<IChartLegendConfig> | undefined {
  if (legend) {
    const placement = legend.placement ?? 'right';
    return {
      placement,
      orientation: legend.orientation ?? (placement === 'top' || placement === 'bottom' ? 'inline' : 'rows'),
      itemFormat: legend.itemFormat ?? 'swatchLabelValue',
      valueAlignment: legend.valueAlignment ?? 'near',
      swatchSize: legend.swatchSize ?? 10,
    };
  }
  if (donutLayout === 'matrixRight' && showBreakdownRows === true) {
    return {
      placement: 'right',
      orientation: 'rows',
      itemFormat: 'swatchLabelValue',
      valueAlignment: 'near',
      swatchSize: 10,
    };
  }
  return undefined;
}

/**
 * Compute the CSS `grid-template-columns` value for the rows-orientation
 * legend based on what each item shows + how the value column aligns.
 *
 *   swatch + label + value, near → 'auto auto auto'  (value sits next to label)
 *   swatch + label + value, far  → 'auto 1fr auto'   (label grows, value flush right)
 *   swatch + label, _            → 'auto auto'
 *   label + value, near          → 'auto auto'
 *   label + value, far           → '1fr auto'
 *   label only, _                → 'auto'
 */
function computeLegendGridColumns(
  itemFormat: IChartLegendConfig['itemFormat'],
  valueAlignment: IChartLegendConfig['valueAlignment']
): string {
  const showSwatch = itemFormat === 'swatchLabelValue' || itemFormat === 'swatchLabel';
  const showValue = itemFormat === 'swatchLabelValue' || itemFormat === 'labelValue';
  if (showSwatch && showValue) {
    return valueAlignment === 'far' ? 'auto 1fr auto' : 'auto auto auto';
  }
  if (showSwatch && !showValue) return 'auto auto';
  if (!showSwatch && showValue) {
    return valueAlignment === 'far' ? '1fr auto' : 'auto auto';
  }
  return 'auto';
}

function formatBreakdownValue(value: number, format: ICardConfig['breakdownValueFormat']): string {
  switch (format) {
    case 'scoreOver100':
      return `${Math.round(value)}/100`;
    case 'percentage':
      return `${Math.round(value * 100)}%`;
    case 'percentScore':
      return `${Math.round(value)}%`;
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
  // v1.4.6 — default arc width is ~38% of radius (innerRadius 0.62).
  // v1.4.2 went to 0.72 (very thin ring); UAT round 4 asked for a slightly
  // wider arc — 0.62 keeps the "ring" feel while showing more color area.
  // Chart defs that explicitly set `innerRadius` continue to win.
  innerRadius = 0.62,
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

  // Center display string. Resolution priority:
  //   1. donutCenterLabel from cardConfig (FR-VH-01 explicit override)
  //   2. centerLabel prop (legacy, kept for parity)
  //   3. computed centerValueText
  // matrixRight layout renders this via an absolute-positioned overlay so the
  // font size scales with the donut diameter (typical use is a letter grade).
  // Standard layout keeps using Fluent's `valueInsideDonut` for byte-identical
  // back-compat with every pre-FR-VH-01 chart def (NFR-05).
  const effectiveCenterLabel = cardConfig?.donutCenterLabel ?? centerLabel;
  const centerOverlayValue = showCenterValue ? effectiveCenterLabel || centerValueText : undefined;
  const valueInside = centerOverlayValue;

  const chartProps: IChartProps = {
    chartTitle: title,
  };

  // ===== v1.4.4 — placement-driven layout with legend =====
  // Resolved legend config: explicit `cardConfig.legend` wins; legacy
  // `donutLayout: "matrixRight"` + `showBreakdownRows: true` derive
  // `placement: "right"` defaults so every pre-v1.4.4 chart def renders
  // identically. When undefined, falls through to standard layout below.
  const effectiveLegend = resolveLegendConfig(cardConfig?.legend, donutLayout, cardConfig?.showBreakdownRows);

  if (effectiveLegend) {
    const placement = effectiveLegend.placement;
    const breakdownFormat = cardConfig?.breakdownValueFormat;
    // Donut size: for side-by-side placements (right/left/hidden) cap at 45%
    // container width so the legend has room. For top/bottom, take more width
    // (75%) since the legend stacks vertically. Always honor `height` cap.
    const widthFraction = placement === 'top' || placement === 'bottom' ? 0.75 : 0.45;
    const donutSize = Math.max(120, Math.min(containerWidth * widthFraction, height));
    // v1.4.6 — center font 28% of donut diameter (was 36% in v1.4.2),
    // capped at 72px. UAT feedback: smaller letter reads better at glance.
    const centerFontSize = `${Math.min(Math.round(donutSize * 0.28), 72)}px`;
    const showSwatch =
      effectiveLegend.itemFormat === 'swatchLabelValue' || effectiveLegend.itemFormat === 'swatchLabel';
    const showValue = effectiveLegend.itemFormat === 'swatchLabelValue' || effectiveLegend.itemFormat === 'labelValue';

    const layoutClass =
      placement === 'right'
        ? styles.layoutRight
        : placement === 'left'
          ? styles.layoutLeft
          : placement === 'top'
            ? styles.layoutTop
            : placement === 'bottom'
              ? styles.layoutBottom
              : styles.layoutHidden;

    const donutNode = (
      <div className={styles.donutCell}>
        <FluentDonutChart
          data={{ chartData }}
          width={donutSize}
          height={donutSize}
          hideLegend
          hideTooltip={false}
          innerRadius={innerRadius * (donutSize / 2)}
          {...chartProps}
        />
        {centerOverlayValue !== undefined && (
          <span className={styles.centerOverlay} style={{ fontSize: centerFontSize }} aria-live="polite">
            {centerOverlayValue}
          </span>
        )}
      </div>
    );

    const legendNode =
      placement === 'hidden' ? null : effectiveLegend.orientation === 'inline' ? (
        <div className={styles.legendInline} role="list" aria-label="Legend">
          {data.map((dp, idx) => {
            const swatchColor = chartData[idx]?.color;
            const rowValue =
              dp.value == null ? (cardConfig?.nullDisplay ?? '—') : formatBreakdownValue(dp.value, breakdownFormat);
            return (
              <span key={`${dp.label}-${idx}`} className={styles.legendInlineItem} role="listitem">
                {showSwatch && swatchColor && (
                  <span
                    className={styles.breakdownSwatch}
                    style={{
                      backgroundColor: swatchColor,
                      width: `${effectiveLegend.swatchSize}px`,
                      height: `${effectiveLegend.swatchSize}px`,
                    }}
                    aria-hidden={true}
                  />
                )}
                <Text className={styles.breakdownLabel}>{dp.label}</Text>
                {showValue && (
                  <Text className={styles.breakdownValue} aria-live="polite">
                    {rowValue}
                  </Text>
                )}
              </span>
            );
          })}
        </div>
      ) : (
        <div
          className={styles.legendRows}
          style={{
            gridTemplateColumns: computeLegendGridColumns(effectiveLegend.itemFormat, effectiveLegend.valueAlignment),
          }}
          role="list"
          aria-label="Legend"
        >
          {data.map((dp, idx) => {
            const swatchColor = chartData[idx]?.color;
            const rowValue =
              dp.value == null ? (cardConfig?.nullDisplay ?? '—') : formatBreakdownValue(dp.value, breakdownFormat);
            return (
              <React.Fragment key={`${dp.label}-${idx}`}>
                {showSwatch &&
                  (swatchColor ? (
                    <span
                      className={styles.breakdownSwatch}
                      style={{
                        backgroundColor: swatchColor,
                        width: `${effectiveLegend.swatchSize}px`,
                        height: `${effectiveLegend.swatchSize}px`,
                      }}
                      aria-hidden={true}
                    />
                  ) : (
                    <span aria-hidden={true} />
                  ))}
                <Text className={styles.breakdownLabel} role="listitem">
                  {dp.label}
                </Text>
                {showValue && (
                  <Text className={styles.breakdownValue} aria-live="polite">
                    {rowValue}
                  </Text>
                )}
              </React.Fragment>
            );
          })}
        </div>
      );

    return (
      <div className={styles.container} ref={containerRef}>
        {title && <Text className={styles.title}>{title}</Text>}
        <div className={layoutClass}>
          {/* DOM order: legend before donut for left/top placement so
              screen readers + tab order make sense; donut before legend
              for right/bottom. Visual placement is controlled by the
              grid track layout regardless of DOM order. */}
          {(placement === 'left' || placement === 'top') && legendNode}
          {donutNode}
          {(placement === 'right' || placement === 'bottom') && legendNode}
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
