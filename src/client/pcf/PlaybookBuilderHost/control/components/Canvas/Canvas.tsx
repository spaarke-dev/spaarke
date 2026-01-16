/**
 * Canvas Component - React Flow v10 for PCF
 *
 * Main canvas using react-flow-renderer (v10).
 * Supports node placement, edge connections, zoom/pan, and drag-drop.
 */

import * as React from 'react';
import { useCallback, useRef, DragEvent, CSSProperties } from 'react';
import ReactFlow, {
  Background,
  Controls,
  ReactFlowInstance,
  Node,
  BackgroundVariant,
} from 'react-flow-renderer';
import { makeStyles, tokens } from '@fluentui/react-components';
import { useCanvasStore, PlaybookNodeType, PlaybookNode } from '../../stores';
import { nodeTypes } from '../Nodes';
import { edgeTypes } from '../Edges';
import 'react-flow-renderer/dist/style.css';

const useStyles = makeStyles({
  container: {
    // Use absolute positioning to fill the relatively-positioned parent
    // This ensures the canvas fills all available space (fixes bottom spacing issue)
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// CSS custom properties for React Flow theming
const reactFlowStyle: CSSProperties = {
  backgroundColor: 'var(--colorNeutralBackground1)',
};


/**
 * Main canvas component using React Flow v10.
 * Supports node placement, edge connections, zoom/pan, and drag-drop.
 */
export const Canvas = React.memo(function Canvas() {
  const styles = useStyles();
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const reactFlowInstance = useRef<ReactFlowInstance | null>(null);

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
  const onInit = useCallback((instance: ReactFlowInstance) => {
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

        // Calculate drop position in flow coordinates (v10 uses project method)
        const bounds = reactFlowWrapper.current.getBoundingClientRect();
        const position = reactFlowInstance.current.project({
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

  return (
    <div ref={reactFlowWrapper} className={styles.container}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
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
      >
        <Background
          variant={BackgroundVariant.Dots}
          gap={16}
          size={1}
          color={tokens.colorNeutralStroke2}
        />
        <Controls showZoom showFitView showInteractive />
      </ReactFlow>
    </div>
  );
});
