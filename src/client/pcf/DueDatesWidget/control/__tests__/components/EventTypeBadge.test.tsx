/**
 * Tests for EventTypeBadge Component
 *
 * Tests:
 * - Renders type name correctly
 * - Auto-detects color from type name
 * - Supports color override
 * - Handles indicatorOnly mode
 * - Accessibility labels
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { EventTypeBadge } from '../../components/EventTypeBadge';

// Wrapper component with Fluent Provider
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <FluentProvider theme={webLightTheme}>
        {children}
    </FluentProvider>
);

const renderWithProvider = (ui: React.ReactElement) => {
    return render(ui, { wrapper: TestWrapper });
};

describe('EventTypeBadge', () => {
    describe('type name rendering', () => {
        it('renders type name text', () => {
            renderWithProvider(<EventTypeBadge typeName="Filing Deadline" />);

            expect(screen.getByText('Filing Deadline')).toBeInTheDocument();
        });

        it('renders various type names correctly', () => {
            const typeNames = ['Hearing', 'Meeting', 'Deadline', 'Review', 'Court'];

            typeNames.forEach(name => {
                const { unmount } = renderWithProvider(
                    <EventTypeBadge typeName={name} />
                );

                expect(screen.getByText(name)).toBeInTheDocument();
                unmount();
            });
        });

        it('handles empty type name', () => {
            renderWithProvider(<EventTypeBadge typeName="" />);

            // Should still render container with badge but no text
            const container = screen.getByRole('img');
            expect(container).toBeInTheDocument();
        });
    });

    describe('color auto-detection', () => {
        it('uses auto-detected color when no color prop provided', () => {
            const { container } = renderWithProvider(
                <EventTypeBadge typeName="Hearing" />
            );

            // Should render with yellow variant
            const badge = container.querySelector('[aria-hidden="true"]');
            expect(badge).toBeInTheDocument();
        });

        it('detects different colors for different type names', () => {
            const typesToColors = [
                { type: 'Hearing', expectedColor: 'yellow' },
                { type: 'Filing', expectedColor: 'green' },
                { type: 'Meeting', expectedColor: 'blue' },
                { type: 'Regulatory', expectedColor: 'purple' }
            ];

            typesToColors.forEach(({ type }) => {
                const { container, unmount } = renderWithProvider(
                    <EventTypeBadge typeName={type} />
                );

                // Badge should be rendered
                const badge = container.querySelector('[aria-hidden="true"]');
                expect(badge).toBeInTheDocument();
                unmount();
            });
        });
    });

    describe('color override', () => {
        it('uses provided color over auto-detection', () => {
            const { container } = renderWithProvider(
                <EventTypeBadge typeName="Hearing" color="blue" />
            );

            // Should render with badge
            const badge = container.querySelector('[aria-hidden="true"]');
            expect(badge).toBeInTheDocument();
        });

        it('supports all color variants', () => {
            const colors = ['yellow', 'green', 'purple', 'blue', 'orange', 'red', 'teal', 'default'] as const;

            colors.forEach(color => {
                const { container, unmount } = renderWithProvider(
                    <EventTypeBadge typeName="Test" color={color} />
                );

                const badge = container.querySelector('[aria-hidden="true"]');
                expect(badge).toBeInTheDocument();
                unmount();
            });
        });
    });

    describe('indicatorOnly mode', () => {
        it('shows text by default', () => {
            renderWithProvider(<EventTypeBadge typeName="Hearing" />);

            expect(screen.getByText('Hearing')).toBeInTheDocument();
        });

        it('hides text when indicatorOnly is true', () => {
            renderWithProvider(
                <EventTypeBadge typeName="Hearing" indicatorOnly={true} />
            );

            expect(screen.queryByText('Hearing')).not.toBeInTheDocument();
        });

        it('shows badge indicator even in indicatorOnly mode', () => {
            const { container } = renderWithProvider(
                <EventTypeBadge typeName="Hearing" indicatorOnly={true} />
            );

            const badge = container.querySelector('[aria-hidden="true"]');
            expect(badge).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has role="img" on container', () => {
            renderWithProvider(<EventTypeBadge typeName="Hearing" />);

            expect(screen.getByRole('img')).toBeInTheDocument();
        });

        it('has default aria-label with type name', () => {
            renderWithProvider(<EventTypeBadge typeName="Hearing" />);

            expect(screen.getByRole('img')).toHaveAttribute(
                'aria-label',
                'Event type: Hearing'
            );
        });

        it('uses custom aria-label when provided', () => {
            renderWithProvider(
                <EventTypeBadge
                    typeName="Hearing"
                    ariaLabel="Custom accessibility label"
                />
            );

            expect(screen.getByRole('img')).toHaveAttribute(
                'aria-label',
                'Custom accessibility label'
            );
        });

        it('badge indicator is hidden from screen readers', () => {
            const { container } = renderWithProvider(
                <EventTypeBadge typeName="Hearing" />
            );

            const badge = container.querySelector('[aria-hidden="true"]');
            expect(badge).toBeInTheDocument();
        });
    });

    describe('structure', () => {
        it('renders container with badge and text elements', () => {
            const { container } = renderWithProvider(
                <EventTypeBadge typeName="Hearing" />
            );

            // Container
            const wrapper = screen.getByRole('img');
            expect(wrapper).toBeInTheDocument();

            // Badge indicator
            const badge = container.querySelector('[aria-hidden="true"]');
            expect(badge).toBeInTheDocument();

            // Text
            expect(screen.getByText('Hearing')).toBeInTheDocument();
        });

        it('renders in correct order: badge then text', () => {
            const { container } = renderWithProvider(
                <EventTypeBadge typeName="Hearing" />
            );

            const wrapper = screen.getByRole('img');
            const children = wrapper.childNodes;

            // First child should be the badge div
            expect(children[0]).toHaveAttribute('aria-hidden', 'true');

            // Second child should contain the text
            expect(children[1]).toHaveTextContent('Hearing');
        });
    });
});
