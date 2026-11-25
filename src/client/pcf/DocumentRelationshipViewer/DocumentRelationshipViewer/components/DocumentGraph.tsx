/**
 * DocumentGraph - React Flow canvas with d3-force layout
 *
 * This component renders the document relationship graph using React Flow
 * with node positions from the shared synchronous `useForceSimulation` (the same
 * layout the code page uses, #3093): positions exist before the first render, so
 * there is no "Calculating layout" pass and `fitView` frames the final layout.
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 compatible APIs (using react-flow-renderer v10)
 */

import * as React from 'react';
import ReactFlow, {
  Background,
  Controls,
  MiniMap,
  useNodesState,
  useEdgesState,
  BackgroundVariant,
  Node,
  Edge,
  NodeTypes,
  EdgeTypes,
} from 'react-flow-renderer';
import 'react-flow-renderer/dist/style.css';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { useForceSimulation } from '@spaarke/ui-components/dist/hooks/useForceSimulation';
import type { ForceNode, ForceEdge } from '@spaarke/ui-components/dist/hooks/useForceSimulation';
import type {
  DocumentGraphNode as DocumentNode,
  DocumentGraphEdge as DocumentEdge,
  DocumentNodeData,
  ForceLayoutOptions,
} from '@spaarke/ui-components/dist/components/DocumentRelationshipGraph';
import { DocumentNode as DocumentNodeComponent } from './DocumentNode';
import { DocumentEdge as DocumentEdgeComponent } from './DocumentEdge';

/**
 * Props for DocumentGraph component
 */
export interface DocumentGraphProps {
  /** Nodes from API response */
  nodes: DocumentNode[];
  /** Edges from API response */
  edges: DocumentEdge[];
  /** Whether dark mode is enabled */
  isDarkMode?: boolean;
  /** Callback when node is selected */
  onNodeSelect?: (node: DocumentNode) => void;
  /** Layout options */
  layoutOptions?: ForceLayoutOptions;
  /** Container width */
  width?: number;
  /** Container height */
  height?: number;
  /** Whether to show minimap (default: false) */
  showMinimap?: boolean;
  /** Compact mode for icon-only display in fieldBound mode */
  compactMode?: boolean;
}

/**
 * Styles using Fluent UI design tokens (ADR-021 compliant)
 */
const useStyles = makeStyles({
  container: {
    width: '100%',
    height: '100%',
    minHeight: '300px',
    position: 'relative',
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    color: tokens.colorNeutralForeground3,
    gap: tokens.spacingVerticalM,
  },
});

/**
 * Node types registry for React Flow
 * Uses custom DocumentNode component from Task 012
 */
const nodeTypes: NodeTypes = {
  document: DocumentNodeComponent as unknown as NodeTypes['default'],
};

/**
 * Edge types registry for React Flow
 * Uses custom DocumentEdge component from Task 013
 */
const edgeTypes: EdgeTypes = {
  similarity: DocumentEdgeComponent as unknown as EdgeTypes['default'],
};

/**
 * DocumentGraph component - Renders document relationship graph
 */
export const DocumentGraph: React.FC<DocumentGraphProps> = ({
  nodes: inputNodes,
  edges: inputEdges,
  isDarkMode = false,
  onNodeSelect,
  layoutOptions,
  showMinimap = false,
  compactMode = false,
}) => {
  const styles = useStyles();

  // Force-layout inputs (similarity → link weight). The source node is the pinned hub.
  const forceNodes = React.useMemo<ForceNode[]>(
    () => inputNodes.map(n => ({ id: n.id, label: n.data.name, isSource: n.data.isSource })),
    [inputNodes]
  );
  const forceEdges = React.useMemo<ForceEdge[]>(
    () => inputEdges.map(e => ({ source: e.source, target: e.target, weight: e.data?.similarity ?? 0.5 })),
    [inputEdges]
  );

  // Centre on (0,0) - React Flow's fitView handles the viewport (a container-centred layout
  // misplaced the graph on wide viewports).
  const layout = useForceSimulation(forceNodes, forceEdges, {
    mode: 'hub-spoke',
    chargeStrength: layoutOptions?.chargeStrength ?? -1000,
    linkDistanceMultiplier: layoutOptions?.distanceMultiplier ?? 400,
    collisionRadius: layoutOptions?.collisionRadius ?? 100,
    center: { x: layoutOptions?.centerX ?? 0, y: layoutOptions?.centerY ?? 0 },
  });

  // Positioned nodes for React Flow, with compactMode on the node data
  const layoutNodes = React.useMemo<Node<DocumentNodeData>[]>(() => {
    const positions = new Map(layout.nodes.map(pn => [pn.id, pn]));
    return inputNodes.map(node => {
      const pos = positions.get(node.id);
      return {
        ...node,
        position: { x: pos?.x ?? 0, y: pos?.y ?? 0 },
        data: { ...node.data, compactMode },
      };
    });
  }, [inputNodes, layout.nodes, compactMode]);

  const [nodes, setNodes, onNodesChange] = useNodesState(layoutNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(inputEdges as Edge[]);

  // Update nodes when layout changes or compact mode changes
  React.useEffect(() => {
    setNodes(layoutNodes);
  }, [layoutNodes, setNodes]);

  // Update edges when the input changes
  React.useEffect(() => {
    setEdges(inputEdges as Edge[]);
  }, [inputEdges, setEdges]);

  // Handle node click
  const handleNodeClick = React.useCallback(
    (_event: React.MouseEvent, node: Node) => {
      if (onNodeSelect) {
        onNodeSelect(node as DocumentNode);
      }
    },
    [onNodeSelect]
  );

  // Empty state
  if (inputNodes.length === 0) {
    return (
      <div className={styles.container}>
        <div className={styles.emptyState}>
          <Text size={400}>No document relationships to display</Text>
          <Text size={200}>Select a document to view its relationships</Text>
        </div>
      </div>
    );
  }

  // Apply custom edge type for similarity-based styling (Task 013)
  const typedEdges = edges.map(edge => ({
    ...edge,
    type: 'similarity', // Use custom DocumentEdge component
  }));

  return (
    <div className={styles.container}>
      <ReactFlow
        nodes={nodes}
        edges={typedEdges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onNodeClick={handleNodeClick}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        fitView
        fitViewOptions={{
          padding: 0.2,
          maxZoom: 1.5,
          minZoom: 0.3,
        }}
        minZoom={0.1}
        maxZoom={2}
        connectOnClick={false}
        attributionPosition="bottom-left"
        style={{ width: '100%', height: '100%' }}
      >
        <Background
          variant={BackgroundVariant.Dots}
          gap={20}
          size={1}
          color={isDarkMode ? tokens.colorNeutralStroke2 : tokens.colorNeutralStroke2}
        />
        <Controls showZoom showFitView showInteractive={false} />
        {showMinimap && (
          <MiniMap
            nodeColor={(node: Node<DocumentNodeData>) => {
              return node.data?.isSource ? tokens.colorBrandBackground : tokens.colorNeutralBackground3;
            }}
            maskColor={isDarkMode ? tokens.colorNeutralBackgroundAlpha2 : tokens.colorNeutralBackgroundAlpha}
            style={{
              backgroundColor: isDarkMode ? tokens.colorNeutralBackground2 : tokens.colorNeutralBackground1,
            }}
          />
        )}
      </ReactFlow>
    </div>
  );
};

export default DocumentGraph;
