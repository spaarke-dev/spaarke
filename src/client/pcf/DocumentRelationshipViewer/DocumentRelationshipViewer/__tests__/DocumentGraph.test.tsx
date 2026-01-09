/**
 * DocumentGraph component tests
 *
 * Tests:
 * - Empty state rendering
 * - Hook calls and configuration
 * - Props handling
 *
 * Note: React Flow rendering is mocked to avoid memory issues in tests.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { DocumentNode, DocumentEdge } from '../types/graph';

// Mock react-flow-renderer to avoid heavy rendering
jest.mock('react-flow-renderer', () => ({
    __esModule: true,
    default: ({ children }: { children: React.ReactNode }) => (
        <div data-testid="react-flow-mock">{children}</div>
    ),
    Background: () => <div data-testid="background" />,
    Controls: () => <div data-testid="controls" />,
    MiniMap: () => <div data-testid="minimap" />,
    useNodesState: jest.fn((initial) => [initial, jest.fn(), jest.fn()]),
    useEdgesState: jest.fn((initial) => [initial, jest.fn(), jest.fn()]),
    BackgroundVariant: { Dots: 'dots' },
}));

// Mock the useForceLayout hook
jest.mock('../hooks/useForceLayout', () => ({
    useForceLayout: jest.fn(() => ({
        layoutNodes: [],
        layoutEdges: [],
        isSimulating: false,
        recalculate: jest.fn(),
    })),
}));

import { useForceLayout } from '../hooks/useForceLayout';
const mockUseForceLayout = useForceLayout as jest.MockedFunction<typeof useForceLayout>;

// Import component after mocks are set up
import { DocumentGraph } from '../components/DocumentGraph';

// Wrapper to provide Fluent UI context
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

// Sample test data
const createTestNode = (id: string, isSource = false): DocumentNode => ({
    id,
    type: 'document',
    position: { x: 0, y: 0 },
    data: {
        documentId: id,
        name: `Document ${id}.pdf`,
        fileType: 'pdf',
        size: 1024 * 1024,
        similarity: isSource ? 1.0 : 0.85,
        isSource,
        parentEntityName: 'Test Matter',
        fileUrl: `https://example.com/${id}.pdf`,
    },
});

const createTestEdge = (source: string, target: string): DocumentEdge => ({
    id: `${source}-${target}`,
    source,
    target,
    data: {
        similarity: 0.85,
    },
});

const sampleNodes: DocumentNode[] = [
    createTestNode('source-1', true),
    createTestNode('related-1', false),
    createTestNode('related-2', false),
];

const sampleEdges: DocumentEdge[] = [
    createTestEdge('source-1', 'related-1'),
    createTestEdge('source-1', 'related-2'),
];

describe('DocumentGraph', () => {
    beforeEach(() => {
        // Reset mock to default behavior
        mockUseForceLayout.mockReturnValue({
            layoutNodes: sampleNodes.map((n, i) => ({
                ...n,
                position: { x: i * 100, y: i * 50 },
            })),
            layoutEdges: sampleEdges,
            isSimulating: false,
            recalculate: jest.fn(),
        });
    });

    describe('Empty State', () => {
        it('renders empty state message when no nodes provided', () => {
            render(
                <TestWrapper>
                    <DocumentGraph nodes={[]} edges={[]} />
                </TestWrapper>
            );

            expect(screen.getByText('No document relationships to display')).toBeInTheDocument();
            expect(
                screen.getByText('Select a document to view its relationships')
            ).toBeInTheDocument();
        });

        it('does not render React Flow when nodes array is empty', () => {
            render(
                <TestWrapper>
                    <DocumentGraph nodes={[]} edges={[]} />
                </TestWrapper>
            );

            // React Flow mock should not be rendered
            expect(screen.queryByTestId('react-flow-mock')).not.toBeInTheDocument();
        });
    });

    describe('Graph Rendering', () => {
        it('renders React Flow when nodes are provided', () => {
            render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            // React Flow mock should be rendered
            expect(screen.getByTestId('react-flow-mock')).toBeInTheDocument();
        });

        it('calls useForceLayout with nodes and edges', () => {
            render(
                <TestWrapper>
                    <DocumentGraph
                        nodes={sampleNodes}
                        edges={sampleEdges}
                        width={800}
                        height={600}
                    />
                </TestWrapper>
            );

            expect(mockUseForceLayout).toHaveBeenCalledWith(
                sampleNodes,
                sampleEdges,
                expect.objectContaining({
                    centerX: 400,
                    centerY: 300,
                })
            );
        });

        it('uses default dimensions when not provided', () => {
            render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            expect(mockUseForceLayout).toHaveBeenCalledWith(
                sampleNodes,
                sampleEdges,
                expect.objectContaining({
                    centerX: 400, // 800 / 2
                    centerY: 300, // 600 / 2
                })
            );
        });
    });

    describe('Loading State', () => {
        it('shows loading spinner when simulation is in progress', () => {
            mockUseForceLayout.mockReturnValue({
                layoutNodes: sampleNodes,
                layoutEdges: sampleEdges,
                isSimulating: true,
                recalculate: jest.fn(),
            });

            render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            expect(screen.getByText('Calculating layout...')).toBeInTheDocument();
        });

        it('hides loading spinner when simulation completes', () => {
            mockUseForceLayout.mockReturnValue({
                layoutNodes: sampleNodes,
                layoutEdges: sampleEdges,
                isSimulating: false,
                recalculate: jest.fn(),
            });

            render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            expect(screen.queryByText('Calculating layout...')).not.toBeInTheDocument();
        });
    });

    describe('Layout Options', () => {
        it('passes custom layout options to useForceLayout', () => {
            const customOptions = {
                distanceMultiplier: 300,
                collisionRadius: 75,
                chargeStrength: -500,
            };

            render(
                <TestWrapper>
                    <DocumentGraph
                        nodes={sampleNodes}
                        edges={sampleEdges}
                        layoutOptions={customOptions}
                        width={1000}
                        height={800}
                    />
                </TestWrapper>
            );

            expect(mockUseForceLayout).toHaveBeenCalledWith(
                sampleNodes,
                sampleEdges,
                expect.objectContaining({
                    ...customOptions,
                    centerX: 500,
                    centerY: 400,
                })
            );
        });
    });

    describe('Subcomponents', () => {
        it('renders Background component', () => {
            render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            expect(screen.getByTestId('background')).toBeInTheDocument();
        });

        it('renders Controls component', () => {
            render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            expect(screen.getByTestId('controls')).toBeInTheDocument();
        });

        it('renders MiniMap component', () => {
            render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            expect(screen.getByTestId('minimap')).toBeInTheDocument();
        });
    });

    describe('Dark Mode', () => {
        it('accepts isDarkMode prop', () => {
            // Component should render without errors with dark mode enabled
            const { container } = render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} isDarkMode />
                </TestWrapper>
            );

            expect(container.firstChild).toBeInTheDocument();
        });
    });

    describe('Nodes and Edges Updates', () => {
        it('calls useForceLayout with updated nodes', () => {
            const { rerender } = render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            const newNodes = [...sampleNodes, createTestNode('related-3', false)];

            rerender(
                <TestWrapper>
                    <DocumentGraph nodes={newNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            // useForceLayout should be called with new nodes
            expect(mockUseForceLayout).toHaveBeenLastCalledWith(
                newNodes,
                sampleEdges,
                expect.any(Object)
            );
        });

        it('calls useForceLayout with updated edges', () => {
            const { rerender } = render(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={sampleEdges} />
                </TestWrapper>
            );

            const newEdges = [...sampleEdges, createTestEdge('related-1', 'related-2')];

            rerender(
                <TestWrapper>
                    <DocumentGraph nodes={sampleNodes} edges={newEdges} />
                </TestWrapper>
            );

            expect(mockUseForceLayout).toHaveBeenLastCalledWith(
                sampleNodes,
                newEdges,
                expect.any(Object)
            );
        });
    });
});
