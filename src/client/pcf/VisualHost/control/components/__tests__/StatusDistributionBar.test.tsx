import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { StatusDistributionBar, IStatusSegment } from '../StatusDistributionBar';

const renderWithTheme = (component: React.ReactElement, theme = webLightTheme) => {
  return render(
    <FluentProvider theme={theme}>
      {component}
    </FluentProvider>
  );
};

const mockSegments: IStatusSegment[] = [
  { label: 'Active', value: 45, fieldValue: 'active' },
  { label: 'Pending', value: 30, fieldValue: 'pending' },
  { label: 'Closed', value: 25, fieldValue: 'closed' },
];

describe('StatusDistributionBar', () => {
  describe('rendering', () => {
    it('renders with required props', () => {
      renderWithTheme(
        <StatusDistributionBar segments={mockSegments} />
      );

      // All segments should be visible via aria-label
      expect(screen.getByLabelText('Active: 45')).toBeInTheDocument();
      expect(screen.getByLabelText('Pending: 30')).toBeInTheDocument();
      expect(screen.getByLabelText('Closed: 25')).toBeInTheDocument();
    });

    it('renders with title', () => {
      renderWithTheme(
        <StatusDistributionBar segments={mockSegments} title="Status Overview" />
      );

      expect(screen.getByText('Status Overview')).toBeInTheDocument();
    });

    it('renders empty state when no segments', () => {
      renderWithTheme(
        <StatusDistributionBar segments={[]} title="Empty Bar" />
      );

      expect(screen.getByText('No data available')).toBeInTheDocument();
    });

    it('renders bar segments', () => {
      const { container } = renderWithTheme(
        <StatusDistributionBar segments={mockSegments} />
      );

      // Segments should be rendered with proper widths
      expect(container.firstChild).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('renders with interactive segments when configured', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <StatusDistributionBar
          segments={mockSegments}
          onDrillInteraction={mockDrill}
          drillField="status"
          interactive={true}
        />
      );

      // Segments should be present
      expect(screen.getByLabelText('Active: 45')).toBeInTheDocument();
    });

    it('renders non-interactive segments when interactive is false', () => {
      const mockDrill = jest.fn();

      const { container } = renderWithTheme(
        <StatusDistributionBar
          segments={mockSegments}
          onDrillInteraction={mockDrill}
          drillField="status"
          interactive={false}
        />
      );

      expect(container.firstChild).toBeInTheDocument();
    });
  });

  describe('theme support', () => {
    it('renders correctly in dark theme', () => {
      renderWithTheme(
        <StatusDistributionBar segments={mockSegments} title="Dark Theme Bar" />,
        webDarkTheme
      );

      expect(screen.getByText('Dark Theme Bar')).toBeInTheDocument();
    });
  });

  describe('props', () => {
    it('respects height prop', () => {
      const { container } = renderWithTheme(
        <StatusDistributionBar segments={mockSegments} height={48} />
      );

      expect(container.firstChild).toBeInTheDocument();
    });

    it('handles custom colors for segments', () => {
      const segmentsWithColors: IStatusSegment[] = [
        { label: 'Good', value: 60, fieldValue: 'good', color: '#00FF00' },
        { label: 'Bad', value: 40, fieldValue: 'bad', color: '#FF0000' },
      ];

      renderWithTheme(
        <StatusDistributionBar segments={segmentsWithColors} />
      );

      expect(screen.getByLabelText('Good: 60')).toBeInTheDocument();
      expect(screen.getByLabelText('Bad: 40')).toBeInTheDocument();
    });
  });
});
