/**
 * Tests for EventListItem Component
 *
 * Tests:
 * - Renders event information correctly
 * - Click and keyboard navigation
 * - Loading/navigating state
 * - Accessibility
 * - Component composition (DateColumn, EventTypeBadge, DaysUntilDueBadge)
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { EventListItem, IEventListItemProps } from '../../components/EventListItem';

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

// Default test props
const defaultProps: IEventListItemProps = {
    id: 'event-123',
    name: 'Test Filing Deadline',
    dueDate: createLocalDate(2026, 2, 10), // Feb 10, 2026 at noon local time
    eventType: 'type-456',
    eventTypeName: 'Filing Deadline',
    daysUntilDue: 6,
    isOverdue: false
};

describe('EventListItem', () => {
    describe('basic rendering', () => {
        it('renders event name', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            expect(screen.getByText('Test Filing Deadline')).toBeInTheDocument();
        });

        it('renders event type badge', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            expect(screen.getByText('Filing Deadline')).toBeInTheDocument();
        });

        it('renders date column with day number', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            // Feb 10 is day 10
            expect(screen.getByText('10')).toBeInTheDocument();
        });

        it('renders days until due badge', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            // 6 days until due
            expect(screen.getByText('6')).toBeInTheDocument();
        });

        it('renders description when provided', () => {
            renderWithProvider(
                <EventListItem
                    {...defaultProps}
                    description="Important regulatory filing due"
                />
            );

            expect(screen.getByText('Important regulatory filing due')).toBeInTheDocument();
        });

        it('does not render description when not provided', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            // Description text should not be in DOM if not provided
            expect(screen.queryByText('Important regulatory filing due')).not.toBeInTheDocument();
        });
    });

    describe('overdue state', () => {
        it('displays overdue badge for overdue events', () => {
            renderWithProvider(
                <EventListItem
                    {...defaultProps}
                    daysUntilDue={3}
                    isOverdue={true}
                />
            );

            // Overdue shows +N format
            expect(screen.getByText('+3')).toBeInTheDocument();
        });

        it('includes overdue info in aria-label', () => {
            renderWithProvider(
                <EventListItem
                    {...defaultProps}
                    daysUntilDue={3}
                    isOverdue={true}
                />
            );

            const item = screen.getByRole('button');
            expect(item.getAttribute('aria-label')).toContain('overdue');
        });
    });

    describe('click handling', () => {
        it('calls onClick with id and eventType when clicked', () => {
            const handleClick = jest.fn();

            renderWithProvider(
                <EventListItem {...defaultProps} onClick={handleClick} />
            );

            fireEvent.click(screen.getByRole('button'));

            expect(handleClick).toHaveBeenCalledTimes(1);
            expect(handleClick).toHaveBeenCalledWith('event-123', 'type-456');
        });

        it('does not throw when clicked without onClick handler', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            expect(() => {
                fireEvent.click(screen.getByRole('button'));
            }).not.toThrow();
        });
    });

    describe('keyboard navigation', () => {
        it('activates on Enter key', () => {
            const handleClick = jest.fn();

            renderWithProvider(
                <EventListItem {...defaultProps} onClick={handleClick} />
            );

            const item = screen.getByRole('button');
            fireEvent.keyDown(item, { key: 'Enter' });

            expect(handleClick).toHaveBeenCalledTimes(1);
            expect(handleClick).toHaveBeenCalledWith('event-123', 'type-456');
        });

        it('activates on Space key', () => {
            const handleClick = jest.fn();

            renderWithProvider(
                <EventListItem {...defaultProps} onClick={handleClick} />
            );

            const item = screen.getByRole('button');
            fireEvent.keyDown(item, { key: ' ' });

            expect(handleClick).toHaveBeenCalledTimes(1);
        });

        it('does not activate on other keys', () => {
            const handleClick = jest.fn();

            renderWithProvider(
                <EventListItem {...defaultProps} onClick={handleClick} />
            );

            const item = screen.getByRole('button');
            fireEvent.keyDown(item, { key: 'Tab' });
            fireEvent.keyDown(item, { key: 'Escape' });
            fireEvent.keyDown(item, { key: 'a' });

            expect(handleClick).not.toHaveBeenCalled();
        });

        it('is focusable (tabIndex 0)', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            const item = screen.getByRole('button');
            expect(item).toHaveAttribute('tabIndex', '0');
        });
    });

    describe('navigating/loading state', () => {
        it('shows spinner when isNavigating is true', () => {
            renderWithProvider(
                <EventListItem {...defaultProps} isNavigating={true} />
            );

            expect(screen.getByLabelText('Opening event...')).toBeInTheDocument();
        });

        it('does not show spinner by default', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            expect(screen.queryByLabelText('Opening event...')).not.toBeInTheDocument();
        });

        it('prevents click when navigating', () => {
            const handleClick = jest.fn();

            renderWithProvider(
                <EventListItem
                    {...defaultProps}
                    onClick={handleClick}
                    isNavigating={true}
                />
            );

            fireEvent.click(screen.getByRole('button'));

            expect(handleClick).not.toHaveBeenCalled();
        });

        it('prevents keyboard activation when navigating', () => {
            const handleClick = jest.fn();

            renderWithProvider(
                <EventListItem
                    {...defaultProps}
                    onClick={handleClick}
                    isNavigating={true}
                />
            );

            const item = screen.getByRole('button');
            fireEvent.keyDown(item, { key: 'Enter' });
            fireEvent.keyDown(item, { key: ' ' });

            expect(handleClick).not.toHaveBeenCalled();
        });

        it('sets tabIndex to -1 when navigating', () => {
            renderWithProvider(
                <EventListItem {...defaultProps} isNavigating={true} />
            );

            const item = screen.getByRole('button');
            expect(item).toHaveAttribute('tabIndex', '-1');
        });

        it('indicates busy state for screen readers', () => {
            renderWithProvider(
                <EventListItem {...defaultProps} isNavigating={true} />
            );

            const item = screen.getByRole('button');
            expect(item).toHaveAttribute('aria-busy', 'true');
        });

        it('indicates disabled state for screen readers', () => {
            renderWithProvider(
                <EventListItem {...defaultProps} isNavigating={true} />
            );

            const item = screen.getByRole('button');
            expect(item).toHaveAttribute('aria-disabled', 'true');
        });
    });

    describe('event type color', () => {
        it('accepts custom eventTypeColor prop', () => {
            renderWithProvider(
                <EventListItem {...defaultProps} eventTypeColor="purple" />
            );

            // Should render with the badge
            expect(screen.getByText('Filing Deadline')).toBeInTheDocument();
        });

        it('uses auto-detected color when not provided', () => {
            renderWithProvider(
                <EventListItem
                    {...defaultProps}
                    eventTypeName="Hearing"
                />
            );

            // Should render with auto-detected yellow for Hearing
            expect(screen.getByText('Hearing')).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has role="button"', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            expect(screen.getByRole('button')).toBeInTheDocument();
        });

        it('has comprehensive aria-label', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            const item = screen.getByRole('button');
            const ariaLabel = item.getAttribute('aria-label');

            expect(ariaLabel).toContain('Test Filing Deadline');
            expect(ariaLabel).toContain('Filing Deadline');
            expect(ariaLabel).toContain('due');
        });

        it('includes loading state in aria-label when navigating', () => {
            renderWithProvider(
                <EventListItem {...defaultProps} isNavigating={true} />
            );

            const item = screen.getByRole('button');
            const ariaLabel = item.getAttribute('aria-label');

            expect(ariaLabel).toContain('loading');
        });
    });

    describe('structure and composition', () => {
        it('composes DateColumn, EventTypeBadge, and DaysUntilDueBadge', () => {
            renderWithProvider(<EventListItem {...defaultProps} />);

            // DateColumn shows day number
            expect(screen.getByText('10')).toBeInTheDocument();

            // EventTypeBadge shows type name
            expect(screen.getByText('Filing Deadline')).toBeInTheDocument();

            // DaysUntilDueBadge shows days
            expect(screen.getByText('6')).toBeInTheDocument();
        });

        it('renders with correct element hierarchy', () => {
            const { container } = renderWithProvider(
                <EventListItem {...defaultProps} />
            );

            // Root is the button container
            const root = screen.getByRole('button');
            expect(root).toBeInTheDocument();

            // Contains multiple child sections
            expect(root.children.length).toBeGreaterThan(0);
        });
    });

    describe('edge cases', () => {
        it('handles very long event names', () => {
            const longName = 'A'.repeat(200);

            renderWithProvider(
                <EventListItem {...defaultProps} name={longName} />
            );

            expect(screen.getByText(longName)).toBeInTheDocument();
        });

        it('handles very long descriptions', () => {
            const longDesc = 'B'.repeat(500);

            renderWithProvider(
                <EventListItem {...defaultProps} description={longDesc} />
            );

            expect(screen.getByText(longDesc)).toBeInTheDocument();
        });

        it('handles today\'s date', () => {
            const today = new Date();

            renderWithProvider(
                <EventListItem
                    {...defaultProps}
                    dueDate={today}
                    daysUntilDue={0}
                />
            );

            // DaysUntilDueBadge should show "Today"
            expect(screen.getByText('Today')).toBeInTheDocument();
        });

        it('handles empty event type name', () => {
            renderWithProvider(
                <EventListItem {...defaultProps} eventTypeName="" />
            );

            // Should still render without crashing
            expect(screen.getByRole('button')).toBeInTheDocument();
        });
    });
});
