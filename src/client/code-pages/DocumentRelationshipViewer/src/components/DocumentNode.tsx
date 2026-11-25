/**
 * DocumentNode — @xyflow/react v12 node for the relationship graph.
 *
 * The card itself is the shared `DocumentNodeCard` (@spaarke/ui-components, #3093); this wrapper
 * only supplies @xyflow/react's connection handles.
 */

import React from 'react';
import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { DocumentNodeCard, type DocumentNodeData, type DocumentNodeHandleSpec } from '@spaarke/ui-components';

const renderHandle = (spec: DocumentNodeHandleSpec): React.ReactNode => (
  <Handle type={spec.type} position={spec.side === 'left' ? Position.Left : Position.Right} style={spec.style} />
);

export const DocumentNode: React.FC<NodeProps<Node<DocumentNodeData>>> = ({ data, selected }) => (
  <DocumentNodeCard data={data} selected={selected} renderHandle={renderHandle} />
);

export default DocumentNode;
