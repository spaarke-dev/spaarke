/**
 * DocumentNode — react-flow-renderer v10 node for the relationship graph (React 16, ADR-022).
 *
 * The card itself is the shared `DocumentNodeCard` (@spaarke/ui-components, #3093); this wrapper
 * only supplies react-flow-renderer's connection handles.
 */

import * as React from 'react';
import { Handle, Position, NodeProps } from 'react-flow-renderer';
// The card's own module, not the folder barrel: the barrel also carries the API client (and
// @spaarke/auth), which the node does not need.
import { DocumentNodeCard } from '@spaarke/ui-components/dist/components/DocumentRelationshipGraph/DocumentNodeCard';
import type { DocumentNodeHandleSpec } from '@spaarke/ui-components/dist/components/DocumentRelationshipGraph/DocumentNodeCard';
import type { DocumentNodeData } from '@spaarke/ui-components/dist/components/DocumentRelationshipGraph';

/**
 * The shared card is typed against the shared library's React 19 types; this PCF compiles against
 * React 16 types, so its JSX and `ReactNode` do not line up (TS2786). Re-type it with this PCF's
 * React types — the same per-import cast the other PCFs use for shared components.
 */
interface DocumentNodeCardR16Props {
  data: DocumentNodeData;
  selected?: boolean;
  renderHandle?: (spec: DocumentNodeHandleSpec) => React.ReactNode;
  showPreviewLink?: boolean;
  showDetails?: boolean;
  onOpenRecord?: (data: DocumentNodeData) => void;
}
const DocumentNodeCardR16 = DocumentNodeCard as unknown as React.ComponentType<DocumentNodeCardR16Props>;

const renderHandle = (spec: DocumentNodeHandleSpec): React.ReactNode => (
  <Handle type={spec.type} position={spec.side === 'left' ? Position.Left : Position.Right} style={spec.style} />
);

/** Opens the node's Dataverse record in a new tab. */
const openRecord = (data: DocumentNodeData): void => {
  if (data.recordUrl) {
    window.open(data.recordUrl, '_blank', 'noopener,noreferrer');
  }
};

/**
 * In this PCF a node click only selects the node, so the card's preview link (which relies on the
 * node click) is off; the details tooltip and the open-record link are on, as before #3093.
 */
export const DocumentNode: React.FC<NodeProps<DocumentNodeData>> = ({ data, selected }) => (
  <DocumentNodeCardR16
    data={data}
    selected={selected}
    renderHandle={renderHandle}
    showPreviewLink={false}
    showDetails
    onOpenRecord={openRecord}
  />
);

export default DocumentNode;
