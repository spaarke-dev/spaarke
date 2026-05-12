/**
 * MiniGraph — A compact SVG node-link diagram for previewing document relationships.
 *
 * Pure SVG rendering — no @xyflow/react dependency. Uses the shared
 * useForceSimulation hook for layout computation.
 *
 * Non-interactive preview: clicking anywhere opens the full viewer.
 *
 * @see ADR-021 - Fluent UI v9 design tokens for theming
 * @see ADR-022 - React 16 compatible (useMemo only)
 */
import * as React from 'react';
import type { MiniGraphNode, MiniGraphEdge } from '../../types/MiniGraphTypes';
export interface IMiniGraphProps {
    /** Nodes to display in the preview. */
    nodes: MiniGraphNode[];
    /** Edges connecting nodes. */
    edges: MiniGraphEdge[];
    /** SVG width in pixels. */
    width?: number;
    /** SVG height in pixels. */
    height?: number;
    /** Click handler — typically opens the full viewer. */
    onClick?: () => void;
}
export declare const MiniGraph: React.FC<IMiniGraphProps>;
export default MiniGraph;
//# sourceMappingURL=MiniGraph.d.ts.map