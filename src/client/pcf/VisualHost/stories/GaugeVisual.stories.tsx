/**
 * GaugeVisual Stories
 * Storybook stories for the GaugeVisual component
 */

import * as React from 'react';
import type { Meta, StoryObj } from '@storybook/react';
import { GaugeVisual, IGaugeVisualProps } from '../control/components/GaugeVisual';
import type { IAggregatedDataPoint, ICardConfig, IColorThreshold } from '../control/types';

const meta: Meta<typeof GaugeVisual> = {
  title: 'Charts/GaugeVisual',
  component: GaugeVisual,
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component:
          'GaugeVisual renders semicircular arc gauges in a responsive CSS Grid layout. ' +
          'Each gauge displays a proportional arc fill, a center-formatted value ' +
          '(letter grade, percentage, currency, etc.), and a label below the arc. ' +
          'Color logic mirrors MetricCardMatrix using value threshold / token set resolution.',
      },
    },
  },
  tags: ['autodocs'],
  argTypes: {
    title: {
      description: 'Title displayed above the gauge grid',
      control: { type: 'text' },
    },
    columns: {
      description: 'Fixed columns override (overrides cardConfig.columns)',
      control: { type: 'number' },
    },
    width: {
      description: 'Available width in pixels (from PCF property)',
      control: { type: 'number' },
    },
    height: {
      description: 'Minimum height in pixels (from PCF property)',
      control: { type: 'number' },
    },
  },
};

export default meta;
type Story = StoryObj<typeof GaugeVisual>;

// Grade color thresholds: A (0.9-1.0) = success, B (0.8-0.89) = brand,
// C (0.7-0.79) = warning, D (0.6-0.69) = warning, F (0.0-0.59) = danger
const gradeColorThresholds: IColorThreshold[] = [
  { range: [0.9, 1.0], tokenSet: 'success' },
  { range: [0.8, 0.899], tokenSet: 'brand' },
  { range: [0.7, 0.799], tokenSet: 'warning' },
  { range: [0.6, 0.699], tokenSet: 'warning' },
  { range: [0.0, 0.599], tokenSet: 'danger' },
];

// Card config for letter grade display with value thresholds
const gradeCardConfig: ICardConfig = {
  valueFormat: 'letterGrade',
  colorSource: 'valueThreshold',
  colorThresholds: gradeColorThresholds,
  cardSize: 'medium',
  sortBy: 'label',
  compact: false,
  showTitle: false,
  accentFromOptionSet: false,
  showAccentBar: false,
  nullDisplay: '\u2014',
};

// Default story: 3 grade gauges (A, C, F) with threshold-based colors
export const Default: Story = {
  args: {
    dataPoints: [
      { label: 'Budget Controls', value: 0.92, fieldValue: 0.92 },
      { label: 'Guidelines Compliance', value: 0.78, fieldValue: 0.78 },
      { label: 'Outcomes Success', value: 0.45, fieldValue: 0.45 },
    ] as IAggregatedDataPoint[],
    cardConfig: gradeCardConfig,
  },
};

// Single gauge story
export const SingleGauge: Story = {
  args: {
    dataPoints: [{ label: 'Overall Score', value: 0.87, fieldValue: 0.87 }] as IAggregatedDataPoint[],
    cardConfig: {
      ...gradeCardConfig,
      showTitle: true,
    },
    title: 'Performance',
  },
};

// Ratio mode: gauge showing spent/budget as a ratio (0.0 to 1.0)
export const RatioMode: Story = {
  args: {
    dataPoints: [{ label: 'Budget Used', value: 0.64, fieldValue: 0.64 }] as IAggregatedDataPoint[],
    cardConfig: {
      valueFormat: 'percentage',
      colorSource: 'valueThreshold',
      colorThresholds: [
        { range: [0.0, 0.749], tokenSet: 'success' },
        { range: [0.75, 0.899], tokenSet: 'warning' },
        { range: [0.9, 1.0], tokenSet: 'danger' },
      ],
      cardSize: 'medium',
      sortBy: 'label',
      compact: false,
      showTitle: true,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: '\u2014',
    } as ICardConfig,
    title: 'Budget Utilization',
  },
};

// No-data state: empty data points showing "Not yet assessed"
export const NoData: Story = {
  args: {
    dataPoints: [] as IAggregatedDataPoint[],
    cardConfig: gradeCardConfig,
    title: 'Performance Grades',
  },
};

// Custom columns: 2 fixed columns
export const CustomColumns: Story = {
  args: {
    dataPoints: [
      { label: 'Milestone Adherence', value: 0.91, fieldValue: 0.91 },
      { label: 'Risk Management', value: 0.73, fieldValue: 0.73 },
      { label: 'Stakeholder Satisfaction', value: 0.55, fieldValue: 0.55 },
      { label: 'Resource Allocation', value: 0.84, fieldValue: 0.84 },
    ] as IAggregatedDataPoint[],
    cardConfig: gradeCardConfig,
    columns: 2,
  },
};

// Percentage format gauges
export const PercentageFormat: Story = {
  args: {
    dataPoints: [
      { label: 'Completion Rate', value: 0.82, fieldValue: 0.82 },
      { label: 'On-Time Delivery', value: 0.67, fieldValue: 0.67 },
      { label: 'Defect Rate', value: 0.12, fieldValue: 0.12 },
    ] as IAggregatedDataPoint[],
    cardConfig: {
      valueFormat: 'percentage',
      colorSource: 'valueThreshold',
      colorThresholds: [
        { range: [0.75, 1.0], tokenSet: 'success' },
        { range: [0.5, 0.749], tokenSet: 'warning' },
        { range: [0.0, 0.499], tokenSet: 'danger' },
      ],
      cardSize: 'medium',
      sortBy: 'label',
      compact: false,
      showTitle: false,
      accentFromOptionSet: false,
      showAccentBar: false,
      nullDisplay: '\u2014',
    } as ICardConfig,
  },
};

// Grid of 6 gauges — full dashboard row
export const GaugeDashboard: Story = {
  render: () => {
    const dataPoints: IAggregatedDataPoint[] = [
      { label: 'Budget Controls', value: 0.92, fieldValue: 0.92 },
      { label: 'Guidelines Compliance', value: 0.78, fieldValue: 0.78 },
      { label: 'Outcomes Success', value: 0.45, fieldValue: 0.45 },
      { label: 'Risk Management', value: 0.88, fieldValue: 0.88 },
      { label: 'Timeline Adherence', value: 0.61, fieldValue: 0.61 },
      { label: 'Stakeholder Engagement', value: 0.95, fieldValue: 0.95 },
    ];

    return (
      <div style={{ width: '640px', padding: '16px' }}>
        <GaugeVisual
          dataPoints={dataPoints}
          cardConfig={{
            ...gradeCardConfig,
            showTitle: true,
            columns: 3,
          }}
          title="Project Health Dashboard"
        />
      </div>
    );
  },
};
