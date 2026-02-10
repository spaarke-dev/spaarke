/**
 * Tests for DateColumn Component
 *
 * Tests:
 * - Renders day number correctly
 * - Renders day abbreviation correctly
 * - Handles different days of the week
 * - Uses correct styling
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DateColumn } from '../../components/DateColumn';

// Wrapper component with Fluent Provider
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <FluentProvider theme={webLightTheme}>
        {children}
    </FluentProvider>
);

const renderWithProvider = (ui: React.ReactElement) => {
    return render(ui, { wrapper: TestWrapper });
};

// Helper to create a date at local noon to avoid timezone issues
const createLocalDate = (year: number, month: number, day: number): Date => {
    return new Date(year, month - 1, day, 12, 0, 0);
};

describe('DateColumn', () => {
    describe('day number rendering', () => {
        it('renders single digit day number', () => {
            const date = createLocalDate(2026, 2, 4); // 4th
            renderWithProvider(<DateColumn date={date} />);

            expect(screen.getByText('4')).toBeInTheDocument();
        });

        it('renders double digit day number', () => {
            const date = createLocalDate(2026, 2, 15); // 15th
            renderWithProvider(<DateColumn date={date} />);

            expect(screen.getByText('15')).toBeInTheDocument();
        });

        it('renders day 1', () => {
            const date = createLocalDate(2026, 2, 1);
            renderWithProvider(<DateColumn date={date} />);

            expect(screen.getByText('1')).toBeInTheDocument();
        });

        it('renders day 31', () => {
            const date = createLocalDate(2026, 1, 31);
            renderWithProvider(<DateColumn date={date} />);

            expect(screen.getByText('31')).toBeInTheDocument();
        });
    });

    describe('day abbreviation rendering', () => {
        // Create dates using local time to ensure consistent day-of-week
        const dayAbbreviations = [
            { date: createLocalDate(2026, 2, 1), expected: 'SUN' }, // Sunday
            { date: createLocalDate(2026, 2, 2), expected: 'MON' }, // Monday
            { date: createLocalDate(2026, 2, 3), expected: 'TUE' }, // Tuesday
            { date: createLocalDate(2026, 2, 4), expected: 'WED' }, // Wednesday
            { date: createLocalDate(2026, 2, 5), expected: 'THU' }, // Thursday
            { date: createLocalDate(2026, 2, 6), expected: 'FRI' }, // Friday
            { date: createLocalDate(2026, 2, 7), expected: 'SAT' }  // Saturday
        ];

        it.each(dayAbbreviations)(
            'renders correct day abbreviation',
            ({ date, expected }) => {
                renderWithProvider(<DateColumn date={date} />);

                expect(screen.getByText(expected)).toBeInTheDocument();
            }
        );
    });

    describe('combined rendering', () => {
        it('renders both day number and abbreviation', () => {
            const date = createLocalDate(2026, 2, 4); // Wednesday the 4th
            renderWithProvider(<DateColumn date={date} />);

            expect(screen.getByText('4')).toBeInTheDocument();
            expect(screen.getByText('WED')).toBeInTheDocument();
        });

        it('renders correctly for different months', () => {
            // January 15, 2026 (Thursday)
            const janDate = createLocalDate(2026, 1, 15);
            const { unmount } = renderWithProvider(<DateColumn date={janDate} />);

            expect(screen.getByText('15')).toBeInTheDocument();
            expect(screen.getByText('THU')).toBeInTheDocument();

            unmount();

            // December 25, 2026 (Friday)
            const decDate = createLocalDate(2026, 12, 25);
            renderWithProvider(<DateColumn date={decDate} />);

            expect(screen.getByText('25')).toBeInTheDocument();
            expect(screen.getByText('FRI')).toBeInTheDocument();
        });
    });

    describe('structure and styling', () => {
        it('has proper container structure', () => {
            const date = new Date('2026-02-04');
            const { container } = renderWithProvider(<DateColumn date={date} />);

            // Container should have the date column class
            const wrapper = container.firstChild as HTMLElement;
            expect(wrapper).toBeInTheDocument();

            // Should contain both text elements
            const textElements = wrapper?.querySelectorAll('span');
            expect(textElements).toHaveLength(2);
        });

        it('day number appears before abbreviation in DOM', () => {
            const date = createLocalDate(2026, 2, 4);
            renderWithProvider(<DateColumn date={date} />);

            // Both elements should be present in the document
            const dayNumber = screen.getByText('4');
            const dayAbbrev = screen.getByText('WED');

            expect(dayNumber).toBeInTheDocument();
            expect(dayAbbrev).toBeInTheDocument();

            // Both should be in the same container
            expect(dayNumber.parentElement).toBe(dayAbbrev.parentElement);
        });
    });

    describe('edge cases', () => {
        it('handles leap year date (Feb 29)', () => {
            // 2028 is a leap year
            const leapDate = createLocalDate(2028, 2, 29);
            renderWithProvider(<DateColumn date={leapDate} />);

            expect(screen.getByText('29')).toBeInTheDocument();
            expect(screen.getByText('TUE')).toBeInTheDocument();
        });

        it('handles year boundaries', () => {
            const newYears = createLocalDate(2026, 1, 1); // Thursday
            renderWithProvider(<DateColumn date={newYears} />);

            expect(screen.getByText('1')).toBeInTheDocument();
            expect(screen.getByText('THU')).toBeInTheDocument();
        });

        it('handles dates with time component', () => {
            // Date with time should still extract correct day
            const dateWithTime = createLocalDate(2026, 2, 4);
            renderWithProvider(<DateColumn date={dateWithTime} />);

            expect(screen.getByText('4')).toBeInTheDocument();
        });
    });
});
