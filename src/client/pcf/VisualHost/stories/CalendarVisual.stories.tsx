/**
 * CalendarVisual Stories
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import { CalendarVisual, ICalendarEvent } from "../control/components/CalendarVisual";

const meta: Meta<typeof CalendarVisual> = {
  title: "Charts/CalendarVisual",
  component: CalendarVisual,
  parameters: {
    layout: "padded",
    docs: {
      description: {
        component: "CalendarVisual displays a monthly calendar grid with event indicators.",
      },
    },
  },
  tags: ["autodocs"],
};

export default meta;
type Story = StoryObj<typeof CalendarVisual>;

const handleDrill = action("onDrillInteraction");

// Generate sample events for the current month
const generateEvents = (): ICalendarEvent[] => {
  const today = new Date();
  const year = today.getFullYear();
  const month = today.getMonth();

  return [
    { date: new Date(year, month, 3), count: 2 },
    { date: new Date(year, month, 7), count: 5 },
    { date: new Date(year, month, 12), count: 1 },
    { date: new Date(year, month, 15), count: 8 },
    { date: new Date(year, month, 18), count: 3 },
    { date: new Date(year, month, 22), count: 4 },
    { date: new Date(year, month, 25), count: 2 },
    { date: new Date(year, month, 28), count: 6 },
  ];
};

export const Default: Story = {
  args: {
    events: generateEvents(),
    title: "Deadlines This Month",
    onDrillInteraction: handleDrill,
    drillField: "duedate",
  },
};

export const NoNavigation: Story = {
  args: {
    events: generateEvents(),
    title: "Current Month Only",
    showNavigation: false,
    onDrillInteraction: handleDrill,
    drillField: "duedate",
  },
};

export const EmptyCalendar: Story = {
  args: {
    events: [],
    title: "No Events",
    onDrillInteraction: handleDrill,
    drillField: "duedate",
  },
};

export const HighDensity: Story = {
  args: {
    events: (() => {
      const today = new Date();
      const events: ICalendarEvent[] = [];
      for (let i = 1; i <= 28; i++) {
        if (i % 2 === 0) {
          events.push({
            date: new Date(today.getFullYear(), today.getMonth(), i),
            count: Math.floor(Math.random() * 10) + 1,
          });
        }
      }
      return events;
    })(),
    title: "Busy Month",
    onDrillInteraction: handleDrill,
    drillField: "duedate",
  },
};

export const NonInteractive: Story = {
  args: {
    events: generateEvents(),
    title: "View Only",
    showNavigation: true,
  },
};
