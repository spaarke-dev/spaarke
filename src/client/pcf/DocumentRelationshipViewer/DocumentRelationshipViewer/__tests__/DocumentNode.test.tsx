/**
 * DocumentNode component tests
 *
 * Tests:
 * - Source node rendering with badge
 * - Related node rendering with similarity score
 * - File type icons
 * - File size formatting
 * - Orphan file display (Task 064)
 * - Compact mode rendering (Task 064)
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

    describe('Orphan File Display', () => {
        it('displays "File only" badge for orphan files', () => {
            const props = createNodeProps({
                isOrphanFile: true,
                documentId: undefined, // No linked Dataverse record
                similarity: 0.75,
            });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText('File only')).toBeInTheDocument();
        });

        it('shows both orphan badge and similarity for orphan files with score', () => {
            const props = createNodeProps({
                isOrphanFile: true,
                similarity: 0.80,
            });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText('File only')).toBeInTheDocument();
            expect(screen.getByText('80%')).toBeInTheDocument();
        });

        it('does not show orphan badge for regular related nodes', () => {
            const props = createNodeProps({
                isOrphanFile: false,
                similarity: 0.85,
            });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.queryByText('File only')).not.toBeInTheDocument();
            expect(screen.getByText('Similarity')).toBeInTheDocument();
        });

        it('shows file type for orphan files', () => {
            const props = createNodeProps({
                isOrphanFile: true,
                fileType: 'pdf',
            });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(/PDF/)).toBeInTheDocument();
        });
    });

    describe('Compact Mode', () => {
        it('renders compact mode with icon only', () => {
            const props = createNodeProps({
                compactMode: true,
                name: 'Test Document.pdf',
            });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            // In compact mode, document name should NOT be rendered as text content
            // (it's only in the tooltip/title attribute)
            expect(screen.queryByText('Test Document.pdf')).not.toBeInTheDocument();
        });

        it('includes document name in compact mode tooltip', () => {
            const props = createNodeProps({
                compactMode: true,
                name: 'Test Document.pdf',
                similarity: 0.85,
            });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            // Check the tooltip via title attribute
            const iconContainer = document.querySelector('[title*="Test Document.pdf"]');
            expect(iconContainer).toBeInTheDocument();
        });

        it('includes "File only" in compact mode tooltip for orphan files', () => {
            const props = createNodeProps({
                compactMode: true,
                name: 'Orphan File.pdf',
                isOrphanFile: true,
            });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            // Check the tooltip includes "(File only)" for orphan files
            const iconContainer = document.querySelector('[title*="File only"]');
            expect(iconContainer).toBeInTheDocument();
        });
    });

    describe('Extended File Type Icons', () => {
        it.each([
            ['pptx', 'PPTX'],
            ['ppt', 'PPT'],
            ['msg', 'MSG'],
            ['eml', 'EML'],
            ['html', 'HTML'],
            ['xml', 'XML'],
            ['zip', 'ZIP'],
            ['rar', 'RAR'],
            ['mp4', 'MP4'],
            ['csv', 'CSV'],
        ])('renders %s file type correctly', (fileType, expectedLabel) => {
            const props = createNodeProps({ fileType });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(new RegExp(expectedLabel))).toBeInTheDocument();
        });

        it('renders unknown file type with question mark icon', () => {
            const props = createNodeProps({ fileType: 'unknown' });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(/UNKNOWN/)).toBeInTheDocument();
        });

        it('renders file type with default icon', () => {
            const props = createNodeProps({ fileType: 'file' });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            expect(screen.getByText(/FILE/)).toBeInTheDocument();
        });
    });

    describe('Default Values', () => {
        it('uses default fileType when undefined', () => {
            const props = createNodeProps({ fileType: undefined });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            // Should show UNKNOWN when fileType is undefined
            expect(screen.getByText(/UNKNOWN/)).toBeInTheDocument();
        });

        it('shows zero similarity correctly', () => {
            const props = createNodeProps({ similarity: 0 });

            render(
                <TestWrapper>
                    <DocumentNode {...props} />
                </TestWrapper>
            );

            // When similarity is 0, footer should not be shown for related nodes
            expect(screen.queryByText('Similarity')).not.toBeInTheDocument();
        });
    });
});
