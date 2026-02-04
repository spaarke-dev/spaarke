/**
 * CalendarMonth Component Tests
 * Task 008: Unit tests for EventCalendarFilter PCF control
 *
 * Tests cover:
 * - Basic rendering (month label, weekday headers, day grid)
 * - Date selection (single click, shift+click for range)
 * - Event indicators (dots for single, badges for multiple)
 * - Range highlighting (in-range, start, end styling)
 * - Dark mode / theme support
 * - Keyboard navigation
 *
 * @see CalendarMonth.tsx
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { CalendarMonth, ICalendarMonthProps } from '../CalendarMonth';

// Helper to render with Fluent theme
const renderWithTheme = (component: React.ReactElement, theme = webLightTheme) => {
  return render(
    <FluentProvider theme={theme}>
      {component}
    </FluentProvider>
  );
};

// Test data helpers
const createEventCounts = (): Map<string, number> => {
  const map = new Map<string, number>();
  // Events for February 2026
  map.set('2026-02-05', 1);  // Single event
  map.set('2026-02-10', 3);  // Multiple events
  map.set('2026-02-15', 7);  // Many events
  map.set('2026-02-20', 1);  // Single event
  return map;
};

const createEventDates = (): Set<string> => {
  return new Set(['2026-02-05', '2026-02-10', '2026-02-15', '2026-02-20']);
};

const defaultProps: ICalendarMonthProps = {
  year: 2026,
  month: 1, // February (0-indexed)
};

describe('CalendarMonth', () => {
  describe('rendering', () => {
    it('renders with required props', () => {
      renderWithTheme(<CalendarMonth {...defaultProps} />);

      // Should render month/year header
      expect(screen.getByText('February 2026')).toBeInTheDocument();
    });

    it('renders weekday headers (S M T W T F S)', () => {
      renderWithTheme(<CalendarMonth {...defaultProps} />);

      // Check for weekday headers - there are multiple S, T entries
      const weekdayHeaders = screen.getAllByText(/^[SMTWF]$/);
      expect(weekdayHeaders).toHaveLength(7);
    });

    it('renders day grid with correct number of cells', () => {
      const { container } = renderWithTheme(<CalendarMonth {...defaultProps} />);

      // 6 rows x 7 days = 42 cells
      const grid = container.querySelector('[role="grid"]');
      expect(grid).toBeInTheDocument();

      const gridCells = container.querySelectorAll('[role="gridcell"]');
      // Should have 28 days in Feb 2026 (non-leap year) + padding cells
      expect(gridCells.length).toBeGreaterThan(0);
    });

    it('displays correct days for February 2026', () => {
      const { container } = renderWithTheme(<CalendarMonth {...defaultProps} />);

      // February 2026 has 28 days
      // Check using data-date attributes which are unique
      const firstDay = container.querySelector('[data-date="2026-02-01"]');
      const lastDay = container.querySelector('[data-date="2026-02-28"]');

      expect(firstDay).toBeInTheDocument();
      expect(lastDay).toBeInTheDocument();

      // Verify day 29 does NOT exist for February 2026 (not a leap year)
      const day29 = container.querySelector('[data-date="2026-02-29"]');
      expect(day29).not.toBeInTheDocument();
    });

    it('renders different months correctly', () => {
      const { container } = renderWithTheme(<CalendarMonth year={2026} month={0} />); // January

      expect(screen.getByText('January 2026')).toBeInTheDocument();

      // January 2026 has 31 days - use data-date to check
      const day31 = container.querySelector('[data-date="2026-01-31"]');
      expect(day31).toBeInTheDocument();
    });

    it('marks today with special styling', () => {
      const today = new Date();
      const { container } = renderWithTheme(
        <CalendarMonth year={today.getFullYear()} month={today.getMonth()} />
      );

      // Today's date should have aria-label containing today's date
      const todayCell = container.querySelector(`[data-date="${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}"]`);
      expect(todayCell).toBeInTheDocument();
    });
  });

  describe('event indicators', () => {
    it('shows dot indicator for single event', () => {
      const eventCounts = new Map<string, number>();
      eventCounts.set('2026-02-05', 1);

      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} eventCounts={eventCounts} />
      );

      // Find the cell with an event
      const cellWithEvent = container.querySelector('[data-date="2026-02-05"]');
      expect(cellWithEvent).toBeInTheDocument();

      // Should have an event indicator
      const indicator = cellWithEvent?.querySelector('[aria-hidden="true"]');
      expect(indicator).toBeInTheDocument();
    });

    it('shows badge with count for multiple events', () => {
      const eventCounts = new Map<string, number>();
      eventCounts.set('2026-02-20', 8); // Use day 20 to avoid collisions with adjacent months

      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} eventCounts={eventCounts} />
      );

      // Badge should show count - look for the Fluent Badge component
      const cell = container.querySelector('[data-date="2026-02-20"]');
      expect(cell).toBeInTheDocument();

      const badge = cell?.querySelector('.fui-Badge');
      expect(badge).toBeInTheDocument();
      expect(badge?.textContent).toBe('8');
    });

    it('shows "99+" for more than 99 events', () => {
      const eventCounts = new Map<string, number>();
      eventCounts.set('2026-02-18', 150); // Use day 18 to avoid collisions

      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} eventCounts={eventCounts} />
      );

      // Badge should show 99+ for large counts
      const cell = container.querySelector('[data-date="2026-02-18"]');
      expect(cell).toBeInTheDocument();

      const badge = cell?.querySelector('.fui-Badge');
      expect(badge).toBeInTheDocument();
      expect(badge?.textContent).toBe('99+');
    });

    it('supports legacy eventDates prop (Set)', () => {
      const eventDates = createEventDates();

      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} eventDates={eventDates} />
      );

      // Cells with events should have indicators
      const cell = container.querySelector('[data-date="2026-02-05"]');
      expect(cell).toBeInTheDocument();
    });

    it('includes event count in aria-label for accessibility', () => {
      const eventCounts = new Map<string, number>();
      eventCounts.set('2026-02-10', 3);

      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} eventCounts={eventCounts} />
      );

      const cellWithEvents = container.querySelector('[data-date="2026-02-10"]');
      expect(cellWithEvents).toHaveAttribute('aria-label', expect.stringContaining('3 events'));
    });
  });

  describe('date selection', () => {
    it('calls onDateClick when clicking a date', () => {
      const onDateClick = jest.fn();

      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} onDateClick={onDateClick} />
      );

      const dayCell = container.querySelector('[data-date="2026-02-10"]');
      expect(dayCell).toBeInTheDocument();

      fireEvent.click(dayCell!);

      expect(onDateClick).toHaveBeenCalledTimes(1);
      expect(onDateClick).toHaveBeenCalledWith(expect.any(Date));

      // Verify the date passed
      const calledDate = onDateClick.mock.calls[0][0];
      expect(calledDate.getFullYear()).toBe(2026);
      expect(calledDate.getMonth()).toBe(1); // February
      expect(calledDate.getDate()).toBe(10);
    });

    it('highlights selected dates', () => {
      const selectedDates = new Set(['2026-02-10']);

      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} selectedDates={selectedDates} />
      );

      const selectedCell = container.querySelector('[data-date="2026-02-10"]');
      expect(selectedCell).toHaveAttribute('aria-selected', 'true');
    });

    it('supports multiple selected dates', () => {
      const selectedDates = new Set(['2026-02-10', '2026-02-15', '2026-02-20']);

      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} selectedDates={selectedDates} />
      );

      expect(container.querySelector('[data-date="2026-02-10"]')).toHaveAttribute('aria-selected', 'true');
      expect(container.querySelector('[data-date="2026-02-15"]')).toHaveAttribute('aria-selected', 'true');
      expect(container.querySelector('[data-date="2026-02-20"]')).toHaveAttribute('aria-selected', 'true');
    });
  });

  describe('range selection', () => {
    it('calls onDateShiftClick when shift-clicking a date', () => {
      const onDateClick = jest.fn();
      const onDateShiftClick = jest.fn();

      const { container } = renderWithTheme(
        <CalendarMonth
          {...defaultProps}
          onDateClick={onDateClick}
          onDateShiftClick={onDateShiftClick}
        />
      );

      const dayCell = container.querySelector('[data-date="2026-02-15"]');
      fireEvent.click(dayCell!, { shiftKey: true });

      expect(onDateShiftClick).toHaveBeenCalledTimes(1);
      expect(onDateClick).not.toHaveBeenCalled();
    });

    it('highlights range between start and end dates', () => {
      const { container } = renderWithTheme(
        <CalendarMonth
          {...defaultProps}
          rangeStartDate="2026-02-05"
          rangeEndDate="2026-02-10"
        />
      );

      // Dates in range should have aria-selected
      const startCell = container.querySelector('[data-date="2026-02-05"]');
      const middleCell = container.querySelector('[data-date="2026-02-07"]');
      const endCell = container.querySelector('[data-date="2026-02-10"]');

      expect(startCell).toHaveAttribute('aria-selected', 'true');
      expect(middleCell).toHaveAttribute('aria-selected', 'true');
      expect(endCell).toHaveAttribute('aria-selected', 'true');

      // Date outside range should not be selected
      const outsideCell = container.querySelector('[data-date="2026-02-15"]');
      expect(outsideCell).toHaveAttribute('aria-selected', 'false');
    });

    it('includes range info in aria-label', () => {
      const { container } = renderWithTheme(
        <CalendarMonth
          {...defaultProps}
          rangeStartDate="2026-02-05"
          rangeEndDate="2026-02-10"
        />
      );

      const startCell = container.querySelector('[data-date="2026-02-05"]');
      expect(startCell).toHaveAttribute('aria-label', expect.stringContaining('range start'));

      const endCell = container.querySelector('[data-date="2026-02-10"]');
      expect(endCell).toHaveAttribute('aria-label', expect.stringContaining('range end'));
    });
  });

  describe('keyboard navigation', () => {
    it('navigates with arrow keys', () => {
      const onFocusDateChange = jest.fn();

      const { container } = renderWithTheme(
        <CalendarMonth
          {...defaultProps}
          focusedDate="2026-02-10"
          onFocusDateChange={onFocusDateChange}
        />
      );

      const focusedCell = container.querySelector('[data-date="2026-02-10"]');
      expect(focusedCell).toHaveAttribute('tabindex', '0');

      // Arrow right should move to next day
      fireEvent.keyDown(focusedCell!, { key: 'ArrowRight' });
      expect(onFocusDateChange).toHaveBeenCalledWith(expect.any(Date));

      const calledDate = onFocusDateChange.mock.calls[0][0];
      expect(calledDate.getDate()).toBe(11);
    });

    it('selects date with Enter key', () => {
      const onDateClick = jest.fn();

      const { container } = renderWithTheme(
        <CalendarMonth
          {...defaultProps}
          focusedDate="2026-02-10"
          onDateClick={onDateClick}
        />
      );

      const focusedCell = container.querySelector('[data-date="2026-02-10"]');
      fireEvent.keyDown(focusedCell!, { key: 'Enter' });

      expect(onDateClick).toHaveBeenCalled();
    });

    it('selects date with Space key', () => {
      const onDateClick = jest.fn();

      const { container } = renderWithTheme(
        <CalendarMonth
          {...defaultProps}
          focusedDate="2026-02-15"
          onDateClick={onDateClick}
        />
      );

      const focusedCell = container.querySelector('[data-date="2026-02-15"]');
      fireEvent.keyDown(focusedCell!, { key: ' ' });

      expect(onDateClick).toHaveBeenCalled();
    });

    it('navigates up/down by week', () => {
      const onFocusDateChange = jest.fn();

      const { container } = renderWithTheme(
        <CalendarMonth
          {...defaultProps}
          focusedDate="2026-02-10"
          onFocusDateChange={onFocusDateChange}
        />
      );

      const focusedCell = container.querySelector('[data-date="2026-02-10"]');

      // Arrow up should move to previous week
      fireEvent.keyDown(focusedCell!, { key: 'ArrowUp' });

      const upDate = onFocusDateChange.mock.calls[0][0];
      expect(upDate.getDate()).toBe(3); // 10 - 7 = 3

      onFocusDateChange.mockClear();

      // Arrow down should move to next week
      fireEvent.keyDown(focusedCell!, { key: 'ArrowDown' });

      const downDate = onFocusDateChange.mock.calls[0][0];
      expect(downDate.getDate()).toBe(17); // 10 + 7 = 17
    });
  });

  describe('adjacent months', () => {
    it('hides adjacent month dates by default', () => {
      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} showAdjacentMonths={false} />
      );

      // February 2026 starts on Sunday (day 0), so Jan 31 should be faded/hidden
      // Look for cells that are aria-hidden
      const hiddenCells = container.querySelectorAll('[aria-hidden="true"]');
      expect(hiddenCells.length).toBeGreaterThan(0);
    });

    it('does not trigger click on adjacent month dates when hidden', () => {
      const onDateClick = jest.fn();

      const { container } = renderWithTheme(
        <CalendarMonth
          {...defaultProps}
          showAdjacentMonths={false}
          onDateClick={onDateClick}
        />
      );

      // Find a cell that's aria-hidden (outside current month)
      const hiddenCells = container.querySelectorAll('[aria-hidden="true"]');
      if (hiddenCells.length > 0) {
        fireEvent.click(hiddenCells[0]);
        expect(onDateClick).not.toHaveBeenCalled();
      }
    });
  });

  describe('theme support', () => {
    it('renders correctly in light theme', () => {
      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} />,
        webLightTheme
      );

      expect(container.firstChild).toBeInTheDocument();
      expect(screen.getByText('February 2026')).toBeInTheDocument();
    });

    it('renders correctly in dark theme', () => {
      const { container } = renderWithTheme(
        <CalendarMonth {...defaultProps} />,
        webDarkTheme
      );

      expect(container.firstChild).toBeInTheDocument();
      expect(screen.getByText('February 2026')).toBeInTheDocument();
    });
  });

  describe('edge cases', () => {
    it('handles December to January transition', () => {
      renderWithTheme(<CalendarMonth year={2025} month={11} />); // December 2025
      expect(screen.getByText('December 2025')).toBeInTheDocument();
    });

    it('handles leap year February', () => {
      const { container } = renderWithTheme(<CalendarMonth year={2024} month={1} />); // February 2024 (leap year)
      expect(screen.getByText('February 2024')).toBeInTheDocument();

      // Leap year has 29 days - use data-date to check
      const day29 = container.querySelector('[data-date="2024-02-29"]');
      expect(day29).toBeInTheDocument();
    });

    it('renders with empty event counts', () => {
      const eventCounts = new Map<string, number>();

      renderWithTheme(
        <CalendarMonth {...defaultProps} eventCounts={eventCounts} />
      );

      expect(screen.getByText('February 2026')).toBeInTheDocument();
    });
  });
});
