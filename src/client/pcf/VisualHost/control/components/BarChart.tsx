/**
 * BarChart Component
 * Renders vertical or horizontal bar charts using Fluent UI Charting
 * Supports click-to-drill for viewing underlying records
 */

import * as React from "react";
import { useRef, useState, useEffect } from "react";
import {
  VerticalBarChart,
  HorizontalBarChart,
  IVerticalBarChartDataPoint,
  IHorizontalBarChartWithAxisDataPoint,
  IChartProps,
} from "@fluentui/react-charting";
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import type { DrillInteraction, IAggregatedDataPoint } from "../types";

export type BarOrientation = "vertical" | "horizontal";

export interface IBarChartProps {
  /** Data points to display */
  data: IAggregatedDataPoint[];
  /** Chart title */
  title?: string;
  /** Chart orientation */
  orientation?: BarOrientation;
  /** Whether to show data labels on bars */
  showLabels?: boolean;
  /** Whether to show the legend */
  showLegend?: boolean;
  /** Callback when a bar is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Height of the chart in pixels */
  height?: number;
  /** Whether the chart should be responsive */
  responsive?: boolean;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    minHeight: "200px",
  },
  title: {
    marginBottom: tokens.spacingVerticalS,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  chartWrapper: {
    flex: 1,
    position: "relative",
    minHeight: "150px",
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
 * Get Fluent-compatible color palette using design tokens
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
 * BarChart - Renders categorical data as vertical or horizontal bars
 */
export const BarChart: React.FC<IBarChartProps> = ({
  data,
  title,
  orientation = "vertical",
  showLabels = true,
  showLegend = false,
  onDrillInteraction,
  drillField,
  height = 300,
  responsive = true,
}) => {
  const styles = useStyles();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState<number>(400);

  // Handle responsive sizing
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

  // Handle bar click for drill-through
  const handleBarClick = (dataPoint: IAggregatedDataPoint) => {
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

  // Convert data to Fluent charting format
  const chartData: IVerticalBarChartDataPoint[] = data.map((point, index) => ({
    x: point.label,
    y: point.value,
    color: point.color || colors[index % colors.length],
    legend: point.label,
    xAxisCalloutData: point.label,
    yAxisCalloutData: point.value.toString(),
    onClick: () => handleBarClick(point),
  }));

  // For horizontal chart, we need different data format
  const horizontalData: IHorizontalBarChartWithAxisDataPoint[] = data.map(
    (point, index) => ({
      x: point.value,
      y: point.label,
      color: point.color || colors[index % colors.length],
      legend: point.label,
      xAxisCalloutData: point.value.toString(),
      yAxisCalloutData: point.label,
      onClick: () => handleBarClick(point),
    })
  );

  const chartProps: IChartProps = {
    chartTitle: title,
  };

  return (
    <div className={styles.container} ref={containerRef}>
      {title && <Text className={styles.title}>{title}</Text>}
      <div className={styles.chartWrapper}>
        {orientation === "vertical" ? (
          <VerticalBarChart
            data={chartData}
            width={responsive ? containerWidth : undefined}
            height={height}
            hideLegend={!showLegend}
            hideTooltip={false}
            barWidth={32}
            yAxisTickCount={5}
            {...chartProps}
          />
        ) : (
          <HorizontalBarChart
            data={horizontalData.map((point) => ({
              chartTitle: String(point.y),
              chartData: [
                {
                  legend: point.legend || "",
                  horizontalBarChartdata: { x: point.x, y: 0 },
                  color: point.color,
                  onClick: point.onClick,
                },
              ],
            }))}
          />
        )}
      </div>
    </div>
  );
};
