/**
 * DocumentGraph component tests
 *
 * Tests:
 * - Empty state rendering
 * - The shared layout hook's inputs and options (#3093: the PCF uses the code page's synchronous
 *   `useForceSimulation`, so positions exist on the first render and there is no layout spinner)
 * - Props handling
 *
 * Note: React Flow rendering is mocked to avoid memory issues in tests.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type {
  DocumentGraphNode as DocumentNode,
  DocumentGraphEdge as DocumentEdge,
} from '@spaarke/ui-components/dist/components/DocumentRelationshipGraph';

interface MockFlowNode {
  id: string;
  position: { x: number; y: number };
  data: { compactMode?: boolean };
}

// Mock react-flow-renderer to avoid heavy rendering; expose the nodes React Flow receives.
jest.mock('react-flow-renderer', () => ({
  __esModule: true,
  default: ({ children, nodes }: { children: React.ReactNode; nodes: MockFlowNode[] }) => (
    <div data-testid="react-flow-mock">
      {nodes.map(n => (
        <span
          key={n.id}
          data-testid={`node-${n.id}`}
          data-x={n.position.x}
          data-y={n.position.y}
          data-compact={String(!!n.data.compactMode)}
        />
      ))}
      {children}
    </div>
  ),
  Background: () => <div data-testid="background" />,
  Controls: () => <div data-testid="controls" />,
  MiniMap: () => <div data-testid="minimap" />,
  Handle: () => null,
  Position: { Left: 'left', Right: 'right' },
  useNodesState: jest.fn((initial: unknown[]) => [initial, jest.fn(), jest.fn()]),
  useEdgesState: jest.fn((initial: unknown[]) => [initial, jest.fn(), jest.fn()]),
  BackgroundVariant: { Dots: 'dots' },
}));

// Mock the shared layout hook (d3-force is covered by the shared library's own tests).
jest.mock('@spaarke/ui-components/dist/hooks/useForceSimulation', () => ({
  useForceSimulation: jest.fn(),
}));

import { useForceSimulation } from '@spaarke/ui-components/dist/hooks/useForceSimulation';
const mockUseForceSimulation = useForceSimulation as jest.MockedFunction<typeof useForceSimulation>;

// Import component after mocks are set up
import { DocumentGraph } from '../components/DocumentGraph';

const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

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

const createTestEdge = (source: string, target: string, similarity = 0.85): DocumentEdge => ({
  id: `${source}-${target}`,
  source,
  target,
  data: { similarity, relationshipType: 'semantic', relationshipLabel: 'Semantic' },
});

const sampleNodes: DocumentNode[] = [
  createTestNode('source-1', true),
  createTestNode('related-1', false),
  createTestNode('related-2', false),
];

const sampleEdges: DocumentEdge[] = [
  createTestEdge('source-1', 'related-1'),
  createTestEdge('source-1', 'related-2', 0.6),
];

const positioned = (nodes: DocumentNode[]) => ({
  nodes: nodes.map((n, i) => ({
    id: n.id,
    label: n.data.name,
    isSource: n.data.isSource,
    x: i * 100 + 10,
    y: i * 50 + 5,
  })),
  edges: [],
});

describe('DocumentGraph', () => {
  beforeEach(() => {
    mockUseForceSimulation.mockReset();
    mockUseForceSimulation.mockImplementation(
      nodes =>
        positioned(sampleNodes.filter(s => nodes.some(n => n.id === s.id))) as unknown as ReturnType<
          typeof useForceSimulation
        >
    );
  });

  const renderGraph = (props: Partial<React.ComponentProps<typeof DocumentGraph>> = {}) =>
    render(
      <TestWrapper>
        <DocumentGraph nodes={sampleNodes} edges={sampleEdges} {...props} />
      </TestWrapper>
    );

  describe('Empty State', () => {
    it('renders empty state message when no nodes provided', () => {
      renderGraph({ nodes: [], edges: [] });

      expect(screen.getByText('No document relationships to display')).toBeInTheDocument();
      expect(screen.getByText('Select a document to view its relationships')).toBeInTheDocument();
      expect(screen.queryByTestId('react-flow-mock')).not.toBeInTheDocument();
    });
  });

  describe('Layout', () => {
    it('gives React Flow the laid-out positions on the first render, with no layout spinner', () => {
      renderGraph();

      expect(screen.getByTestId('node-source-1')).toHaveAttribute('data-x', '10');
      expect(screen.getByTestId('node-related-2')).toHaveAttribute('data-x', '210');
      expect(screen.getByTestId('node-related-2')).toHaveAttribute('data-y', '105');
      expect(screen.queryByText('Calculating layout...')).not.toBeInTheDocument();
    });

    it('feeds the hub-spoke layout the nodes (source pinned) and similarity-weighted links, centred on the origin', () => {
      renderGraph({ width: 1000, height: 800 });

      expect(mockUseForceSimulation).toHaveBeenCalledWith(
        [
          { id: 'source-1', label: 'Document source-1.pdf', isSource: true },
          { id: 'related-1', label: 'Document related-1.pdf', isSource: false },
          { id: 'related-2', label: 'Document related-2.pdf', isSource: false },
        ],
        [
          { source: 'source-1', target: 'related-1', weight: 0.85 },
          { source: 'source-1', target: 'related-2', weight: 0.6 },
        ],
        // width/height do not move the centre: fitView positions the viewport.
        {
          mode: 'hub-spoke',
          chargeStrength: -1000,
          linkDistanceMultiplier: 400,
          collisionRadius: 100,
          center: { x: 0, y: 0 },
        }
      );
    });

    it('passes custom layout options through', () => {
      renderGraph({ layoutOptions: { distanceMultiplier: 300, collisionRadius: 75, chargeStrength: -500 } });

      expect(mockUseForceSimulation).toHaveBeenLastCalledWith(expect.any(Array), expect.any(Array), {
        mode: 'hub-spoke',
        chargeStrength: -500,
        linkDistanceMultiplier: 300,
        collisionRadius: 75,
        center: { x: 0, y: 0 },
      });
    });

    it('stamps compactMode onto every node', () => {
      renderGraph({ compactMode: true });
      expect(screen.getByTestId('node-related-1')).toHaveAttribute('data-compact', 'true');
    });

    it('lays out again with the new nodes', () => {
      const { rerender } = renderGraph();
      const newNodes = [...sampleNodes, createTestNode('related-3', false)];

      rerender(
        <TestWrapper>
          <DocumentGraph nodes={newNodes} edges={sampleEdges} />
        </TestWrapper>
      );

      expect(mockUseForceSimulation.mock.calls[mockUseForceSimulation.mock.calls.length - 1][0]).toHaveLength(4);
    });
  });

  describe('Subcomponents', () => {
    it('renders Background and Controls, and no MiniMap by default', () => {
      renderGraph();

      expect(screen.getByTestId('background')).toBeInTheDocument();
      expect(screen.getByTestId('controls')).toBeInTheDocument();
      expect(screen.queryByTestId('minimap')).not.toBeInTheDocument();
    });

    it('renders MiniMap component when showMinimap is true', () => {
      renderGraph({ showMinimap: true });
      expect(screen.getByTestId('minimap')).toBeInTheDocument();
    });
  });

  describe('Dark Mode', () => {
    it('accepts isDarkMode prop', () => {
      const { container } = renderGraph({ isDarkMode: true });
      expect(container.firstChild).toBeInTheDocument();
    });
  });
});
