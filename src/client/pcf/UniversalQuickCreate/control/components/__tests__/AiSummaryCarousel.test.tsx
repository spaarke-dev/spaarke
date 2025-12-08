/**
 * AiSummaryCarousel Unit Tests
 *
 * Tests for the AI Summary Carousel component.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
    AiSummaryCarousel,
    AiSummaryCarouselProps,
    DocumentSummaryState
} from '../AiSummaryCarousel';

// Wrapper component with FluentProvider
const renderWithProvider = (props: AiSummaryCarouselProps) => {
    return render(
        <FluentProvider theme={webLightTheme}>
            <AiSummaryCarousel {...props} />
        </FluentProvider>
    );
};

// Test document fixtures
const createDocument = (
    id: string,
    fileName: string,
    status: DocumentSummaryState['status'],
    summary?: string,
    error?: string
): DocumentSummaryState => ({
    documentId: id,
    fileName,
    status,
    summary,
    error
});

describe('AiSummaryCarousel', () => {
    describe('empty state', () => {
        it('should render nothing when documents array is empty', () => {
            renderWithProvider({ documents: [] });
            // Should not render any carousel or panel content
            expect(screen.queryByRole('region')).not.toBeInTheDocument();
            expect(screen.queryByText(/AI Summaries/)).not.toBeInTheDocument();
        });
    });

    describe('single document', () => {
        it('should render AiSummaryPanel directly for single document', () => {
            const documents = [
                createDocument('doc-1', 'single-file.pdf', 'complete', 'Summary text')
            ];
            renderWithProvider({ documents });

            // Should show the file name and summary
            expect(screen.getByText('single-file.pdf')).toBeInTheDocument();
            expect(screen.getByText(/Summary text/)).toBeInTheDocument();

            // Should NOT show carousel navigation
            expect(screen.queryByText(/of/)).not.toBeInTheDocument();
        });
    });

    describe('multiple documents - navigation', () => {
        const documents = [
            createDocument('doc-1', 'file-1.pdf', 'complete', 'Summary 1'),
            createDocument('doc-2', 'file-2.docx', 'streaming', 'Partial summary'),
            createDocument('doc-3', 'file-3.txt', 'pending')
        ];

        it('should show "1 of 3" for 3 documents', () => {
            renderWithProvider({ documents });
            expect(screen.getByText('1 of 3')).toBeInTheDocument();
        });

        it('should show first document initially', () => {
            renderWithProvider({ documents });
            expect(screen.getByText('file-1.pdf')).toBeInTheDocument();
        });

        it('should navigate to next document when right arrow clicked', () => {
            renderWithProvider({ documents });

            // Click next
            const nextButton = screen.getByLabelText('Next document');
            fireEvent.click(nextButton);

            // Should show second document
            expect(screen.getByText('file-2.docx')).toBeInTheDocument();
            expect(screen.getByText('2 of 3')).toBeInTheDocument();
        });

        it('should navigate to previous document when left arrow clicked', () => {
            renderWithProvider({ documents });

            // Navigate to second document first
            const nextButton = screen.getByLabelText('Next document');
            fireEvent.click(nextButton);
            expect(screen.getByText('2 of 3')).toBeInTheDocument();

            // Click previous
            const prevButton = screen.getByLabelText('Previous document');
            fireEvent.click(prevButton);

            // Should show first document again
            expect(screen.getByText('file-1.pdf')).toBeInTheDocument();
            expect(screen.getByText('1 of 3')).toBeInTheDocument();
        });

        it('should disable previous button on first document', () => {
            renderWithProvider({ documents });
            const prevButton = screen.getByLabelText('Previous document');
            expect(prevButton).toBeDisabled();
        });

        it('should disable next button on last document', () => {
            renderWithProvider({ documents });

            // Navigate to last document
            const nextButton = screen.getByLabelText('Next document');
            fireEvent.click(nextButton); // 2nd
            fireEvent.click(nextButton); // 3rd

            expect(screen.getByText('3 of 3')).toBeInTheDocument();
            expect(nextButton).toBeDisabled();
        });
    });

    describe('keyboard navigation', () => {
        const documents = [
            createDocument('doc-1', 'file-1.pdf', 'complete', 'Summary 1'),
            createDocument('doc-2', 'file-2.docx', 'complete', 'Summary 2'),
            createDocument('doc-3', 'file-3.txt', 'complete', 'Summary 3')
        ];

        it('should navigate with ArrowRight key', () => {
            renderWithProvider({ documents });

            const carousel = screen.getByRole('region', { name: /AI summaries for 3 documents/i });
            fireEvent.keyDown(carousel, { key: 'ArrowRight' });

            expect(screen.getByText('2 of 3')).toBeInTheDocument();
        });

        it('should navigate with ArrowLeft key', () => {
            renderWithProvider({ documents });

            const carousel = screen.getByRole('region', { name: /AI summaries for 3 documents/i });

            // Go to second document
            fireEvent.keyDown(carousel, { key: 'ArrowRight' });
            expect(screen.getByText('2 of 3')).toBeInTheDocument();

            // Go back to first
            fireEvent.keyDown(carousel, { key: 'ArrowLeft' });
            expect(screen.getByText('1 of 3')).toBeInTheDocument();
        });

        it('should not navigate past first document with ArrowLeft', () => {
            renderWithProvider({ documents });

            const carousel = screen.getByRole('region', { name: /AI summaries for 3 documents/i });
            fireEvent.keyDown(carousel, { key: 'ArrowLeft' });

            // Should still be on first
            expect(screen.getByText('1 of 3')).toBeInTheDocument();
        });

        it('should not navigate past last document with ArrowRight', () => {
            renderWithProvider({ documents });

            const carousel = screen.getByRole('region', { name: /AI summaries for 3 documents/i });
            fireEvent.keyDown(carousel, { key: 'ArrowRight' }); // 2
            fireEvent.keyDown(carousel, { key: 'ArrowRight' }); // 3
            fireEvent.keyDown(carousel, { key: 'ArrowRight' }); // Still 3

            expect(screen.getByText('3 of 3')).toBeInTheDocument();
        });
    });

    describe('aggregate status display', () => {
        it('should show complete count', () => {
            const documents = [
                createDocument('doc-1', 'file-1.pdf', 'complete', 'Done'),
                createDocument('doc-2', 'file-2.pdf', 'complete', 'Done'),
                createDocument('doc-3', 'file-3.pdf', 'pending')
            ];
            renderWithProvider({ documents });

            // Should show "2" badge for complete
            expect(screen.getByText('2')).toBeInTheDocument();
            expect(screen.getByText('complete')).toBeInTheDocument();
        });

        it('should show streaming count', () => {
            const documents = [
                createDocument('doc-1', 'file-1.pdf', 'streaming', 'Partial...'),
                createDocument('doc-2', 'file-2.pdf', 'streaming', 'Also partial...'),
                createDocument('doc-3', 'file-3.pdf', 'pending')
            ];
            renderWithProvider({ documents });

            expect(screen.getByText('2')).toBeInTheDocument();
            expect(screen.getByText(/generating/)).toBeInTheDocument();
        });

        it('should show pending count', () => {
            const documents = [
                createDocument('doc-1', 'file-1.pdf', 'complete', 'Done'),
                createDocument('doc-2', 'file-2.pdf', 'pending'),
                createDocument('doc-3', 'file-3.pdf', 'pending')
            ];
            renderWithProvider({ documents });

            expect(screen.getByText('pending')).toBeInTheDocument();
        });

        it('should show error count as failed', () => {
            const documents = [
                createDocument('doc-1', 'file-1.pdf', 'complete', 'Done'),
                createDocument('doc-2', 'file-2.pdf', 'error', undefined, 'Network error')
            ];
            renderWithProvider({ documents });

            expect(screen.getByText('failed')).toBeInTheDocument();
        });

        it('should show mixed statuses correctly', () => {
            const documents = [
                createDocument('doc-1', 'file-1.pdf', 'complete', 'Done'),
                createDocument('doc-2', 'file-2.pdf', 'complete', 'Done'),
                createDocument('doc-3', 'file-3.pdf', 'streaming', 'Partial'),
                createDocument('doc-4', 'file-4.pdf', 'pending'),
                createDocument('doc-5', 'file-5.pdf', 'error', undefined, 'Failed')
            ];
            renderWithProvider({ documents });

            expect(screen.getByText('complete')).toBeInTheDocument();
            expect(screen.getByText(/generating/)).toBeInTheDocument();
            expect(screen.getByText('pending')).toBeInTheDocument();
            expect(screen.getByText('failed')).toBeInTheDocument();
        });
    });

    describe('retry functionality', () => {
        it('should call onRetry with current document id', () => {
            const onRetry = jest.fn();
            const documents = [
                createDocument('doc-1', 'file-1.pdf', 'error', undefined, 'Failed'),
                createDocument('doc-2', 'file-2.pdf', 'complete', 'Done')
            ];
            renderWithProvider({ documents, onRetry });

            // First document has error, should show retry button
            const retryButton = screen.getByRole('button', { name: /retry/i });
            fireEvent.click(retryButton);

            expect(onRetry).toHaveBeenCalledWith('doc-1');
        });

        it('should call onRetry for correct document after navigation', () => {
            const onRetry = jest.fn();
            const documents = [
                createDocument('doc-1', 'file-1.pdf', 'complete', 'Done'),
                createDocument('doc-2', 'file-2.pdf', 'error', undefined, 'Network error')
            ];
            renderWithProvider({ documents, onRetry });

            // Navigate to second document (which has error)
            const nextButton = screen.getByLabelText('Next document');
            fireEvent.click(nextButton);

            // Retry the second document
            const retryButton = screen.getByRole('button', { name: /retry/i });
            fireEvent.click(retryButton);

            expect(onRetry).toHaveBeenCalledWith('doc-2');
        });
    });

    describe('accessibility', () => {
        const documents = [
            createDocument('doc-1', 'file-1.pdf', 'complete', 'Summary 1'),
            createDocument('doc-2', 'file-2.docx', 'streaming', 'Partial')
        ];

        it('should have carousel role description', () => {
            renderWithProvider({ documents });
            const carousel = screen.getByRole('region', { name: /AI summaries for 2 documents/i });
            expect(carousel).toHaveAttribute('aria-roledescription', 'carousel');
        });

        it('should have navigation group with label', () => {
            renderWithProvider({ documents });
            expect(screen.getByRole('group', { name: 'Carousel navigation' })).toBeInTheDocument();
        });

        it('should have aria-live region for page indicator', () => {
            renderWithProvider({ documents });
            const pageIndicator = screen.getByText('1 of 2');
            expect(pageIndicator).toHaveAttribute('aria-live', 'polite');
        });

        it('should have status region for aggregate status', () => {
            renderWithProvider({ documents });
            expect(screen.getByRole('status', { name: 'Summary status overview' })).toBeInTheDocument();
        });

        it('should be focusable with tabIndex', () => {
            renderWithProvider({ documents });
            const carousel = screen.getByRole('region', { name: /AI summaries for 2 documents/i });
            expect(carousel).toHaveAttribute('tabIndex', '0');
        });
    });

    describe('concurrent streaming indicator', () => {
        it('should show max concurrent note when more than max are streaming', () => {
            const documents = [
                createDocument('doc-1', 'file-1.pdf', 'streaming', 'Stream 1'),
                createDocument('doc-2', 'file-2.pdf', 'streaming', 'Stream 2'),
                createDocument('doc-3', 'file-3.pdf', 'streaming', 'Stream 3'),
                createDocument('doc-4', 'file-4.pdf', 'streaming', 'Stream 4'),
                createDocument('doc-5', 'file-5.pdf', 'pending')
            ];
            renderWithProvider({ documents, maxConcurrent: 3 });

            // Should show that max concurrent is 3
            expect(screen.getByText(/3 max/)).toBeInTheDocument();
        });
    });
});
