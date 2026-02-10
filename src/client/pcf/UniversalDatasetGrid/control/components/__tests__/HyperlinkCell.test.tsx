/**
 * HyperlinkCell Component Tests
 * Task 018: Add Unit Tests for Grid Enhancements
 *
 * Tests cover:
 * - Component rendering
 * - Click handling and side pane opening
 * - Keyboard accessibility
 * - Disabled state
 * - Empty text handling
 *
 * @see HyperlinkCell.tsx
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { HyperlinkCell, HyperlinkCellProps } from '../HyperlinkCell';

// Mock the sidePaneUtils module
jest.mock('../../utils/sidePaneUtils', () => ({
    openEventDetailPane: jest.fn().mockResolvedValue({ success: true, paneId: 'eventDetailPane' })
}));

// Mock the logger
jest.mock('../../utils/logger', () => ({
    logger: {
        debug: jest.fn(),
        info: jest.fn(),
        warn: jest.fn(),
        error: jest.fn()
    }
}));

// Import after mocking
import { openEventDetailPane } from '../../utils/sidePaneUtils';

// Wrapper component for FluentProvider
const renderWithProvider = (ui: React.ReactElement) => {
    return render(
        <FluentProvider theme={webLightTheme}>
            {ui}
        </FluentProvider>
    );
};

describe('HyperlinkCell', () => {
    const defaultProps: HyperlinkCellProps = {
        displayText: 'Test Event',
        recordId: '12345678-1234-1234-1234-123456789012'
    };

    beforeEach(() => {
        jest.clearAllMocks();
    });

    describe('rendering', () => {
        it('renders link with display text', () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button', { name: /open details for test event/i });
            expect(link).toBeInTheDocument();
            expect(link).toHaveTextContent('Test Event');
        });

        it('renders as anchor element', () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            expect(link.tagName).toBe('A');
        });

        it('has href="#" for styling', () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            expect(link).toHaveAttribute('href', '#');
        });

        it('has accessible label', () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button', { name: /open details for test event/i });
            expect(link).toHaveAttribute('aria-label', 'Open details for Test Event');
        });

        it('has tabIndex for keyboard navigation', () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            expect(link).toHaveAttribute('tabIndex', '0');
        });
    });

    describe('empty text handling', () => {
        it('renders dash for empty displayText', () => {
            renderWithProvider(
                <HyperlinkCell
                    {...defaultProps}
                    displayText=""
                />
            );

            expect(screen.getByText('-')).toBeInTheDocument();
            expect(screen.queryByRole('button')).not.toBeInTheDocument();
        });

        it('renders dash for whitespace displayText', () => {
            renderWithProvider(
                <HyperlinkCell
                    {...defaultProps}
                    displayText="   "
                />
            );

            expect(screen.getByText('-')).toBeInTheDocument();
            expect(screen.queryByRole('button')).not.toBeInTheDocument();
        });
    });

    describe('disabled state', () => {
        it('renders as plain text when disabled', () => {
            renderWithProvider(
                <HyperlinkCell
                    {...defaultProps}
                    disabled={true}
                />
            );

            expect(screen.getByText('Test Event')).toBeInTheDocument();
            expect(screen.queryByRole('button')).not.toBeInTheDocument();
        });

        it('does not trigger click when disabled', async () => {
            renderWithProvider(
                <HyperlinkCell
                    {...defaultProps}
                    disabled={true}
                />
            );

            const text = screen.getByText('Test Event');
            fireEvent.click(text);

            expect(openEventDetailPane).not.toHaveBeenCalled();
        });
    });

    describe('click handling', () => {
        it('opens side pane on click', async () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            fireEvent.click(link);

            await waitFor(() => {
                expect(openEventDetailPane).toHaveBeenCalledWith({
                    eventId: '12345678-1234-1234-1234-123456789012',
                    eventType: undefined
                });
            });
        });

        it('passes eventType to side pane', async () => {
            renderWithProvider(
                <HyperlinkCell
                    {...defaultProps}
                    eventType="87654321-4321-4321-4321-210987654321"
                />
            );

            const link = screen.getByRole('button');
            fireEvent.click(link);

            await waitFor(() => {
                expect(openEventDetailPane).toHaveBeenCalledWith({
                    eventId: '12345678-1234-1234-1234-123456789012',
                    eventType: '87654321-4321-4321-4321-210987654321'
                });
            });
        });

        it('prevents default on click', () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            const event = new MouseEvent('click', { bubbles: true, cancelable: true });
            const preventDefaultSpy = jest.spyOn(event, 'preventDefault');

            fireEvent(link, event);

            expect(preventDefaultSpy).toHaveBeenCalled();
        });

        it('stops propagation on click', () => {
            const parentHandler = jest.fn();
            renderWithProvider(
                <div onClick={parentHandler}>
                    <HyperlinkCell {...defaultProps} />
                </div>
            );

            const link = screen.getByRole('button');
            fireEvent.click(link);

            // Parent handler should not be called due to stopPropagation
            expect(parentHandler).not.toHaveBeenCalled();
        });

        it('calls onSidePaneOpened callback on success', async () => {
            const onSidePaneOpened = jest.fn();
            renderWithProvider(
                <HyperlinkCell
                    {...defaultProps}
                    onSidePaneOpened={onSidePaneOpened}
                />
            );

            const link = screen.getByRole('button');
            fireEvent.click(link);

            await waitFor(() => {
                expect(onSidePaneOpened).toHaveBeenCalledWith('12345678-1234-1234-1234-123456789012');
            });
        });

        it('handles side pane error gracefully', async () => {
            (openEventDetailPane as jest.Mock).mockResolvedValueOnce({
                success: false,
                error: 'Failed to open pane'
            });

            const onSidePaneOpened = jest.fn();
            renderWithProvider(
                <HyperlinkCell
                    {...defaultProps}
                    onSidePaneOpened={onSidePaneOpened}
                />
            );

            const link = screen.getByRole('button');
            fireEvent.click(link);

            await waitFor(() => {
                expect(openEventDetailPane).toHaveBeenCalled();
            });

            // onSidePaneOpened should NOT be called on error
            expect(onSidePaneOpened).not.toHaveBeenCalled();
        });

        it('handles exception in openEventDetailPane', async () => {
            (openEventDetailPane as jest.Mock).mockRejectedValueOnce(new Error('Network error'));

            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');

            // Should not throw
            expect(() => {
                fireEvent.click(link);
            }).not.toThrow();

            await waitFor(() => {
                expect(openEventDetailPane).toHaveBeenCalled();
            });
        });
    });

    describe('keyboard handling', () => {
        it('opens side pane on Enter key', async () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            fireEvent.keyDown(link, { key: 'Enter', code: 'Enter' });

            await waitFor(() => {
                expect(openEventDetailPane).toHaveBeenCalled();
            });
        });

        it('opens side pane on Space key', async () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            fireEvent.keyDown(link, { key: ' ', code: 'Space' });

            await waitFor(() => {
                expect(openEventDetailPane).toHaveBeenCalled();
            });
        });

        it('does not open side pane on other keys', async () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            fireEvent.keyDown(link, { key: 'Tab', code: 'Tab' });

            expect(openEventDetailPane).not.toHaveBeenCalled();
        });

        it('prevents default on Enter key', () => {
            renderWithProvider(<HyperlinkCell {...defaultProps} />);

            const link = screen.getByRole('button');
            const event = new KeyboardEvent('keydown', {
                key: 'Enter',
                code: 'Enter',
                bubbles: true,
                cancelable: true
            });
            const preventDefaultSpy = jest.spyOn(event, 'preventDefault');

            fireEvent(link, event);

            expect(preventDefaultSpy).toHaveBeenCalled();
        });

        it('stops propagation on keyboard activation', () => {
            const parentHandler = jest.fn();
            renderWithProvider(
                <div onKeyDown={parentHandler}>
                    <HyperlinkCell {...defaultProps} />
                </div>
            );

            const link = screen.getByRole('button');
            fireEvent.keyDown(link, { key: 'Enter', code: 'Enter' });

            expect(parentHandler).not.toHaveBeenCalled();
        });
    });

    describe('with eventType', () => {
        it('renders normally with eventType', () => {
            renderWithProvider(
                <HyperlinkCell
                    {...defaultProps}
                    eventType="87654321-4321-4321-4321-210987654321"
                />
            );

            const link = screen.getByRole('button', { name: /open details for test event/i });
            expect(link).toBeInTheDocument();
        });
    });
});
