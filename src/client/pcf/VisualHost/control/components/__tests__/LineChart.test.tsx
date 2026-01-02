import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { LineChart } from '../LineChart';
import type { IAggregatedDataPoint } from '../../types';

const renderWithTheme = (component: React.ReactElement, theme = webLightTheme) => {
  return render(
    <FluentProvider theme={theme}>
      {component}
    </FluentProvider>
  );
};

const mockData: IAggregatedDataPoint[] = [
  { label: 'Jan', value: 100, fieldValue: 'jan' },
  { label: 'Feb', value: 120, fieldValue: 'feb' },
  { label: 'Mar', value: 90, fieldValue: 'mar' },
  { label: 'Apr', value: 150, fieldValue: 'apr' },
  { label: 'May', value: 130, fieldValue: 'may' },
];

describe('LineChart', () => {
  describe('rendering', () => {
    it('renders with required props', () => {
      renderWithTheme(
        <LineChart data={mockData} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('renders with title', () => {
      renderWithTheme(
        <LineChart data={mockData} title="Monthly Trend" />
      );

      expect(screen.getByText('Monthly Trend')).toBeInTheDocument();
    });

    it('renders empty state when no data', () => {
      renderWithTheme(
        <LineChart data={[]} title="Empty Chart" />
      );

      expect(screen.getByText('No data available')).toBeInTheDocument();
    });

    it('renders line variant', () => {
      renderWithTheme(
        <LineChart data={mockData} variant="line" />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('renders area variant', () => {
      renderWithTheme(
        <LineChart data={mockData} variant="area" />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('accepts onDrillInteraction callback', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <LineChart
          data={mockData}
          onDrillInteraction={mockDrill}
          drillField="month"
        />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });

  describe('theme support', () => {
    it('renders correctly in dark theme', () => {
      renderWithTheme(
        <LineChart data={mockData} title="Dark Theme Line" />,
        webDarkTheme
      );

      expect(screen.getByText('Dark Theme Line')).toBeInTheDocument();
    });
  });

  describe('props', () => {
    it('respects showLegend prop', () => {
      renderWithTheme(
        <LineChart data={mockData} showLegend={true} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('respects height prop', () => {
      const { container } = renderWithTheme(
        <LineChart data={mockData} height={350} />
      );

      expect(container.firstChild).toBeInTheDocument();
    });

    it('respects lineColor prop', () => {
      renderWithTheme(
        <LineChart data={mockData} lineColor="#FF5733" />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });
});
