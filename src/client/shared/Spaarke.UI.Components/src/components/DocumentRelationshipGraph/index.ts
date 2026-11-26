/**
 * DocumentRelationshipGraph — the data layer and node card shared by the DocumentRelationshipViewer
 * code page and PCF (#3093). Each host keeps its own canvas (`@xyflow/react` vs `react-flow-renderer`),
 * lays the nodes out with `useForceSimulation`, and wraps `DocumentNodeCard` with its library's handles.
 *
 * React 16-safe; exported from both the main barrel and `pcf-safe`.
 */

export { DocumentNodeCard, getFileTypeIcon, getParentHubLabel, getCompactNodeTitle } from './DocumentNodeCard';
export type { DocumentNodeCardProps, DocumentNodeHandleSpec, DocumentNodeHandleSide } from './DocumentNodeCard';
export { VisualizationApiService, VisualizationApiError, mapToGraphData } from './VisualizationApiService';
export type { DocumentGraphData } from './VisualizationApiService';
export { useVisualizationApi, formatVisualizationError } from './useVisualizationApi';
export type { UseVisualizationApiOptions, VisualizationApiState } from './useVisualizationApi';
export { RELATIONSHIP_TYPES, getRelationshipLabel, isParentHubNode } from './types';
export type {
  ApiDocumentEdge,
  ApiDocumentEdgeData,
  ApiDocumentNode,
  ApiDocumentNodeData,
  DocumentEdgeData,
  DocumentGraphEdge,
  DocumentGraphNode,
  DocumentGraphResponse,
  DocumentNodeData,
  DocumentRelationship,
  ForceLayoutOptions,
  GraphMetadata,
  NodeType,
  RelationshipTypeKey,
  VisualizationQueryParams,
} from './types';
