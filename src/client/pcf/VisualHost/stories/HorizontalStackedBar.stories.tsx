/**
 * HorizontalStackedBar Stories
 * Storybook stories for the HorizontalStackedBar component
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import {
  HorizontalStackedBar,
  IHorizontalStackedBarProps,
} from "../control/components/HorizontalStackedBar";
import type {
  IAggregatedDataPoint,
  ICardConfig,
  IColorThreshold,
} from "../control/types";

const meta: Meta<typeof HorizontalStackedBar> = {
  title: "Charts/HorizontalStackedBar",
  component: HorizontalStackedBar,
  parameters: {
    layout: "centered",
    docs: {
      description: {
        component:
          "HorizontalStackedBar renders a single horizontal progress bar for financial/budget visualization. " +
          "dataPoints[0] = current/spent value, dataPoints[1] = total/budget value. " +
          "Displays total (top-right), spent (bottom-left), and remaining (bottom-right) labels. " +
          "Supports color thresholds to change fill color based on the fill ratio.",
      },
    },
  },
  tags: ["autodocs"],
  argTypes: {
    title: {
      description: "Optional title displayed above the bar",
      control: { type: "text" },
    },
    height: {
      description: "Bar height in pixels (default: 14)",
      control: { type: "number" },
    },
  },
};

export default meta;
type Story = StoryObj<typeof HorizontalStackedBar>;

// Default story: financial bar with $18,200 spent of $50,000 budget
export const Default: Story = {
  args: {
    dataPoints: [
      { label: "spent", value: 18200, fieldValue: 18200 },
      { label: "budget", value: 50000, fieldValue: 50000 },
    ] as IAggregatedDataPoint[],
    cardConfig: {
      valueFormat: "currency",
      colorSource: "none",
      cardSize: "medium",
      sortBy: "label",
      compact: false,
      showTitle: false,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: "\u2014",
    } as ICardConfig,
  },
};

// Over budget: $55,000 spent of $50,000 budget (over 100%)
export const OverBudget: Story = {
  args: {
    dataPoints: [
      { label: "spent", value: 55000, fieldValue: 55000 },
      { label: "budget", value: 50000, fieldValue: 50000 },
    ] as IAggregatedDataPoint[],
    cardConfig: {
      valueFormat: "currency",
      colorSource: "valueThreshold",
      colorThresholds: [
        { range: [0.0, 0.749], tokenSet: "success" },
        { range: [0.75, 0.899], tokenSet: "warning" },
        { range: [0.9, 1.0], tokenSet: "danger" },
      ] as IColorThreshold[],
      cardSize: "medium",
      sortBy: "label",
      compact: false,
      showTitle: false,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: "\u2014",
    } as ICardConfig,
    title: "Q4 Campaign Spend",
  },
};

// Small values with shortNumber formatting
export const SmallValues: Story = {
  args: {
    dataPoints: [
      { label: "used", value: 312, fieldValue: 312 },
      { label: "allocated", value: 500, fieldValue: 500 },
    ] as IAggregatedDataPoint[],
    cardConfig: {
      valueFormat: "shortNumber",
      colorSource: "valueThreshold",
      colorThresholds: [
        { range: [0.0, 0.749], tokenSet: "success" },
        { range: [0.75, 0.899], tokenSet: "warning" },
        { range: [0.9, 1.0], tokenSet: "danger" },
      ] as IColorThreshold[],
      cardSize: "medium",
      sortBy: "label",
      compact: false,
      showTitle: false,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: "\u2014",
    } as ICardConfig,
    title: "Hours Used",
  },
};

// No data: empty data points
export const NoData: Story = {
  args: {
    dataPoints: [] as IAggregatedDataPoint[],
    cardConfig: {
      valueFormat: "currency",
      colorSource: "none",
      cardSize: "medium",
      sortBy: "label",
      compact: false,
      showTitle: false,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: "\u2014",
    } as ICardConfig,
    title: "Project Budget",
  },
};

// Threshold-based color: near budget limit (warning zone)
export const NearBudgetLimit: Story = {
  args: {
    dataPoints: [
      { label: "spent", value: 43500, fieldValue: 43500 },
      { label: "budget", value: 50000, fieldValue: 50000 },
    ] as IAggregatedDataPoint[],
    cardConfig: {
      valueFormat: "currency",
      colorSource: "valueThreshold",
      colorThresholds: [
        { range: [0.0, 0.749], tokenSet: "success" },
        { range: [0.75, 0.899], tokenSet: "warning" },
        { range: [0.9, 1.0], tokenSet: "danger" },
      ] as IColorThreshold[],
      cardSize: "medium",
      sortBy: "label",
      compact: false,
      showTitle: false,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: "\u2014",
    } as ICardConfig,
    title: "Matter Budget",
  },
};

// Custom bar height
export const TallBar: Story = {
  args: {
    dataPoints: [
      { label: "spent", value: 28750, fieldValue: 28750 },
      { label: "budget", value: 75000, fieldValue: 75000 },
    ] as IAggregatedDataPoint[],
    cardConfig: {
      valueFormat: "currency",
      colorSource: "none",
      cardSize: "medium",
      sortBy: "label",
      compact: false,
      showTitle: false,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: "\u2014",
    } as ICardConfig,
    title: "Annual Budget",
    height: 24,
  },
};

// Multiple bars stacked in a container
export const MultipleBars: Story = {
  render: () => {
    const sharedConfig: ICardConfig = {
      valueFormat: "currency",
      colorSource: "valueThreshold",
      colorThresholds: [
        { range: [0.0, 0.749], tokenSet: "success" },
        { range: [0.75, 0.899], tokenSet: "warning" },
        { range: [0.9, 1.0], tokenSet: "danger" },
      ],
      cardSize: "medium",
      sortBy: "label",
      compact: false,
      showTitle: false,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: "\u2014",
    };

    const bars: { title: string; spent: number; budget: number }[] = [
      { title: "Legal Fees", spent: 18200, budget: 50000 },
      { title: "Expert Witnesses", spent: 43500, budget: 50000 },
      { title: "Court Costs", spent: 52000, budget: 50000 },
      { title: "Administrative", spent: 8750, budget: 25000 },
    ];

    return (
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          gap: "16px",
          width: "480px",
          padding: "16px",
        }}
      >
        {bars.map((bar) => (
          <HorizontalStackedBar
            key={bar.title}
            title={bar.title}
            dataPoints={[
              { label: "spent", value: bar.spent, fieldValue: bar.spent },
              { label: "budget", value: bar.budget, fieldValue: bar.budget },
            ]}
            cardConfig={sharedConfig}
          />
        ))}
      </div>
    );
  },
};
