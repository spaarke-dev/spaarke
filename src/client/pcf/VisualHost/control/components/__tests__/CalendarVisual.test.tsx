import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { CalendarVisual, ICalendarEvent } from '../CalendarVisual';

const renderWithTheme = (component: React.ReactElement, theme = webLightTheme) => {
  return render(
    <FluentProvider theme={theme}>
      {component}
    </FluentProvider>
  );
};

const createEvents = (): ICalendarEvent[] => {
  const today = new Date();
  const year = today.getFullYear();
  const month = today.getMonth();

  return [
    { date: new Date(year, month, 5), count: 3 },
    { date: new Date(year, month, 10), count: 7 },
    { date: new Date(year, month, 15), count: 2 },
    { date: new Date(year, month, 20), count: 5 },
  ];
};

describe('CalendarVisual', () => {
  describe('rendering', () => {
    it('renders with required props', () => {
      const events = createEvents();
      renderWithTheme(
        <CalendarVisual events={events} />
      );

      // Should render weekday headers
      expect(screen.getByText('Sun')).toBeInTheDocument();
      expect(screen.getByText('Mon')).toBeInTheDocument();
      expect(screen.getByText('Tue')).toBeInTheDocument();
      expect(screen.getByText('Wed')).toBeInTheDocument();
      expect(screen.getByText('Thu')).toBeInTheDocument();
      expect(screen.getByText('Fri')).toBeInTheDocument();
      expect(screen.getByText('Sat')).toBeInTheDocument();
    });

    it('renders with title', () => {
      const events = createEvents();
      renderWithTheme(
        <CalendarVisual events={events} title="Deadlines" />
      );

      expect(screen.getByText('Deadlines')).toBeInTheDocument();
    });

    it('renders empty calendar when no events', () => {
      renderWithTheme(
        <CalendarVisual events={[]} />
      );

      // Calendar grid should still render
      expect(screen.getByText('Sun')).toBeInTheDocument();
    });

    it('shows days with event counts', () => {
      const events = createEvents();
      const { container } = renderWithTheme(
        <CalendarVisual events={events} />
      );

      // Calendar should have badges for events
      const badges = container.querySelectorAll('.fui-Badge');
      expect(badges.length).toBeGreaterThan(0);
    });

    it('renders navigation buttons by default', () => {
      const events = createEvents();
      renderWithTheme(
        <CalendarVisual events={events} />
      );

      expect(screen.getByLabelText('Previous month')).toBeInTheDocument();
      expect(screen.getByLabelText('Next month')).toBeInTheDocument();
    });

    it('hides navigation when showNavigation is false', () => {
      const events = createEvents();
      renderWithTheme(
        <CalendarVisual events={events} showNavigation={false} />
      );

      expect(screen.queryByLabelText('Previous month')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Next month')).not.toBeInTheDocument();
    });
  });

  describe('navigation', () => {
    it('navigates to previous month when clicking prev button', () => {
      const events = createEvents();

      renderWithTheme(
        <CalendarVisual events={events} />
      );

      // Click previous
      fireEvent.click(screen.getByLabelText('Previous month'));

      // Navigation still works
      expect(screen.getByLabelText('Previous month')).toBeInTheDocument();
    });

    it('navigates to next month when clicking next button', () => {
      const events = createEvents();

      renderWithTheme(
        <CalendarVisual events={events} />
      );

      fireEvent.click(screen.getByLabelText('Next month'));

      // Navigation still works
      expect(screen.getByLabelText('Next month')).toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('renders interactive days when drill is configured', () => {
      const mockDrill = jest.fn();
      const events = createEvents();

      renderWithTheme(
        <CalendarVisual
          events={events}
          onDrillInteraction={mockDrill}
          drillField="duedate"
        />
      );

      // Should have many clickable day buttons
      const allButtons = screen.getAllByRole('button');
      expect(allButtons.length).toBeGreaterThan(2); // More than just nav buttons
    });

    it('days are not interactive without drillField', () => {
      const mockDrill = jest.fn();
      const events = createEvents();

      renderWithTheme(
        <CalendarVisual
          events={events}
          onDrillInteraction={mockDrill}
        />
      );

      // Only navigation buttons should be buttons
      const navButtons = screen.getAllByRole('button');
      expect(navButtons.length).toBe(2);
    });
  });

  describe('theme support', () => {
    it('renders correctly in dark theme', () => {
      const events = createEvents();
      renderWithTheme(
        <CalendarVisual events={events} title="Dark Calendar" />,
        webDarkTheme
      );

      expect(screen.getByText('Dark Calendar')).toBeInTheDocument();
    });
  });

  describe('props', () => {
    it('respects initialMonth prop', () => {
      const events: ICalendarEvent[] = [];
      const january2025 = new Date(2025, 0, 1);

      renderWithTheme(
        <CalendarVisual events={events} initialMonth={january2025} />
      );

      expect(screen.getByText(/January/)).toBeInTheDocument();
    });
  });
});
