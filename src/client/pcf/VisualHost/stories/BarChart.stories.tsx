/**
 * BarChart Stories
 * Storybook stories for the BarChart component
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import { BarChart } from "../control/components/BarChart";
import type { IAggregatedDataPoint } from "../control/types";

const meta: Meta<typeof BarChart> = {
  title: "Charts/BarChart",
  component: BarChart,
  parameters: {
    layout: "padded",
    docs: {
      description: {
        component:
          "BarChart displays categorical data as vertical or horizontal bars. " +
          "Supports click-to-drill for viewing underlying records.",
      },
    },
  },
  tags: ["autodocs"],
  argTypes: {
    orientation: {
      description: "Chart orientation",
      control: { type: "select" },
      options: ["vertical", "horizontal"],
    },
    showLabels: {
      description: "Whether to show data labels on bars",
      control: { type: "boolean" },
    },
    showLegend: {
      description: "Whether to show the legend",
      control: { type: "boolean" },
    },
    height: {
      description: "Height of the chart in pixels",
      control: { type: "number" },
    },
    responsive: {
      description: "Whether the chart should be responsive",
      control: { type: "boolean" },
    },
    title: {
      description: "Chart title",
      control: { type: "text" },
    },
    drillField: {
      description: "Field name for drill interaction",
      control: { type: "text" },
    },
  },
};

export default meta;
type Story = StoryObj<typeof BarChart>;

// Handler for drill interactions
const handleDrill = action("onDrillInteraction");

// Sample data for stories
const statusData: IAggregatedDataPoint[] = [
  { label: "Open", value: 45, fieldValue: "open" },
  { label: "In Progress", value: 32, fieldValue: "inprogress" },
  { label: "Pending", value: 18, fieldValue: "pending" },
  { label: "Resolved", value: 89, fieldValue: "resolved" },
  { label: "Closed", value: 156, fieldValue: "closed" },
];

const monthlyData: IAggregatedDataPoint[] = [
  { label: "Jan", value: 12500, fieldValue: "2024-01" },
  { label: "Feb", value: 15800, fieldValue: "2024-02" },
  { label: "Mar", value: 18200, fieldValue: "2024-03" },
  { label: "Apr", value: 14300, fieldValue: "2024-04" },
  { label: "May", value: 21500, fieldValue: "2024-05" },
  { label: "Jun", value: 19800, fieldValue: "2024-06" },
];

const regionData: IAggregatedDataPoint[] = [
  { label: "North America", value: 2450000, fieldValue: "na" },
  { label: "Europe", value: 1890000, fieldValue: "eu" },
  { label: "Asia Pacific", value: 1620000, fieldValue: "apac" },
  { label: "Latin America", value: 780000, fieldValue: "latam" },
  { label: "Middle East", value: 450000, fieldValue: "mea" },
];

// Default vertical bar chart
export const Default: Story = {
  args: {
    data: statusData,
    title: "Matters by Status",
    orientation: "vertical",
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

// Horizontal bar chart
export const Horizontal: Story = {
  args: {
    data: regionData,
    title: "Revenue by Region",
    orientation: "horizontal",
    height: 350,
    onDrillInteraction: handleDrill,
    drillField: "region",
  },
};

// Monthly trend
export const MonthlyTrend: Story = {
  args: {
    data: monthlyData,
    title: "Cases per Month",
    orientation: "vertical",
    showLegend: true,
    onDrillInteraction: handleDrill,
    drillField: "month",
  },
};

// With legend
export const WithLegend: Story = {
  args: {
    data: statusData,
    title: "Status Distribution",
    showLegend: true,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

// Custom colors
export const CustomColors: Story = {
  args: {
    data: [
      { label: "Critical", value: 12, fieldValue: "critical", color: "#D13438" },
      { label: "High", value: 28, fieldValue: "high", color: "#FF8C00" },
      { label: "Medium", value: 45, fieldValue: "medium", color: "#FFB900" },
      { label: "Low", value: 67, fieldValue: "low", color: "#107C10" },
    ],
    title: "Issues by Priority",
    onDrillInteraction: handleDrill,
    drillField: "priority",
  },
};

// Non-interactive (no drill)
export const NonInteractive: Story = {
  args: {
    data: monthlyData,
    title: "Monthly Overview (View Only)",
    orientation: "vertical",
  },
};

// Compact height
export const Compact: Story = {
  args: {
    data: statusData.slice(0, 3),
    title: "Top 3 Status",
    height: 200,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

// Empty data
export const EmptyData: Story = {
  args: {
    data: [],
    title: "No Data Available",
  },
};

// Large dataset
export const LargeDataset: Story = {
  args: {
    data: [
      { label: "Category A", value: 150, fieldValue: "a" },
      { label: "Category B", value: 230, fieldValue: "b" },
      { label: "Category C", value: 180, fieldValue: "c" },
      { label: "Category D", value: 290, fieldValue: "d" },
      { label: "Category E", value: 120, fieldValue: "e" },
      { label: "Category F", value: 350, fieldValue: "f" },
      { label: "Category G", value: 200, fieldValue: "g" },
      { label: "Category H", value: 270, fieldValue: "h" },
      { label: "Category I", value: 160, fieldValue: "i" },
      { label: "Category J", value: 310, fieldValue: "j" },
    ],
    title: "10 Categories",
    height: 400,
    onDrillInteraction: handleDrill,
    drillField: "category",
  },
};

// Side by side comparison
export const Comparison: Story = {
  render: () => (
    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "24px" }}>
      <BarChart
        data={statusData}
        title="Vertical"
        orientation="vertical"
        onDrillInteraction={handleDrill}
        drillField="status"
      />
      <BarChart
        data={statusData}
        title="Horizontal"
        orientation="horizontal"
        onDrillInteraction={handleDrill}
        drillField="status"
      />
    </div>
  ),
};
