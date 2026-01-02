/**
 * DonutChart Stories
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import { DonutChart } from "../control/components/DonutChart";
import type { IAggregatedDataPoint } from "../control/types";

const meta: Meta<typeof DonutChart> = {
  title: "Charts/DonutChart",
  component: DonutChart,
  parameters: {
    layout: "centered",
    docs: {
      description: {
        component: "DonutChart displays proportional data with drill-through support.",
      },
    },
  },
  tags: ["autodocs"],
};

export default meta;
type Story = StoryObj<typeof DonutChart>;

const handleDrill = action("onDrillInteraction");

const statusData: IAggregatedDataPoint[] = [
  { label: "Open", value: 45, fieldValue: "open" },
  { label: "In Progress", value: 32, fieldValue: "inprogress" },
  { label: "Pending", value: 18, fieldValue: "pending" },
  { label: "Resolved", value: 89, fieldValue: "resolved" },
];

export const Default: Story = {
  args: {
    data: statusData,
    title: "Matters by Status",
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

export const PieChart: Story = {
  args: {
    data: statusData,
    title: "Distribution (Pie)",
    innerRadius: 0,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

export const WithCustomCenter: Story = {
  args: {
    data: statusData,
    title: "Total Cases",
    centerLabel: "184 Total",
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

export const NoLegend: Story = {
  args: {
    data: statusData,
    title: "Compact View",
    showLegend: false,
    height: 200,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

export const EmptyData: Story = {
  args: {
    data: [],
    title: "No Data",
  },
};
