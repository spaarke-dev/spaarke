/**
 * DonutChart Component
 * Renders donut/pie charts using Fluent UI Charting
 * Supports click-to-drill for viewing underlying records
 */

import * as React from "react";
import { useRef, useState, useEffect } from "react";
import {
  DonutChart as FluentDonutChart,
  IChartDataPoint,
  IChartProps,
} from "@fluentui/react-charting";
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import type { DrillInteraction, IAggregatedDataPoint } from "../../types";

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
  /** Custom center label */
  centerLabel?: string;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    minHeight: "200px",
    alignItems: "center",
  },
  title: {
    marginBottom: tokens.spacingVerticalS,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    textAlign: "center",
  },
  chartWrapper: {
    flex: 1,
    position: "relative",
    minHeight: "150px",
    display: "flex",
    justifyContent: "center",
    alignItems: "center",
  },
  placeholder: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    color: tokens.colorNeutralForeground3,
  },
});

/**
 * Get Fluent-compatible color palette
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
}) => {
  const styles = useStyles();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState<number>(400);

  useEffect(() => {
    if (!responsive || !containerRef.current) return;

    const resizeObserver = new ResizeObserver((entries) => {
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
        operator: "eq",
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

  const colors = getColorPalette();
  const total = data.reduce((sum, point) => sum + point.value, 0);

  const chartData: IChartDataPoint[] = data.map((point, index) => ({
    legend: point.label,
    data: point.value,
    color: point.color || colors[index % colors.length],
    onClick: () => handleSliceClick(point),
  }));

  const chartProps: IChartProps = {
    chartTitle: title,
  };

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
          valueInsideDonut={showCenterValue ? (centerLabel || total.toLocaleString()) : undefined}
          {...chartProps}
        />
      </div>
    </div>
  );
};
