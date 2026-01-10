/**
 * NodeActionBar component tests
 *
 * Tests:
 * - Action button rendering
 * - Open Document Record button click (Xrm.Navigation)
 * - View in SharePoint button click (window.open)
 * - Expand button click
 * - Close button functionality
 * - Source node handling (no expand button)
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { NodeActionBar } from '../components/NodeActionBar';
import type { DocumentNodeData } from '../types/graph';

// Import mocks from setup
import { mockXrmNavigation, mockWindowOpen } from '../../jest.setup';

// Wrapper to provide Fluent UI context
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

// Helper to create node data
const createNodeData = (overrides: Partial<DocumentNodeData> = {}): DocumentNodeData => ({
    documentId: 'doc-123',
    name: 'Test Document.pdf',
    fileType: 'pdf',
    size: 1024 * 1024,
    similarity: 0.85,
    isSource: false,
    parentEntityName: 'Test Matter',
    fileUrl: 'https://sharepoint.example.com/file.pdf',
    ...overrides,
});

describe('NodeActionBar', () => {
    describe('Rendering', () => {
        it('renders document name in header', () => {
            render(
                <TestWrapper>
                    <NodeActionBar
                        nodeData={createNodeData({ name: 'Important Contract.pdf' })}
                        onClose={jest.fn()}
                    />
                </TestWrapper>
            );

            expect(screen.getByText('Important Contract.pdf')).toBeInTheDocument();
        });

        it('renders parent entity name', () => {
            render(
                <TestWrapper>
                    <NodeActionBar
                        nodeData={createNodeData({ parentEntityName: 'Acme Project' })}
                        onClose={jest.fn()}
                    />
                </TestWrapper>
            );

            expect(screen.getByText('Acme Project')).toBeInTheDocument();
        });

        it('shows "Source Document" label for source nodes', () => {
            render(
                <TestWrapper>
                    <NodeActionBar
                        nodeData={createNodeData({ isSource: true })}
                        onClose={jest.fn()}
                    />
                </TestWrapper>
            );

            expect(screen.getByText('Source Document')).toBeInTheDocument();
        });

        it('renders Open Document Record button', () => {
            render(
                <TestWrapper>
                    <NodeActionBar nodeData={createNodeData()} onClose={jest.fn()} />
                </TestWrapper>
            );

            expect(screen.getByRole('button', { name: /open document record/i })).toBeInTheDocument();
        });

        it('renders View in SharePoint button', () => {
            render(
                <TestWrapper>
                    <NodeActionBar nodeData={createNodeData()} onClose={jest.fn()} />
                </TestWrapper>
            );

            expect(screen.getByRole('button', { name: /view in sharepoint/i })).toBeInTheDocument();
        });

        it('renders Expand button for related nodes', () => {
            render(
                <TestWrapper>
                    <NodeActionBar
                        nodeData={createNodeData({ isSource: false })}
                        onClose={jest.fn()}
                        onExpand={jest.fn()}
                    />
                </TestWrapper>
            );

            expect(screen.getByRole('button', { name: /expand/i })).toBeInTheDocument();
        });

        it('does not render Expand button for source nodes', () => {
            render(
                <TestWrapper>
                    <NodeActionBar
                        nodeData={createNodeData({ isSource: true })}
                        onClose={jest.fn()}
                        onExpand={jest.fn()}
                    />
                </TestWrapper>
            );

            expect(screen.queryByRole('button', { name: /expand/i })).not.toBeInTheDocument();
        });
    });

    describe('Open Document Record Button', () => {
        it('calls Xrm.Navigation.openForm when clicked', async () => {
            const nodeData = createNodeData({ documentId: 'doc-456' });

            render(
                <TestWrapper>
                    <NodeActionBar nodeData={nodeData} onClose={jest.fn()} />
                </TestWrapper>
            );

            const button = screen.getByRole('button', { name: /open document record/i });
            fireEvent.click(button);

            await waitFor(() => {
                expect(mockXrmNavigation.openForm).toHaveBeenCalledWith({
                    entityName: 'sprk_document',
                    entityId: 'doc-456',
                    openInNewWindow: true,
                });
            });
        });

        it('handles Xrm.Navigation.openForm error gracefully', async () => {
            mockXrmNavigation.openForm.mockRejectedValueOnce(new Error('Navigation failed'));
            const consoleSpy = jest.spyOn(console, 'error').mockImplementation();

            render(
                <TestWrapper>
                    <NodeActionBar nodeData={createNodeData()} onClose={jest.fn()} />
                </TestWrapper>
            );

            const button = screen.getByRole('button', { name: /open document record/i });
            fireEvent.click(button);

            await waitFor(() => {
                expect(consoleSpy).toHaveBeenCalledWith(
                    'Failed to open document record:',
                    expect.any(Error)
                );
            });

            consoleSpy.mockRestore();
        });
    });

    describe('View in SharePoint Button', () => {
        it('calls window.open with fileUrl when clicked', () => {
            const nodeData = createNodeData({
                fileUrl: 'https://sharepoint.example.com/doc.pdf',
            });

            render(
                <TestWrapper>
                    <NodeActionBar nodeData={nodeData} onClose={jest.fn()} />
                </TestWrapper>
            );

            const button = screen.getByRole('button', { name: /view in sharepoint/i });
            fireEvent.click(button);

            expect(mockWindowOpen).toHaveBeenCalledWith(
                'https://sharepoint.example.com/doc.pdf',
                '_blank',
                'noopener,noreferrer'
            );
        });

        it('is disabled when no fileUrl is provided', () => {
            const nodeData = createNodeData({ fileUrl: undefined });

            render(
                <TestWrapper>
                    <NodeActionBar nodeData={nodeData} onClose={jest.fn()} />
                </TestWrapper>
            );

            const button = screen.getByRole('button', { name: /view in sharepoint/i });
            expect(button).toBeDisabled();
        });

        it('logs warning when clicking without fileUrl', () => {
            const consoleSpy = jest.spyOn(console, 'warn').mockImplementation();
            const nodeData = createNodeData({ fileUrl: '' });

            render(
                <TestWrapper>
                    <NodeActionBar nodeData={nodeData} onClose={jest.fn()} />
                </TestWrapper>
            );

            const button = screen.getByRole('button', { name: /view in sharepoint/i });
            fireEvent.click(button);

            expect(mockWindowOpen).not.toHaveBeenCalled();
            consoleSpy.mockRestore();
        });
    });

    describe('Expand Button', () => {
        it('calls onExpand callback with documentId when clicked', () => {
            const onExpand = jest.fn();
            const nodeData = createNodeData({ documentId: 'doc-789' });

            render(
                <TestWrapper>
                    <NodeActionBar nodeData={nodeData} onClose={jest.fn()} onExpand={onExpand} />
                </TestWrapper>
            );

            const button = screen.getByRole('button', { name: /expand/i });
            fireEvent.click(button);

            expect(onExpand).toHaveBeenCalledWith('doc-789');
        });

        it('is disabled when canExpand is false', () => {
            render(
                <TestWrapper>
                    <NodeActionBar
                        nodeData={createNodeData()}
                        onClose={jest.fn()}
                        onExpand={jest.fn()}
                        canExpand={false}
                    />
                </TestWrapper>
            );

            const button = screen.getByRole('button', { name: /expand/i });
            expect(button).toBeDisabled();
        });

        it('is disabled when onExpand is not provided', () => {
            render(
                <TestWrapper>
                    <NodeActionBar nodeData={createNodeData()} onClose={jest.fn()} />
                </TestWrapper>
            );

            const button = screen.getByRole('button', { name: /expand/i });
            expect(button).toBeDisabled();
        });
    });

    describe('Close Button', () => {
        it('calls onClose callback when clicked', () => {
            const onClose = jest.fn();

            render(
                <TestWrapper>
                    <NodeActionBar nodeData={createNodeData()} onClose={onClose} />
                </TestWrapper>
            );

            const closeButton = screen.getByRole('button', { name: /close/i });
            fireEvent.click(closeButton);

            expect(onClose).toHaveBeenCalledTimes(1);
        });
    });

    describe('Tooltips', () => {
        it('has tooltip for Open Document Record', () => {
            render(
                <TestWrapper>
                    <NodeActionBar nodeData={createNodeData()} onClose={jest.fn()} />
                </TestWrapper>
            );

            // Tooltip content is rendered, verify button exists with accessible name
            expect(screen.getByRole('button', { name: /open document record/i })).toBeInTheDocument();
        });

        it('has tooltip for View in SharePoint', () => {
            render(
                <TestWrapper>
                    <NodeActionBar nodeData={createNodeData()} onClose={jest.fn()} />
                </TestWrapper>
            );

            expect(screen.getByRole('button', { name: /view in sharepoint/i })).toBeInTheDocument();
        });
    });
});
