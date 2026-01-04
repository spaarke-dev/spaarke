/**
 * MiniTable Stories
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import { MiniTable, IMiniTableColumn, IMiniTableItem } from "../control/components/MiniTable";

const meta: Meta<typeof MiniTable> = {
  title: "Charts/MiniTable",
  component: MiniTable,
  parameters: {
    layout: "padded",
    docs: {
      description: {
        component: "MiniTable displays a compact ranked table with drill-through support.",
      },
    },
  },
  tags: ["autodocs"],
};

export default meta;
type Story = StoryObj<typeof MiniTable>;

const handleDrill = action("onDrillInteraction");

const columns: IMiniTableColumn[] = [
  { key: "name", header: "Client" },
  { key: "value", header: "Revenue", isValue: true },
];

const items: IMiniTableItem[] = [
  { id: "1", values: { name: "Acme Corp", value: "$2.4M" }, fieldValue: "acme-001" },
  { id: "2", values: { name: "TechStart Inc", value: "$1.8M" }, fieldValue: "tech-002" },
  { id: "3", values: { name: "Global Partners", value: "$1.5M" }, fieldValue: "global-003" },
  { id: "4", values: { name: "Innovation Labs", value: "$1.2M" }, fieldValue: "innov-004" },
  { id: "5", values: { name: "Summit Group", value: "$980K" }, fieldValue: "summit-005" },
  { id: "6", values: { name: "BlueSky Co", value: "$850K" }, fieldValue: "blue-006" },
  { id: "7", values: { name: "Apex Solutions", value: "$720K" }, fieldValue: "apex-007" },
];

export const Default: Story = {
  args: {
    items,
    columns,
    title: "Top 5 Clients by Revenue",
    topN: 5,
    onDrillInteraction: handleDrill,
    drillField: "accountid",
  },
};

export const Top10: Story = {
  args: {
    items,
    columns,
    title: "Top 10 Clients",
    topN: 10,
    onDrillInteraction: handleDrill,
    drillField: "accountid",
  },
};

export const NoRank: Story = {
  args: {
    items,
    columns,
    title: "Recent Clients",
    showRank: false,
    onDrillInteraction: handleDrill,
    drillField: "accountid",
  },
};

export const MultipleColumns: Story = {
  args: {
    items: [
      { id: "1", values: { matter: "Smith vs Jones", hours: "156", amount: "$45,200" }, fieldValue: "m-001" },
      { id: "2", values: { matter: "Tech Corp IP Case", hours: "142", amount: "$41,800" }, fieldValue: "m-002" },
      { id: "3", values: { matter: "Estate Planning - Davis", hours: "98", amount: "$28,500" }, fieldValue: "m-003" },
      { id: "4", values: { matter: "Contract Review - ABC", hours: "87", amount: "$25,100" }, fieldValue: "m-004" },
      { id: "5", values: { matter: "M&A Advisory", hours: "76", amount: "$22,800" }, fieldValue: "m-005" },
    ],
    columns: [
      { key: "matter", header: "Matter", width: "200px" },
      { key: "hours", header: "Hours", isValue: true },
      { key: "amount", header: "Amount", isValue: true },
    ],
    title: "Top Matters by Hours",
    onDrillInteraction: handleDrill,
    drillField: "matterid",
  },
};

export const NonInteractive: Story = {
  args: {
    items,
    columns,
    title: "View Only",
    interactive: false,
  },
};

export const EmptyData: Story = {
  args: {
    items: [],
    columns,
    title: "No Data",
  },
};
