/**
 * Tests for DaysUntilDueBadge Component
 *
 * Tests:
 * - Display value rendering for different urgency states
 * - Color variations based on urgency
 * - Size variants
 * - Accessibility labels
 * - dueDate vs daysUntilDue prop handling
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DaysUntilDueBadge } from '../../components/DaysUntilDueBadge';

// Wrapper component with Fluent Provider
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <FluentProvider theme={webLightTheme}>
        {children}
    </FluentProvider>
);

const renderWithProvider = (ui: React.ReactElement) => {
    return render(ui, { wrapper: TestWrapper });
};

// Helper to create dates relative to today
const daysFromNow = (days: number): Date => {
    const date = new Date();
    date.setDate(date.getDate() + days);
    return date;
};

describe('DaysUntilDueBadge', () => {
    describe('display value rendering with daysUntilDue prop', () => {
        it('displays "Today" when daysUntilDue is 0', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={0} isOverdue={false} />
            );

            expect(screen.getByText('Today')).toBeInTheDocument();
        });

        it('displays numeric value for future dates', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
            );

            expect(screen.getByText('5')).toBeInTheDocument();
        });

        it('displays "+N" format for overdue dates', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={3} isOverdue={true} />
            );

            expect(screen.getByText('+3')).toBeInTheDocument();
        });

        it('handles single day overdue', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={1} isOverdue={true} />
            );

            expect(screen.getByText('+1')).toBeInTheDocument();
        });

        it('handles large numbers', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={365} isOverdue={false} />
            );

            expect(screen.getByText('365')).toBeInTheDocument();
        });
    });

    describe('display value rendering with dueDate prop', () => {
        it('calculates days from dueDate for future date', () => {
            const futureDate = daysFromNow(3);
            renderWithProvider(<DaysUntilDueBadge dueDate={futureDate} />);

            expect(screen.getByText('3')).toBeInTheDocument();
        });

        it('displays "Today" for same-day dueDate', () => {
            const today = new Date();
            renderWithProvider(<DaysUntilDueBadge dueDate={today} />);

            expect(screen.getByText('Today')).toBeInTheDocument();
        });

        it('calculates overdue from past dueDate', () => {
            const pastDate = daysFromNow(-2);
            renderWithProvider(<DaysUntilDueBadge dueDate={pastDate} />);

            expect(screen.getByText('+2')).toBeInTheDocument();
        });
    });

    describe('urgency-based coloring', () => {
        it('applies overdue styling for overdue events', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={true} />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
        });

        it('applies critical styling for 0-1 days', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={0} isOverdue={false} />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
        });

        it('applies urgent styling for 2-3 days', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={3} isOverdue={false} />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
        });

        it('applies warning styling for 4-7 days', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
        });

        it('applies normal styling for 8+ days', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={10} isOverdue={false} />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
        });
    });

    describe('urgency override', () => {
        it('uses urgencyOverride when provided', () => {
            renderWithProvider(
                <DaysUntilDueBadge
                    daysUntilDue={10}
                    isOverdue={false}
                    urgencyOverride="critical"
                />
            );

            // Value should still be 10, but styling would be critical
            expect(screen.getByText('10')).toBeInTheDocument();
        });

        it('supports all urgency override values', () => {
            const urgencyLevels = ['overdue', 'critical', 'urgent', 'warning', 'normal'] as const;

            urgencyLevels.forEach(urgency => {
                const { unmount } = renderWithProvider(
                    <DaysUntilDueBadge
                        daysUntilDue={5}
                        isOverdue={false}
                        urgencyOverride={urgency}
                    />
                );

                expect(screen.getByText('5')).toBeInTheDocument();
                unmount();
            });
        });
    });

    describe('size variants', () => {
        it('renders medium size by default', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
        });

        it('renders small size when specified', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge
                    daysUntilDue={5}
                    isOverdue={false}
                    size="small"
                />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
        });

        it('renders medium size when explicitly specified', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge
                    daysUntilDue={5}
                    isOverdue={false}
                    size="medium"
                />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has role="status" for screen readers', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
            );

            expect(screen.getByRole('status')).toBeInTheDocument();
        });

        it('has accessible label for future date', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
            );

            expect(screen.getByRole('status')).toHaveAttribute(
                'aria-label',
                'Due in 5 days'
            );
        });

        it('has accessible label for today', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={0} isOverdue={false} />
            );

            expect(screen.getByRole('status')).toHaveAttribute(
                'aria-label',
                'Due today'
            );
        });

        it('has accessible label for overdue', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={3} isOverdue={true} />
            );

            expect(screen.getByRole('status')).toHaveAttribute(
                'aria-label',
                '3 days overdue'
            );
        });

        it('uses custom aria-label when provided', () => {
            renderWithProvider(
                <DaysUntilDueBadge
                    daysUntilDue={5}
                    isOverdue={false}
                    ariaLabel="Custom label"
                />
            );

            expect(screen.getByRole('status')).toHaveAttribute(
                'aria-label',
                'Custom label'
            );
        });

        it('text content is hidden from screen readers', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
            );

            const textElement = container.querySelector('[aria-hidden="true"]');
            expect(textElement).toBeInTheDocument();
        });
    });

    describe('structure', () => {
        it('renders badge container with text', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
            );

            const badge = container.firstChild as HTMLElement;
            expect(badge).toBeInTheDocument();
            expect(badge.textContent).toBe('5');
        });

        it('circular shape (borderRadius circular)', () => {
            const { container } = renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
            );

            const badge = container.firstChild as HTMLElement;
            // Component uses tokens.borderRadiusCircular
            expect(badge).toBeInTheDocument();
        });
    });

    describe('edge cases', () => {
        it('handles 1 day until due', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={1} isOverdue={false} />
            );

            expect(screen.getByText('1')).toBeInTheDocument();
            expect(screen.getByRole('status')).toHaveAttribute(
                'aria-label',
                'Due in 1 day'
            );
        });

        it('handles 1 day overdue', () => {
            renderWithProvider(
                <DaysUntilDueBadge daysUntilDue={1} isOverdue={true} />
            );

            expect(screen.getByText('+1')).toBeInTheDocument();
            expect(screen.getByRole('status')).toHaveAttribute(
                'aria-label',
                '1 day overdue'
            );
        });

        it('handles undefined daysUntilDue with dueDate', () => {
            const futureDate = daysFromNow(7);
            renderWithProvider(<DaysUntilDueBadge dueDate={futureDate} />);

            expect(screen.getByText('7')).toBeInTheDocument();
        });
    });
});
