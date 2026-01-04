/**
 * StatusDistributionBar Stories
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import { StatusDistributionBar, IStatusSegment } from "../control/components/StatusDistributionBar";

const meta: Meta<typeof StatusDistributionBar> = {
  title: "Charts/StatusDistributionBar",
  component: StatusDistributionBar,
  parameters: {
    layout: "padded",
    docs: {
      description: {
        component: "StatusDistributionBar shows status distribution as a horizontal stacked bar.",
      },
    },
  },
  tags: ["autodocs"],
};

export default meta;
type Story = StoryObj<typeof StatusDistributionBar>;

const handleDrill = action("onDrillInteraction");

const statusSegments: IStatusSegment[] = [
  { label: "Active", value: 45, fieldValue: "active" },
  { label: "Pending", value: 23, fieldValue: "pending" },
  { label: "On Hold", value: 12, fieldValue: "onhold" },
  { label: "Closed", value: 67, fieldValue: "closed" },
];

export const Default: Story = {
  args: {
    segments: statusSegments,
    title: "Case Status Distribution",
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

export const ShowPercentages: Story = {
  args: {
    segments: statusSegments,
    title: "Status by Percentage",
    showCounts: false,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

export const TallBar: Story = {
  args: {
    segments: statusSegments,
    title: "Larger Status Bar",
    height: 48,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

export const NoLabels: Story = {
  args: {
    segments: statusSegments,
    title: "Compact (No Labels)",
    showLabels: false,
    height: 24,
    onDrillInteraction: handleDrill,
    drillField: "statuscode",
  },
};

export const NonInteractive: Story = {
  args: {
    segments: statusSegments,
    title: "View Only",
    interactive: false,
  },
};

export const EmptyData: Story = {
  args: {
    segments: [],
    title: "No Data",
  },
};
