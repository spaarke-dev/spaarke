import { useCallback, useRef, DragEvent, CSSProperties } from 'react';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  BackgroundVariant,
  ReactFlowInstance,
  Node,
  Edge,
} from '@xyflow/react';
import type { PlaybookNode } from '../../stores';
import { makeStyles, tokens } from '@fluentui/react-components';
import { useCanvasStore, PlaybookNodeData, PlaybookNodeType } from '../../stores';
import { nodeTypes } from '../Nodes';
import '@xyflow/react/dist/style.css';

const useStyles = makeStyles({
  container: {
    width: '100%',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// CSS custom properties for React Flow theming (applied via style attribute)
const reactFlowStyle: CSSProperties = {
  // @ts-expect-error CSS custom properties
  '--xy-background-color': tokens.colorNeutralBackground1,
  '--xy-minimap-background': tokens.colorNeutralBackground2,
  '--xy-controls-background': tokens.colorNeutralBackground2,
  '--xy-controls-border-color': tokens.colorNeutralStroke1,
};

// Node colors based on type for minimap
const nodeColorByType: Record<PlaybookNodeType, string> = {
  aiAnalysis: '#0078D4',     // Blue - AI operations
  aiCompletion: '#0078D4',
  condition: '#FFB900',       // Yellow - Control flow
  deliverOutput: '#107C10',   // Green - Delivery
  createTask: '#8764B8',      // Purple - Integration
  sendEmail: '#8764B8',
  wait: '#E3008C',           // Magenta - Control flow
};

/**
 * Main canvas component using React Flow.
 * Supports node placement, edge connections, zoom/pan, and drag-drop.
 */
export function Canvas() {
  const styles = useStyles();
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const reactFlowInstance = useRef<ReactFlowInstance<PlaybookNode, Edge> | null>(null);

  // Get state and actions from store
  const {
    nodes,
    edges,
    onNodesChange,
    onEdgesChange,
    onConnect,
    selectNode,
    onDrop,
  } = useCanvasStore();

  // Handle React Flow initialization
  const onInit = useCallback((instance: ReactFlowInstance<PlaybookNode, Edge>) => {
    reactFlowInstance.current = instance;
  }, []);

  // Handle node selection
  const onNodeClick = useCallback(
    (_event: React.MouseEvent, node: Node) => {
      selectNode(node.id);
    },
    [selectNode]
  );

  // Handle click on canvas (deselect)
  const onPaneClick = useCallback(() => {
    selectNode(null);
  }, [selectNode]);

  // Handle drag over for drop target
  const onDragOver = useCallback((event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
  }, []);

  // Handle drop from palette
  const handleDrop = useCallback(
    (event: DragEvent<HTMLDivElement>) => {
      event.preventDefault();

      if (!reactFlowWrapper.current || !reactFlowInstance.current) return;

      // Get node data from drag event
      const nodeTypeData = event.dataTransfer.getData('application/reactflow');
      if (!nodeTypeData) return;

      try {
        const { type, label } = JSON.parse(nodeTypeData) as {
          type: PlaybookNodeType;
          label: string;
        };

        // Calculate drop position in flow coordinates
        const bounds = reactFlowWrapper.current.getBoundingClientRect();
        const position = reactFlowInstance.current.screenToFlowPosition({
          x: event.clientX - bounds.left,
          y: event.clientY - bounds.top,
        });

        onDrop(position, type, label);
      } catch (e) {
        console.error('Failed to parse dropped node data:', e);
      }
    },
    [onDrop]
  );

  // MiniMap node color function
  const getMinimapNodeColor = useCallback((node: Node): string => {
    const nodeData = node.data as PlaybookNodeData;
    return nodeColorByType[nodeData.type] || tokens.colorNeutralForeground3;
  }, []);

  return (
    <div ref={reactFlowWrapper} className={styles.container}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onInit={onInit}
        onNodeClick={onNodeClick}
        onPaneClick={onPaneClick}
        onDragOver={onDragOver}
        onDrop={handleDrop}
        fitView
        snapToGrid
        snapGrid={[16, 16]}
        defaultEdgeOptions={{
          type: 'smoothstep',
          animated: true,
        }}
        style={reactFlowStyle}
        proOptions={{ hideAttribution: true }}
      >
        <Background
          variant={BackgroundVariant.Dots}
          gap={16}
          size={1}
          color={tokens.colorNeutralStroke2}
        />
        <Controls
          showZoom
          showFitView
          showInteractive
          position="bottom-left"
        />
        <MiniMap
          nodeColor={getMinimapNodeColor}
          nodeStrokeWidth={2}
          zoomable
          pannable
          position="bottom-right"
        />
      </ReactFlow>
    </div>
  );
}
