import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { MetricCard } from '../MetricCard';

const renderWithTheme = (component: React.ReactElement, theme = webLightTheme) => {
  return render(
    <FluentProvider theme={theme}>
      {component}
    </FluentProvider>
  );
};

describe('MetricCard', () => {
  describe('rendering', () => {
    it('renders with required props', () => {
      renderWithTheme(
        <MetricCard label="Total Sales" value={1500} />
      );

      expect(screen.getByText('Total Sales')).toBeInTheDocument();
      expect(screen.getByText('1.5K')).toBeInTheDocument();
    });

    it('renders string value directly', () => {
      renderWithTheme(
        <MetricCard label="Status" value="Active" />
      );

      expect(screen.getByText('Status')).toBeInTheDocument();
      expect(screen.getByText('Active')).toBeInTheDocument();
    });

    it('formats large numbers with K suffix', () => {
      renderWithTheme(
        <MetricCard label="Count" value={5000} />
      );

      expect(screen.getByText('5.0K')).toBeInTheDocument();
    });

    it('formats millions with M suffix', () => {
      renderWithTheme(
        <MetricCard label="Revenue" value={2500000} />
      );

      expect(screen.getByText('2.5M')).toBeInTheDocument();
    });

    it('renders trend indicator when provided', () => {
      renderWithTheme(
        <MetricCard label="Sales" value={100} trend="up" trendValue={15} />
      );

      expect(screen.getByText('+15.0%')).toBeInTheDocument();
    });

    it('renders down trend indicator', () => {
      renderWithTheme(
        <MetricCard label="Sales" value={100} trend="down" trendValue={-10} />
      );

      expect(screen.getByText('-10.0%')).toBeInTheDocument();
    });

    it('renders description when provided', () => {
      renderWithTheme(
        <MetricCard label="Sales" value={100} description="From last month" />
      );

      expect(screen.getByText('From last month')).toBeInTheDocument();
    });

    it('renders in compact mode', () => {
      renderWithTheme(
        <MetricCard label="Compact" value={42} compact={true} />
      );

      expect(screen.getByText('Compact')).toBeInTheDocument();
      expect(screen.getByText('42')).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('calls onDrillInteraction when clicked with drillField', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MetricCard
          label="Sales"
          value={100}
          onDrillInteraction={mockDrill}
          drillField="salesfield"
          drillValue="all"
        />
      );

      const card = screen.getByRole('button');
      fireEvent.click(card);

      expect(mockDrill).toHaveBeenCalledWith({
        field: 'salesfield',
        operator: 'eq',
        value: 'all',
        label: 'Sales',
      });
    });

    it('uses value as drillValue when drillValue not specified', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MetricCard
          label="Count"
          value={42}
          onDrillInteraction={mockDrill}
          drillField="count"
        />
      );

      const card = screen.getByRole('button');
      fireEvent.click(card);

      expect(mockDrill).toHaveBeenCalledWith({
        field: 'count',
        operator: 'eq',
        value: 42,
        label: 'Count',
      });
    });

    it('handles keyboard interaction', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MetricCard
          label="Sales"
          value={100}
          onDrillInteraction={mockDrill}
          drillField="salesfield"
        />
      );

      const card = screen.getByRole('button');
      fireEvent.keyDown(card, { key: 'Enter' });

      expect(mockDrill).toHaveBeenCalled();
    });

    it('does not call onDrillInteraction without drillField', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MetricCard
          label="Sales"
          value={100}
          onDrillInteraction={mockDrill}
        />
      );

      // Card should not be a button without drill capability
      expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });

    it('does not call onDrillInteraction when interactive is false', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MetricCard
          label="Sales"
          value={100}
          onDrillInteraction={mockDrill}
          drillField="salesfield"
          interactive={false}
        />
      );

      // Card should not be a button when not interactive
      expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });
  });

  describe('theme support', () => {
    it('renders correctly in dark theme', () => {
      renderWithTheme(
        <MetricCard label="Dark Mode" value={42} />,
        webDarkTheme
      );

      expect(screen.getByText('Dark Mode')).toBeInTheDocument();
      expect(screen.getByText('42')).toBeInTheDocument();
    });
  });
});
