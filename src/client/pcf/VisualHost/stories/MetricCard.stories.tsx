/**
 * MetricCard Stories
 * Storybook stories for the MetricCard component
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import { MetricCard, IMetricCardProps } from "../control/components/MetricCard";
import type { DrillInteraction } from "../control/types";

const meta: Meta<typeof MetricCard> = {
  title: "Charts/MetricCard",
  component: MetricCard,
  parameters: {
    layout: "centered",
    docs: {
      description: {
        component:
          "MetricCard displays a single aggregate value with optional trend indicator. " +
          "Supports click-to-drill for viewing underlying records.",
      },
    },
  },
  tags: ["autodocs"],
  argTypes: {
    value: {
      description: "The main metric value to display",
      control: { type: "text" },
    },
    label: {
      description: "Label describing what the metric represents",
      control: { type: "text" },
    },
    description: {
      description: "Optional description or subtitle",
      control: { type: "text" },
    },
    trend: {
      description: "Trend direction (up = positive, down = negative)",
      control: { type: "select" },
      options: ["up", "down", "neutral", undefined],
    },
    trendValue: {
      description: "Percentage change for trend display",
      control: { type: "number" },
    },
    interactive: {
      description: "Whether the card should be interactive",
      control: { type: "boolean" },
    },
    compact: {
      description: "Compact mode for smaller displays",
      control: { type: "boolean" },
    },
    drillField: {
      description: "Field name for drill interaction",
      control: { type: "text" },
    },
    drillValue: {
      description: "Value to filter by when drilling",
      control: { type: "text" },
    },
  },
};

export default meta;
type Story = StoryObj<typeof MetricCard>;

// Handler for drill interactions
const handleDrill = action("onDrillInteraction");

// Default story
export const Default: Story = {
  args: {
    value: 1234,
    label: "Total Records",
    onDrillInteraction: handleDrill,
    drillField: "total",
    drillValue: "all",
  },
};

// With trend up
export const TrendUp: Story = {
  args: {
    value: 45,
    label: "Open Matters",
    trend: "up",
    trendValue: 12.5,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
    drillValue: 1,
  },
};

// With trend down
export const TrendDown: Story = {
  args: {
    value: 128,
    label: "Pending Tasks",
    trend: "down",
    trendValue: -8.3,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
    drillValue: 2,
  },
};

// Large number formatting
export const LargeNumber: Story = {
  args: {
    value: 1250000,
    label: "Total Revenue",
    description: "Year to date",
    trend: "up",
    trendValue: 15.2,
    onDrillInteraction: handleDrill,
    drillField: "revenue",
    drillValue: "ytd",
  },
};

// Currency value
export const CurrencyValue: Story = {
  args: {
    value: "$1.2M",
    label: "Revenue",
    description: "This quarter",
    trend: "up",
    trendValue: 8.7,
    onDrillInteraction: handleDrill,
    drillField: "quarter",
    drillValue: "Q4",
  },
};

// Compact mode
export const Compact: Story = {
  args: {
    value: 42,
    label: "Active Users",
    compact: true,
    onDrillInteraction: handleDrill,
    drillField: "active",
    drillValue: true,
  },
};

// Non-interactive
export const NonInteractive: Story = {
  args: {
    value: 99.5,
    label: "Uptime %",
    description: "Last 30 days",
    interactive: false,
  },
};

// With description
export const WithDescription: Story = {
  args: {
    value: 87,
    label: "Cases Resolved",
    description: "This week (15% above target)",
    trend: "up",
    trendValue: 15,
    onDrillInteraction: handleDrill,
    drillField: "resolved",
    drillValue: true,
  },
};

// Grid of metrics
export const MetricGrid: Story = {
  render: () => {
    const metrics: IMetricCardProps[] = [
      {
        value: 1250,
        label: "Total Accounts",
        trend: "up",
        trendValue: 5.2,
        drillField: "account",
        drillValue: "all",
      },
      {
        value: 45,
        label: "Open Opportunities",
        trend: "down",
        trendValue: -3.1,
        drillField: "opportunity",
        drillValue: "open",
      },
      {
        value: "$2.4M",
        label: "Pipeline Value",
        trend: "up",
        trendValue: 12.8,
        drillField: "pipeline",
        drillValue: "active",
      },
      {
        value: 89,
        label: "Win Rate %",
        description: "Last quarter",
        drillField: "winrate",
        drillValue: "q4",
      },
    ];

    return (
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(2, 1fr)",
          gap: "16px",
          padding: "16px",
        }}
      >
        {metrics.map((metric, index) => (
          <MetricCard key={index} {...metric} onDrillInteraction={handleDrill} />
        ))}
      </div>
    );
  },
};

// Dashboard row of compact metrics
export const DashboardRow: Story = {
  render: () => {
    const metrics: IMetricCardProps[] = [
      { value: 142, label: "New", compact: true, drillField: "status", drillValue: "new" },
      { value: 87, label: "In Progress", compact: true, drillField: "status", drillValue: "inprogress" },
      { value: 234, label: "Resolved", compact: true, drillField: "status", drillValue: "resolved" },
      { value: 12, label: "Escalated", compact: true, drillField: "status", drillValue: "escalated" },
    ];

    return (
      <div
        style={{
          display: "flex",
          gap: "12px",
          padding: "16px",
          flexWrap: "wrap",
        }}
      >
        {metrics.map((metric, index) => (
          <MetricCard key={index} {...metric} onDrillInteraction={handleDrill} />
        ))}
      </div>
    );
  },
};
