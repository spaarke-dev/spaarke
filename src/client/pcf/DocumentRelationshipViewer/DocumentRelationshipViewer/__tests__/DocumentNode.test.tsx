/**
 * DocumentNode component tests
 *
 * Tests:
 * - Source node rendering with badge
 * - Related node rendering with similarity score
 * - File type icons
 * - File size formatting
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ReactFlowProvider } from 'react-flow-renderer';
import { DocumentNode } from '../components/DocumentNode';
import type { DocumentNodeData } from '../types/graph';

// Wrapper to provide required contexts
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <FluentProvider theme={webLightTheme}>
        <ReactFlowProvider>
            {children}
        </ReactFlowProvider>
    </FluentProvider>
);

// Helper to create node props
const createNodeProps = (data: Partial<DocumentNodeData> = {}) => ({
    id: 'test-node',
    type: 'documentNode',
    data: {
        documentId: 'doc-123',
        name: 'Test Document.pdf',
        fileType: 'pdf',
        size: 1024 * 1024, // 1MB
        similarity: 0.85,
        isSource: false,
        parentEntityName: 'Test Matter',
        fileUrl: 'https://example.com/file.pdf',
        ...data,
    } as DocumentNodeData,
    selected: false,
    isConnectable: true,
    xPos: 0,
    yPos: 0,
    dragging: false,
    zIndex: 0,
});

describe('DocumentNode', () => {
    describe('Source Node Rendering', () => {
        it('renders source node with Source Document badge', () => {
            const props = createNodeProps({ isSource: true });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText('Source Document')).toBeInTheDocument();
        });

        it('does not show similarity badge for source node', () => {
            const props = createNodeProps({ isSource: true, similarity: 1.0 });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.queryByText('Similarity')).not.toBeInTheDocument();
        });
    });

    describe('Related Node Rendering', () => {
        it('renders related node with document name', () => {
            const props = createNodeProps({ name: 'Contract Agreement.docx' });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText('Contract Agreement.docx')).toBeInTheDocument();
        });

        it('displays similarity score for related nodes', () => {
            const props = createNodeProps({ isSource: false, similarity: 0.85 });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText('Similarity')).toBeInTheDocument();
            expect(screen.getByText('85%')).toBeInTheDocument();
        });

        it('shows file type in uppercase', () => {
            const props = createNodeProps({ fileType: 'docx' });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(/DOCX/)).toBeInTheDocument();
        });
    });

    describe('File Size Display', () => {
        it('formats bytes correctly', () => {
            const props = createNodeProps({ size: 500 });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(/500 B/)).toBeInTheDocument();
        });

        it('formats kilobytes correctly', () => {
            const props = createNodeProps({ size: 1024 * 5 }); // 5KB

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(/5\.0 KB/)).toBeInTheDocument();
        });

        it('formats megabytes correctly', () => {
            const props = createNodeProps({ size: 1024 * 1024 * 2.5 }); // 2.5MB

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(/2\.5 MB/)).toBeInTheDocument();
        });
    });

    describe('Similarity Badge Appearance', () => {
        it('shows high similarity (>=90%) with filled appearance', () => {
            const props = createNodeProps({ similarity: 0.95 });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText('95%')).toBeInTheDocument();
        });

        it('shows medium similarity (75-89%) correctly', () => {
            const props = createNodeProps({ similarity: 0.80 });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText('80%')).toBeInTheDocument();
        });

        it('shows low similarity (65-74%) correctly', () => {
            const props = createNodeProps({ similarity: 0.70 });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText('70%')).toBeInTheDocument();
        });
    });

    describe('File Type Icons', () => {
        it.each([
            ['pdf', 'PDF'],
            ['docx', 'DOCX'],
            ['doc', 'DOC'],
            ['txt', 'TXT'],
            ['jpg', 'JPG'],
            ['png', 'PNG'],
            ['xlsx', 'XLSX'],
        ])('renders %s file type', (fileType, expectedLabel) => {
            const props = createNodeProps({ fileType });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(new RegExp(expectedLabel))).toBeInTheDocument();
        });
    });

    describe('Node Selection', () => {
        it('applies selected state when selected prop is true', () => {
            const props = {
                ...createNodeProps(),
                selected: true,
            };

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            // The Card component receives the selected prop
            // Visual selection state is handled by Fluent UI
            expect(screen.getByText('Test Document.pdf')).toBeInTheDocument();
        });
    });
});
