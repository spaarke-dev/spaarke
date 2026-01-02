/**
 * LineChart Stories
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import { LineChart } from "../control/components/LineChart";
import type { IAggregatedDataPoint } from "../control/types";

const meta: Meta<typeof LineChart> = {
  title: "Charts/LineChart",
  component: LineChart,
  parameters: {
    layout: "padded",
    docs: {
      description: {
        component: "LineChart displays trend data as line or area chart with drill-through support.",
      },
    },
  },
  tags: ["autodocs"],
};

export default meta;
type Story = StoryObj<typeof LineChart>;

const handleDrill = action("onDrillInteraction");

const monthlyData: IAggregatedDataPoint[] = [
  { label: "Jan", value: 120, fieldValue: "2024-01" },
  { label: "Feb", value: 150, fieldValue: "2024-02" },
  { label: "Mar", value: 180, fieldValue: "2024-03" },
  { label: "Apr", value: 140, fieldValue: "2024-04" },
  { label: "May", value: 210, fieldValue: "2024-05" },
  { label: "Jun", value: 190, fieldValue: "2024-06" },
];

export const Default: Story = {
  args: {
    data: monthlyData,
    title: "Cases per Month",
    variant: "line",
    onDrillInteraction: handleDrill,
    drillField: "month",
  },
};

export const AreaChart: Story = {
  args: {
    data: monthlyData,
    title: "Revenue Trend",
    variant: "area",
    onDrillInteraction: handleDrill,
    drillField: "month",
  },
};

export const WithLegend: Story = {
  args: {
    data: monthlyData,
    title: "Monthly Trend",
    showLegend: true,
    onDrillInteraction: handleDrill,
    drillField: "month",
  },
};

export const EmptyData: Story = {
  args: {
    data: [],
    title: "No Data",
  },
};

export const Comparison: Story = {
  render: () => (
    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "24px" }}>
      <LineChart
        data={monthlyData}
        title="Line Variant"
        variant="line"
        onDrillInteraction={handleDrill}
        drillField="month"
      />
      <LineChart
        data={monthlyData}
        title="Area Variant"
        variant="area"
        onDrillInteraction={handleDrill}
        drillField="month"
      />
    </div>
  ),
};
