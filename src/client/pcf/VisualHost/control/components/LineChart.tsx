/**
 * LineChart Component
 * Renders line or area charts using Fluent UI Charting
 * Supports click-to-drill for viewing underlying records
 */

import * as React from "react";
import { useRef, useState, useEffect } from "react";
import {
  LineChart as FluentLineChart,
  AreaChart,
  ILineChartPoints,
  IChartProps,
} from "@fluentui/react-charting";
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import type { DrillInteraction, IAggregatedDataPoint } from "../types";

export type ChartVariant = "line" | "area";

export interface ILineChartProps {
  /** Data points to display */
  data: IAggregatedDataPoint[];
  /** Chart title */
  title?: string;
  /** Chart variant (line or area) */
  variant?: ChartVariant;
  /** Whether to show data labels */
  showLabels?: boolean;
  /** Whether to show the legend */
  showLegend?: boolean;
  /** Callback when a point is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Height of the chart in pixels */
  height?: number;
  /** Whether the chart should be responsive */
  responsive?: boolean;
  /** Line color override */
  lineColor?: string;
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
 * LineChart - Renders time-series or trend data as line or area chart
 */
export const LineChart: React.FC<ILineChartProps> = ({
  data,
  title,
  variant = "line",
  showLabels = false,
  showLegend = false,
  onDrillInteraction,
  drillField,
  height = 300,
  responsive = true,
  lineColor,
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

  const handlePointClick = (dataPoint: IAggregatedDataPoint) => {
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

  const color = lineColor || tokens.colorBrandBackground;

  const chartData: ILineChartPoints[] = [
    {
      legend: title || "Data",
      data: data.map((point, index) => ({
        x: index,
        y: point.value,
        xAxisCalloutData: point.label,
        onDataPointClick: () => handlePointClick(point),
      })),
      color: color,
    },
  ];

  const chartProps: IChartProps = {
    chartTitle: title,
  };

  return (
    <div className={styles.container} ref={containerRef}>
      {title && <Text className={styles.title}>{title}</Text>}
      <div className={styles.chartWrapper}>
        {variant === "line" ? (
          <FluentLineChart
            data={{ lineChartData: chartData }}
            width={responsive ? containerWidth : undefined}
            height={height}
            hideLegend={!showLegend}
            hideTooltip={false}
            {...chartProps}
          />
        ) : (
          <AreaChart
            data={{ lineChartData: chartData }}
            width={responsive ? containerWidth : undefined}
            height={height}
            hideLegend={!showLegend}
            hideTooltip={false}
            {...chartProps}
          />
        )}
      </div>
    </div>
  );
};
