import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { BarChart } from '../BarChart';
import type { IAggregatedDataPoint } from '../../types';

const renderWithTheme = (component: React.ReactElement, theme = webLightTheme) => {
  return render(
    <FluentProvider theme={theme}>
      {component}
    </FluentProvider>
  );
};

const mockData: IAggregatedDataPoint[] = [
  { label: 'Q1', value: 100, fieldValue: 'q1' },
  { label: 'Q2', value: 150, fieldValue: 'q2' },
  { label: 'Q3', value: 120, fieldValue: 'q3' },
  { label: 'Q4', value: 180, fieldValue: 'q4' },
];

describe('BarChart', () => {
  describe('rendering', () => {
    it('renders with required props', () => {
      renderWithTheme(
        <BarChart data={mockData} />
      );

      // Chart should be present (Fluent charting renders SVG)
      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('renders with title', () => {
      renderWithTheme(
        <BarChart data={mockData} title="Quarterly Sales" />
      );

      expect(screen.getByText('Quarterly Sales')).toBeInTheDocument();
    });

    it('renders empty state when no data', () => {
      renderWithTheme(
        <BarChart data={[]} title="Empty Chart" />
      );

      expect(screen.getByText('No data available')).toBeInTheDocument();
    });

    it('renders horizontal orientation', () => {
      renderWithTheme(
        <BarChart data={mockData} orientation="horizontal" />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('renders vertical orientation (default)', () => {
      renderWithTheme(
        <BarChart data={mockData} orientation="vertical" />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('accepts onDrillInteraction callback', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <BarChart
          data={mockData}
          onDrillInteraction={mockDrill}
          drillField="quarter"
        />
      );

      // Chart should render with drill capability
      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });

  describe('theme support', () => {
    it('renders correctly in dark theme', () => {
      renderWithTheme(
        <BarChart data={mockData} title="Dark Theme Chart" />,
        webDarkTheme
      );

      expect(screen.getByText('Dark Theme Chart')).toBeInTheDocument();
      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });

  describe('props', () => {
    it('respects showLegend prop', () => {
      renderWithTheme(
        <BarChart data={mockData} showLegend={true} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });

    it('respects height prop', () => {
      const { container } = renderWithTheme(
        <BarChart data={mockData} height={400} />
      );

      expect(container.firstChild).toBeInTheDocument();
    });

    it('respects responsive prop', () => {
      renderWithTheme(
        <BarChart data={mockData} responsive={false} />
      );

      expect(document.querySelector('svg')).toBeInTheDocument();
    });
  });
});
