/**
 * AiSummaryPanel Unit Tests
 *
 * Tests for the AI Summary Panel component.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AiSummaryPanel, AiSummaryPanelProps, SummaryStatus } from '../AiSummaryPanel';

// Wrapper component with FluentProvider
const renderWithProvider = (props: AiSummaryPanelProps) => {
    return render(
        <FluentProvider theme={webLightTheme}>
            <AiSummaryPanel {...props} />
        </FluentProvider>
    );
};

describe('AiSummaryPanel', () => {
    const defaultProps: AiSummaryPanelProps = {
        documentId: 'doc-123',
        fileName: 'test-document.pdf',
        status: 'pending'
    };

    describe('rendering', () => {
        it('should render file name', () => {
            renderWithProvider(defaultProps);
            expect(screen.getByText('test-document.pdf')).toBeInTheDocument();
        });

        it('should render with correct document id data attribute', () => {
            const { container } = renderWithProvider(defaultProps);
            const panel = container.querySelector('[data-document-id="doc-123"]');
            expect(panel).toBeInTheDocument();
        });

        it('should render with correct aria-label', () => {
            renderWithProvider(defaultProps);
            expect(screen.getByRole('article', { name: 'AI summary for test-document.pdf' })).toBeInTheDocument();
        });
    });

    describe('status: pending', () => {
        it('should show pending badge', () => {
            renderWithProvider({ ...defaultProps, status: 'pending' });
            expect(screen.getByText('Pending')).toBeInTheDocument();
        });

        it('should show spinner and preparing message', () => {
            renderWithProvider({ ...defaultProps, status: 'pending' });
            expect(screen.getByText('Preparing summary...')).toBeInTheDocument();
        });
    });

    describe('status: streaming', () => {
        it('should show streaming badge', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'streaming',
                summary: 'This is a partial summary'
            });
            expect(screen.getByText('Generating...')).toBeInTheDocument();
        });

        it('should show summary text', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'streaming',
                summary: 'This is a partial summary'
            });
            expect(screen.getByText(/This is a partial summary/)).toBeInTheDocument();
        });

        it('should show cursor animation', () => {
            const { container } = renderWithProvider({
                ...defaultProps,
                status: 'streaming',
                summary: 'Streaming text'
            });
            // The cursor should be present (as a span with aria-hidden)
            const cursor = container.querySelector('[aria-hidden="true"]');
            expect(cursor).toBeInTheDocument();
        });

        it('should have aria-live region set to polite', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'streaming',
                summary: 'Streaming'
            });
            const region = screen.getByRole('region', { name: 'Document summary' });
            expect(region).toHaveAttribute('aria-live', 'polite');
        });
    });

    describe('status: complete', () => {
        it('should show complete badge', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'complete',
                summary: 'Complete summary text'
            });
            expect(screen.getByText('Complete')).toBeInTheDocument();
        });

        it('should show full summary without cursor', () => {
            const { container } = renderWithProvider({
                ...defaultProps,
                status: 'complete',
                summary: 'Complete summary text'
            });
            expect(screen.getByText(/Complete summary text/)).toBeInTheDocument();
            // No cursor in complete state
            const summaryRegion = screen.getByRole('region', { name: 'Document summary' });
            expect(summaryRegion.querySelector('[aria-hidden="true"]')).not.toBeInTheDocument();
        });

        it('should have aria-live set to off', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'complete',
                summary: 'Complete'
            });
            const region = screen.getByRole('region', { name: 'Document summary' });
            expect(region).toHaveAttribute('aria-live', 'off');
        });
    });

    describe('status: error', () => {
        it('should show error badge', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'error',
                error: 'Failed to generate summary'
            });
            expect(screen.getByText('Error')).toBeInTheDocument();
        });

        it('should show error message', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'error',
                error: 'Failed to generate summary'
            });
            expect(screen.getByText('Failed to generate summary')).toBeInTheDocument();
        });

        it('should show default error message when error prop is not provided', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'error'
            });
            expect(screen.getByText('An error occurred while generating the summary.')).toBeInTheDocument();
        });

        it('should show retry button when onRetry is provided', () => {
            const onRetry = jest.fn();
            renderWithProvider({
                ...defaultProps,
                status: 'error',
                error: 'Error',
                onRetry
            });
            expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
        });

        it('should not show retry button when onRetry is not provided', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'error',
                error: 'Error'
            });
            expect(screen.queryByRole('button', { name: /retry/i })).not.toBeInTheDocument();
        });

        it('should call onRetry when retry button is clicked', () => {
            const onRetry = jest.fn();
            renderWithProvider({
                ...defaultProps,
                status: 'error',
                error: 'Error',
                onRetry
            });
            fireEvent.click(screen.getByRole('button', { name: /retry/i }));
            expect(onRetry).toHaveBeenCalledTimes(1);
        });
    });

    describe('status: skipped', () => {
        it('should show skipped badge', () => {
            renderWithProvider({ ...defaultProps, status: 'skipped' });
            expect(screen.getByText('Skipped')).toBeInTheDocument();
        });

        it('should show skipped message', () => {
            renderWithProvider({ ...defaultProps, status: 'skipped' });
            expect(screen.getByText('Summary skipped by user')).toBeInTheDocument();
        });
    });

    describe('status: not-supported', () => {
        it('should show not-supported badge', () => {
            renderWithProvider({ ...defaultProps, status: 'not-supported' });
            expect(screen.getByText('Not Supported')).toBeInTheDocument();
        });

        it('should show not-supported message', () => {
            renderWithProvider({ ...defaultProps, status: 'not-supported' });
            expect(screen.getByText('File type not supported for AI summarization')).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('should have proper aria-label for status badge', () => {
            renderWithProvider({ ...defaultProps, status: 'complete', summary: 'Test' });
            expect(screen.getByLabelText('Summary status: Complete')).toBeInTheDocument();
        });

        it('should have proper aria-label for retry button', () => {
            renderWithProvider({
                ...defaultProps,
                status: 'error',
                error: 'Error',
                onRetry: jest.fn()
            });
            expect(screen.getByRole('button', { name: 'Retry summary for test-document.pdf' })).toBeInTheDocument();
        });
    });
});
