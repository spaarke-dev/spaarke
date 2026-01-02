/**
 * ChartRenderer Component Stories
 * Tests the visual switching logic for all chart types
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import { ChartRenderer } from "../control/components/ChartRenderer";
import { VisualType, AggregationType } from "../control/types";
import type { IChartDefinition, IChartData, IAggregatedDataPoint } from "../control/types";

// Sample data for stories
const sampleDataPoints: IAggregatedDataPoint[] = [
  { label: "Active", value: 45, fieldValue: "active", color: "#0078D4" },
  { label: "Pending", value: 30, fieldValue: "pending", color: "#FFB900" },
  { label: "Closed", value: 25, fieldValue: "closed", color: "#107C10" },
  { label: "Cancelled", value: 10, fieldValue: "cancelled", color: "#D13438" },
];

const monthlyDataPoints: IAggregatedDataPoint[] = [
  { label: "Jan", value: 100, fieldValue: "jan" },
  { label: "Feb", value: 120, fieldValue: "feb" },
  { label: "Mar", value: 90, fieldValue: "mar" },
  { label: "Apr", value: 150, fieldValue: "apr" },
  { label: "May", value: 130, fieldValue: "may" },
  { label: "Jun", value: 180, fieldValue: "jun" },
];

const baseChartData: IChartData = {
  dataPoints: sampleDataPoints,
  totalRecords: 110,
  aggregationType: AggregationType.Count,
  aggregationField: "statuscode",
  groupByField: "statuscode",
};

const baseChartDefinition: IChartDefinition = {
  sprk_chartdefinitionid: "story-001",
  sprk_name: "Sample Chart",
  sprk_description: "A sample chart for testing",
  sprk_visualtype: VisualType.BarChart,
  sprk_aggregationtype: AggregationType.Count,
  sprk_sourceentity: "account",
  sprk_groupbyfield: "statuscode",
  sprk_configurationjson: JSON.stringify({
    showTitle: true,
    showLegend: true,
  }),
};

// Story metadata
const meta: Meta<typeof ChartRenderer> = {
  title: "Core/ChartRenderer",
  component: ChartRenderer,
  parameters: {
    layout: "centered",
    docs: {
      description: {
        component:
          "ChartRenderer dynamically renders the appropriate chart component based on the visual type in the chart definition. This is the central switching logic for Visual Host.",
      },
    },
  },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div style={{ width: "600px", height: "400px", padding: "1rem" }}>
        <Story />
      </div>
    ),
  ],
  argTypes: {
    chartDefinition: {
      description: "Chart definition from sprk_chartdefinition entity",
      control: "object",
    },
    chartData: {
      description: "Aggregated data for chart rendering",
      control: "object",
    },
    onDrillInteraction: {
      description: "Callback when user interacts with chart for drill-through",
      action: "drillInteraction",
    },
    height: {
      description: "Height override for the chart",
      control: { type: "number", min: 100, max: 600 },
    },
  },
};

export default meta;
type Story = StoryObj<typeof ChartRenderer>;

// MetricCard
export const MetricCard: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Total Records",
      sprk_description: "Count of all records",
      sprk_visualtype: VisualType.MetricCard,
      sprk_configurationjson: JSON.stringify({
        trend: "up",
        trendValue: 12.5,
      }),
    },
    chartData: {
      ...baseChartData,
      dataPoints: [{ label: "Total", value: 110, fieldValue: null }],
    },
    onDrillInteraction: action("drillInteraction"),
    height: 300,
  },
};

// BarChart - Vertical
export const BarChartVertical: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Status Distribution",
      sprk_description: "Records by status",
      sprk_visualtype: VisualType.BarChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        orientation: "vertical",
        showLegend: false,
      }),
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300,
  },
};

// BarChart - Horizontal
export const BarChartHorizontal: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Status Distribution",
      sprk_visualtype: VisualType.BarChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        orientation: "horizontal",
      }),
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300,
  },
};

// LineChart
export const LineChart: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Monthly Trend",
      sprk_description: "Records over time",
      sprk_visualtype: VisualType.LineChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showLegend: false,
      }),
    },
    chartData: {
      ...baseChartData,
      dataPoints: monthlyDataPoints,
      groupByField: "month",
    },
    onDrillInteraction: action("drillInteraction"),
    height: 300,
  },
};

// AreaChart
export const AreaChart: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Cumulative Growth",
      sprk_visualtype: VisualType.AreaChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        lineColor: "#0078D4",
      }),
    },
    chartData: {
      ...baseChartData,
      dataPoints: monthlyDataPoints,
      groupByField: "month",
    },
    onDrillInteraction: action("drillInteraction"),
    height: 300,
  },
};

// DonutChart
export const DonutChart: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Status Breakdown",
      sprk_visualtype: VisualType.DonutChart,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showCenterValue: true,
        centerLabel: "Total",
        showLegend: true,
      }),
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300,
  },
};

// StatusBar
export const StatusDistributionBar: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Pipeline Status",
      sprk_visualtype: VisualType.StatusBar,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showLabels: true,
        showCounts: true,
      }),
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300,
  },
};

// Calendar
export const Calendar: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Activity Calendar",
      sprk_visualtype: VisualType.Calendar,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showNavigation: true,
      }),
    },
    chartData: {
      ...baseChartData,
      dataPoints: [
        { label: "Meeting", value: 3, fieldValue: new Date().toISOString() },
        { label: "Call", value: 2, fieldValue: new Date().toISOString() },
      ],
    },
    onDrillInteraction: action("drillInteraction"),
    height: 400,
  },
};

// MiniTable
export const MiniTable: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Top Items",
      sprk_visualtype: VisualType.MiniTable,
      sprk_configurationjson: JSON.stringify({
        showTitle: true,
        showRank: true,
        topN: 5,
      }),
    },
    chartData: baseChartData,
    onDrillInteraction: action("drillInteraction"),
    height: 300,
  },
};

// No Data State
export const NoData: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Empty Chart",
      sprk_visualtype: VisualType.BarChart,
    },
    chartData: {
      dataPoints: [],
      totalRecords: 0,
      aggregationType: AggregationType.Count,
    },
    height: 300,
  },
};

// Without Drill Interaction
export const NoDrillThrough: Story = {
  args: {
    chartDefinition: {
      ...baseChartDefinition,
      sprk_name: "Display Only",
      sprk_visualtype: VisualType.BarChart,
    },
    chartData: baseChartData,
    onDrillInteraction: undefined,
    height: 300,
  },
};

// All Visual Types Gallery
export const AllVisualTypes: Story = {
  render: () => {
    const visualTypes = [
      { type: VisualType.MetricCard, name: "MetricCard" },
      { type: VisualType.BarChart, name: "BarChart" },
      { type: VisualType.LineChart, name: "LineChart" },
      { type: VisualType.DonutChart, name: "DonutChart" },
      { type: VisualType.StatusBar, name: "StatusBar" },
      { type: VisualType.MiniTable, name: "MiniTable" },
    ];

    return (
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(2, 1fr)",
          gap: "1rem",
          padding: "1rem",
          width: "1000px",
        }}
      >
        {visualTypes.map(({ type, name }) => (
          <div
            key={type}
            style={{
              border: "1px solid #e0e0e0",
              borderRadius: "8px",
              padding: "1rem",
              height: "300px",
            }}
          >
            <ChartRenderer
              chartDefinition={{
                ...baseChartDefinition,
                sprk_chartdefinitionid: `gallery-${type}`,
                sprk_name: name,
                sprk_visualtype: type,
                sprk_configurationjson: JSON.stringify({
                  showTitle: true,
                  showLegend: true,
                }),
              }}
              chartData={
                type === VisualType.MetricCard
                  ? {
                      ...baseChartData,
                      dataPoints: [{ label: "Total", value: 110, fieldValue: null }],
                    }
                  : type === VisualType.LineChart
                  ? { ...baseChartData, dataPoints: monthlyDataPoints }
                  : baseChartData
              }
              onDrillInteraction={action(`drill-${name}`)}
              height={250}
            />
          </div>
        ))}
      </div>
    );
  },
  decorators: [
    (Story) => (
      <div style={{ width: "1100px" }}>
        <Story />
      </div>
    ),
  ],
};
