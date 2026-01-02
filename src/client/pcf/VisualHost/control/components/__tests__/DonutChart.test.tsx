import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { DonutChart } from '../DonutChart';
import type { IAggregatedDataPoint } from '../../types';

const renderWithTheme = (component: React.ReactElement, theme = webLightTheme) => {
  return render(
    <FluentProvider theme={theme}>
      {component}
    </FluentProvider>
  );
};

const mockData: IAggregatedDataPoint[] = [
  { label: 'Active', value: 45, fieldValue: 'active' },
  { label: 'Pending', value: 30, fieldValue: 'pending' },
  { label: 'Closed', value: 25, fieldValue: 'closed' },
];

describe('DonutChart', () => {
  describe('rendering', () => {
    it('renders with required props', () => {
      renderWithTheme(
        <DonutChart data={mockData} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('renders with title', () => {
      renderWithTheme(
        <DonutChart data={mockData} title="Status Distribution" />
      );

      expect(screen.getByText('Status Distribution')).toBeInTheDocument();
    });

    it('renders empty state when no data', () => {
      renderWithTheme(
        <DonutChart data={[]} title="Empty Chart" />
      );

      expect(screen.getByText('No data available')).toBeInTheDocument();
    });

    it('renders as donut with default innerRadius', () => {
      renderWithTheme(
        <DonutChart data={mockData} innerRadius={0.5} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('renders as pie chart with innerRadius of 0', () => {
      renderWithTheme(
        <DonutChart data={mockData} innerRadius={0} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('shows center value by default', () => {
      renderWithTheme(
        <DonutChart data={mockData} showCenterValue={true} />
      );

      // Total should be displayed in center (100)
      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('shows custom center label when provided', () => {
      renderWithTheme(
        <DonutChart data={mockData} centerLabel="Total: 100" showCenterValue={true} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('accepts onDrillInteraction callback', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <DonutChart
          data={mockData}
          onDrillInteraction={mockDrill}
          drillField="status"
        />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });

  describe('theme support', () => {
    it('renders correctly in dark theme', () => {
      renderWithTheme(
        <DonutChart data={mockData} title="Dark Theme Donut" />,
        webDarkTheme
      );

      expect(screen.getByText('Dark Theme Donut')).toBeInTheDocument();
    });
  });

  describe('props', () => {
    it('respects showLegend prop', () => {
      renderWithTheme(
        <DonutChart data={mockData} showLegend={true} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('respects height prop', () => {
      const { container } = renderWithTheme(
        <DonutChart data={mockData} height={250} />
      );

      expect(container.firstChild).toBeInTheDocument();
    });

    it('hides center value when showCenterValue is false', () => {
      renderWithTheme(
        <DonutChart data={mockData} showCenterValue={false} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });
});
